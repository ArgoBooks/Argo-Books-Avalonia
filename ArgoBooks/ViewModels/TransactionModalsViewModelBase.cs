using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Base class for transaction modals (Expense and Revenue).
/// Provides common modal state, form fields, and operations.
/// </summary>
/// <typeparam name="TDisplayItem">The display item type (ExpenseDisplayItem or RevenueDisplayItem)</typeparam>
/// <typeparam name="TLineItem">The line item type (ExpenseLineItem or RevenueLineItem)</typeparam>
public abstract partial class TransactionModalsViewModelBase<TDisplayItem, TLineItem> : ViewModelBase
    where TDisplayItem : class
    where TLineItem : TransactionLineItemBase, new()
{
    #region Abstract Properties

    /// <summary>
    /// The transaction type name (e.g., "Expense" or "Revenue").
    /// </summary>
    protected abstract string TransactionTypeName { get; }

    /// <summary>
    /// The entity name for the counterparty (e.g., "Supplier" or "Customer").
    /// </summary>
    protected abstract string CounterpartyName { get; }

    /// <summary>
    /// The category type to filter by.
    /// </summary>
    protected abstract CategoryType CategoryTypeFilter { get; }

    /// <summary>
    /// Whether to use cost price (expenses) or selling price (revenue) for products.
    /// </summary>
    protected abstract bool UseCostPrice { get; }

    /// <summary>
    /// Whether lines take a typed item name and category, creating the product and category on
    /// save when they don't exist yet, and letting a line move its product to another category.
    /// </summary>
    protected virtual bool AllowsTypedItems => false;

    #endregion

    #region Events

    public event EventHandler? TransactionSaved;
    public event EventHandler? TransactionDeleted;
    public event EventHandler? FiltersApplied;
    public event EventHandler? FiltersCleared;
    public event EventHandler? ScrollToLineItemsRequested;

    protected void RaiseTransactionSaved() => TransactionSaved?.Invoke(this, EventArgs.Empty);
    protected void RaiseTransactionDeleted() => TransactionDeleted?.Invoke(this, EventArgs.Empty);

    #endregion

    #region Modal State

    [ObservableProperty]
    private bool _isAddEditModalOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private bool _isItemStatusModalOpen;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _modalTitle = string.Empty;

    [ObservableProperty]
    private string _saveButtonText = string.Empty;

    #endregion

    #region Item Status Modal Fields

    protected TDisplayItem? ItemStatusItem;

    [ObservableProperty]
    private string _itemStatusModalTitle = "Update Item Status";

    [ObservableProperty]
    private string _itemStatusAction = string.Empty;

    [ObservableProperty]
    private string _itemStatusItemDescription = string.Empty;

    [ObservableProperty]
    private string? _selectedItemStatusReason;

    [ObservableProperty]
    private string _itemStatusNotes = string.Empty;

    [ObservableProperty]
    private bool _isUndoAction;

    [ObservableProperty]
    private string _itemStatusSaveButtonText = "Confirm";

    [ObservableProperty]
    private bool _hasItemStatusReasonError;

    [ObservableProperty]
    private string _itemStatusReasonErrorMessage = string.Empty;

    public abstract ObservableCollection<string> LostDamagedReasonOptions { get; }
    public abstract ObservableCollection<string> ReturnReasonOptions { get; }
    public abstract ObservableCollection<string> UndoReasonOptions { get; }

    [ObservableProperty]
    private ObservableCollection<string> _currentReasonOptions = [];

    #endregion

    #region Add/Edit Modal Fields

    protected string EditingTransactionId = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _modalDate = DateTimeOffset.Now;

    [ObservableProperty]
    private CounterpartyOption? _selectedCounterparty;

    // Called when SelectedCounterparty changes - allows derived classes to notify alias properties
    partial void OnSelectedCounterpartyChanged(CounterpartyOption? value)
    {
        OnCounterpartyChanged(value);
    }

    /// <summary>
    /// Virtual method called when SelectedCounterparty changes.
    /// Override in derived classes to notify alias property changes (e.g., SelectedCustomer, SelectedSupplier).
    /// </summary>
    protected virtual void OnCounterpartyChanged(CounterpartyOption? value) { }

    [ObservableProperty]
    private bool _hasCounterpartyError;

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private bool _hasCategoryError;

    [ObservableProperty]
    private string _modalDescription = string.Empty;

    [ObservableProperty]
    private bool _hasDescriptionError;

    [ObservableProperty]
    private decimal _modalQuantity = 1;

    [ObservableProperty]
    private decimal _modalUnitPrice;

    [ObservableProperty]
    private bool _hasUnitPriceError;

    [ObservableProperty]
    private decimal _modalTaxAmount;

    [ObservableProperty]
    private decimal _modalShipping;

    [ObservableProperty]
    private decimal _modalDiscount;

    [ObservableProperty]
    private decimal _modalFee;

    [ObservableProperty]
    private string _selectedPaymentMethod = "Cash";

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReceipt))]
    private string _receiptFileName = "No receipt attached";

    public bool HasReceipt => !string.IsNullOrEmpty(ReceiptFileName) && ReceiptFileName != "No receipt attached";

    [ObservableProperty]
    private bool _hasTotalMismatchWarning;

    [ObservableProperty]
    private string _totalMismatchWarningMessage = string.Empty;

    protected string? ReceiptFilePath;

    // Save error state for offline USD conversion failures
    [ObservableProperty]
    private bool _isSavingTransaction;

    [ObservableProperty]
    private bool _hasSaveError;

    [ObservableProperty]
    private string _saveErrorMessage = string.Empty;

    [RelayCommand]
    private void DismissSaveError()
    {
        HasSaveError = false;
        SaveErrorMessage = string.Empty;
    }

    // USD conversion result - stored for use by derived classes
    protected MonetaryValue? ConvertedTotal;
    protected MonetaryValue? ConvertedTaxAmount;
    protected MonetaryValue? ConvertedShippingCost;
    protected MonetaryValue? ConvertedDiscount;
    protected MonetaryValue? ConvertedFee;

    /// <summary>
    /// Set to true when saving a transaction offline without USD conversion.
    /// Passed through to the transaction model's IsPendingConversion flag.
    /// </summary>
    protected bool IsPendingConversion;

    public ObservableCollection<CounterpartyOption> CounterpartyOptions { get; } = [];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];
    public ObservableCollection<ProductOption> ProductOptions { get; } = [];
    public ObservableCollection<string> PaymentMethodOptions { get; } = new(PaymentMethodExtensions.GetCommonOptions());
    public ObservableCollection<TLineItem> LineItems { get; } = [];

    // Original values for change detection in edit mode
    private DateTimeOffset? _originalModalDate;
    private string? _originalCounterpartyId;
    private string? _originalCategoryId;
    private decimal _originalTaxAmount;
    private decimal _originalShipping;
    private decimal _originalDiscount;
    private decimal _originalFee;
    private string _originalPaymentMethod = "Cash";
    private string _originalNotes = string.Empty;
    private List<(string? ProductId, string? CategoryId, string Description, decimal? Quantity, decimal? UnitPrice, string? ItemText, string? CategoryText)> _originalLineItems = [];

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasEnteredData =>
        SelectedCounterparty != null ||
        SelectedCategory != null ||
        !string.IsNullOrWhiteSpace(ModalNotes) ||
        ModalTaxAmount > 0 ||
        ModalShipping > 0 ||
        ModalDiscount > 0 ||
        ModalFee > 0 ||
        LineItems.Any(li => li.SelectedProduct != null || li.SelectedCategory != null || !string.IsNullOrWhiteSpace(li.Description) || (li.UnitPrice ?? 0) > 0 ||
                            !string.IsNullOrWhiteSpace(li.ItemText) || !string.IsNullOrWhiteSpace(li.CategoryText));

    /// <summary>
    /// Typed text counts as a change even though it moves no selection: typing over a picked
    /// product changes only the box's text, and that text is what the save acts on.
    /// </summary>
    private static bool SameText(string? a, string? b) =>
        string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal compared to original values.
    /// </summary>
    public bool HasEditModalChanges
    {
        get
        {
            if (ModalDate != _originalModalDate) return true;
            if (SelectedCounterparty?.Id != _originalCounterpartyId) return true;
            if (SelectedCategory?.Id != _originalCategoryId) return true;
            if (ModalTaxAmount != _originalTaxAmount) return true;
            if (ModalShipping != _originalShipping) return true;
            if (ModalDiscount != _originalDiscount) return true;
            if (ModalFee != _originalFee) return true;
            if (SelectedPaymentMethod != _originalPaymentMethod) return true;
            if (ModalNotes != _originalNotes) return true;

            // Compare line items
            if (LineItems.Count != _originalLineItems.Count) return true;
            for (int i = 0; i < LineItems.Count; i++)
            {
                var current = LineItems[i];
                var original = _originalLineItems[i];
                if (current.SelectedProduct?.Id != original.ProductId ||
                    current.SelectedCategory?.Id != original.CategoryId ||
                    current.Description != original.Description ||
                    current.Quantity != original.Quantity ||
                    current.UnitPrice != original.UnitPrice ||
                    !SameText(current.ItemText, original.ItemText) ||
                    !SameText(current.CategoryText, original.CategoryText))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Captures the current form state as original values for change detection.
    /// </summary>
    protected void CaptureOriginalValues()
    {
        _originalModalDate = ModalDate;
        _originalCounterpartyId = SelectedCounterparty?.Id;
        _originalCategoryId = SelectedCategory?.Id;
        _originalTaxAmount = ModalTaxAmount;
        _originalShipping = ModalShipping;
        _originalDiscount = ModalDiscount;
        _originalFee = ModalFee;
        _originalPaymentMethod = SelectedPaymentMethod;
        _originalNotes = ModalNotes;
        _originalLineItems = LineItems.Select(li => (li.SelectedProduct?.Id, li.SelectedCategory?.Id, li.Description, li.Quantity, li.UnitPrice, li.ItemText, li.CategoryText)).ToList();
    }

    // Computed totals
    public decimal Subtotal => LineItems.Count > 0
        ? LineItems.Sum(li => li.Amount)
        : ModalQuantity * ModalUnitPrice;
    public decimal TaxAmount => ModalTaxAmount;
    public decimal DiscountAmount => ModalDiscount;
    public decimal ShippingAmount => ModalShipping;
    public decimal FeeAmount => ModalFee;
    public decimal Total => Subtotal + TaxAmount + ShippingAmount + FeeAmount - DiscountAmount;

    /// <summary>
    /// The currency of the entry being edited when it isn't the company's, otherwise null. An entry
    /// keeps its own currency, so its amounts load and save as recorded.
    /// </summary>
    private string? _entryCurrency;

    /// <summary>The currency the form's amounts are in.</summary>
    protected string FormCurrencyCode => _entryCurrency ?? CurrencyService.CurrentCurrencyCode;

    /// <summary>
    /// Follows the tax label. Another currency is named by its code, since several share a symbol.
    /// </summary>
    public string TaxCurrencyLabel => $" ({_entryCurrency ?? CurrencyService.CurrentSymbol})";

    private void SetEntryCurrency(string? currency)
    {
        _entryCurrency = currency;
        OnPropertyChanged(nameof(TaxCurrencyLabel));
    }

    private string FormatAmount(decimal amount) =>
        _entryCurrency == null ? CurrencyService.Format(amount) : CurrencyInfo.GetByCode(_entryCurrency).Format(amount);

    public string SubtotalFormatted => FormatAmount(Subtotal);
    public string TaxAmountFormatted => FormatAmount(TaxAmount);
    // Only show the leading "-" (and the green "savings" colour, via HasDiscount) when there's an
    // actual discount; a zero discount reads as a plain neutral amount like the other lines.
    public string DiscountAmountFormatted => DiscountAmount > 0
        ? $"-{FormatAmount(DiscountAmount)}"
        : FormatAmount(DiscountAmount);

    /// <summary>True when a discount is applied, used to colour the discount amount green.</summary>
    public bool HasDiscount => DiscountAmount > 0;
    public string ShippingAmountFormatted => FormatAmount(ShippingAmount);
    public string FeeAmountFormatted => FormatAmount(FeeAmount);
    public string TotalFormatted => _entryCurrency == null
        ? CurrencyService.Format(Total)
        : CurrencyInfo.GetByCode(_entryCurrency).Format(Total, includeCode: true);

    partial void OnModalQuantityChanged(decimal value) => UpdateTotals();
    partial void OnModalUnitPriceChanged(decimal value) => UpdateTotals();
    partial void OnModalTaxAmountChanged(decimal value) => UpdateTotals();
    partial void OnModalShippingChanged(decimal value) => UpdateTotals();
    partial void OnModalDiscountChanged(decimal value) => UpdateTotals();
    partial void OnModalFeeChanged(decimal value) => UpdateTotals();

    private void OnLineItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A picked product brings its category into the line's category box.
        if (e.PropertyName == nameof(TransactionLineItemBase.SelectedProduct) &&
            sender is TLineItem { SelectedProduct: { } product } lineItem)
            lineItem.SelectedCategory = CategoryOptionFor(product);

        UpdateTotals();
    }

    protected void UpdateTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(ShippingAmount));
        OnPropertyChanged(nameof(FeeAmount));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(SubtotalFormatted));
        OnPropertyChanged(nameof(TaxAmountFormatted));
        OnPropertyChanged(nameof(DiscountAmountFormatted));
        OnPropertyChanged(nameof(HasDiscount));
        OnPropertyChanged(nameof(ShippingAmountFormatted));
        OnPropertyChanged(nameof(FeeAmountFormatted));
        OnPropertyChanged(nameof(TotalFormatted));

        // The stored-total mismatch is a load-time data-integrity check (it flags AI-scanned or
        // imported transactions whose stored total didn't match their line items - see the call in
        // the edit-load path). Once the user edits, they are defining the values themselves, so
        // re-checking the new total against the now-stale stored total just produces a false warning
        // (e.g. changing an expense from $10 to $100 warned that $100 != the stored $10). Clear it.
        HasTotalMismatchWarning = false;
        TotalMismatchWarningMessage = string.Empty;
    }

    /// <summary>
    /// Compares the recomputed total against the stored transaction total
    /// and shows a warning if they don't match (e.g. from AI receipt scan data).
    /// </summary>
    protected void ValidateTotalMismatch(decimal storedTotal)
    {
        HasTotalMismatchWarning = false;
        TotalMismatchWarningMessage = string.Empty;

        if (storedTotal == 0) return;

        var diff = Math.Abs(Total - storedTotal);
        if (diff > 0.02m)
        {
            HasTotalMismatchWarning = true;
            TotalMismatchWarningMessage = string.Format(
                "Line items ({0}) + tax ({1}) + shipping ({2}) + fee ({3}) - discount ({4}) = {5}, but the stored total is {6}. Some values may be incorrect.".Translate(),
                FormatAmount(Subtotal),
                FormatAmount(TaxAmount),
                FormatAmount(ShippingAmount),
                FormatAmount(FeeAmount),
                FormatAmount(DiscountAmount),
                FormatAmount(Total),
                FormatAmount(storedTotal));
        }
    }

    #endregion

    #region Delete Confirmation

    [ObservableProperty]
    private string _deleteTransactionId = string.Empty;

    [ObservableProperty]
    private string _deleteTransactionDescription = string.Empty;

    [ObservableProperty]
    private string _deleteTransactionAmount = string.Empty;

    #endregion

    #region Filter Modal Fields

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private CounterpartyOption? _filterSelectedCounterparty;

    [ObservableProperty]
    private string? _filterCounterpartyId;

    [ObservableProperty]
    private CategoryOption? _filterSelectedCategory;

    [ObservableProperty]
    private string? _filterCategoryId;

    [ObservableProperty]
    private string? _filterAmountMin;

    [ObservableProperty]
    private string? _filterAmountMax;

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    // Original filter values for change detection (captured when modal opens)
    private string _originalFilterStatus = "All";
    private string? _originalFilterCounterpartyId;
    private string? _originalFilterCategoryId;
    private string? _originalFilterAmountMin;
    private string? _originalFilterAmountMax;
    private DateTimeOffset? _originalFilterDateFrom;
    private DateTimeOffset? _originalFilterDateTo;

    public ObservableCollection<string> StatusFilterOptions { get; } = new(TransactionStatusExtensions.GetFilterOptions());

    #endregion

    #region Constructor

    protected TransactionModalsViewModelBase()
    {
        LoadCounterpartyOptions();
        LoadCategoryOptions();
        LoadProductOptions();
    }

    #endregion

    #region Data Loading

    protected abstract void LoadCounterpartyOptions();

    protected void LoadCategoryOptions()
    {
        CategoryOptions.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Categories == null)
            return;

        var categories = companyData.Categories
            .Where(c => c.Type == CategoryTypeFilter)
            .OrderBy(c => c.Name);

        foreach (var category in categories)
        {
            CategoryOptions.Add(new CategoryOption { Id = category.Id, Name = category.Name });
        }
    }

    protected void LoadProductOptions()
    {
        ProductOptions.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null)
            return;

        // Filter products based on category type
        foreach (var product in companyData.Products.OrderBy(p => p.Name))
        {
            var category = companyData.Categories?.FirstOrDefault(c => c.Id == product.CategoryId);

            // Skip products that don't match our category type
            if (CategoryTypeFilter == CategoryType.Expense && category?.Type == CategoryType.Revenue)
                continue;
            if (CategoryTypeFilter == CategoryType.Revenue && category?.Type == CategoryType.Expense)
                continue;

            ProductOptions.Add(new ProductOption
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                UnitPrice = UseCostPrice ? product.CostPrice : product.UnitPrice,
                CategoryId = product.CategoryId
            });
        }
    }

    protected void LoadCounterpartyOptionsForFilter()
    {
        CounterpartyOptions.Clear();
        CounterpartyOptions.Add(new CounterpartyOption { Id = null, Name = $"All {CounterpartyName}s" });

        LoadCounterpartyOptionsInternal();
    }

    protected abstract void LoadCounterpartyOptionsInternal();

    protected void LoadCategoryOptionsForFilter()
    {
        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryOption { Id = null, Name = "All Categories" });

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Categories == null)
            return;

        var categories = companyData.Categories
            .Where(c => c.Type == CategoryTypeFilter)
            .OrderBy(c => c.Name);

        foreach (var category in categories)
        {
            CategoryOptions.Add(new CategoryOption { Id = category.Id, Name = category.Name });
        }
    }

    #endregion

    #region Add Modal

    [RelayCommand]
    public void OpenAddModal()
    {
        _ = App.TelemetryManager?.TrackFeatureAsync(
            CategoryTypeFilter == CategoryType.Expense
                ? FeatureName.ExpenseCreateOpened
                : FeatureName.RevenueCreateOpened);

        LoadCounterpartyOptions();
        LoadCategoryOptions();
        LoadProductOptions();
        ResetForm();
        IsEditMode = false;
        ModalTitle = $"Add {TransactionTypeName}";
        SaveButtonText = $"Add {TransactionTypeName}";
        IsAddEditModalOpen = true;
    }

    #endregion

    #region Edit Modal

    public abstract void OpenEditModal(TDisplayItem? item);

    protected void PopulateFormFromTransaction(Transaction transaction)
    {
        ModalDate = new DateTimeOffset(transaction.Date);
        var productId = transaction.LineItems.FirstOrDefault()?.ProductId;
        var product = productId != null ? App.CompanyManager?.CompanyData?.GetProduct(productId) : null;
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == product?.CategoryId);

        // Amounts load as recorded, in the entry's own currency. Converting them into the company
        // currency made saving an untouched EUR 100 entry relabel it as a company-currency 125.
        var txCurrency = string.IsNullOrEmpty(transaction.OriginalCurrency) ? "USD" : transaction.OriginalCurrency.ToUpperInvariant();
        SetEntryCurrency(string.Equals(txCurrency, CurrencyService.CurrentCurrencyCode, StringComparison.OrdinalIgnoreCase) ? null : txCurrency);

        ModalTaxAmount = transaction.TaxAmount;
        ModalShipping = transaction.ShippingCost;
        ModalDiscount = transaction.Discount;
        ModalFee = transaction.Fee;

        SelectedPaymentMethod = transaction.PaymentMethod.ToString();
        ModalNotes = transaction.Notes;

        LineItems.Clear();
        if (transaction.LineItems.Count > 0)
        {
            foreach (var li in transaction.LineItems)
            {
                var selectedProduct = ProductOptions.FirstOrDefault(p => p.Id == li.ProductId) ?? StoredProductOption(li.ProductId);
                var lineItem = new TLineItem
                {
                    CurrencyCode = _entryCurrency,
                    SelectedProduct = selectedProduct,
                    SelectedCategory = CategoryOptionFor(selectedProduct),
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice
                };
                // Fill each box's text along with its selection, so what the box shows never depends
                // on which of its two bindings the dropdown applies first.
                lineItem.ItemText = selectedProduct?.Name ?? li.Description;
                lineItem.CategoryText = lineItem.SelectedCategory?.Name;
                lineItem.PropertyChanged += OnLineItemPropertyChanged;
                LineItems.Add(lineItem);
            }
        }
        else
        {
            // Fallback for old data without line items
            var lineItem = new TLineItem
            {
                CurrencyCode = _entryCurrency,
                Description = transaction.Description,
                ItemText = transaction.Description,
                Quantity = transaction.Quantity,
                UnitPrice = transaction.UnitPrice
            };
            lineItem.PropertyChanged += OnLineItemPropertyChanged;
            LineItems.Add(lineItem);
        }
        UpdateTotals();

        // Load receipt info from the Receipt record (not ReferenceNumber, which stores general references like PO numbers)
        if (!string.IsNullOrEmpty(transaction.ReceiptId))
        {
            var receipt = App.CompanyManager?.CompanyData?.Receipts.FirstOrDefault(r => r.Id == transaction.ReceiptId);
            if (receipt != null)
            {
                ReceiptFilePath = receipt.OriginalFilePath;
                ReceiptFileName = receipt.FileName;
            }
            else
            {
                ReceiptFilePath = null;
                ReceiptFileName = "No receipt attached";
            }
        }
        else
        {
            ReceiptFilePath = null;
            ReceiptFileName = "No receipt attached";
        }

        ClearValidationErrors();

        // Check if stored total matches recomputed total (catches AI receipt scan mismatches)
        ValidateTotalMismatch(transaction.Total);

        // Capture original values for change detection
        CaptureOriginalValues();
    }

    #endregion

    #region Delete Confirmation

    [RelayCommand]
    protected void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        DeleteTransactionId = string.Empty;
        DeleteTransactionDescription = string.Empty;
        DeleteTransactionAmount = string.Empty;
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Returns true if any filter has been changed from the state when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterStatus != _originalFilterStatus ||
        FilterSelectedCounterparty?.Id != _originalFilterCounterpartyId ||
        FilterSelectedCategory?.Id != _originalFilterCategoryId ||
        FilterAmountMin != _originalFilterAmountMin ||
        FilterAmountMax != _originalFilterAmountMax ||
        FilterDateFrom != _originalFilterDateFrom ||
        FilterDateTo != _originalFilterDateTo;

    /// <summary>
    /// Captures the current filter state as original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterStatus = FilterStatus;
        _originalFilterCounterpartyId = FilterSelectedCounterparty?.Id;
        _originalFilterCategoryId = FilterSelectedCategory?.Id;
        _originalFilterAmountMin = FilterAmountMin;
        _originalFilterAmountMax = FilterAmountMax;
        _originalFilterDateFrom = FilterDateFrom;
        _originalFilterDateTo = FilterDateTo;
    }

    private void ResetFilterDefaults()
    {
        FilterStatus = "All";
        FilterSelectedCounterparty = null;
        FilterSelectedCategory = null;
        FilterAmountMin = null;
        FilterAmountMax = null;
        FilterDateFrom = null;
        FilterDateTo = null;
    }

    public void OpenFilterModal()
    {
        LoadCounterpartyOptionsForFilter();
        LoadCategoryOptionsForFilter();
        CaptureOriginalFilterValues();
        IsFilterModalOpen = true;
    }

    [RelayCommand]
    protected void CloseFilterModal()
    {
        IsFilterModalOpen = false;
    }

    [RelayCommand]
    protected async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync()) return;

            // Reset filter values to the state when modal was opened
            FilterStatus = _originalFilterStatus;
            FilterSelectedCounterparty = CounterpartyOptions.FirstOrDefault(c => c.Id == _originalFilterCounterpartyId);
            FilterSelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == _originalFilterCategoryId);
            FilterAmountMin = _originalFilterAmountMin;
            FilterAmountMax = _originalFilterAmountMax;
            FilterDateFrom = _originalFilterDateFrom;
            FilterDateTo = _originalFilterDateTo;
        }

        CloseFilterModal();
    }

    [RelayCommand]
    protected void ApplyFilters()
    {
        FilterCounterpartyId = FilterSelectedCounterparty?.Id;
        FilterCategoryId = FilterSelectedCategory?.Id;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    protected virtual void ClearFilters()
    {
        ResetFilterDefaults();
        FilterCounterpartyId = null;
        FilterCategoryId = null;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region Item Status Modal

    protected void OpenItemStatusModal(TDisplayItem? item, string action, string title, string buttonText, bool isUndo, ObservableCollection<string> reasonOptions)
    {
        if (item == null) return;

        ItemStatusItem = item;
        ItemStatusAction = action;
        ItemStatusModalTitle = title;
        ItemStatusItemDescription = GetItemStatusDescription(item);
        ItemStatusSaveButtonText = buttonText;
        IsUndoAction = isUndo;
        CurrentReasonOptions = new ObservableCollection<string>(reasonOptions);
        SelectedItemStatusReason = null;
        ItemStatusNotes = string.Empty;
        IsItemStatusModalOpen = true;
    }

    protected abstract string GetItemStatusDescription(TDisplayItem item);

    [RelayCommand]
    protected void CloseItemStatusModal()
    {
        IsItemStatusModalOpen = false;
        ItemStatusItem = null;
        ItemStatusAction = string.Empty;
        SelectedItemStatusReason = null;
        ItemStatusNotes = string.Empty;
        HasItemStatusReasonError = false;
        ItemStatusReasonErrorMessage = string.Empty;
    }

    [RelayCommand]
    protected abstract void ConfirmItemStatus();

    protected static LostDamagedReason MapToLostDamagedReason(string reason)
    {
        return reason.ToLowerInvariant() switch
        {
            "damaged in transit" or "damaged during storage" or "defective product" or "customer damaged" => LostDamagedReason.Damaged,
            "lost in warehouse" => LostDamagedReason.Lost,
            "expired" => LostDamagedReason.Expired,
            _ => LostDamagedReason.Other
        };
    }

    #endregion

    #region Save Transaction

    [RelayCommand]
    protected void CloseAddEditModal()
    {
        IsAddEditModalOpen = false;
        ResetForm();
    }

    /// <summary>
    /// Requests to close the Add/Edit modal, showing confirmation if data was entered (Add mode) or changed (Edit mode).
    /// </summary>
    [RelayCommand]
    protected async Task RequestCloseAddEditModalAsync()
    {
        // In edit mode, check if changes were made; in add mode, check if data was entered
        var hasUnsavedWork = IsEditMode ? HasEditModalChanges : HasEnteredData;

        if (hasUnsavedWork)
        {
            var confirmed = IsEditMode
                ? await ConfirmDiscardEditsAsync()
                : await ConfirmDiscardNewAsync();

            if (!confirmed) return;
        }

        CloseAddEditModal();
    }

    [RelayCommand]
    protected async Task SaveTransactionAsync()
    {
        ClearValidationErrors();
        HasSaveError = false;
        SaveErrorMessage = string.Empty;

        if (LineItems.Count == 0)
        {
            ReportValidationBlock("no-line-items");
            ValidationMessage = "Please add at least one line item.".Translate();
            HasValidationMessage = true;
            return;
        }

        // Every line needs a product. Where lines take typed items, a name that matches no product is
        // created as one on save (ResolveTypedItems), so here it only needs a category to go in.
        var hasProductErrors = false;
        var hasCategoryErrors = false;
        foreach (var lineItem in LineItems)
        {
            if (!AllowsTypedItems)
            {
                if (lineItem.SelectedProduct == null)
                {
                    lineItem.HasProductError = true;
                    hasProductErrors = true;
                }
                continue;
            }

            if (FindExistingProduct(lineItem) != null) continue;

            if (string.IsNullOrWhiteSpace(lineItem.ItemText))
            {
                lineItem.HasProductError = true;
                hasProductErrors = true;
            }
            if (lineItem.SelectedCategory == null && string.IsNullOrWhiteSpace(lineItem.CategoryText))
            {
                lineItem.HasCategoryError = true;
                hasCategoryErrors = true;
            }
        }

        if (hasProductErrors || hasCategoryErrors)
        {
            ReportValidationBlock(hasProductErrors ? "line-item-missing-product" : "line-item-missing-category");
            ValidationMessage = AllowsTypedItems
                ? "Each line needs an item and a category".Translate()
                : "Please select a product for all line items".Translate();
            HasValidationMessage = true;
            ScrollToLineItemsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        List<TypedLine>? typedLines = null;
        if (AllowsTypedItems)
        {
            typedLines = PlanTypedItems(companyData, out var typedProblem);
            if (typedLines == null)
            {
                ReportValidationBlock("line-item-typed-conflict");
                ValidationMessage = typedProblem;
                HasValidationMessage = true;
                ScrollToLineItemsRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        IsSavingTransaction = true;
        try
        {
            // Yield to let the UI render the loading indicator
            await Task.Delay(1);

            // Perform USD conversion if currency is not USD
            var currentCurrency = FormCurrencyCode;
            var transactionDate = ModalDate?.DateTime ?? DateTime.Now;

            IsPendingConversion = false;

            if (!string.Equals(currentCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var exchangeService = ExchangeRateService.Instance;
                    if (exchangeService == null)
                    {
                        HasSaveError = true;
                        ReportValidationBlock("no-exchange-rate-service");
                        SaveErrorMessage = "Exchange rate service is not available. Please restart the application.".Translate();
                        return;
                    }

                    // Try to get rate (uses cache first, then fetches from API if needed)
                    var rate = await exchangeService.GetExchangeRateAsync(currentCurrency, "USD", transactionDate, fetchIfMissing: true);
                    if (rate > 0)
                    {
                        ConvertedTotal = await CurrencyService.CreateMonetaryValueAsync(Total, currentCurrency, transactionDate);
                        ConvertedTaxAmount = await CurrencyService.CreateMonetaryValueAsync(TaxAmount, currentCurrency, transactionDate);
                        ConvertedShippingCost = await CurrencyService.CreateMonetaryValueAsync(ShippingAmount, currentCurrency, transactionDate);
                        ConvertedDiscount = await CurrencyService.CreateMonetaryValueAsync(DiscountAmount, currentCurrency, transactionDate);
                        ConvertedFee = await CurrencyService.CreateMonetaryValueAsync(FeeAmount, currentCurrency, transactionDate);
                    }
                    else
                    {
                        // Rate unavailable, save with pending conversion for later
                        IsPendingConversion = true;
                        ConvertedTotal = new MonetaryValue(Total, currentCurrency, 0, transactionDate);
                        ConvertedTaxAmount = new MonetaryValue(TaxAmount, currentCurrency, 0, transactionDate);
                        ConvertedShippingCost = new MonetaryValue(ShippingAmount, currentCurrency, 0, transactionDate);
                        ConvertedDiscount = new MonetaryValue(DiscountAmount, currentCurrency, 0, transactionDate);
                        ConvertedFee = new MonetaryValue(FeeAmount, currentCurrency, 0, transactionDate);
                    }
                }
                catch (Exception)
                {
                    // Network or other error, save with pending conversion
                    IsPendingConversion = true;
                    ConvertedTotal = new MonetaryValue(Total, currentCurrency, 0, transactionDate);
                    ConvertedTaxAmount = new MonetaryValue(TaxAmount, currentCurrency, 0, transactionDate);
                    ConvertedShippingCost = new MonetaryValue(ShippingAmount, currentCurrency, 0, transactionDate);
                    ConvertedDiscount = new MonetaryValue(DiscountAmount, currentCurrency, 0, transactionDate);
                    ConvertedFee = new MonetaryValue(FeeAmount, currentCurrency, 0, transactionDate);
                }
            }
            else
            {
                // USD currency - no conversion needed
                ConvertedTotal = new MonetaryValue(Total, "USD", Total, transactionDate);
                ConvertedTaxAmount = new MonetaryValue(TaxAmount, "USD", TaxAmount, transactionDate);
                ConvertedShippingCost = new MonetaryValue(ShippingAmount, "USD", ShippingAmount, transactionDate);
                ConvertedDiscount = new MonetaryValue(DiscountAmount, "USD", DiscountAmount, transactionDate);
                ConvertedFee = new MonetaryValue(FeeAmount, "USD", FeeAmount, transactionDate);
            }

            if (typedLines != null)
                ResolveTypedItems(companyData, typedLines);

            if (IsEditMode)
            {
                SaveEditedTransaction(companyData);
            }
            else
            {
                SaveNewTransaction(companyData);
            }

            // Capture pending state before CloseAddEditModal resets it via ResetForm
            var wasPendingConversion = IsPendingConversion;

            CloseAddEditModal();

            // Show pending conversion warning after modal closes
            if (wasPendingConversion)
            {
                string message;
                if (transactionDate.Date > DateTime.Today)
                {
                    message = "The transaction date is in the future, so the exchange rate is not yet available. Your transaction has been saved and the converted amount will be updated automatically when the rate becomes available.".Translate();
                }
                else if (!await new ConnectivityService().IsInternetAvailableAsync())
                {
                    message = "You are currently offline. Your transaction has been saved and the converted amount will be updated automatically when you reconnect.".Translate();
                }
                else
                {
                    message = "Your transaction has been saved. The converted amount will be updated automatically when the exchange rate becomes available.".Translate();
                }
                _ = App.ShowWarningMessageBoxAsync(
                    "Pending Conversion".Translate(),
                    message);
            }
        }
        catch (Exception)
        {
            HasSaveError = true;
            SaveErrorMessage = "An unexpected error occurred while saving. Please try again.".Translate();
        }
        finally
        {
            IsSavingTransaction = false;
        }
    }

    [RelayCommand]
    protected async Task RetrySaveAsync()
    {
        await SaveTransactionAsync();
    }

    protected abstract void SaveNewTransaction(CompanyData companyData);
    protected abstract void SaveEditedTransaction(CompanyData companyData);

    protected List<LineItem> CreateModelLineItems()
    {
        return LineItems.Select(li => new LineItem
        {
            ProductId = li.SelectedProduct?.Id,
            Description = li.Description,
            Quantity = li.Quantity ?? 0,
            UnitPrice = li.UnitPrice ?? 0,
            TaxRate = 0,
            Discount = 0
        }).ToList();
    }

    private CategoryOption? FindCategoryOption(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : CategoryOptions.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private ProductOption? FindProductOption(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : ProductOptions.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private CategoryOption? CategoryOptionFor(ProductOption? product) =>
        product?.CategoryId == null ? null : CategoryOptions.FirstOrDefault(c => c.Id == product.CategoryId);

    /// <summary>
    /// A product a saved line points at that this form does not list: a phone capture matches
    /// products by name on either side, so an expense line can hold a revenue product. Kept as the
    /// line's product, or its name reads as a new item clashing with that same product.
    /// </summary>
    private ProductOption? StoredProductOption(string? productId)
    {
        if (string.IsNullOrEmpty(productId) || App.CompanyManager?.CompanyData?.GetProduct(productId) is not { } product)
            return null;

        return new ProductOption
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            UnitPrice = UseCostPrice ? product.CostPrice : product.UnitPrice,
            CategoryId = product.CategoryId
        };
    }

    /// <summary>
    /// The existing product a line refers to: the picked one while the item box still shows its
    /// name, otherwise the product whose name was typed, if there is one.
    /// </summary>
    private ProductOption? FindExistingProduct(TLineItem lineItem)
    {
        var text = lineItem.ItemText?.Trim();
        if (lineItem.SelectedProduct != null &&
            (string.IsNullOrEmpty(text) || string.Equals(text, lineItem.SelectedProduct.Name, StringComparison.OrdinalIgnoreCase)))
            return lineItem.SelectedProduct;
        return FindProductOption(text);
    }

    /// <summary>What one line's boxes name, worked out before anything is created or moved.</summary>
    private sealed class TypedLine
    {
        public required TLineItem Line { get; init; }

        /// <summary>The existing product, or null when <see cref="NewProductName"/> is to be created.</summary>
        public ProductOption? Product { get; init; }

        public string? NewProductName { get; init; }

        /// <summary>The existing category, or null when <see cref="NewCategoryName"/> is to be created or the box is empty.</summary>
        public CategoryOption? Category { get; init; }

        public string? NewCategoryName { get; init; }

        /// <summary>Whether the existing <see cref="Product"/> moves into the line's category.</summary>
        public bool MovesProduct { get; init; }
    }

    /// <summary>
    /// Works out what each line's item and category boxes name, and refuses a save that would
    /// contradict itself, before anything is created or moved. The boxes' text decides, not their
    /// selections, because typing over a pick changes only the text. Clearing a selection is no
    /// way round that: the dropdown empties its text when its selection is cleared.
    /// </summary>
    /// <returns>The plan, or null with <paramref name="problem"/> saying what to fix.</returns>
    private List<TypedLine>? PlanTypedItems(CompanyData companyData, out string problem)
    {
        problem = string.Empty;
        var plan = new List<TypedLine>();

        // Where each product starts the save. Every line is compared with this rather than with a
        // move an earlier line asked for.
        string? StartingCategory(ProductOption option) =>
            companyData.GetProduct(option.Id ?? string.Empty)?.CategoryId ?? option.CategoryId;

        foreach (var lineItem in LineItems)
        {
            var categoryText = lineItem.CategoryText?.Trim();
            var category = lineItem.SelectedCategory;
            string? newCategoryName = null;
            if (!string.IsNullOrEmpty(categoryText) &&
                !string.Equals(categoryText, lineItem.SelectedCategory?.Name, StringComparison.OrdinalIgnoreCase))
            {
                category = FindCategoryOption(categoryText);
                if (category == null)
                    newCategoryName = categoryText;
            }

            var product = FindExistingProduct(lineItem);
            if (product == null)
            {
                var name = lineItem.ItemText?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                // Product names are unique across expenses and revenue, and this form lists only
                // its own side, so a name from the other side matched nothing above.
                if (companyData.Products.Any(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                {
                    lineItem.HasProductError = true;
                    problem = "There is already a product called {0}. Give this item a different name.".TranslateFormat(name);
                    return null;
                }

                plan.Add(new TypedLine { Line = lineItem, NewProductName = name, Category = category, NewCategoryName = newCategoryName });
                continue;
            }

            // Typing an existing product's name over a picked one leaves the category box showing
            // the pick's category. That was never a choice about the typed product, so it stays put.
            var boxShowsPick = lineItem.SelectedProduct != null && lineItem.SelectedProduct != product &&
                               newCategoryName == null && category?.Id == StartingCategory(lineItem.SelectedProduct);

            var moves = !boxShowsPick &&
                        (newCategoryName != null || (category?.Id != null && category.Id != StartingCategory(product)));

            plan.Add(new TypedLine { Line = lineItem, Product = product, Category = category, NewCategoryName = newCategoryName, MovesProduct = moves });
        }

        // A product is in one category, so two lines sending it to different ones, or one new name
        // typed into two categories, cannot both be done. Refused rather than letting a line win.
        foreach (var group in plan.GroupBy(t => t.Product != null ? "id:" + t.Product.Id : "new:" + t.NewProductName!.ToUpperInvariant()))
        {
            var destinations = group
                .Where(t => t.Product == null || t.MovesProduct)
                .Select(t => t.NewCategoryName != null ? "new:" + t.NewCategoryName.ToUpperInvariant() : t.Category?.Id)
                .Distinct()
                .Count();
            if (destinations <= 1) continue;

            foreach (var typed in group)
                typed.Line.HasCategoryError = true;
            problem = "{0} has a different category on another line. An item can only be in one category."
                .TranslateFormat(group.First().Product?.Name ?? group.First().NewProductName ?? string.Empty);
            return null;
        }

        return plan;
    }

    private CategoryOption CreateCategory(CompanyData companyData, string name, List<Category> created)
    {
        companyData.IdCounters.Category++;
        var typePrefix = CategoryTypeFilter == CategoryType.Expense ? "PUR" : "SAL";
        var category = new Category
        {
            Id = $"CAT-{typePrefix}-{companyData.IdCounters.Category:D3}",
            Name = name,
            Type = CategoryTypeFilter
        };
        companyData.Categories.Add(category);
        created.Add(category);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.CategoryCreated);

        var option = new CategoryOption { Id = category.Id, Name = category.Name };
        CategoryOptions.Add(option);
        return option;
    }

    private ProductOption CreateProduct(CompanyData companyData, string name, decimal price, CategoryOption? category, List<Product> created)
    {
        var id = new Core.Data.IdGenerator(companyData).NextProductId();
        var product = new Product
        {
            Id = id,
            Name = name,
            Sku = id,
            CategoryId = category?.Id,
            Type = CategoryTypeFilter,
            CostPrice = UseCostPrice ? price : 0,
            UnitPrice = UseCostPrice ? 0 : price
        };
        companyData.Products.Add(product);
        created.Add(product);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ProductCreated);

        var option = new ProductOption { Id = id, Name = name, UnitPrice = price, CategoryId = category?.Id };
        ProductOptions.Add(option);
        return option;
    }

    /// <summary>
    /// Carries out a plan from <see cref="PlanTypedItems"/>: the new categories and products, and
    /// the moves. Everything created or changed here is one undo step, ahead of the transaction's.
    /// </summary>
    private void ResolveTypedItems(CompanyData companyData, List<TypedLine> plan)
    {
        var createdCategories = new List<Category>();
        var createdProducts = new List<Product>();
        var moved = new List<(Product Product, string? OldCategoryId, string NewCategoryId)>();

        foreach (var typed in plan)
        {
            var category = typed.NewCategoryName == null
                ? typed.Category
                : FindCategoryOption(typed.NewCategoryName) ?? CreateCategory(companyData, typed.NewCategoryName, createdCategories);

            var product = typed.Product
                          ?? FindProductOption(typed.NewProductName)
                          ?? CreateProduct(companyData, typed.NewProductName!, typed.Line.UnitPrice ?? 0, category, createdProducts);

            // Categories belong to products, so a move takes every transaction using the product
            // along. Recorded once, from where the product started, so undo puts it back there.
            if (typed.MovesProduct && category?.Id != null && product.CategoryId != category.Id &&
                companyData.GetProduct(product.Id ?? string.Empty) is { } record)
            {
                moved.Add((record, record.CategoryId, category.Id));
                record.CategoryId = category.Id;
                product.CategoryId = category.Id;
            }

            if (typed.Line.SelectedProduct != product)
            {
                // Setting the product fills an empty price from it. The total was converted from
                // the line as it stood, so the line keeps the price it was saved with.
                var price = typed.Line.UnitPrice;
                typed.Line.SelectedProduct = product;
                typed.Line.UnitPrice = price;
            }
        }

        if (createdCategories.Count == 0 && createdProducts.Count == 0 && moved.Count == 0) return;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Update products and categories from {TransactionTypeName.ToLowerInvariant()}",
            () =>
            {
                foreach (var m in moved) m.Product.CategoryId = m.OldCategoryId;
                foreach (var p in createdProducts) companyData.Products.Remove(p);
                foreach (var c in createdCategories) companyData.Categories.Remove(c);
                companyData.MarkAsModified();
            },
            () =>
            {
                companyData.Categories.AddRange(createdCategories);
                companyData.Products.AddRange(createdProducts);
                foreach (var m in moved) m.Product.CategoryId = m.NewCategoryId;
                companyData.MarkAsModified();
            }));
    }

    /// <summary>
    /// Replaces the row's conversion queue entries in the company file and in the conversion
    /// service's copy, which converts from its own entries rather than from the row.
    /// </summary>
    protected static void SetQueuedConversions(CompanyData companyData, string transactionId, List<PendingConversion> entries)
    {
        companyData.PendingConversions.RemoveAll(p => p.TransactionId == transactionId);
        companyData.PendingConversions.AddRange(entries);
        _ = PendingConversionService.Instance?.MirrorAsync(companyData, [transactionId]);
    }

    protected (string description, decimal totalQuantity, decimal averageUnitPrice) GetLineItemSummary()
    {
        var description = LineItems.Count == 1
            ? LineItems[0].Description
            : string.Join(", ", LineItems.Select(li => li.Description).Where(d => !string.IsNullOrEmpty(d)));
        var totalQuantity = LineItems.Sum(li => li.Quantity ?? 0);
        var averageUnitPrice = LineItems.Count > 0 ? LineItems.Average(li => li.UnitPrice ?? 0) : 0;
        return (description, totalQuantity, averageUnitPrice);
    }

    protected void ResetForm()
    {
        EditingTransactionId = string.Empty;
        SetEntryCurrency(null);
        ModalDate = DateTimeOffset.Now;
        SelectedCounterparty = null;
        SelectedCategory = null;
        ModalDescription = string.Empty;
        ModalQuantity = 1;
        ModalUnitPrice = 0;
        ModalTaxAmount = 0;
        ModalShipping = 0;
        ModalDiscount = 0;
        ModalFee = 0;
        SelectedPaymentMethod = "Cash";
        ModalNotes = string.Empty;
        foreach (var li in LineItems)
            li.PropertyChanged -= OnLineItemPropertyChanged;
        LineItems.Clear();
        AddLineItem();
        ReceiptFileName = "No receipt attached";
        ReceiptFilePath = null;
        ConvertedTotal = null;
        ConvertedTaxAmount = null;
        ConvertedShippingCost = null;
        ConvertedDiscount = null;
        ConvertedFee = null;
        IsPendingConversion = false;
        ClearValidationErrors();
    }

    /// <summary>
    /// Records a save the app refused. Warning rather than Error because the server keeps the
    /// message on a warning and drops it on an error, and the rule that fired is the point.
    /// Carries the rule, never the value that failed it.
    /// </summary>
    private void ReportValidationBlock(string reason)
    {
        App.ErrorLogger?.LogWarning(
            $"Save refused on the {TransactionTypeName} form: {reason}",
            $"{TransactionTypeName}Modal.SaveTransactionAsync",
            ErrorCategory.Validation,
            reason);
    }

    protected void ClearValidationErrors()
    {
        HasCounterpartyError = false;
        HasCategoryError = false;
        HasDescriptionError = false;
        HasUnitPriceError = false;
        ValidationMessage = string.Empty;
        HasValidationMessage = false;
        HasSaveError = false;
        SaveErrorMessage = string.Empty;
        IsSavingTransaction = false;
        HasTotalMismatchWarning = false;
        TotalMismatchWarningMessage = string.Empty;
    }

    #endregion

    #region Navigation Commands

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _supplierSavedHandler;
    private EventHandler? _customerSavedHandler;
    private EventHandler? _categorySavedHandler;
    private EventHandler? _productSavedHandler;

    [RelayCommand]
    protected void OpenCreateCounterparty()
    {
        if (CounterpartyName == "Supplier")
        {
            var supplierModals = App.SupplierModalsViewModel;
            if (supplierModals == null) return;

            CreateModalSubscription.RearmOnce(ref _supplierSavedHandler,
                h => supplierModals.SupplierSaved += h,
                h => supplierModals.SupplierSaved -= h,
                () =>
                {
                    LoadCounterpartyOptions();
                    SelectCounterparty(supplierModals.LastSavedSupplierId);
                });
            supplierModals.OpenAddModal();
        }
        else
        {
            var customerModals = App.CustomerModalsViewModel;
            if (customerModals == null) return;

            CreateModalSubscription.RearmOnce(ref _customerSavedHandler,
                h => customerModals.CustomerSaved += h,
                h => customerModals.CustomerSaved -= h,
                () =>
                {
                    LoadCounterpartyOptions();
                    SelectCounterparty(customerModals.LastSavedCustomerId);
                });
            customerModals.OpenAddModal();
        }
    }

    /// <summary>
    /// Selects the counterparty option with the given id, if present, after the options reload.
    /// </summary>
    private void SelectCounterparty(string? counterpartyId)
    {
        if (string.IsNullOrEmpty(counterpartyId)) return;
        var option = CounterpartyOptions.FirstOrDefault(c => c.Id == counterpartyId);
        if (option != null)
            SelectedCounterparty = option;
    }

    [RelayCommand]
    protected void OpenCreateCategory(TLineItem? lineItem)
    {
        var categoryModals = App.CategoryModalsViewModel;
        if (categoryModals == null) return;

        var isExpense = CategoryTypeFilter == CategoryType.Expense;
        CreateModalSubscription.RearmOnce(ref _categorySavedHandler,
            h => categoryModals.CategorySaved += h,
            h => categoryModals.CategorySaved -= h,
            () =>
            {
                LoadCategoryOptions();

                // Auto-select the category the user just created, into the line that asked for it.
                var newCategory = CategoryOptions.FirstOrDefault(c => c.Id == categoryModals.LastSavedCategoryId);
                if (newCategory == null) return;
                if (lineItem != null)
                    lineItem.SelectedCategory = newCategory;
                else
                    SelectedCategory = newCategory;
            });
        categoryModals.OpenAddModal(isExpense);
    }

    [RelayCommand]
    protected void OpenCreateProduct(TLineItem? lineItem)
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        var isExpense = CategoryTypeFilter == CategoryType.Expense;
        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                LoadProductOptions();

                // Auto-select the new product into the line item whose dropdown launched the create.
                if (lineItem != null)
                {
                    var newProduct = ProductOptions.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                    if (newProduct != null)
                        lineItem.SelectedProduct = newProduct;
                }
            });
        productModals.OpenAddModal(isExpense);
    }

    #endregion

    #region Line Items

    [RelayCommand]
    protected void AddLineItem()
    {
        var lineItem = new TLineItem { CurrencyCode = _entryCurrency };
        lineItem.PropertyChanged += OnLineItemPropertyChanged;
        LineItems.Add(lineItem);
        UpdateTotals();
    }

    [RelayCommand]
    protected void RemoveLineItem(TLineItem? item)
    {
        if (item != null && LineItems.Count > 1)
        {
            item.PropertyChanged -= OnLineItemPropertyChanged;
            LineItems.Remove(item);
            UpdateTotals();
        }
    }

    #endregion

    #region Receipt

    [RelayCommand]
    protected async Task AttachReceipt()
    {
        var topLevel = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Receipt",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.pdf"
                    ]
                },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0)
        {
            var file = files[0];
            ReceiptFilePath = file.Path.LocalPath;
            ReceiptFileName = file.Name;
        }
    }

    protected static string GetFileType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    #endregion

    #region Inventory Helpers

    /// <summary>
    /// Finds or auto-creates an InventoryItem for a product.
    /// </summary>
    private static (InventoryItem item, bool wasCreated) FindOrCreateInventoryItem(
        CompanyData companyData, Product product, decimal unitPrice)
    {
        var inventoryItem = companyData.Inventory.FirstOrDefault(inv => inv.ProductId == product.Id);
        if (inventoryItem != null) return (inventoryItem, false);

        companyData.IdCounters.InventoryItem++;
        inventoryItem = new InventoryItem
        {
            Id = $"INV-ITM-{companyData.IdCounters.InventoryItem:D5}",
            ProductId = product.Id,
            Sku = product.Sku,
            LocationId = "default",
            InStock = 0,
            Status = InventoryStatus.OutOfStock,
            UnitCost = unitPrice,
            LastUpdated = DateTime.UtcNow
        };
        companyData.Inventory.Add(inventoryItem);
        return (inventoryItem, true);
    }

    /// <summary>
    /// Adjusts inventory for each line item whose product has TrackInventory enabled.
    /// Creates StockAdjustment audit records and auto-creates InventoryItems when missing.
    /// </summary>
    protected static List<InventoryAdjustmentResult> AdjustInventoryForLineItems(
        CompanyData companyData, List<LineItem> lineItems, string transactionId, bool isExpense)
    {
        var results = new List<InventoryAdjustmentResult>();

        foreach (var lineItem in lineItems)
        {
            if (string.IsNullOrEmpty(lineItem.ProductId)) continue;

            var product = companyData.Products.FirstOrDefault(p => p.Id == lineItem.ProductId);
            if (product is not { TrackInventory: true }) continue;

            var qty = (int)Math.Round(lineItem.Quantity, MidpointRounding.AwayFromZero);
            if (qty == 0) continue;

            var (inventoryItem, wasCreated) = FindOrCreateInventoryItem(companyData, product, lineItem.UnitPrice);

            var oldStock = inventoryItem.InStock;
            inventoryItem.InStock += isExpense ? qty : -qty;
            inventoryItem.Status = inventoryItem.CalculateStatus();
            inventoryItem.LastUpdated = DateTime.UtcNow;
            App.CheckAndNotifyStockStatus(inventoryItem, oldStock);

            // Create audit record
            companyData.IdCounters.StockAdjustment++;
            var adjustment = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = inventoryItem.Id,
                AdjustmentType = isExpense ? AdjustmentType.Add : AdjustmentType.Remove,
                Quantity = qty,
                PreviousStock = oldStock,
                NewStock = inventoryItem.InStock,
                Reason = isExpense ? "Expense transaction" : "Revenue transaction",
                ReferenceNumber = transactionId,
                Timestamp = DateTime.UtcNow,
                IsAutoGenerated = true
            };
            companyData.StockAdjustments.Add(adjustment);

            results.Add(new InventoryAdjustmentResult
            {
                ProductName = product.Name,
                InventoryItemId = inventoryItem.Id,
                AdjustmentId = adjustment.Id,
                OldStock = oldStock,
                NewStock = inventoryItem.InStock,
                WasCreated = wasCreated
            });
        }

        return results;
    }

    /// <summary>
    /// Adjusts inventory for an edited transaction by computing the net quantity diff per product.
    /// Creates a single StockAdjustment per product instead of revert+reapply.
    /// </summary>
    protected static List<InventoryAdjustmentResult> AdjustInventoryForEdit(
        CompanyData companyData, List<LineItem> oldLineItems, List<LineItem> newLineItems,
        string transactionId, bool isExpense, string? reason = null)
    {
        // Build per-product quantity maps
        var oldQtyByProduct = new Dictionary<string, int>();
        var newQtyByProduct = new Dictionary<string, int>();

        foreach (var li in oldLineItems)
        {
            if (string.IsNullOrEmpty(li.ProductId)) continue;
            var qty = (int)Math.Round(li.Quantity, MidpointRounding.AwayFromZero);
            oldQtyByProduct[li.ProductId] = oldQtyByProduct.GetValueOrDefault(li.ProductId) + qty;
        }

        foreach (var li in newLineItems)
        {
            if (string.IsNullOrEmpty(li.ProductId)) continue;
            var qty = (int)Math.Round(li.Quantity, MidpointRounding.AwayFromZero);
            newQtyByProduct[li.ProductId] = newQtyByProduct.GetValueOrDefault(li.ProductId) + qty;
        }

        // Union of all product IDs
        var allProductIds = oldQtyByProduct.Keys.Union(newQtyByProduct.Keys).Distinct().ToList();

        var results = new List<InventoryAdjustmentResult>();

        foreach (var productId in allProductIds)
        {
            var product = companyData.Products.FirstOrDefault(p => p.Id == productId);
            if (product is not { TrackInventory: true }) continue;

            var oldQty = oldQtyByProduct.GetValueOrDefault(productId);
            var newQty = newQtyByProduct.GetValueOrDefault(productId);
            var diff = newQty - oldQty; // positive = more items, negative = fewer items
            if (diff == 0) continue;

            // For expenses: +diff means more purchased (add stock), -diff means less (remove stock)
            // For revenue: +diff means more sold (remove stock), -diff means less (add stock)
            var stockChange = isExpense ? diff : -diff;

            var unitPrice = newLineItems.FirstOrDefault(li => li.ProductId == productId)?.UnitPrice
                         ?? oldLineItems.FirstOrDefault(li => li.ProductId == productId)?.UnitPrice ?? 0;
            var (inventoryItem, wasCreated) = FindOrCreateInventoryItem(companyData, product, unitPrice);

            var oldStock = inventoryItem.InStock;
            inventoryItem.InStock += stockChange;
            inventoryItem.Status = inventoryItem.CalculateStatus();
            inventoryItem.LastUpdated = DateTime.UtcNow;
            App.CheckAndNotifyStockStatus(inventoryItem, oldStock);

            companyData.IdCounters.StockAdjustment++;
            var adjustment = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = inventoryItem.Id,
                AdjustmentType = stockChange > 0 ? AdjustmentType.Add : AdjustmentType.Remove,
                Quantity = Math.Abs(stockChange),
                PreviousStock = oldStock,
                NewStock = inventoryItem.InStock,
                Reason = reason ?? (isExpense ? "Expense edited" : "Revenue edited"),
                ReferenceNumber = transactionId,
                Timestamp = DateTime.UtcNow,
                IsAutoGenerated = true
            };
            companyData.StockAdjustments.Add(adjustment);

            results.Add(new InventoryAdjustmentResult
            {
                ProductName = product.Name,
                InventoryItemId = inventoryItem.Id,
                AdjustmentId = adjustment.Id,
                OldStock = oldStock,
                NewStock = inventoryItem.InStock,
                WasCreated = wasCreated
            });
        }

        return results;
    }

    /// <summary>
    /// Reverts inventory adjustments (for undo). Restores stock, removes audit records,
    /// and removes auto-created InventoryItems.
    /// </summary>
    protected static void RevertInventoryAdjustments(CompanyData companyData, List<InventoryAdjustmentResult> adjustments)
    {
        // Newest first: when several lines adjusted the same item, each OldStock is the stock before
        // that line, so only the earliest one holds the true original and it must be applied last.
        for (var i = adjustments.Count - 1; i >= 0; i--)
        {
            var result = adjustments[i];
            if (result.WasCreated)
            {
                companyData.Inventory.RemoveAll(inv => inv.Id == result.InventoryItemId);
            }
            else
            {
                var inventoryItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == result.InventoryItemId);
                if (inventoryItem != null)
                {
                    var stockBeforeRevert = inventoryItem.InStock;
                    inventoryItem.InStock = result.OldStock;
                    inventoryItem.Status = inventoryItem.CalculateStatus();
                    inventoryItem.LastUpdated = DateTime.UtcNow;
                    App.CheckAndNotifyStockStatus(inventoryItem, stockBeforeRevert);
                }
            }

            companyData.StockAdjustments.RemoveAll(a => a.Id == result.AdjustmentId);
        }
    }

    /// <summary>
    /// Shows notifications for inventory adjustments (auto-created items, low stock warnings).
    /// </summary>
    protected static void ShowInventoryNotifications(
        List<InventoryAdjustmentResult> results, bool isExpense)
    {
        var created = results.Where(r => r.WasCreated).ToList();
        if (created.Count > 0)
        {
            var names = string.Join(", ", created.Select(r => r.ProductName));
            App.AddNotification(
                "Inventory Created".Translate(),
                string.Format("Inventory item created for {0}.".Translate(), names),
                NotificationType.Info);
        }

        if (!isExpense)
        {
            var lowStock = results.Where(r => r.NewStock < 0).ToList();
            if (lowStock.Count > 0)
            {
                var warnings = string.Join("; ", lowStock.Select(r =>
                    $"{r.ProductName} ({r.OldStock} available, sold {r.OldStock - r.NewStock})"));
                App.AddNotification(
                    "Insufficient Stock".Translate(),
                    string.Format("Insufficient stock: {0}".Translate(), warnings),
                    NotificationType.Warning);
            }
        }
    }

    /// <summary>
    /// Shows notifications for inventory changes from editing a transaction.
    /// </summary>
    protected static void ShowEditInventoryNotifications(List<InventoryAdjustmentResult> results)
    {
        if (results.Count == 0) return;

        var changes = string.Join("; ", results.Select(r =>
            $"{r.ProductName}: {r.OldStock} → {r.NewStock}"));
        App.AddNotification(
            "Inventory Updated".Translate(),
            string.Format("Inventory updated: {0}".Translate(), changes),
            NotificationType.Info);
    }

    #endregion
}

