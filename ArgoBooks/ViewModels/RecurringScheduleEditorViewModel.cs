using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Create/edit form for one recurring schedule. Lives on the shell so its overlay covers the
/// window, and is shared by the Expenses and Revenue tabs rather than existing twice.
/// </summary>
public partial class RecurringScheduleEditorViewModel : ViewModelBase
{
    /// <summary>Raised after a save so the list that opened it can reload.</summary>
    public event Action? Saved;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private int _frequencyIndex = (int)Frequency.Monthly;

    [ObservableProperty]
    private DateTimeOffset? _startDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    /// <summary>Customers on the revenue side, suppliers on the expense side.</summary>
    public ObservableCollection<CounterpartyOption> CounterpartyOptions { get; } = [];

    [ObservableProperty]
    private CounterpartyOption? _selectedCounterparty;

    [ObservableProperty]
    private bool _hasCounterpartyError;

    [ObservableProperty]
    private string _counterpartyLabel = string.Empty;

    [ObservableProperty]
    private string _counterpartyPlaceholder = string.Empty;

    [ObservableProperty]
    private string _counterpartyAddNewText = string.Empty;

    /// <summary>Generated entries need a line item with a product, the same as one entered by hand.</summary>
    public ObservableCollection<ProductOption> ProductOptions { get; } = [];

    [ObservableProperty]
    private ProductOption? _selectedProduct;

    [ObservableProperty]
    private bool _hasProductError;

    [ObservableProperty]
    private string _productAddNewText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => ErrorMessage.Length > 0;

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    private EventHandler? _counterpartySavedHandler;
    private EventHandler? _productSavedHandler;
    private CategoryType _side = CategoryType.Expense;
    private string? _editingId;
    private EditState? _originalSnapshot;

    public void ShowNew(CategoryType side)
    {
        _side = side;
        _editingId = null;
        Title = (side == CategoryType.Revenue ? "New recurring revenue" : "New recurring expense").Translate();
        Amount = string.Empty;
        FrequencyIndex = (int)Frequency.Monthly;
        StartDate = DateTimeOffset.Now;
        EndDate = null;
        ErrorMessage = string.Empty;
        LoadCounterparties(side);
        LoadProducts(side);
        SelectedCounterparty = null;
        SelectedProduct = null;
        HasCounterpartyError = false;
        HasProductError = false;
        _originalSnapshot = Snapshot();
        IsOpen = true;
    }

    public void ShowEdit(RecurringTransaction schedule)
    {
        if (schedule.Template == null) return;

        _side = schedule.Type;
        _editingId = schedule.Id;
        Title = "Edit recurring transaction".Translate();
        Amount = schedule.Template.Total.ToString("0.##");
        FrequencyIndex = (int)schedule.Frequency;
        StartDate = new DateTimeOffset(schedule.StartDate);
        EndDate = schedule.EndDate == null ? null : new DateTimeOffset(schedule.EndDate.Value);
        ErrorMessage = string.Empty;
        LoadCounterparties(schedule.Type);
        var currentId = schedule.Type == CategoryType.Revenue
            ? (schedule.RevenueTemplate?.CustomerId)
            : (schedule.ExpenseTemplate?.SupplierId);
        SelectedCounterparty = CounterpartyOptions.FirstOrDefault(o => o.Id == currentId);
        LoadProducts(schedule.Type);
        var productId = schedule.Template?.LineItems.FirstOrDefault()?.ProductId;
        SelectedProduct = ProductOptions.FirstOrDefault(o => o.Id == productId);
        HasCounterpartyError = false;
        HasProductError = false;
        _originalSnapshot = Snapshot();
        IsOpen = true;
    }

    private void LoadCounterparties(CategoryType side)
    {
        CounterpartyOptions.Clear();
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        CounterpartyLabel = (side == CategoryType.Revenue ? "Customer" : "Supplier").Translate();
        CounterpartyPlaceholder = (side == CategoryType.Revenue
            ? "Search for a customer..."
            : "Search for a supplier...").Translate();
        CounterpartyAddNewText = (side == CategoryType.Revenue
            ? "Create new customer"
            : "Create new supplier").Translate();
        ProductAddNewText = "Create new product".Translate();

        OptionLoader.Fill(CounterpartyOptions, side == CategoryType.Revenue
            ? OptionLoader.Customers(data).AsOptions<CounterpartyOption>()
            : OptionLoader.Suppliers(data).AsOptions<CounterpartyOption>());
    }

