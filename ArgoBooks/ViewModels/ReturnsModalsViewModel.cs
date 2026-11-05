using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Returns modals (Record, Filter, View Details, Undo).
/// </summary>
public partial class ReturnsModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Raised when a return is undone.
    /// </summary>
    public event EventHandler? ReturnUndone;

    /// <summary>Raised when a return is recorded, or its recording undone or redone.</summary>
    public event EventHandler? ReturnRecorded;

    #endregion

    #region Filter Modal State

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    [ObservableProperty]
    private string _filterReason = "All";

    /// <summary>
    /// Reason filter options.
    /// </summary>
    public ObservableCollection<string> ReasonOptions { get; } = ["All", "Defective", "Wrong Item", "Not as Described", "Changed Mind", "Other"];

    #endregion

    #region Filter Modal Commands

    private sealed record FilterValues(DateTimeOffset? DateFrom, DateTimeOffset? DateTo, string Reason)
    {
        public static readonly FilterValues Default = new(null, null, "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterDateFrom, FilterDateTo, FilterReason),
        v =>
        {
            FilterDateFrom = v.DateFrom;
            FilterDateTo = v.DateTo;
            FilterReason = v.Reason;
        });

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal()
    {
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>
    /// Closes the filter modal, asking first and putting the filters back if they were changed.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Reset();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region View Details Modal State

    [ObservableProperty]
    private bool _isViewDetailsModalOpen;

    [ObservableProperty]
    private string _viewDetailsId = string.Empty;

    [ObservableProperty]
    private string _viewDetailsProduct = string.Empty;

    [ObservableProperty]
    private string _viewDetailsDate = string.Empty;

    [ObservableProperty]
    private string _viewDetailsRefund = string.Empty;

    [ObservableProperty]
    private string _viewDetailsReason = string.Empty;

    [ObservableProperty]
    private string _viewDetailsNotes = string.Empty;

    /// <summary>
    /// Opens the view details modal with the specified return details.
    /// </summary>
    public void OpenViewDetailsModal(string id, string product, string date, string refund, string reason, string notes)
    {
        ViewDetailsId = id;
        ViewDetailsProduct = product;
        ViewDetailsDate = date;
        ViewDetailsRefund = refund;
        ViewDetailsReason = reason;
        ViewDetailsNotes = string.IsNullOrWhiteSpace(notes) ? "No notes provided" : notes;
        IsViewDetailsModalOpen = true;
    }

    /// <summary>
    /// Closes the view details modal.
    /// </summary>
    [RelayCommand]
    private void CloseViewDetailsModal()
    {
        IsViewDetailsModalOpen = false;
    }

    #endregion

    #region Undo Return Modal State

    private Return? _undoReturn;

    [ObservableProperty]
    private bool _isUndoReturnModalOpen;

    [ObservableProperty]
    private string _undoReturnItemDescription = string.Empty;

    [ObservableProperty]
    private string _undoReturnReason = string.Empty;

    /// <summary>
    /// Opens the undo return modal for the specified return.
    /// </summary>
    public void OpenUndoReturnModal(Return returnItem, string description)
    {
        _undoReturn = returnItem;
        UndoReturnItemDescription = description;
        UndoReturnReason = string.Empty;
        IsUndoReturnModalOpen = true;
    }

    /// <summary>
    /// Closes the undo return modal.
    /// </summary>
    [RelayCommand]
    private void CloseUndoReturnModal()
    {
        IsUndoReturnModalOpen = false;
        _undoReturn = null;
    }

    /// <summary>
    /// Confirms the undo return action.
    /// </summary>
    [RelayCommand]
    private void ConfirmUndoReturn()
    {
        if (_undoReturn == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            CloseUndoReturnModal();
            return;
        }

        // Undoing a return takes out its refund and puts back the stock it moved, along with the record.
        var change = TransactionReturns.Remove(companyData, _undoReturn);
        companyData.MarkAsModified();
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Undo return '{change.Record.Id}'",
            () => { change.PutBack(companyData); Changed(companyData, change, ReturnUndone); },
            () => { change.TakeOut(companyData); Changed(companyData, change, ReturnUndone); }));

        App.CompanyManager?.MarkAsChanged();
        Changed(companyData, change, ReturnUndone);
        CloseUndoReturnModal();
    }

    // A credit note changes what the invoice still owes, which the payment portal keeps its own copy of.
    private void Changed(Core.Data.CompanyData companyData, TransactionReturns.Change change, EventHandler? raised)
    {
        companyData.MarkAsModified();
        if (change.Refund?.CreditNote is { InvoiceId.Length: > 0 } creditNote)
            App.PortalBalanceSyncService?.Queue(creditNote.InvoiceId);
        raised?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens Undo Return for the most recent return of a sale or purchase, as its row's undo button does.</summary>
    public void OpenUndoLatestReturn(string transactionId)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null || TransactionReturns.LatestFor(companyData, transactionId) is not { } latest)
            return;
        OpenUndoReturnModal(latest, $"{latest.Id} - {ReturnsPageViewModel.ProductNamesOf(latest)}");
    }

    #endregion

    #region Record Return Modal

    private Transaction? _returning;

    [ObservableProperty]
    private bool _isRecordReturnModalOpen;

    [ObservableProperty]
    private string _recordReturnTitle = string.Empty;

    [ObservableProperty]
    private string _recordReturnSubtitle = string.Empty;

    public ObservableCollection<ReturnLineRow> ReturnLines { get; } = [];

    public ObservableCollection<string> RecordReturnReasons { get; } = [];

    [ObservableProperty]
    private string? _selectedRecordReturnReason;

    [ObservableProperty]
    private bool _hasRecordReturnReasonError;

    [ObservableProperty]
    private DateTimeOffset? _recordReturnDate;

    [ObservableProperty]
    private bool _showRefundShipping;

    [ObservableProperty]
    private bool _refundShipping;

    [ObservableProperty]
    private string _refundShippingLabel = string.Empty;

    [ObservableProperty]
    private bool _showRefundFee;

    [ObservableProperty]
    private bool _refundFee;

    [ObservableProperty]
    private string _refundFeeLabel = string.Empty;

    [ObservableProperty]
    private bool _showMoveStock;

    [ObservableProperty]
    private bool _moveStock = true;

    [ObservableProperty]
    private string _moveStockLabel = string.Empty;

    [ObservableProperty]
    private decimal? _restockingFee;

    [ObservableProperty]
    private string _recordReturnNotes = string.Empty;

    [ObservableProperty]
    private string _returnItemsText = string.Empty;

    [ObservableProperty]
    private string _returnDiscountText = string.Empty;

    [ObservableProperty]
    private bool _showReturnDiscount;

    [ObservableProperty]
    private string _returnTaxText = string.Empty;

    [ObservableProperty]
    private string _returnExtrasText = string.Empty;

    [ObservableProperty]
    private bool _showReturnExtras;

    [ObservableProperty]
    private string _returnRestockingText = string.Empty;

    [ObservableProperty]
    private bool _showReturnRestocking;

    [ObservableProperty]
    private string _returnRefundTotalText = string.Empty;

    [ObservableProperty]
    private bool _canRecordReturn;

    [ObservableProperty]
    private string _returnRefundNote = string.Empty;

    partial void OnRefundShippingChanged(bool value) => UpdateReturnSummary();
    partial void OnRefundFeeChanged(bool value) => UpdateReturnSummary();
    partial void OnRestockingFeeChanged(decimal? value) => UpdateReturnSummary();
    partial void OnSelectedRecordReturnReasonChanged(string? value)
    {
        if (value != null)
            HasRecordReturnReasonError = false;
    }

    /// <summary>
    /// Opens the return form for a sale or purchase, listing what is left to return with every unit
    /// chosen, so returning everything is one click and returning part means lowering the quantities.
    /// </summary>
    public void OpenRecordReturnModal(Transaction transaction, IEnumerable<string> reasons)
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        _returning = transaction;
        var isPurchase = transaction is Expense;
        var counterparty = isPurchase
            ? companyData.GetSupplier((transaction as Expense)?.SupplierId ?? "")?.Name
            : companyData.GetCustomer((transaction as Revenue)?.CustomerId ?? "")?.Name;
        RecordReturnTitle = isPurchase ? "Return Purchase {0}".TranslateFormat(transaction.Id) : "Return Sale {0}".TranslateFormat(transaction.Id);
        RecordReturnSubtitle = string.IsNullOrEmpty(counterparty)
            ? DateFormatService.Format(transaction.Date)
            : $"{counterparty} · {DateFormatService.Format(transaction.Date)}";

        foreach (var row in ReturnLines)
            row.PropertyChanged -= OnReturnLineChanged;
        ReturnLines.Clear();
        foreach (var line in TransactionReturns.LinesOf(companyData, transaction))
        {
            var row = new ReturnLineRow(line, transaction.OriginalCurrency);
            row.PropertyChanged += OnReturnLineChanged;
            ReturnLines.Add(row);
        }

        var (shippingRefunded, feeRefunded) = TransactionReturns.AlreadyRefunded(companyData, transaction.Id);
        ShowRefundShipping = transaction.ShippingCost > 0 && !shippingRefunded;
        RefundShippingLabel = "Refund the shipping ({0})".TranslateFormat(CurrencyInfo.FormatAmount(transaction.ShippingCost, transaction.OriginalCurrency));
        RefundShipping = false;
        ShowRefundFee = transaction.Fee > 0 && !feeRefunded;
        RefundFeeLabel = "Refund the fee ({0})".TranslateFormat(CurrencyInfo.FormatAmount(transaction.Fee, transaction.OriginalCurrency));
        RefundFee = false;

        ShowMoveStock = ReturnLines.Any(r => InventoryStockService.TracksStock(companyData, r.Line.Item));
        MoveStockLabel = isPurchase
            ? "Take the returned items out of stock".Translate()
            : "Put the returned items back in stock".Translate();
        MoveStock = true;

        RecordReturnReasons.Clear();
        foreach (var reason in reasons)
            RecordReturnReasons.Add(reason);
        SelectedRecordReturnReason = null;
        HasRecordReturnReasonError = false;
        RecordReturnDate = DateTimeOffset.Now;
        RestockingFee = null;
        RecordReturnNotes = string.Empty;
        ReturnRefundNote = RefundNoteFor(companyData, transaction);

        UpdateReturnSummary();
        IsRecordReturnModalOpen = true;
    }

    // What happens to the money, which depends on whether it was paid and how, so the form says it before saving.
    private static string RefundNoteFor(Core.Data.CompanyData data, Transaction transaction)
    {
        if (transaction is Expense purchase)
            return purchase.IsPaid
                ? "Recorded as a refund from the supplier on the return date.".Translate()
                : "This bill isn't paid yet, so the return comes off what you owe.".Translate();

        var sale = (Revenue)transaction;
        var invoice = string.IsNullOrEmpty(sale.InvoiceId) ? null : data.Invoices.FirstOrDefault(i => i.Id == sale.InvoiceId);
        var paidOnline = invoice != null && data.Payments.Any(p => p.InvoiceId == invoice.Id && p.Source == PaymentSource.Online);
        var owes = invoice != null ? invoice.Total - invoice.AmountPaid - invoice.AmountCredited > 0 : !RevenueAggregator.IsCollected(sale);
        var paidSome = invoice == null ? !owes : invoice.AmountPaid > 0;

        if (owes && !paidSome)
            return "This sale isn't paid yet, so the return comes off what the customer owes.".Translate();
        if (owes)
            return paidOnline
                ? "The return comes off what the customer still owes first. Refund anything more through the payment provider.".Translate()
                : "The return comes off what the customer still owes first, and anything more is refunded to them.".Translate();
        return paidOnline
            ? "This sale was paid online, so refund it through the payment provider; the refund arrives with the next payment sync.".Translate()
            : "Recorded as a refund to the customer on the return date.".Translate();
    }

    private void OnReturnLineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnLineRow.Quantity))
            UpdateReturnSummary();
    }

    private Dictionary<int, decimal> ChosenQuantities() =>
        ReturnLines.Where(r => r.ChosenQuantity > 0).ToDictionary(r => r.Line.Index, r => r.ChosenQuantity);

    private void UpdateReturnSummary()
    {
        if (_returning is not { } transaction) return;

        var currency = transaction.OriginalCurrency;
        string Money(decimal amount) => CurrencyInfo.FormatAmount(amount, currency);

        var quantities = ChosenQuantities();
        foreach (var row in ReturnLines)
            row.SetAmount(TransactionReturns.AmountsFor(transaction,
                new Dictionary<int, decimal> { [row.Line.Index] = row.ChosenQuantity }, false, false).Items);

        var amounts = TransactionReturns.AmountsFor(transaction, quantities, RefundShipping && ShowRefundShipping, RefundFee && ShowRefundFee);
        var restocking = Math.Min(Math.Max(0m, RestockingFee ?? 0m), amounts.Total);

        ReturnItemsText = Money(amounts.Items);
        ShowReturnDiscount = amounts.Discount != 0;
        ReturnDiscountText = $"-{Money(amounts.Discount)}";
        ReturnTaxText = Money(amounts.Tax);
        ShowReturnExtras = amounts.Shipping + amounts.Fee != 0;
        ReturnExtrasText = Money(amounts.Shipping + amounts.Fee);
        ShowReturnRestocking = restocking != 0;
        ReturnRestockingText = $"-{Money(restocking)}";
        ReturnRefundTotalText = Money(amounts.Total - restocking);
        CanRecordReturn = quantities.Count > 0 || (RefundShipping && ShowRefundShipping) || (RefundFee && ShowRefundFee);
    }

    [RelayCommand]
    private void CloseRecordReturnModal()
    {
        IsRecordReturnModalOpen = false;
        _returning = null;
    }

    [RelayCommand]
    private void RecordReturn()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null || _returning is not { } transaction || !CanRecordReturn) return;

        if (string.IsNullOrEmpty(SelectedRecordReturnReason))
        {
            HasRecordReturnReasonError = true;
            return;
        }

        var change = TransactionReturns.Record(companyData, transaction, new TransactionReturns.Request(
            ChosenQuantities(),
            RefundShipping && ShowRefundShipping,
            RefundFee && ShowRefundFee,
            RestockingFee ?? 0m,
            MoveStock && ShowMoveStock,
            SelectedRecordReturnReason,
            RecordReturnNotes.Trim(),
            (RecordReturnDate ?? DateTimeOffset.Now).DateTime));

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Record return '{change.Record.Id}' of '{transaction.Id}'",
            () => { change.TakeOut(companyData); Changed(companyData, change, ReturnRecorded); },
            () => { change.PutBack(companyData); Changed(companyData, change, ReturnRecorded); }));

        var whole = TransactionReturns.ReturnedState(companyData, transaction).All;
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ReturnRecorded,
            $"{(transaction is Expense ? "purchase" : "sale")}:{(whole ? "all" : "part")}{(change.Record.Restocked ? ":stock" : "")}");

        App.CompanyManager?.MarkAsChanged();
        Changed(companyData, change, ReturnRecorded);
        CloseRecordReturnModal();
    }

    #endregion
}