/// <summary>
/// Result of a single inventory adjustment for a line item.
/// </summary>
public class InventoryAdjustmentResult
{
    public string ProductName { get; init; } = string.Empty;
    public string InventoryItemId { get; init; } = string.Empty;
    public string AdjustmentId { get; init; } = string.Empty;
    public int OldStock { get; init; }
    public int NewStock { get; init; }
    public bool WasCreated { get; init; }
}

/// <summary>
/// Base class for transaction line items.
/// </summary>
public abstract partial class TransactionLineItemBase : ObservableObject
{
    [ObservableProperty]
    private ProductOption? _selectedProduct;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private decimal? _quantity = 1;

    [ObservableProperty]
    private decimal? _unitPrice;

    [ObservableProperty]
    private bool _hasProductError;

    /// <summary>
    /// The line's category box, on forms that take typed items: the category a new product goes
    /// in, or the one a picked product is moved to.
    /// </summary>
    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private bool _hasCategoryError;

    /// <summary>
    /// Text in the category box. A name matching no existing category is created on save.
    /// </summary>
    [ObservableProperty]
    private string? _categoryText;

    /// <summary>
    /// Text in the item box. A name that matches no product becomes a new product on save; until
    /// then it stands as the line's description. Once a product is picked the box shows its name
    /// and the description follows the product instead.
    /// </summary>
    [ObservableProperty]
    private string? _itemText;