    /// <summary>
    /// Opens the create-product modal on top of this one, on the side this schedule belongs to,
    /// and selects whatever comes back.
    /// </summary>
    [RelayCommand]
    private void CreateProduct()
    {
        var products = App.ProductModalsViewModel;
        if (products == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => products.ProductSaved += h,
            h => products.ProductSaved -= h,
            () =>
            {
                LoadProducts(_side);
                SelectedProduct = ProductOptions.FirstOrDefault(o => o.Id == products.LastSavedProductId);
                HasProductError = false;
            });
        products.OpenAddModal(_side == CategoryType.Expense);
    }

    /// <summary>
    /// Opens the create-customer or create-supplier modal on top of this one and selects whatever
    /// comes back, matching how the transaction modals offer it.
    /// </summary>
    [RelayCommand]
    private void CreateCounterparty()
    {
        if (_side == CategoryType.Revenue)
        {
            var customers = App.CustomerModalsViewModel;
            if (customers == null) return;

            CreateModalSubscription.RearmOnce(ref _counterpartySavedHandler,
                h => customers.CustomerSaved += h,
                h => customers.CustomerSaved -= h,
                () =>
                {
                    LoadCounterparties(_side);
                    SelectedCounterparty = CounterpartyOptions
                        .FirstOrDefault(o => o.Id == customers.LastSavedCustomerId);
                    HasCounterpartyError = false;
                });
            customers.OpenAddModal();
        }
        else
        {
            var suppliers = App.SupplierModalsViewModel;
            if (suppliers == null) return;

            CreateModalSubscription.RearmOnce(ref _counterpartySavedHandler,
                h => suppliers.SupplierSaved += h,
                h => suppliers.SupplierSaved -= h,
                () =>
                {
                    LoadCounterparties(_side);
                    SelectedCounterparty = CounterpartyOptions
                        .FirstOrDefault(o => o.Id == suppliers.LastSavedSupplierId);
                    HasCounterpartyError = false;
                });
            suppliers.OpenAddModal();
        }
    }

