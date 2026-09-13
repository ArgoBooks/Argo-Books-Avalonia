using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the self-contained bank statement import modal.
/// Parses a bank statement file, categorizes each line (learned rules first, then a single
/// batched AI pass that matches existing products/suppliers/customers or proposes new ones),
/// lets the user review, then creates plain Expense/Revenue transactions. Each transaction is
/// attached to a product, which carries its category. Entry point: <see cref="OpenAsync"/>.
/// </summary>
public partial class BankStatementImportModalViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _includedCount;

    /// <summary>Name of the file being imported, shown under the modal title.</summary>
    [ObservableProperty] private string? _fileName;

    /// <summary>True while parsing and AI categorization run, before the row table is revealed.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>Progress percent (0-100) for the AI categorization phase.</summary>
    [ObservableProperty] private double _categorizeProgress;

    /// <summary>Shows the determinate progress bar during the AI categorization call.</summary>
    [ObservableProperty] private bool _showCategorizeProgress;

    /// <summary>Message shown in the modal's loading state (PDF read phase vs categorize phase).</summary>
    [ObservableProperty] private string _loadingMessage = "Categorizing your statement...";

    // The bar % where the categorize phase begins. For a PDF import the read phase fills 0..this and
    // categorize fills this..100, so the whole import is ONE continuous bar. Stays 0 for CSV/Excel
    // (parsing is instant), where categorize fills the whole bar.
    private double _categorizeProgressFloor;

    // True once a PDF import has charged its single "bank" AI import at extraction, so the follow-up
    // categorization pass neither re-checks the limit nor charges again. Stays false for CSV/Excel,
    // which pay their one credit at the categorization step instead.
    private bool _pdfExtractionCharged;

    /// <summary>True when the AI categorization pass was skipped, so the rows were left for the user.</summary>
    [ObservableProperty] private bool _aiUnavailable;

    /// <summary>Specific reason the AI pass was skipped (limit reached, offline, etc.), shown in the footer.</summary>
    [ObservableProperty] private string? _aiUnavailableMessage;

    /// <summary>Footer summary: selected out of total, e.g. "4 of 6 lines to import".</summary>
    public string IncludedCountText => "{0} of {1} lines to import".TranslateFormat(IncludedCount, Rows.Count);

    partial void OnIncludedCountChanged(int value) => OnPropertyChanged(nameof(IncludedCountText));
    [ObservableProperty] private bool _hasValidationMessage;

    // Set to true the first time the user clicks Import, so error highlights appear.
    private bool _validationAttempted;

    public ObservableCollection<ImportLineRow> Rows { get; } = [];
    public ObservableCollection<Product> AvailableProducts { get; } = [];
    public ObservableCollection<Supplier> AvailableSuppliers { get; } = [];
    public ObservableCollection<Customer> AvailableCustomers { get; } = [];

    // --- Pending-product editor (opened from a row's "New" chip or "Create one") ---------------
    // Edits a row's pending new product (name + category) without creating any entity; everything
    // is applied to the row and only materialized when the statement is imported.
    public ObservableCollection<Category> ProductEditorCategories { get; } = [];
    private ImportLineRow? _editingRow;

    [ObservableProperty] private bool _isProductEditorOpen;
    [ObservableProperty] private string _productEditorName = string.Empty;
    [ObservableProperty] private Category? _productEditorCategoryObject;
    [ObservableProperty] private string? _productEditorCategorySearchText;
    [ObservableProperty] private bool _productEditorNameError;

    partial void OnProductEditorNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) ProductEditorNameError = false;
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses <paramref name="filePath"/> and opens the modal for user review. Returns once the
    /// modal is shown; the AI categorization pass then fills in the rest in the background.
    /// </summary>
    public async Task OpenAsync(string filePath)
    {
        FileName = Path.GetFileName(filePath);
        AiUnavailable = false;
        AiUnavailableMessage = null;
        _categorizeProgressFloor = 0;
        _pdfExtractionCharged = false;

        List<BankStatementLine> lines;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".pdf")
        {
            lines = await ImportPdfStatementAsync(filePath);
            // The PDF path shows its own messaging (usage limit, cancel, extraction failure),
            // so just bail quietly when it returns nothing.
            if (lines.Count == 0) return;
        }
        else
        {
            var parser = new BankStatementImportService(App.ErrorLogger);
            if (ext == ".csv")
                lines = await parser.ParseCsvAsync(filePath);
            else
                lines = await parser.ParseExcelAsync(filePath);

            // Don't fail silently when the file isn't a recognizable bank statement (e.g. the user
            // picked the wrong spreadsheet): tell them what's expected instead of doing nothing.
            if (lines.Count == 0)
            {
                await App.ShowInfoMessageBoxAsync(
                    "Import Bank Statement".Translate(),
                    "No transactions were found in this file. Make sure it's a bank statement with Date, Description and Amount (or Debit/Credit) columns.".Translate());
                return;
            }
        }

        _validationAttempted = false;

        // Show the modal in its loading state, run deterministic + AI categorization, then reveal the
        // table. For a PDF the modal is already open from the reading phase and the bar is partway
        // along (_categorizeProgressFloor): keep it there so the import is one continuous bar instead
        // of two. For CSV/Excel the floor is 0 and categorize fills the whole bar.
        LoadingMessage = "Categorizing your statement...".Translate();
        CategorizeProgress = _categorizeProgressFloor;
        ShowCategorizeProgress = _categorizeProgressFloor > 0;
        IsLoading = true;
        IsOpen = true;

        PopulateRows(lines);
        await CategorizeWithAiAsync();

        IsLoading = false;
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task Import()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        var toImport = Rows.Where(r => r.IsIncluded).ToList();
        if (toImport.Count == 0) return;

        // Gate: show required-field errors if any included row is incomplete.
        if (!_validationAttempted || toImport.Any(r => !r.IsComplete))
        {
            _validationAttempted = true;
            RefreshState();
            if (toImport.Any(r => !r.IsComplete)) return;
        }

        // Confirm before re-importing rows that already exist, so the user knowingly creates
        // duplicate transactions (or backs out and unchecks them).
        var duplicateCount = toImport.Count(r => r.AlreadyImported);
        if (duplicateCount > 0 && App.ConfirmationDialog is { } confirm)
        {
            var result = await confirm.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Import duplicates?".Translate(),
                Message = "{0} of these transactions already exist in your books. Importing will create duplicate copies. Import them again anyway?".TranslateFormat(duplicateCount),
                PrimaryButtonText = "Import anyway".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });
            if (result != ConfirmationResult.Primary)
                return;
        }

        var resolutions = toImport.Select(r => new BankLineResolution
        {
            Line = new BankStatementLine
            {
                Id = string.IsNullOrEmpty(r.SourceLine.Id) ? Guid.NewGuid().ToString("N") : r.SourceLine.Id,
                Date = r.Date.DateTime,
                Description = r.Description,
                Amount = r.CreateAsRevenue ? Math.Abs(r.Amount) : -Math.Abs(r.Amount)
            },
            Type = r.CreateAsRevenue ? BookRecordType.Revenue : BookRecordType.Expense,
            ProductId = r.ResolvedProductId,
            NewProductName = r.ResolvedProductId == null ? r.NewProductName : null,
            ProductCategoryId = r.NewProductCategoryId,
            NewProductCategoryName = r.NewProductCategoryId == null ? r.NewProductCategoryName : null,
            CounterpartyId = r.ResolvedCounterpartyId,
            NewCounterpartyName = r.ResolvedCounterpartyId == null ? r.NewCounterpartyName : null
        }).ToList();

        // Snapshot id counters before CreateFromLines bumps them.
        var preCounters = new IdCounterSnapshot(data.IdCounters);

        // linkToBankLine: false -> plain transactions, no bank-match flag.
        var creation = new BankLineImportService().CreateFromLines(data, resolutions, linkToBankLine: false);
        creation.MirrorPendingConversions(data);

        // Learn a rule per line (merchant -> product + counterparty) so the next import is pre-filled.
        var ruleCaptures = LearnRules(data, resolutions);

        var postCounters = new IdCounterSnapshot(data.IdCounters);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            "Import bank statement".Translate(),
            () => UndoImport(data, creation, ruleCaptures, preCounters),
            () => RedoImport(data, creation, ruleCaptures, postCounters)));

        App.CompanyManager?.MarkAsChanged();
        IsOpen = false;
    }

    [RelayCommand]
    private async Task Cancel()
    {
        // Confirm discard like other modals whenever an import is in flight: either rows are already
        // parsed (spreadsheet, and the AI-categorization phase), or an import is still loading with no
        // rows yet (a PDF extraction reads "Reading PDF statement..." before any rows exist). Checking
        // Rows.Count alone silently closed the modal mid-PDF-extraction with no prompt.
        if ((Rows.Count > 0 || IsLoading) && !await ConfirmDiscardNewAsync())
            return;
        IsOpen = false;
    }

    private List<RuleLearningCapture> LearnRules(CompanyData data, List<BankLineResolution> resolutions)
    {
        var ruleCaptures = new List<RuleLearningCapture>();

        foreach (var res in resolutions)
        {
            // Resolve the final product id (existing, or the one just created by name+type).
            var type = res.Type == BookRecordType.Revenue ? CategoryType.Revenue : CategoryType.Expense;
            var productId = res.ProductId;
            if (productId == null && !string.IsNullOrWhiteSpace(res.NewProductName))
                productId = data.Products.FirstOrDefault(p =>
                    p.Type == type && string.Equals(p.Name, res.NewProductName!.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

            if (productId == null) continue; // nothing meaningful to remember

            var counterpartyId = res.CounterpartyId;
            if (counterpartyId == null && !string.IsNullOrWhiteSpace(res.NewCounterpartyName))
            {
                counterpartyId = res.Type == BookRecordType.Revenue
                    ? data.Customers.FirstOrDefault(c => string.Equals(c.Name, res.NewCounterpartyName!.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
                    : data.Suppliers.FirstOrDefault(s => string.Equals(s.Name, res.NewCounterpartyName!.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
            }

            var categoryId = data.GetProduct(productId)?.CategoryId ?? string.Empty;

            // A bank import rule exists to auto-assign a CATEGORY (that's the Bank import rules tab's
            // whole purpose). If the resolved product has no category, there's nothing for the rule
            // to assign, so don't create a blank-category rule that just clutters the list.
            if (string.IsNullOrEmpty(categoryId)) continue;

            var token = MerchantNormalizer.Normalize(res.Line.Description);
            var existing = data.BankCategoryRules.FirstOrDefault(r =>
                r.Pattern == token && r.MatchType == RuleMatchType.Contains);

            RulePriorState? prior = existing == null ? null : new RulePriorState(
                existing.CategoryId, existing.ProductId, existing.TransactionType, existing.CounterpartyId,
                existing.Source, existing.UpdatedAt);

            var rule = CategoryRuleService.Learn(data, res.Line.Description, categoryId, res.Type, counterpartyId, productId);

            var post = new RulePostState(rule.CategoryId, rule.ProductId, rule.TransactionType, rule.CounterpartyId,
                rule.Source, rule.UpdatedAt);

            ruleCaptures.Add(new RuleLearningCapture(rule, prior, post));
        }

        return ruleCaptures;
    }

    // -----------------------------------------------------------------------
    // AI categorization (batched, runs after the modal opens)
    // -----------------------------------------------------------------------

    private async Task CategorizeWithAiAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        var pending = Rows.Where(r => !r.HasProduct || !r.HasCounterparty).ToList();
        if (pending.Count == 0) return;

        try
        {
            using var usage = new AiImportUsageService(App.LicenseService, App.ErrorLogger, importType: "bank");
            // PDF imports already paid their single bank-import credit at extraction, so they neither
            // re-check the limit nor charge again here. CSV/Excel imports pay their one credit at this
            // step, so they check availability first.
            if (!_pdfExtractionCharged)
            {
                var check = await usage.CheckUsageAsync();
                // No AI imports available (or offline): leave blanks for the user. The import still works.
                if (!check.CanImport)
                {
                    SetAiUnavailable(!string.IsNullOrEmpty(check.ErrorMessage)
                        ? "AI categorization is unavailable: couldn't reach the server.".Translate()
                        : check.MonthlyLimit > 0
                            ? "AI categorization is off: you've used all {0} AI imports this month.".TranslateFormat(check.MonthlyLimit)
                            : "AI categorization needs a registered company.".Translate());
                    return;
                }
            }

            using var gemini = new GeminiService(App.ErrorLogger);
            if (!gemini.IsConfigured)
            {
                SetAiUnavailable("AI categorization needs a registered company.".Translate());
                return;
            }

            // Build the request and call the model off the UI thread. Assembling the request from
            // every existing product/category/supplier/customer and JSON-serializing them into the
            // prompt is synchronous and was freezing the loading spinner (~0.5s) on companies with
            // a lot of data. ApplySuggestions runs back on the UI thread (it touches bound rows).
            ShowCategorizeProgress = true;
            // Continue the one bar from where the read phase left off (0 for CSV/Excel, 60 for PDF).
            var floor = _categorizeProgressFloor;
            CategorizeProgress = floor;
            using var ticker = new EstimatedProgressTicker(
                OperationKind.BankCategorize, pct => CategorizeProgress = floor + pct * (100 - floor) / 100, sizeFeature: pending.Count);
            ticker.Start();
            var suggestions = await Task.Run(() =>
            {
                var request = BuildCategorizationRequest(data, pending);
                return gemini.GetBankLineSuggestionsAsync(request);
            });
            ticker.Complete();

            if (suggestions == null || suggestions.Count == 0)
            {
                SetAiUnavailable("AI couldn't categorize this statement. Select products manually.".Translate());
                return;
            }

            // The Gemini call already ran and cost a credit, so record usage even if the user has since
            // closed the modal. Skipping it on close would let a start/cancel loop make real AI calls
            // without ever consuming quota. (The server's own per-identity rate limit still caps the
            // absolute number of calls, so this is about quota fairness, not an unbounded bill.)
            // PDF imports are exempt: they already charged their single bank-import credit at extraction.
            if (!_pdfExtractionCharged)
                await usage.IncrementUsageAsync();

            // Closed during the call: the credit is counted, but don't bother writing suggestions back
            // to rows nobody is viewing.
            if (!IsOpen)
                return;

            ApplySuggestions(data, pending, suggestions);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Bank statement AI categorization failed");
        }
    }

    private void SetAiUnavailable(string message)
    {
        AiUnavailable = true;
        AiUnavailableMessage = message;
    }

    private static BankLineCategorizationRequest BuildCategorizationRequest(CompanyData data, List<ImportLineRow> rows)
    {
        var req = new BankLineCategorizationRequest();

        foreach (var p in data.Products)
            req.ExistingProducts.Add(new ExistingProductInfo
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = string.IsNullOrEmpty(p.CategoryId) ? null : data.GetCategory(p.CategoryId)?.Name,
                IsRevenue = p.Type == CategoryType.Revenue
            });

        foreach (var c in data.Categories.Where(c => c.Type == CategoryType.Expense))
            req.ExistingExpenseCategories.Add(new ExistingCategoryInfo { Id = c.Id, Name = c.Name, Description = c.Description });
        foreach (var c in data.Categories.Where(c => c.Type == CategoryType.Revenue))
            req.ExistingRevenueCategories.Add(new ExistingCategoryInfo { Id = c.Id, Name = c.Name, Description = c.Description });

        foreach (var s in data.Suppliers)
            req.ExistingSuppliers.Add(new ExistingSupplierInfo { Id = s.Id, Name = s.Name });
        foreach (var c in data.Customers)
            req.ExistingCustomers.Add(new ExistingSupplierInfo { Id = c.Id, Name = c.Name });

        foreach (var r in rows)
            req.Lines.Add(new BankLineToCategorize { Index = r.Index, Description = r.Description, Amount = r.Amount, IsRevenue = r.CreateAsRevenue });

        return req;
    }

    private void ApplySuggestions(CompanyData data, List<ImportLineRow> pending, List<BankLineSuggestion> suggestions)
    {
        var byIndex = suggestions.Where(s => s.Index >= 0)
            .GroupBy(s => s.Index)
            .ToDictionary(g => g.Key, g => g.First());

        for (var i = 0; i < pending.Count; i++)
        {
            var row = pending[i];

            // Match strictly by the AI's explicit line index (the prompt requires the model to echo
            // it for every line). Positional fallback was unsafe: when a batch fails its lines are
            // omitted from the merged list, so this row would borrow a different line's suggestion.
            // An unmatched row is left blank for the user to fill in.
            if (!byIndex.TryGetValue(row.Index, out var s))
                continue;

            // Product (only fill if the user / rules didn't already set one).
            if (!row.HasProduct)
            {
                if (s.ProductId != null)
                {
                    var prod = AvailableProducts.FirstOrDefault(p => p.Id == s.ProductId);
                    if (prod != null) row.SetExistingProduct(prod, CategoryNameFor(data, prod));
                }
                if (!row.HasProduct && !string.IsNullOrWhiteSpace(s.NewProductName))
                {
                    // Only trust a categoryId that resolves to a real category. The model is asked to echo
                    // an existing id, but sometimes returns a hallucinated id or a category *name* in this
                    // field; in that case treat the value as a new-category name so a real category gets
                    // created rather than a dangling id landing on the product and its learned rule.
                    var existingCat = string.IsNullOrEmpty(s.ProductCategoryId) ? null : data.GetCategory(s.ProductCategoryId);
                    var categoryId = existingCat?.Id;
                    var newCatName = existingCat != null
                        ? null
                        : (!string.IsNullOrWhiteSpace(s.NewProductCategoryName) ? s.NewProductCategoryName : s.ProductCategoryId);
                    row.SetNewProduct(s.NewProductName!, categoryId, newCatName, existingCat?.Name ?? newCatName);
                }
            }

            // Counterparty (only fill if still empty).
            if (!row.HasCounterparty)
            {
                if (s.CounterpartyId != null)
                {
                    if (row.CreateAsRevenue)
                    {
                        var c = AvailableCustomers.FirstOrDefault(x => x.Id == s.CounterpartyId);
                        if (c != null) row.SetExistingCustomer(c);
                    }
                    else
                    {
                        var sup = AvailableSuppliers.FirstOrDefault(x => x.Id == s.CounterpartyId);
                        if (sup != null) row.SetExistingSupplier(sup);
                    }
                }
                if (!row.HasCounterparty && !string.IsNullOrWhiteSpace(s.NewCounterpartyName))
                    row.SetNewCounterparty(s.NewCounterpartyName!);
            }
        }

        RefreshState();
    }

    private static string? CategoryNameFor(CompanyData data, Product product) =>
        string.IsNullOrEmpty(product.CategoryId) ? null : data.GetCategory(product.CategoryId)?.Name;

    // -----------------------------------------------------------------------
    // Undo / Redo
    // -----------------------------------------------------------------------

    private void UndoImport(CompanyData data, BankImportCreation creation,
        List<RuleLearningCapture> ruleCaptures, IdCounterSnapshot preCounters)
    {
        creation.Undo(data);

        // Newest first: when two lines updated the same rule, each capture's Prior is the state the
        // line before it left, so only a reverse walk ends on the rule as it was before the import.
        for (var i = ruleCaptures.Count - 1; i >= 0; i--)
        {
            var cap = ruleCaptures[i];
            if (cap.Prior == null)
            {
                data.BankCategoryRules.Remove(cap.Rule);
            }
            else
            {
                cap.Rule.CategoryId = cap.Prior.CategoryId;
                cap.Rule.ProductId = cap.Prior.ProductId;
                cap.Rule.TransactionType = cap.Prior.TransactionType;
                cap.Rule.CounterpartyId = cap.Prior.CounterpartyId;
                cap.Rule.Source = cap.Prior.Source;
                cap.Rule.UpdatedAt = cap.Prior.UpdatedAt;
            }
        }

        preCounters.RestoreTo(data.IdCounters);
        data.MarkAsModified();
        App.CompanyManager?.MarkAsChanged();
    }

    private void RedoImport(CompanyData data, BankImportCreation creation,
        List<RuleLearningCapture> ruleCaptures, IdCounterSnapshot postCounters)
    {
        creation.Redo(data);

        foreach (var cap in ruleCaptures)
        {
            if (cap.Prior == null)
            {
                if (!data.BankCategoryRules.Contains(cap.Rule))
                    data.BankCategoryRules.Add(cap.Rule);
            }
            else
            {
                cap.Rule.CategoryId = cap.Post.CategoryId;
                cap.Rule.ProductId = cap.Post.ProductId;
                cap.Rule.TransactionType = cap.Post.TransactionType;
                cap.Rule.CounterpartyId = cap.Post.CounterpartyId;
                cap.Rule.Source = cap.Post.Source;
                cap.Rule.UpdatedAt = cap.Post.UpdatedAt;
            }
        }

        postCounters.RestoreTo(data.IdCounters);
        data.MarkAsModified();
        App.CompanyManager?.MarkAsChanged();
    }

    // -----------------------------------------------------------------------
    // Populate / deterministic pre-fill
    // -----------------------------------------------------------------------

    private void PopulateRows(List<BankStatementLine> lines)
    {
        var data = App.CompanyManager?.CompanyData;

        ReloadProducts();
        AvailableSuppliers.Clear();
        AvailableCustomers.Clear();
        if (data != null)
        {
            foreach (var s in data.Suppliers.OrderBy(s => s.Name)) AvailableSuppliers.Add(s);
            foreach (var c in data.Customers.OrderBy(c => c.Name)) AvailableCustomers.Add(c);
        }

        Rows.Clear();

        var index = 0;
        foreach (var line in lines)
        {
            var row = new ImportLineRow(line) { Index = index++ };
            row.OpenCreateProduct = () => OpenProductEditor(row);
            row.OpenCreateCounterparty = () => OpenCreateCounterpartyForRow(row);
            row.ProductCategoryNameLookup = p => data != null ? CategoryNameFor(data, p) : null;

            if (data != null)
                ApplyDeterministicPrefill(data, row);

            row.PropertyChanged += (_, _) => RefreshState();
            Rows.Add(row);
        }

        if (data != null)
            FlagAlreadyImported(data);

        RefreshState();
    }

    /// <summary>
    /// Flags rows that already exist as a transaction (matched on type + date + amount +
    /// description). They stay included and show an "Already imported" note; the import asks the
    /// user to confirm before creating duplicates from them.
    /// </summary>
    private void FlagAlreadyImported(CompanyData data)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in data.Expenses)
            existing.Add(TxKey(isRevenue: false, e.Date, e.Total, e.Description));
        foreach (var r in data.Revenues)
            existing.Add(TxKey(isRevenue: true, r.Date, r.Total, r.Description));

        foreach (var row in Rows)
        {
            var key = TxKey(row.CreateAsRevenue, row.Date.DateTime, row.Amount, row.Description);
            if (existing.Contains(key))
                row.AlreadyImported = true;
        }
    }

    /// <summary>Stable key for duplicate detection: type + date (day) + absolute amount + description.</summary>
    private static string TxKey(bool isRevenue, DateTime date, decimal amount, string? description) =>
        string.Join("|",
            isRevenue ? "R" : "E",
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Math.Abs(amount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            (description ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>Free, instant pre-fill: learned rules first, then an obvious name match against existing entities.</summary>
    private void ApplyDeterministicPrefill(CompanyData data, ImportLineRow row)
    {
        // 1. Learned rule (remembers product + counterparty + type for this merchant).
        var rule = CategoryRuleService.Match(data.BankCategoryRules, row.Description);
        if (rule != null)
        {
            if (rule.TransactionType.HasValue)
                row.CreateAsRevenue = rule.TransactionType.Value == BookRecordType.Revenue;

            if (rule.ProductId != null)
            {
                var prod = AvailableProducts.FirstOrDefault(p => p.Id == rule.ProductId);
                if (prod != null) row.SetExistingProduct(prod, CategoryNameFor(data, prod));
            }

            if (rule.CounterpartyId != null)
                ApplyCounterpartyId(row, rule.CounterpartyId);
        }

        // 2. Obvious text match for a product (merchant name already in your product list).
        if (!row.HasProduct)
        {
            var prod = MatchByName(row.Description, AvailableProducts.Where(p =>
                p.Type == (row.CreateAsRevenue ? CategoryType.Revenue : CategoryType.Expense)), p => p.Name, p => p);
            if (prod != null) row.SetExistingProduct(prod, CategoryNameFor(data, prod));
        }

        // 3. Obvious text match for the counterparty.
        if (!row.HasCounterparty)
        {
            if (row.CreateAsRevenue)
            {
                var c = MatchByName(row.Description, AvailableCustomers, x => x.Name, x => x);
                if (c != null) row.SetExistingCustomer(c);
            }
            else
            {
                var s = MatchByName(row.Description, AvailableSuppliers, x => x.Name, x => x);
                if (s != null) row.SetExistingSupplier(s);
            }
        }
    }

    private void ApplyCounterpartyId(ImportLineRow row, string counterpartyId)
    {
        if (row.CreateAsRevenue)
        {
            var c = AvailableCustomers.FirstOrDefault(x => x.Id == counterpartyId);
            if (c != null) row.SetExistingCustomer(c);
        }
        else
        {
            var s = AvailableSuppliers.FirstOrDefault(x => x.Id == counterpartyId);
            if (s != null) row.SetExistingSupplier(s);
        }
    }

    /// <summary>Returns the first entity whose normalized name appears in the normalized description.</summary>
    private static TResult? MatchByName<TItem, TResult>(string description, IEnumerable<TItem> items,
        Func<TItem, string> nameSelector, Func<TItem, TResult> resultSelector) where TResult : class
    {
        var token = MerchantNormalizer.Normalize(description);
        if (token.Length == 0) return null;

        foreach (var item in items)
        {
            var name = MerchantNormalizer.Normalize(nameSelector(item));
            if (name.Length >= 3 && token.Contains(name, StringComparison.Ordinal))
                return resultSelector(item);
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // "Create one" — open standard create modals and select the new entity on the row
    // -----------------------------------------------------------------------

    /// <summary>
    /// Opens the lightweight editor for a row's pending new product. Lets the user set the product
    /// name and category (an existing category, or a new name created on import) without touching the
    /// shared product modal and without creating any entity until the statement is actually imported.
    /// </summary>
    private void OpenProductEditor(ImportLineRow row)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        _editingRow = row;

        // Offer categories matching the row's type (expense vs revenue).
        var type = row.CreateAsRevenue ? CategoryType.Revenue : CategoryType.Expense;
        ProductEditorCategories.Clear();
        foreach (var c in data.Categories.Where(c => c.Type == type).OrderBy(c => c.Name))
            ProductEditorCategories.Add(c);

        ProductEditorNameError = false;
        ProductEditorName = (row.IsNewProduct ? row.NewProductName : row.ProductSearchText) ?? string.Empty;

        // Seed the category: an existing one as a real selection, otherwise the pending new name as text.
        if (row.NewProductCategoryId != null)
        {
            ProductEditorCategoryObject = ProductEditorCategories.FirstOrDefault(c => c.Id == row.NewProductCategoryId);
            ProductEditorCategorySearchText = ProductEditorCategoryObject?.Name;
        }
        else
        {
            ProductEditorCategoryObject = null;
            ProductEditorCategorySearchText = row.NewProductCategoryName ?? row.CategoryDisplay;
        }

        IsProductEditorOpen = true;
    }

    [RelayCommand]
    private void SaveProductEditor()
    {
        var row = _editingRow;
        if (row == null) return;

        var name = (ProductEditorName).Trim();
        if (name.Length == 0)
        {
            ProductEditorNameError = true;
            return;
        }

        // An explicit selection wins; otherwise treat typed text as an existing category (matched by
        // name) or, failing that, a brand-new category to be created when the statement is imported.
        string? categoryId = ProductEditorCategoryObject?.Id;
        string? newCategoryName = null;
        string? categoryDisplay = ProductEditorCategoryObject?.Name;

        if (categoryId == null)
        {
            var typed = (ProductEditorCategorySearchText ?? string.Empty).Trim();
            if (typed.Length > 0)
            {
                var match = ProductEditorCategories.FirstOrDefault(c => string.Equals(c.Name, typed, StringComparison.OrdinalIgnoreCase));
                if (match != null) { categoryId = match.Id; categoryDisplay = match.Name; }
                else { newCategoryName = typed; categoryDisplay = typed; }
            }
        }

        row.SetNewProduct(name, categoryId, newCategoryName, categoryDisplay);

        IsProductEditorOpen = false;
        _editingRow = null;
        RefreshState();
    }

    [RelayCommand]
    private void CancelProductEditor()
    {
        IsProductEditorOpen = false;
        _editingRow = null;
    }

    /// <summary>
    /// Opens the standard Supplier modal (expense rows) or Customer modal (revenue rows).
    /// When saved, reloads counterparties and selects the new entity on <paramref name="row"/>.
    /// </summary>
    private void OpenCreateCounterpartyForRow(ImportLineRow row)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        if (!row.CreateAsRevenue)
        {
            var supplierModals = App.SupplierModalsViewModel;
            if (supplierModals == null) return;

            var knownIds = data.Suppliers.Select(s => s.Id).ToHashSet();

            void OnSaved(object? s, EventArgs e)
            {
                supplierModals.SupplierSaved -= OnSaved;
                ReloadSuppliers();
                var newSupplier = data.Suppliers.FirstOrDefault(s => !knownIds.Contains(s.Id));
                if (newSupplier != null) row.SetExistingSupplier(newSupplier);
            }

            supplierModals.SupplierSaved += OnSaved;
            supplierModals.OpenAddModal();
        }
        else
        {
            var customerModals = App.CustomerModalsViewModel;
            if (customerModals == null) return;

            var knownIds = data.Customers.Select(c => c.Id).ToHashSet();

            void OnSaved(object? s, EventArgs e)
            {
                customerModals.CustomerSaved -= OnSaved;
                ReloadCustomers();
                var newCustomer = data.Customers.FirstOrDefault(c => !knownIds.Contains(c.Id));
                if (newCustomer != null) row.SetExistingCustomer(newCustomer);
            }

            customerModals.CustomerSaved += OnSaved;
            customerModals.OpenAddModal();
        }
    }

    private void ReloadProducts()
    {
        var data = App.CompanyManager?.CompanyData;
        AvailableProducts.Clear();
        if (data != null)
            foreach (var p in data.Products.OrderBy(p => p.Name))
                AvailableProducts.Add(p);
    }

    private void ReloadSuppliers()
    {
        var data = App.CompanyManager?.CompanyData;
        AvailableSuppliers.Clear();
        if (data != null)
            foreach (var s in data.Suppliers.OrderBy(s => s.Name))
                AvailableSuppliers.Add(s);
    }

    private void ReloadCustomers()
    {
        var data = App.CompanyManager?.CompanyData;
        AvailableCustomers.Clear();
        if (data != null)
            foreach (var c in data.Customers.OrderBy(c => c.Name))
                AvailableCustomers.Add(c);
    }

    private void RefreshState()
    {
        IncludedCount = Rows.Count(r => r.IsIncluded);
        OnPropertyChanged(nameof(IncludedCountText)); // total (Rows.Count) may have changed too

        var includedRows = Rows.Where(r => r.IsIncluded).ToList();

        if (_validationAttempted)
        {
            var allComplete = includedRows.Count > 0 && includedRows.All(r => r.IsComplete);
            HasValidationMessage = includedRows.Count > 0 && !allComplete;

            foreach (var row in includedRows)
            {
                row.HasProductError = !row.HasProduct;
                row.HasCounterpartyError = !row.HasCounterparty;
            }
        }
        else
        {
            HasValidationMessage = false;
        }

        foreach (var row in Rows.Where(r => !r.IsIncluded))
        {
            row.HasProductError = false;
            row.HasCounterpartyError = false;
        }
    }

    /// <summary>
    /// PDF bank statement import. The usage check runs first (as a dialog); on success the import
    /// modal opens and the PDF is read INSIDE it, driving the modal's progress bar, so the whole
    /// import uses one consistent UI rather than a separate floating overlay.
    /// </summary>
    private async Task<List<BankStatementLine>> ImportPdfStatementAsync(string filePath)
    {
        // Usage check runs before the modal opens, so a limit failure shows its prompt without
        // briefly flashing the import modal. A PDF consumes one "bank" AI import, charged at
        // extraction (below); the follow-up AI categorization skips its own charge for PDFs
        // (see _pdfExtractionCharged).
        using var usage = await App.TryBeginBankPdfImportAsync();
        if (usage == null) return [];
        if (App.PdfStatementExtractor == null) return [];

        // Open the import modal and read the PDF inside it (driving the modal's progress bar).
        LoadingMessage = "Reading PDF statement...".Translate();
        CategorizeProgress = 0;
        ShowCategorizeProgress = true;
        IsLoading = true;
        IsOpen = true;

        var bytes = await SharedFileReader.ReadAllBytesAsync(filePath);
        List<BankStatementLine> extracted;
        // Reading fills the first 60% of the bar; the categorize phase fills the rest, so the whole
        // import reads as one continuous bar.
        using (var ticker = new EstimatedProgressTicker(
            OperationKind.BankPdfExtract, pct => CategorizeProgress = pct * 0.6, uploadBytes: bytes.Length))
        {
            ticker.Start();
            extracted = await App.PdfStatementExtractor.ExtractAsync(bytes, Path.GetFileName(filePath));
            ticker.Complete();
        }
        _categorizeProgressFloor = 60;

        if (extracted.Count == 0)
        {
            // The extractor returns nothing both when the PDF has no recognizable transactions and when
            // the server couldn't process it. Don't charge a credit for a no-result extraction; only
            // surface the message if the modal is still open.
            if (IsOpen)
            {
                IsOpen = false;
                IsLoading = false;
                await App.ShowInfoMessageBoxAsync(
                    "Import Bank Statement".Translate(),
                    "We couldn't read any transactions from that PDF. It may not be a recognizable bank statement, or the server couldn't process it. Try again, or import a CSV or Excel export instead.".Translate());
            }
            return [];
        }

        // Extraction succeeded and cost a credit, so consume it even if the user has since closed the
        // modal. Skipping it on close would let a start/cancel loop extract PDFs without ever consuming
        // quota. (The server's per-identity rate limit still caps the absolute number of calls.)
        await usage.IncrementUsageAsync();

        // This PDF import has now paid its single bank-import credit, so the AI categorization pass
        // that runs next must not charge again.
        _pdfExtractionCharged = true;

        // Closed during the read: the credit is counted, but don't hand rows to a modal nobody's viewing.
        if (!IsOpen) return [];

        return extracted;
    }

    // -----------------------------------------------------------------------
    // Nested helper types (rule capture, id-counter snapshot)
    // -----------------------------------------------------------------------

    private sealed record RulePriorState(
        string CategoryId,
        string? ProductId,
        BookRecordType? TransactionType,
        string? CounterpartyId,
        RuleSource Source,
        DateTime UpdatedAt);

    private sealed record RulePostState(
        string CategoryId,
        string? ProductId,
        BookRecordType? TransactionType,
        string? CounterpartyId,
        RuleSource Source,
        DateTime UpdatedAt);

    private sealed record RuleLearningCapture(
        BankCategoryRule Rule,
        RulePriorState? Prior,
        RulePostState Post);

    private sealed class IdCounterSnapshot
    {
        private readonly int _expense;
        private readonly int _revenue;
        private readonly int _supplier;
        private readonly int _customer;
        private readonly int _category;
        private readonly int _product;

        public IdCounterSnapshot(IdCounters counters)
        {
            _expense = counters.Expense;
            _revenue = counters.Revenue;
            _supplier = counters.Supplier;
            _customer = counters.Customer;
            _category = counters.Category;
            _product = counters.Product;
        }

        public void RestoreTo(IdCounters counters)
        {
            counters.Expense = _expense;
            counters.Revenue = _revenue;
            counters.Supplier = _supplier;
            counters.Customer = _customer;
            counters.Category = _category;
            counters.Product = _product;
        }
    }
}

/// <summary>
/// Display and resolution wrapper for a single bank statement line in the import modal.
/// </summary>
public partial class ImportLineRow : ObservableObject
{
    public BankStatementLine SourceLine { get; }

    /// <summary>Position in the parent Rows collection, used to map AI suggestions back to the row.</summary>
    public int Index { get; set; }

    public ImportLineRow(BankStatementLine line)
    {
        SourceLine = line;
        _isIncluded = true;
        _date = line.Date == default ? DateTimeOffset.Now : new DateTimeOffset(line.Date);
        _amount = line.Amount;
        _createAsRevenue = line.Amount > 0;
    }

    [ObservableProperty] private bool _isIncluded;

    /// <summary>True when an identical transaction (type + date + amount + description) already
    /// exists. The row stays included; importing shows a confirmation before creating duplicates.</summary>
    [ObservableProperty] private bool _alreadyImported;

    [ObservableProperty] private DateTimeOffset _date;
    [ObservableProperty] private decimal _amount;

    /// <summary>True = Revenue, False = Expense. Defaults from the sign of the bank line amount.</summary>
    [ObservableProperty] private bool _createAsRevenue;

    /// <summary>Transaction description (always the raw bank text).</summary>
    public string Description => SourceLine.Description;

    // --- Product (carries the category) ---

    [ObservableProperty] private string? _resolvedProductId;

    /// <summary>Pending new product name (AI-proposed or typed) when no existing product is selected.</summary>
    [ObservableProperty] private string? _newProductName;

    /// <summary>Search text bound TwoWay to the product SearchableDropdown.</summary>
    [ObservableProperty] private string? _productSearchText;

    /// <summary>True when this row is creating a new product (its category chip is editable).</summary>
    [ObservableProperty] private bool _isNewProduct;

    /// <summary>Read-only category name shown as a chip, derived from the selected/new product.</summary>
    [ObservableProperty] private string? _categoryDisplay;

    /// <summary>Existing category id for a pending new product, when matched to an existing category.</summary>
    public string? NewProductCategoryId { get; private set; }

    /// <summary>New category name for a pending new product, when none matched.</summary>
    public string? NewProductCategoryName { get; private set; }

    [ObservableProperty] private bool _hasProductError;

    // --- Counterparty ---

    [ObservableProperty] private string? _resolvedCounterpartyId;
    [ObservableProperty] private string? _newCounterpartyName;
    [ObservableProperty] private string? _counterpartySearchText;
    [ObservableProperty] private bool _hasCounterpartyError;

    public bool HasProduct => ResolvedProductId != null || !string.IsNullOrWhiteSpace(NewProductName);
    public bool HasCounterparty => ResolvedCounterpartyId != null || !string.IsNullOrWhiteSpace(NewCounterpartyName);
    public bool IsComplete => HasProduct && HasCounterparty;

    public string DateFormatted => Date.ToString("MMM d, yyyy");

    [RelayCommand] private void SetExpense() => CreateAsRevenue = false;
    [RelayCommand] private void SetRevenue() => CreateAsRevenue = true;

    // -----------------------------------------------------------------------
    // Apply helpers (called by the parent VM after deterministic / AI resolution)
    // -----------------------------------------------------------------------

    public void SetExistingProduct(Product product, string? categoryName)
    {
        _resolvedProductObject = product;
        ResolvedProductId = product.Id;
        IsNewProduct = false;
        NewProductName = null;
        NewProductCategoryId = null;
        NewProductCategoryName = null;
        CategoryDisplay = categoryName;
        ProductSearchText = product.Name;
        HasProductError = false;
        OnPropertyChanged(nameof(ResolvedProductObject));
        OnPropertyChanged(nameof(HasProduct));
    }

    public void SetNewProduct(string name, string? categoryId, string? newCategoryName, string? categoryDisplay)
    {
        _resolvedProductObject = null;
        OnPropertyChanged(nameof(ResolvedProductObject)); // push null to the picker first (it clears its text)
        ResolvedProductId = null;
        IsNewProduct = true;
        NewProductName = name;
        NewProductCategoryId = categoryId;
        NewProductCategoryName = newCategoryName;
        CategoryDisplay = categoryDisplay;
        HasProductError = false;
        OnPropertyChanged(nameof(HasProduct));
        ProductSearchText = name; // set the displayed text last so it wins over the cleared selection
    }

    public void SetExistingSupplier(Supplier supplier)
    {
        _resolvedSupplierObject = supplier;
        ResolvedCounterpartyId = supplier.Id;
        NewCounterpartyName = null;
        CounterpartySearchText = supplier.Name;
        HasCounterpartyError = false;
        OnPropertyChanged(nameof(ResolvedSupplierObject));
        OnPropertyChanged(nameof(HasCounterparty));
    }

    public void SetExistingCustomer(Customer customer)
    {
        _resolvedCustomerObject = customer;
        ResolvedCounterpartyId = customer.Id;
        NewCounterpartyName = null;
        CounterpartySearchText = customer.Name;
        HasCounterpartyError = false;
        OnPropertyChanged(nameof(ResolvedCustomerObject));
        OnPropertyChanged(nameof(HasCounterparty));
    }

    public void SetNewCounterparty(string name)
    {
        ResolvedCounterpartyId = null;
        NewCounterpartyName = name;
        CounterpartySearchText = name;
        HasCounterpartyError = false;
        OnPropertyChanged(nameof(HasCounterparty));
    }

    // -----------------------------------------------------------------------
    // Delegate callbacks — set by BankStatementImportModalViewModel after construction
    // -----------------------------------------------------------------------

    public Action? OpenCreateProduct { get; set; }
    public Action? OpenCreateCounterparty { get; set; }

    /// <summary>Opens the create-product modal. Used by the product dropdown's "Create one" and the
    /// editable category chip on a new-product row.</summary>
    [RelayCommand]
    private void CreateNewProduct(string? typedName) => OpenCreateProduct?.Invoke();

    [RelayCommand]
    private void CreateNewCounterparty(string? typedName) => OpenCreateCounterparty?.Invoke();

    // Object-typed wrappers for SearchableDropdown (which binds SelectedItem).

    private Product? _resolvedProductObject;
    public Product? ResolvedProductObject
    {
        get => _resolvedProductObject;
        set
        {
            if (SetProperty(ref _resolvedProductObject, value) && value != null)
            {
                ResolvedProductId = value.Id;
                IsNewProduct = false;
                NewProductName = null;
                NewProductCategoryId = null;
                NewProductCategoryName = null;
                CategoryDisplay = ProductCategoryNameLookup?.Invoke(value);
                OnPropertyChanged(nameof(HasProduct));
            }
        }
    }

    /// <summary>Set by the parent VM so the row can resolve a product's category name for the chip.</summary>
    public Func<Product, string?>? ProductCategoryNameLookup { get; set; }

    private Supplier? _resolvedSupplierObject;
    public Supplier? ResolvedSupplierObject
    {
        get => _resolvedSupplierObject;
        set
        {
            if (SetProperty(ref _resolvedSupplierObject, value) && value != null)
            {
                ResolvedCounterpartyId = value.Id;
                NewCounterpartyName = null;
                OnPropertyChanged(nameof(HasCounterparty));
            }
        }
    }

    private Customer? _resolvedCustomerObject;
    public Customer? ResolvedCustomerObject
    {
        get => _resolvedCustomerObject;
        set
        {
            if (SetProperty(ref _resolvedCustomerObject, value) && value != null)
            {
                ResolvedCounterpartyId = value.Id;
                NewCounterpartyName = null;
                OnPropertyChanged(nameof(HasCounterparty));
            }
        }
    }
}