/// <summary>A line of the return form: what was sold or bought, what is left to return, and how much is coming back.</summary>
public partial class ReturnLineRow : ObservableObject
{
    public ReturnLineRow(TransactionReturns.Line line, string currency)
    {
        Line = line;
        _currency = currency;
        _quantity = line.Remaining;
    }

    private readonly string _currency;

    public TransactionReturns.Line Line { get; }

    public string Description => string.IsNullOrWhiteSpace(Line.Item.Description) ? "Item".Translate() : Line.Item.Description;

    public bool CanReturn => Line.Remaining > 0;

    /// <summary>What was sold or bought, and how much of it came back before.</summary>
    public string Detail => Line.AlreadyReturned > 0
        ? "{0} of {1} returned already".TranslateFormat(StockUnits.Format(Line.AlreadyReturned), StockUnits.Format(Line.Sold))
        : "{0} sold".TranslateFormat(StockUnits.Format(Line.Sold));

    [ObservableProperty]
    private decimal? _quantity;

    [ObservableProperty]
    private string _amountText = string.Empty;

    /// <summary>The quantity chosen, kept between none and what is left.</summary>
    public decimal ChosenQuantity => Math.Clamp(Quantity ?? 0m, 0m, Line.Remaining);

    internal void SetAmount(decimal amount) => AmountText = CurrencyInfo.FormatAmount(amount, _currency);
}