    /// <summary>The currency of an entry being edited in another currency than the company's, otherwise null.</summary>
    public string? CurrencyCode { get; init; }

    public decimal Amount => (Quantity ?? 0) * (UnitPrice ?? 0);
    public string AmountFormatted => CurrencyCode == null
        ? CurrencyService.Format(Amount)
        : CurrencyInfo.GetByCode(CurrencyCode).Format(Amount);

    partial void OnSelectedProductChanged(ProductOption? value)
    {
        if (value != null)
        {
            Description = value.Name;
            if (UnitPrice is null or 0)
                UnitPrice = value.UnitPrice;
            HasProductError = false;
            HasCategoryError = false;
        }
    }

    partial void OnSelectedCategoryChanged(CategoryOption? value)
    {
        if (value != null)
            HasCategoryError = false;
    }

    partial void OnCategoryTextChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            HasCategoryError = false;
    }

    partial void OnItemTextChanged(string? value)
    {
        if (SelectedProduct != null) return;
        Description = value?.Trim() ?? string.Empty;
        if (Description.Length > 0)
            HasProductError = false;
    }

    partial void OnQuantityChanged(decimal? value)
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountFormatted));
    }

    partial void OnUnitPriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountFormatted));
    }
}

/// <summary>
/// Generic option class for counterparty (Supplier or Customer) selection.
/// </summary>
public class CounterpartyOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}

/// <summary>
/// Option class for product selection in line items.
/// </summary>
public class ProductOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? SupplierId { get; set; }
    public string? CategoryId { get; set; }
    public override string ToString() => Name;
}