    /// <summary>
    /// Mirrors how the transaction modals filter: a product whose category belongs to the other
    /// side is not offered.
    /// </summary>
    private void LoadProducts(CategoryType side)
    {
        ProductOptions.Clear();
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        foreach (var product in data.Products.OrderBy(p => p.Name))
        {
            var category = data.Categories.FirstOrDefault(c => c.Id == product.CategoryId);
            if (category != null && category.Type != side) continue;

            ProductOptions.Add(new ProductOption
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                UnitPrice = side == CategoryType.Expense ? product.CostPrice : product.UnitPrice
            });
        }
    }

    private sealed record EditState(
        string Amount, int FrequencyIndex, DateTime? StartDate, DateTime? EndDate, string? CounterpartyId, string? ProductId);

    private EditState Snapshot() =>
        new(Amount, FrequencyIndex, StartDate?.Date, EndDate?.Date, SelectedCounterparty?.Id, SelectedProduct?.Id);

    private bool IsDirty => Snapshot() != _originalSnapshot;

    /// <summary>Backdrop click and the X both route here, so neither loses typed data silently.</summary>
    [RelayCommand]
    private async Task RequestClose()
    {
        if (IsDirty && !await ConfirmDiscardNewAsync()) return;
        IsOpen = false;
    }

    [RelayCommand]
    private async Task Save()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        if (!decimal.TryParse(Amount, out var amount) || amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.".Translate();
            return;
        }

        if (StartDate == null)
        {
            ErrorMessage = "Choose a start date.".Translate();
            return;
        }

        if (SelectedCounterparty?.Id == null)
        {
            HasCounterpartyError = true;
            ErrorMessage = "Choose a {0}.".TranslateFormat(CounterpartyLabel.ToLowerInvariant());
            return;
        }

        HasCounterpartyError = false;

        var start = StartDate.Value.DateTime.Date;
        var end = EndDate?.DateTime.Date;

        if (end != null && end < start)
        {
            ErrorMessage = "The end date is before the start date.".Translate();
            return;
        }

        var existing = _editingId == null
            ? null
            : data.RecurringTransactions.FirstOrDefault(s => s.Id == _editingId);

        if (existing == null)
        {
            var created = new RecurringTransaction
            {
                Id = new Core.Data.IdGenerator(data).NextRecurringTransactionId(),
                Type = _side,
                Frequency = (Frequency)FrequencyIndex,
                StartDate = start,
                NextDate = start,
                EndDate = end
            };
            await ApplyTemplateAsync(created, amount, start);
            data.RecurringTransactions.Add(created);

            var dateBefore = created.NextDate;
            var generated = OwnEntries(GenerateDueNow(data), created);
            var dateAfter = created.NextDate;

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Add recurring schedule {created.Id}",
                () =>
                {
                    RemoveGenerated(data, generated);
                    created.NextDate = dateBefore;
                    data.RecurringTransactions.RemoveRecord(created);
                    Saved?.Invoke();
                },
                () =>
                {
                    data.RecurringTransactions.RestoreRecord(created);
                    RestoreGenerated(data, generated);
                    created.NextDate = dateAfter;
                    Saved?.Invoke();
                }));

            IsOpen = false;
            App.CompanyManager?.MarkAsChanged();
            _ = App.TelemetryManager?.TrackFeatureAsync(
                FeatureName.RecurringScheduleCreated, $"{_side}:{(Frequency)FrequencyIndex}");
            Saved?.Invoke();
            return;
        }
        else
        {
            var before = Capture(existing);
            var statusBefore = existing.Status;
            var dateBefore = existing.NextDate;
            var lastGeneratedBefore = existing.LastGeneratedAt;
            var startChanged = existing.StartDate.Date != start;
            var oldAmount = existing.Template?.Total ?? 0m;
            var amountChanged = existing.Template != null && oldAmount != amount;

            existing.Frequency = (Frequency)FrequencyIndex;
            existing.StartDate = start;
            existing.EndDate = end;
            await ApplyTemplateAsync(existing, amount, start);

            // The next date came from the old start. Until the schedule has run it is simply the
            // start. After that, everything before the next date is already dealt with, so the new
            // start's dates pick up from there and a moved start cannot book a period twice. Whether
            // it has run is read from the schedule, not from its entries, since those can be deleted,
            // and a skipped or paused-through date moves the next date without leaving an entry.
            if (startChanged)
            {
                var hasRun = existing.LastGeneratedAt != null || dateBefore.Date > before.Start.Date;
                existing.NextDate = hasRun
                    ? RecurrenceSchedule.FirstOnOrAfter(start, existing.Frequency, start.Day, dateBefore)
                    : start;
            }

            // Generation only runs Active schedules, and resuming refuses a Completed one, so an end
            // date moved past the next occurrence would otherwise never produce it.
            if (existing.Status == RecurringTransactionStatus.Completed
                && (end == null || existing.NextDate.Date <= end.Value))
                existing.Status = RecurringTransactionStatus.Active;

            var after = Capture(existing);

            var generated = OwnEntries(GenerateDueNow(data), existing);
            var dateAfter = existing.NextDate;
            var lastGeneratedAfter = existing.LastGeneratedAt;
            var statusAfter = existing.Status;

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Edit recurring schedule {existing.Id}",
                () =>
                {
                    RemoveGenerated(data, generated);
                    Restore(existing, before);
                    existing.NextDate = dateBefore;
                    existing.LastGeneratedAt = lastGeneratedBefore;
                    existing.Status = statusBefore;
                    Saved?.Invoke();
                },
                () =>
                {
                    Restore(existing, after);
                    RestoreGenerated(data, generated);
                    existing.NextDate = dateAfter;
                    existing.LastGeneratedAt = lastGeneratedAfter;
                    existing.Status = statusAfter;
                    Saved?.Invoke();
                }));

            IsOpen = false;
            App.CompanyManager?.MarkAsChanged();
            Saved?.Invoke();

            if (amountChanged)
                await OfferRetroactiveCorrection(existing, oldAmount);
        }
    }

    /// <summary>
    /// Company open is the usual trigger, but a schedule starting today would otherwise show as
    /// due with nothing to show for it until the file was reopened.
    /// </summary>
    private static IReadOnlyList<Transaction> GenerateDueNow(Core.Data.CompanyData data)
    {
        var generated = RecurringTransactionService.GenerateDue(data, DateTime.Today);
        if (generated.Count == 0) return generated;

        var expenses = generated.Count(t => t is Expense);
        RecurringTransactionService.RaiseGenerated(expenses, generated.Count - expenses);
        return generated;
    }

    /// <summary>
    /// Generation runs every schedule that has come due, not only the one just saved. Undoing this
    /// save must not delete an entry another schedule legitimately produced.
    /// </summary>
    private static IReadOnlyList<Transaction> OwnEntries(
        IReadOnlyList<Transaction> generated, RecurringTransaction schedule) =>
        generated.Where(t => t.RecurringScheduleId == schedule.Id).ToList();

    private static void RemoveGenerated(Core.Data.CompanyData data, IReadOnlyList<Transaction> generated)
    {
        foreach (var entry in generated)
        {
            if (entry is Expense expense) data.Expenses.RemoveRecord(expense);
            else if (entry is Revenue revenue) data.Revenues.RemoveRecord(revenue);
        }

        UsdConversion.Restore(data, generated.Select(UsdConversion.KeyOf), []);
    }

    /// <summary>An entry converted before the undo keeps its USD figure, so only one still waiting queues again.</summary>
    private static void RestoreGenerated(Core.Data.CompanyData data, IReadOnlyList<Transaction> generated)
    {
        foreach (var entry in generated)
        {
            if (entry is Expense expense) data.Expenses.RestoreRecord(expense);
            else if (entry is Revenue revenue) data.Revenues.RestoreRecord(revenue);
            UsdConversion.Requeue(data, entry);
        }
    }

    /// <summary>
    /// The amount is entered in the display currency, so it is converted to USD the same way a
    /// hand-entered transaction is. Without this the entry is stored as if it were already USD,
    /// which both misstates the books and leaves the Total column showing "Pending".
    /// </summary>
    private async Task ApplyTemplateAsync(RecurringTransaction schedule, decimal amount, DateTime date)
    {
        var currency = CurrencyService.CurrentCurrencyCode;
        var rate = await UsdConversion.FetchRateAsync(currency, date);

        var description = SelectedProduct?.Name ?? string.Empty;
        var lineItems = new List<Core.Models.Common.LineItem>
        {
            new()
            {
                ProductId = SelectedProduct?.Id,
                Description = description,
                Quantity = 1,
                UnitPrice = amount
            }
        };

        if (schedule.Type == CategoryType.Revenue)
        {
            schedule.ExpenseTemplate = null;
            schedule.RevenueTemplate = new Revenue
            {
                Description = description,
                Amount = amount,
                Subtotal = amount,
                Total = amount,
                Quantity = 1,
                UnitPrice = amount,
                CustomerId = SelectedCounterparty?.Id,
                LineItems = lineItems,
                OriginalCurrency = currency
            };
        }
        else
        {
            schedule.RevenueTemplate = null;
            schedule.ExpenseTemplate = new Expense
            {
                Description = description,
                Amount = amount,
                Total = amount,
                Quantity = 1,
                UnitPrice = amount,
                SupplierId = SelectedCounterparty?.Id,
                LineItems = lineItems,
                OriginalCurrency = currency
            };
        }

        UsdConversion.SetAmounts(schedule.Template!, rate);
    }

    private static (Frequency Freq, DateTime Start, DateTime? End, Expense? Exp, Revenue? Rev) Capture(
        RecurringTransaction s) => (s.Frequency, s.StartDate, s.EndDate, s.ExpenseTemplate, s.RevenueTemplate);

    private static void Restore(
        RecurringTransaction s, (Frequency Freq, DateTime Start, DateTime? End, Expense? Exp, Revenue? Rev) state)
    {
        s.Frequency = state.Freq;
        s.StartDate = state.Start;
        s.EndDate = state.End;
        s.ExpenseTemplate = state.Exp;
        s.RevenueTemplate = state.Rev;
    }

    /// <summary>
    /// A schedule edit changes future occurrences. Ones already generated are only touched when
    /// the user says so, and never when they have been matched against a bank line or changed by
    /// hand.
    /// </summary>
    private async Task OfferRetroactiveCorrection(RecurringTransaction schedule, decimal oldAmount)
    {
        var data = App.CompanyManager?.CompanyData;
        var dialog = App.ConfirmationDialog;
        if (data == null || dialog == null) return;

        var correctable = RecurringTransactionService.FindCorrectableOccurrences(data, schedule, oldAmount);
        if (correctable.Count == 0) return;

        var earliest = correctable.Min(t => t.Date);
        var latest = correctable.Max(t => t.Date);

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Update past entries?".Translate(),
            Message = "{0} entries from {1} to {2} were generated at the old amount. Update them too? Entries matched to a bank line or changed by hand are left alone."
                .TranslateFormat(correctable.Count, earliest.ToString("MMM d, yyyy"), latest.ToString("MMM d, yyyy")),
            PrimaryButtonText = "Update them".Translate(),
            CancelButtonText = "Leave them".Translate()
        });

        if (result != ConfirmationResult.Primary) return;

        var correction = RecurringTransactionService.CorrectOccurrences(data, schedule, correctable);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Update {correctable.Count} past entries for {schedule.Id}",
            () =>
            {
                correction.Revert(data);
                Saved?.Invoke();
            },
            () =>
            {
                correction.Reapply(data);
                Saved?.Invoke();
            }));

        App.CompanyManager?.MarkAsChanged();
        Saved?.Invoke();
    }
}
