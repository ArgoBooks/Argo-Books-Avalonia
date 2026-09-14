using ArgoBooks.Localization;
using ArgoBooks.Services;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for rental records modals.
/// </summary>
public partial class RentalRecordsModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isAddModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isEditModalOpen;

    /// <summary>Add and edit share one form, open while either flag is set.</summary>
    public bool IsFormOpen => IsAddModalOpen || IsEditModalOpen;

    // Set when the form opens and kept on close, so the title doesn't change while it closes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText), nameof(ShowReservationHint))]
    private bool _isEditMode;

    public string FormTitle => IsEditMode ? "Edit Rental Record".Translate() : "New Rental Record".Translate();
    public string FormSaveText => IsEditMode ? "Save Changes".Translate() : "Create Rental".Translate();

    partial void OnIsAddModalOpenChanged(bool value)
    {
        if (value) IsEditMode = false;
    }

    partial void OnIsEditModalOpenChanged(bool value)
    {
        if (value) IsEditMode = true;
    }

    [RelayCommand]
    private Task RequestCloseFormAsync() => IsEditMode ? RequestCloseEditModalAsync() : RequestCloseAddModalAsync();

    [RelayCommand]
    private void SaveForm()
    {
        if (IsEditMode) SaveEditedRecord();
        else SaveNewRecord();
    }

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private bool _isReturnModalOpen;

    [ObservableProperty]
    private bool _isViewModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private CustomerOption? _modalCustomer;

    [ObservableProperty]
    private AccountantOption? _modalAccountant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReservationHint))]
    private DateTimeOffset? _modalStartDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset? _modalDueDate = DateTimeOffset.Now.AddDays(1);

    [ObservableProperty]
    private string? _modalDueDateError;

    /// <summary>A new rental starting after today is saved as a reservation.</summary>
    public bool ShowReservationHint => !IsEditMode && ModalStartDate?.Date > DateTime.Today;

    /// <summary>The days the form's dates cover, which the estimate charges for.</summary>
    public int ModalDays => RentalBookings.ChargeableDays(
        ModalStartDate?.DateTime ?? DateTime.Today, ModalDueDate?.DateTime ?? DateTime.Today);

    partial void OnModalStartDateChanged(DateTimeOffset? value) => OnModalDatesChanged();

    partial void OnModalDueDateChanged(DateTimeOffset? value) => OnModalDatesChanged();

    private void OnModalDatesChanged()
    {
        ModalDueDateError = null;
        foreach (var li in RentalLineItems)
            li.RefreshAmount();
        UpdateLineItemTotals();
    }

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string? _modalCustomerError;

    [ObservableProperty]
    private string? _modalLineItemsError;

    private RentalRecord? _editingRecord;

    private sealed record LineState(string? ItemId, string Quantity, string RateType, string RateAmount, string SecurityDeposit);

    private sealed record EditState(
        string? CustomerId, string? AccountantId, DateTimeOffset? StartDate, DateTimeOffset? DueDate,
        string Notes, Helpers.EquatableArray<LineState> LineItems);

    // The form as the edit modal opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(
        ModalCustomer?.Id, ModalAccountant?.Id, ModalStartDate, ModalDueDate, ModalNotes,
        new Helpers.EquatableArray<LineState>(RentalLineItems.Select(li =>
            new LineState(li.SelectedItem?.Id, li.Quantity, li.RateType, li.RateAmount, li.SecurityDeposit))));

    /// <summary>
    /// Line items in the Add/Edit modal.
    /// </summary>
    public ObservableCollection<RentalModalLineItem> RentalLineItems { get; } = [];

    /// <summary>
    /// Total security deposit across all line items.
    /// </summary>
    [ObservableProperty]
    private string _totalSecurityDeposit = CurrencyService.Format(0);

    /// <summary>
    /// Total estimated amount across all line items.
    /// </summary>
    [ObservableProperty]
    private string _totalEstimatedAmount = CurrencyService.Format(0);

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        ModalCustomer != null ||
        ModalAccountant != null ||
        RentalLineItems.Any(li => li.SelectedItem != null || !string.IsNullOrWhiteSpace(li.RateAmount)) ||
        !string.IsNullOrWhiteSpace(ModalNotes);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges => Capture() != _original;

    partial void OnModalCustomerChanged(CustomerOption? value)
    {
        if (value != null)
        {
            ModalCustomerError = null;
        }
    }

    /// <summary>
    /// Updates total security deposit and estimated amount from all line items.
    /// </summary>
    public void UpdateLineItemTotals()
    {
        var totalDeposit = RentalBookings.TotalDeposit(BuildLines());
        var totalAmount = RentalLineItems.Sum(li => li.Amount);
        TotalSecurityDeposit = CurrencyService.Format(totalDeposit);
        TotalEstimatedAmount = CurrencyService.Format(totalAmount);
    }

    [RelayCommand]
    public void AddRentalLineItem()
    {
        var lineItem = new RentalModalLineItem(this);
        RentalLineItems.Add(lineItem);
        ModalLineItemsError = null;
        UpdateLineItemTotals();
    }

    [RelayCommand]
    public void RemoveRentalLineItem(RentalModalLineItem? item)
    {
        if (item != null && RentalLineItems.Count > 1)
        {
            RentalLineItems.Remove(item);
            UpdateLineItemTotals();
        }
    }

    #endregion

    #region Return Modal Fields

    [ObservableProperty]
    private string _returnRecordId = string.Empty;

    [ObservableProperty]
    private string _returnItemName = string.Empty;

    [ObservableProperty]
    private string _returnCustomerName = string.Empty;

    [ObservableProperty]
    private int _returnQuantity;

    [ObservableProperty]
    private string _returnRateType = "Daily";

    [ObservableProperty]
    private decimal _returnRateAmount;

    [ObservableProperty]
    private DateTimeOffset? _returnDate = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnTotalCostFormatted), nameof(ReturnAmountDueFormatted), nameof(ReturnCostDetail))]
    private decimal _returnTotalCost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnKeptDepositText), nameof(ReturnDepositHeldText))]
    private decimal _returnDeposit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnKeptDepositText))]
    private string _returnDepositRefund = string.Empty;

    [ObservableProperty]
    private string? _returnDepositRefundError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnAmountDueFormatted), nameof(ReturnCostDetail))]
    private string _returnExtraCharges = string.Empty;

    [ObservableProperty]
    private string _returnExtraChargesNote = string.Empty;

    private int _returnLineCount;

    [ObservableProperty]
    private string _returnNotes = string.Empty;

    [ObservableProperty]
    private bool _returnMarkAsPaid;

    private RentalRecord? _returningRecord;

    public string ReturnRateFormatted => _returnLineCount > 1
        ? "{0} items".TranslateFormat(_returnLineCount)
        : $"{CurrencyService.Format(ReturnRateAmount)}/{ReturnRateType}";
    public string ReturnTotalCostFormatted => CurrencyService.Format(ReturnTotalCost);
    public string ReturnDepositFormatted => CurrencyService.Format(ReturnDeposit);
    public string ReturnDepositHeldText => "of {0} held".TranslateFormat(ReturnDepositFormatted);
    public bool HasDeposit => ReturnDeposit > 0;

    private decimal ReturnExtraChargesAmount => decimal.TryParse(ReturnExtraCharges, out var amount) && amount > 0 ? amount : 0;

    // An empty box refunds nothing; text that isn't a number fails validation.
    private decimal ReturnRefundAmount => string.IsNullOrWhiteSpace(ReturnDepositRefund)
        ? 0
        : decimal.TryParse(ReturnDepositRefund, out var amount) ? amount : -1;

    public string ReturnAmountDueFormatted => CurrencyService.Format(ReturnTotalCost + ReturnExtraChargesAmount);

    public string ReturnCostDetail => ReturnExtraChargesAmount > 0
        ? "Rental {0} plus {1} in extra charges".TranslateFormat(CurrencyService.Format(ReturnTotalCost), CurrencyService.Format(ReturnExtraChargesAmount))
        : "Calculated based on rental duration".Translate();

    public string ReturnKeptDepositText => ReturnRefundAmount >= 0 && ReturnRefundAmount < ReturnDeposit
        ? "{0} of the deposit is kept.".TranslateFormat(CurrencyService.Format(ReturnDeposit - ReturnRefundAmount))
        : string.Empty;

    partial void OnReturnDepositRefundChanged(string value) => ReturnDepositRefundError = null;

    #endregion

    #region View Modal Fields

    [ObservableProperty]
    private string _viewRecordId = string.Empty;

    [ObservableProperty]
    private string _viewItemName = string.Empty;

    [ObservableProperty]
    private string _viewCustomerName = string.Empty;

    [ObservableProperty]
    private string _viewAccountantName = string.Empty;

    [ObservableProperty]
    private int _viewQuantity;

    [ObservableProperty]
    private string _viewRateType = string.Empty;

    [ObservableProperty]
    private decimal _viewRateAmount;

    [ObservableProperty]
    private decimal _viewSecurityDeposit;

    [ObservableProperty]
    private DateTime _viewStartDate;

    [ObservableProperty]
    private DateTime _viewDueDate;

    [ObservableProperty]
    private DateTime? _viewReturnDate;

    [ObservableProperty]
    private string _viewStatus = string.Empty;

    [ObservableProperty]
    private decimal _viewTotalCost;

    [ObservableProperty]
    private decimal? _viewDepositRefundedAmount;

    [ObservableProperty]
    private string _viewNotes = string.Empty;

    [ObservableProperty]
    private int _viewDaysOverdue;

    [ObservableProperty]
    private bool _viewHasMultipleItems;

    /// <summary>
    /// Line items for display in the view modal.
    /// </summary>
    public ObservableCollection<RentalViewLineItemDisplay> ViewLineItems { get; } = [];

    public string ViewRateFormatted => $"{CurrencyService.Format(ViewRateAmount)}/{ViewRateType}";
    public string ViewDepositFormatted => CurrencyService.Format(ViewSecurityDeposit);
    public string ViewTotalCostFormatted => CurrencyService.Format(ViewTotalCost);
    public string ViewStartDateFormatted => ViewStartDate.ToString("MMMM d, yyyy");
    public string ViewDueDateFormatted => ViewDueDate.ToString("MMMM d, yyyy");
    public string ViewReturnDateFormatted => ViewReturnDate?.ToString("MMMM d, yyyy") ?? "Not returned";
    public string ViewDepositStatusFormatted =>
        ViewSecurityDeposit <= 0 ? "-"
        : ViewStatus != nameof(RentalStatus.Returned) || ViewDepositRefundedAmount is not { } refunded ? "Held".Translate()
        : refunded >= ViewSecurityDeposit ? "Refunded".Translate()
        : refunded > 0 ? "Refunded {0}, kept {1}".TranslateFormat(CurrencyService.Format(refunded), CurrencyService.Format(ViewSecurityDeposit - refunded))
        : "Not Refunded".Translate();

    [ObservableProperty]
    private string _viewExtraChargesText = string.Empty;

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterStatus = "All";

    // The dropdowns select option objects; null means all.
    [ObservableProperty]
    private CustomerOption? _filterCustomer;

    [ObservableProperty]
    private RentalItemOption? _filterItem;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDateTo;

    [ObservableProperty]
    private DateTimeOffset? _filterDueDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDueDateTo;

    #endregion

    #region Dropdown Options

    public ObservableCollection<RentalItemOption> AvailableItems { get; } = [];
    public ObservableCollection<CustomerOption> AvailableCustomers { get; } = [];
    public ObservableCollection<CustomerOption> FilterCustomerOptions { get; } = [];
    public ObservableCollection<RentalItemOption> FilterItemOptions { get; } = [];
    public ObservableCollection<AccountantOption> AvailableAccountants { get; } = [];
    public ObservableCollection<string> RateTypeOptions { get; } = new(RateTypeExtensions.GetAllNames());
    public ObservableCollection<string> StatusOptions { get; } = new(RentalStatusExtensions.GetFilterOptions());

    #endregion

    #region Events

    public event EventHandler? RecordSaved;
    public event EventHandler? RecordDeleted;
    public event EventHandler? RecordReturned;
    public event EventHandler? FiltersApplied;
    public event EventHandler? FiltersCleared;

    #endregion

    #region Add Record

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingRecord = null;
        ClearModalFields();
        UpdateDropdownOptions();
        IsAddModalOpen = true;
    }

    [RelayCommand]
    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Add modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        if (HasAddModalEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddModal();
    }

    /// <summary>
    /// Opens the create rental item modal on top of the current modal.
    /// </summary>
    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _itemSavedHandler;
    private EventHandler? _customerSavedHandler;

    [RelayCommand]
    private void OpenCreateRentalItem(RentalModalLineItem? lineItem)
    {
        var rentalInventoryModals = App.RentalInventoryModalsViewModel;
        if (rentalInventoryModals == null) return;

        CreateModalSubscription.RearmOnce(ref _itemSavedHandler,
            h => rentalInventoryModals.ItemSaved += h,
            h => rentalInventoryModals.ItemSaved -= h,
            () =>
            {
                UpdateDropdownOptions();

                // Auto-select the new rental item into the line whose dropdown launched the create.
                if (lineItem != null)
                {
                    var newItem = AvailableItems.FirstOrDefault(i => i.Id == rentalInventoryModals.LastSavedItemId);
                    if (newItem != null)
                        lineItem.SelectedItem = newItem;
                }
            });
        rentalInventoryModals.OpenAddModal();
    }

    /// <summary>
    /// Opens the create customer modal on top of the current modal.
    /// </summary>
    [RelayCommand]
    private void OpenCreateCustomer()
    {
        var customerModals = App.CustomerModalsViewModel;
        if (customerModals == null) return;

        CreateModalSubscription.RearmOnce(ref _customerSavedHandler,
            h => customerModals.CustomerSaved += h,
            h => customerModals.CustomerSaved -= h,
            () =>
            {
                UpdateDropdownOptions();

                // Auto-select the customer the user just created.
                var newCustomer = AvailableCustomers.FirstOrDefault(c => c.Id == customerModals.LastSavedCustomerId);
                if (newCustomer != null)
                    ModalCustomer = newCustomer;
            });
        customerModals.OpenAddModal();
    }

    [RelayCommand]
    public void SaveNewRecord()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null || ModalCustomer == null)
            return;

        CreateRental(companyData, ModalCustomer.Id!, ModalAccountant?.Id, BuildLines(),
            ModalStartDate?.DateTime ?? DateTime.Today, ModalDueDate?.DateTime ?? DateTime.Today.AddDays(1),
            ModalNotes.Trim(), () => RecordSaved?.Invoke(this, EventArgs.Empty));

        RecordSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    /// <summary>
    /// Saves a new rental. One starting after today is a reservation and leaves stock alone until it
    /// is checked out; otherwise its units come out of stock now. Availability is the caller's check.
    /// </summary>
    internal static RentalRecord CreateRental(CompanyData companyData, string customerId, string? accountantId,
        List<RentalLineItem> lines, DateTime start, DateTime due, string notes, Action changed)
    {
        companyData.IdCounters.Rental++;
        var rental = new RentalRecord
        {
            Id = $"RNT-{companyData.IdCounters.Rental:D3}",
            Status = start.Date > DateTime.Today ? RentalStatus.Reserved : RentalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        Apply(rental, NewFields(customerId, accountantId, lines, start, due, notes));

        var adjustments = RentalBookings.HoldsStock(rental)
            ? MoveStock(companyData, Units(lines, -1), "Rental", rental.Id)
            : new List<StockAdjustment>();
        companyData.Rentals.Add(rental);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RentalRecordCreated);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Create rental '{rental.Id}'",
            () =>
            {
                companyData.Rentals.Remove(rental);
                ReplayStock(companyData, adjustments, undo: true);
                companyData.MarkAsModified();
                changed();
            },
            () =>
            {
                companyData.Rentals.Add(rental);
                ReplayStock(companyData, adjustments, undo: false);
                companyData.MarkAsModified();
                changed();
                App.CheckAndNotifyRentalOverdue(rental);
            }));

        App.CheckAndNotifyRentalOverdue(rental);
        return rental;
    }

    #endregion

    #region Edit Record

    public void OpenEditModal(RentalRecordDisplayItem? record)
    {
        if (record == null || !record.CanEdit)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var rentalRecord = companyData?.Rentals.FirstOrDefault(r => r.Id == record.Id);
        if (rentalRecord == null)
            return;

        _editingRecord = rentalRecord;
        UpdateDropdownOptions();

        ModalCustomer = AvailableCustomers.FirstOrDefault(c => c.Id == rentalRecord.CustomerId);
        ModalAccountant = AvailableAccountants.FirstOrDefault(a => a.Id == rentalRecord.AccountantId);
        ModalStartDate = new DateTimeOffset(rentalRecord.StartDate);
        ModalDueDate = new DateTimeOffset(rentalRecord.DueDate);
        ModalNotes = rentalRecord.Notes;

        RentalLineItems.Clear();
        foreach (var li in rentalRecord.EffectiveLineItems())
        {
            RentalLineItems.Add(new RentalModalLineItem(this)
            {
                SelectedItem = AvailableItems.FirstOrDefault(i => i.Id == li.RentalItemId),
                Quantity = li.Quantity.ToString(),
                RateType = li.RateType.ToString(),
                RateAmount = li.RateAmount.ToString("0.00"),
                SecurityDeposit = li.SecurityDeposit.ToString()
            });
        }
        UpdateLineItemTotals();

        // Store original values for change detection
        _original = Capture();

        ClearModalErrors();
        IsEditModalOpen = true;
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingRecord = null;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Edit modal, showing confirmation if changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseEditModalAsync()
    {
        if (HasEditModalChanges)
        {
            if (!await ConfirmDiscardEditsAsync())
                return;
        }

        CloseEditModal();
    }

    [RelayCommand]
    public void SaveEditedRecord()
    {
        if (_editingRecord == null || ModalCustomer == null || !ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var rental = _editingRecord;
        var before = FieldsOf(rental);
        var after = NewFields(ModalCustomer.Id!, ModalAccountant?.Id, BuildLines(),
            ModalStartDate?.DateTime ?? DateTime.Today, ModalDueDate?.DateTime ?? DateTime.Today.AddDays(1), ModalNotes.Trim());

        // Only a rental that is out has stock to move: what it had out goes back and the new lines come out.
        var adjustments = RentalBookings.HoldsStock(rental)
            ? MoveStock(companyData,
                Units(rental.EffectiveLineItems(), 1).Concat(Units(after.Lines, -1))
                    .GroupBy(u => u.RentalItemId)
                    .Select(g => (RentalItemId: g.Key, Units: g.Sum(u => u.Units))),
                "Rental edited", rental.Id)
            : new List<StockAdjustment>();

        Apply(rental, after);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit rental '{rental.Id}'",
            () =>
            {
                Apply(rental, before);
                ReplayStock(companyData, adjustments, undo: true);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                Apply(rental, after);
                ReplayStock(companyData, adjustments, undo: false);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
                App.CheckAndNotifyRentalOverdue(rental);
            }));

        RecordSaved?.Invoke(this, EventArgs.Empty);
        App.CheckAndNotifyRentalOverdue(rental);
        CloseEditModal();
    }

    private sealed record RentalFields(
        string CustomerId, string? AccountantId, string RentalItemId, int Quantity, RateType RateType,
        decimal RateAmount, decimal SecurityDeposit, List<RentalLineItem> Lines, DateTime Start, DateTime Due, string Notes);

    private static RentalFields FieldsOf(RentalRecord r) => new(
        r.CustomerId, r.AccountantId, r.RentalItemId, r.Quantity, r.RateType,
        r.RateAmount, r.SecurityDeposit, [.. r.LineItems], r.StartDate, r.DueDate, r.Notes);

    // The record-level item, quantity, rate and deposit repeat the first line and the totals.
    private static RentalFields NewFields(string customerId, string? accountantId, List<RentalLineItem> lines,
        DateTime start, DateTime due, string notes) => new(
        customerId, accountantId, lines[0].RentalItemId, lines.Sum(li => li.Quantity), lines[0].RateType,
        lines[0].RateAmount, RentalBookings.TotalDeposit(lines), lines, start, due, notes);

    private static void Apply(RentalRecord r, RentalFields f)
    {
        r.CustomerId = f.CustomerId;
        r.AccountantId = f.AccountantId;
        r.RentalItemId = f.RentalItemId;
        r.Quantity = f.Quantity;
        r.RateType = f.RateType;
        r.RateAmount = f.RateAmount;
        r.SecurityDeposit = f.SecurityDeposit;
        r.LineItems = [.. f.Lines];
        r.StartDate = f.Start;
        r.DueDate = f.Due;
        r.Notes = f.Notes;
        r.UpdatedAt = DateTime.UtcNow;
    }

    #endregion

    #region Delete Record

    public async void OpenDeleteConfirm(RentalRecordDisplayItem? record)
    {
        try
        {
            if (record == null)
                return;

            if (!await ConfirmDeleteAsync("Delete Rental Record".Translate(),
                    "Are you sure you want to delete this rental record?\n\nRecord ID: {0}".TranslateFormat(record.Id)))
                return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            var rentalRecord = companyData.Rentals.FirstOrDefault(r => r.Id == record.Id);
            if (rentalRecord == null)
            {
                RecordDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Units still out go back into stock, and revenue recorded for paying it goes too.
            var holdsStock = RentalBookings.HoldsStock(rentalRecord);
            var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == rentalRecord.RevenueId);
            List<StockAdjustment>? adjustments = null;

            RemoveWithUndo(companyData, companyData.Rentals, rentalRecord, $"Delete rental '{rentalRecord.Id}'",
                () => RecordDeleted?.Invoke(this, EventArgs.Empty),
                onRemove: () =>
                {
                    if (adjustments != null)
                        ReplayStock(companyData, adjustments, undo: false);
                    else if (holdsStock)
                        adjustments = MoveStock(companyData, Units(rentalRecord.EffectiveLineItems(), 1), "Rental deleted", rentalRecord.Id);
                    if (revenue != null)
                        RemoveRentalRevenue(companyData, revenue);
                },
                onRestore: () =>
                {
                    if (adjustments != null)
                        ReplayStock(companyData, adjustments, undo: true);
                    if (revenue != null)
                        AddRentalRevenue(companyData, revenue);
                });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "RentalRecord.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Return Modal

    public void OpenReturnModal(RentalRecordDisplayItem? record)
    {
        if (record == null || !record.IsActive)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var rentalRecord = companyData?.Rentals.FirstOrDefault(r => r.Id == record.Id);
        if (rentalRecord == null)
            return;

        _returningRecord = rentalRecord;
        _returnLineCount = rentalRecord.EffectiveLineItems().Count;

        ReturnRecordId = rentalRecord.Id;
        ReturnItemName = record.ItemName;
        ReturnCustomerName = record.CustomerName;
        ReturnQuantity = rentalRecord.Quantity;
        ReturnRateType = rentalRecord.RateType.ToString();
        ReturnRateAmount = rentalRecord.RateAmount;
        ReturnDate = DateTimeOffset.Now;
        ReturnDeposit = rentalRecord.SecurityDeposit;
        ReturnDepositRefund = rentalRecord.SecurityDeposit.ToString("0.00");
        ReturnDepositRefundError = null;
        ReturnExtraCharges = string.Empty;
        ReturnExtraChargesNote = string.Empty;
        ReturnMarkAsPaid = false;
        ReturnNotes = string.Empty;

        ReturnTotalCost = RentalBookings.RentalCost(rentalRecord.EffectiveLineItems(), rentalRecord.StartDate,
            ReturnDate?.DateTime ?? DateTime.Today);

        OnPropertyChanged(nameof(ReturnRateFormatted));
        OnPropertyChanged(nameof(ReturnDepositFormatted));
        OnPropertyChanged(nameof(HasDeposit));
        IsReturnModalOpen = true;
    }

    partial void OnReturnDateChanged(DateTimeOffset? value)
    {
        if (_returningRecord == null)
            return;

        ReturnTotalCost = RentalBookings.RentalCost(_returningRecord.EffectiveLineItems(), _returningRecord.StartDate,
            value?.DateTime ?? DateTime.Today);
    }

    [RelayCommand]
    public void CloseReturnModal()
    {
        IsReturnModalOpen = false;
        _returningRecord = null;
    }

    [RelayCommand]
    public void ConfirmReturn()
    {
        if (_returningRecord == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var rental = _returningRecord;
        var refund = ReturnRefundAmount;
        if (refund < 0 || refund > rental.SecurityDeposit)
        {
            ReturnDepositRefundError = "Enter an amount from 0 to {0}.".TranslateFormat(CurrencyService.Format(rental.SecurityDeposit));
            return;
        }

        // Kept as values rather than read from the modal, which resets whenever it reopens, so redo
        // restores exactly what was confirmed.
        var extraCharges = ReturnExtraChargesAmount;
        var notes = string.IsNullOrWhiteSpace(ReturnNotes) ? rental.Notes
            : string.IsNullOrWhiteSpace(rental.Notes) ? ReturnNotes.Trim()
            : $"{rental.Notes}\n\nReturn notes: {ReturnNotes.Trim()}";
        var before = ReturnFieldsOf(rental);
        var after = new ReturnFields(RentalStatus.Returned, ReturnDate?.DateTime, ReturnTotalCost + extraCharges, refund,
            ReturnMarkAsPaid, extraCharges, extraCharges > 0 ? ReturnExtraChargesNote.Trim() : string.Empty, notes, null);
        ApplyReturn(rental, after);

        var revenueDate = rental.ReturnDate ?? DateTime.Now;
        var kept = rental.SecurityDeposit - refund;
        var keptDeposit = kept > 0 ? CreateKeptDepositRevenue(rental, companyData, revenueDate, kept) : null;
        if (keptDeposit != null)
            AddRentalRevenue(companyData, keptDeposit);

        var paidRevenue = rental.Paid && !rental.HasInvoices
            ? RentalBookings.PaidRevenue(companyData, rental, revenueDate, CurrencyService.CurrentCurrencyCode)
            : null;
        if (paidRevenue != null)
        {
            AddRentalRevenue(companyData, paidRevenue);
            after = after with { RevenueId = paidRevenue.Id };
            rental.RevenueId = paidRevenue.Id;
        }

        var adjustments = MoveStock(companyData, Units(rental.EffectiveLineItems(), 1), "Rental return", rental.Id);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Return rental '{rental.Id}'",
            () =>
            {
                ApplyReturn(rental, before);
                if (keptDeposit != null)
                    RemoveRentalRevenue(companyData, keptDeposit);
                if (paidRevenue != null)
                    RemoveRentalRevenue(companyData, paidRevenue);
                ReplayStock(companyData, adjustments, undo: true);
                companyData.MarkAsModified();
                RecordReturned?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                ApplyReturn(rental, after);
                if (keptDeposit != null)
                    AddRentalRevenue(companyData, keptDeposit);
                if (paidRevenue != null)
                    AddRentalRevenue(companyData, paidRevenue);
                ReplayStock(companyData, adjustments, undo: false);
                companyData.MarkAsModified();
                RecordReturned?.Invoke(this, EventArgs.Empty);
            }));

        RecordReturned?.Invoke(this, EventArgs.Empty);
        CloseReturnModal();
        OfferDepositRefund(companyData, rental, refund);
    }

    private sealed record ReturnFields(
        RentalStatus Status, DateTime? ReturnDate, decimal? TotalCost, decimal? DepositRefunded, bool Paid,
        decimal ExtraCharges, string ExtraChargesNote, string Notes, string? RevenueId);

    private static ReturnFields ReturnFieldsOf(RentalRecord r) => new(
        r.Status, r.ReturnDate, r.TotalCost, r.DepositRefunded, r.Paid, r.ExtraCharges, r.ExtraChargesNote, r.Notes, r.RevenueId);

    private static void ApplyReturn(RentalRecord r, ReturnFields f)
    {
        r.Status = f.Status;
        r.ReturnDate = f.ReturnDate;
        r.TotalCost = f.TotalCost;
        r.DepositRefunded = f.DepositRefunded;
        r.Paid = f.Paid;
        r.ExtraCharges = f.ExtraCharges;
        r.ExtraChargesNote = f.ExtraChargesNote;
        r.Notes = f.Notes;
        r.RevenueId = f.RevenueId;
        r.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// A deposit the business keeps is earned, so it becomes revenue on the day the rental comes back
    /// (docs/Calculations.md §4). Only a deposit billed on an invoice is in the books, so a rental with
    /// no invoice has nothing to move. Priced at the invoice's rate, as the invoice's refunds are.
    /// </summary>
    private static Revenue? CreateKeptDepositRevenue(RentalRecord rental, CompanyData companyData, DateTime date, decimal kept)
    {
        var invoice = DepositInvoice(rental, companyData);
        if (invoice == null)
            return null;

        var amount = Math.Min(kept, SecurityDeposits.StillHeld(invoice, companyData.Payments, companyData.Revenues));
        if (amount <= 0)
            return null;

        return new Revenue
        {
            Id = new Core.Data.IdGenerator(companyData).NextRevenueId(date),
            Date = date,
            CustomerId = invoice.CustomerId,
            Description = $"Kept security deposit, rental {rental.Id}",
            Quantity = 1,
            UnitPrice = amount,
            Subtotal = amount,
            Amount = amount,
            Total = amount,
            PaymentMethod = PaymentMethod.Other,
            PaymentStatus = InvoiceTotalsService.IsPaidInFull(invoice) ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Unpaid,
            InvoiceId = invoice.Id,
            ReferenceNumber = invoice.InvoiceNumber,
            IsKeptDeposit = true,
            OriginalCurrency = invoice.OriginalCurrency,
            TotalUSD = invoice.Total > 0 ? invoice.EffectiveTotalUSD * amount / invoice.Total : 0,
            IsPendingConversion = invoice.IsPendingConversion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Invoice? DepositInvoice(RentalRecord rental, CompanyData companyData) =>
        rental.InvoiceIds
            .Select(companyData.GetInvoice)
            .OfType<Invoice>()
            .Where(i => i.SecurityDeposit > 0)
            .OrderBy(i => i.IssueDate)
            .FirstOrDefault();

    /// <summary>
    /// A deposit billed on an invoice paid online has to go back through the provider, so its refund
    /// window opens with only the deposit selected. An invoice paid any other way has no refund to record.
    /// </summary>
    private static void OfferDepositRefund(CompanyData companyData, RentalRecord rental, decimal refund)
    {
        var invoice = refund > 0 ? DepositInvoice(rental, companyData) : null;
        if (invoice == null || App.RefundModalsViewModel is not { } refunds)
            return;

        var held = SecurityDeposits.StillHeld(invoice, companyData.Payments, companyData.Revenues);
        var paidOnline = companyData.Payments.Any(p => p.InvoiceId == invoice.Id && !p.IsRefund
            && p.Source == PaymentSource.Online && !string.IsNullOrEmpty(p.ProviderPaymentId));
        if (held > 0 && paidOnline)
            _ = refunds.OpenForInvoiceAsync(companyData, invoice, depositOnly: Math.Min(refund, held),
                reason: $"Security deposit, rental {rental.Id}");
    }

    private static void AddRentalRevenue(CompanyData companyData, Revenue revenue)
    {
        companyData.Revenues.Add(revenue);
        if (!revenue.IsPendingConversion)
            return;

        // A kept deposit is dated on the return, but the money came in with the invoice, so it waits for the invoice's rate.
        var entry = new PendingConversion
        {
            TransactionId = revenue.Id,
            TransactionType = "Revenue",
            OriginalCurrency = revenue.OriginalCurrency,
            TransactionDate = companyData.GetInvoice(revenue.InvoiceId ?? "")?.IssueDate ?? revenue.Date,
            Total = revenue.Total,
            UnitPrice = revenue.UnitPrice
        };
        companyData.PendingConversions.RemoveAll(p => p.TransactionId == revenue.Id);
        companyData.PendingConversions.Add(entry);
        _ = PendingConversionService.Instance?.AddPendingConversionAsync(entry);
    }

    private static void RemoveRentalRevenue(CompanyData companyData, Revenue revenue)
    {
        companyData.Revenues.Remove(revenue);
        if (companyData.PendingConversions.RemoveAll(p => p.TransactionId == revenue.Id) > 0)
            _ = PendingConversionService.Instance?.ForgetAsync([revenue.Id]);
    }

    #endregion

    #region Check Out, Cancel and Paid

    /// <summary>
    /// Starts a reservation, so its units come out of stock. Picked up early, it starts today.
    /// </summary>
    public void CheckOut(RentalRecordDisplayItem? record)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var rental = companyData?.Rentals.FirstOrDefault(r => r.Id == record?.Id);
        if (companyData == null || rental is not { Status: RentalStatus.Reserved })
            return;

        var lines = rental.EffectiveLineItems();
        var start = rental.StartDate.Date > DateTime.Today ? DateTime.Today : rental.StartDate;
        if (RentalBookings.FindShortfalls(companyData, lines, start, rental.DueDate, takesStock: true, rental).Any(s => s != null))
        {
            App.AddNotification("Not Enough Stock".Translate(),
                "There isn't enough in stock to check out rental {0}.".TranslateFormat(rental.Id), NotificationType.Warning);
            return;
        }

        var oldStart = rental.StartDate;
        rental.Status = RentalStatus.Active;
        rental.StartDate = start;
        rental.UpdatedAt = DateTime.UtcNow;
        var adjustments = MoveStock(companyData, Units(lines, -1), "Rental", rental.Id);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Check out rental '{rental.Id}'",
            () =>
            {
                rental.Status = RentalStatus.Reserved;
                rental.StartDate = oldStart;
                ReplayStock(companyData, adjustments, undo: true);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                rental.Status = RentalStatus.Active;
                rental.StartDate = start;
                ReplayStock(companyData, adjustments, undo: false);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            }));

        RecordSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cancels a reservation. Nothing was taken from stock, so nothing goes back.
    /// </summary>
    public void CancelReservation(RentalRecordDisplayItem? record)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var rental = companyData?.Rentals.FirstOrDefault(r => r.Id == record?.Id);
        if (companyData == null || rental is not { Status: RentalStatus.Reserved })
            return;

        rental.Status = RentalStatus.Cancelled;
        rental.UpdatedAt = DateTime.UtcNow;
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Cancel rental '{rental.Id}'",
            () =>
            {
                rental.Status = RentalStatus.Reserved;
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                rental.Status = RentalStatus.Cancelled;
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            }));

        RecordSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marks a rental paid. Without an invoice to carry the money, it is recorded as revenue.
    /// </summary>
    public void MarkAsPaid(RentalRecordDisplayItem? record) => ChangePaid(record, paid: true);

    /// <summary>
    /// Marks a rental unpaid, removing the revenue that marking it paid recorded.
    /// </summary>
    public void MarkAsUnpaid(RentalRecordDisplayItem? record) => ChangePaid(record, paid: false);

    private void ChangePaid(RentalRecordDisplayItem? record, bool paid)
    {
        var found = App.CompanyManager?.CompanyData;
        var rental = found?.Rentals.FirstOrDefault(r => r.Id == record?.Id);
        if (found == null || rental == null || rental.Paid == paid)
            return;

        CompanyData companyData = found;
        var revenue = !paid
            ? companyData.Revenues.FirstOrDefault(r => r.Id == rental.RevenueId)
            : rental.HasInvoices
                ? null
                : RentalBookings.PaidRevenue(companyData, rental, DateTime.Now, CurrencyService.CurrentCurrencyCode);

        void Set(bool isPaid)
        {
            rental.Paid = isPaid;
            rental.RevenueId = isPaid ? revenue?.Id : null;
            rental.UpdatedAt = DateTime.UtcNow;
            if (revenue != null)
            {
                if (isPaid)
                    AddRentalRevenue(companyData, revenue);
                else
                    RemoveRentalRevenue(companyData, revenue);
            }
            companyData.MarkAsModified();
            RecordSaved?.Invoke(this, EventArgs.Empty);
        }

        Set(paid);
        App.UndoRedoManager.RecordAction(new DelegateAction(
            paid ? $"Mark rental '{rental.Id}' as paid" : $"Mark rental '{rental.Id}' as unpaid",
            () => Set(!paid), () => Set(paid)));
    }

    #endregion

    #region View Modal

    public void OpenViewModal(RentalRecordDisplayItem? record)
    {
        if (record == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var rentalRecord = companyData?.Rentals.FirstOrDefault(r => r.Id == record.Id);
        if (rentalRecord == null)
            return;

        var customer = companyData?.Customers.FirstOrDefault(c => c.Id == rentalRecord.CustomerId);
        var accountant = companyData?.Accountants.FirstOrDefault(a => a.Id == rentalRecord.AccountantId);

        ViewRecordId = rentalRecord.Id;
        ViewItemName = RentalBookings.ItemNames(companyData, rentalRecord);
        ViewCustomerName = customer?.Name ?? "Unknown Customer";
        ViewAccountantName = accountant?.Name ?? "-";
        ViewQuantity = rentalRecord.Quantity;
        ViewRateType = rentalRecord.RateType.ToString();
        ViewRateAmount = rentalRecord.RateAmount;
        ViewSecurityDeposit = rentalRecord.SecurityDeposit;
        ViewStartDate = rentalRecord.StartDate;
        ViewDueDate = rentalRecord.DueDate;
        ViewReturnDate = rentalRecord.ReturnDate;
        ViewStatus = rentalRecord.Status.ToString();
        ViewTotalCost = rentalRecord.TotalCost ?? 0;
        ViewDepositRefundedAmount = rentalRecord.DepositRefunded;
        ViewNotes = rentalRecord.Notes;
        ViewDaysOverdue = rentalRecord.EffectiveDaysOverdue;

        ViewExtraChargesText = rentalRecord.ExtraCharges <= 0 ? string.Empty
            : string.IsNullOrWhiteSpace(rentalRecord.ExtraChargesNote)
                ? "Includes {0} in extra charges".TranslateFormat(CurrencyService.Format(rentalRecord.ExtraCharges))
                : "Includes {0} in extra charges: {1}".TranslateFormat(CurrencyService.Format(rentalRecord.ExtraCharges), rentalRecord.ExtraChargesNote);

        ViewLineItems.Clear();
        var effectiveItems = rentalRecord.EffectiveLineItems();
        var days = RentalBookings.ChargeableDays(rentalRecord.StartDate, rentalRecord.ReturnDate ?? rentalRecord.DueDate);
        ViewHasMultipleItems = effectiveItems.Count > 1;
        foreach (var li in effectiveItems)
        {
            ViewLineItems.Add(new RentalViewLineItemDisplay
            {
                ItemName = RentalBookings.ItemName(companyData, li.RentalItemId),
                Quantity = li.Quantity,
                RateType = li.RateType.ToString(),
                RateAmount = li.RateAmount,
                SecurityDeposit = li.SecurityDeposit * li.Quantity,
                Amount = RentalBookings.LineCost(li, days)
            });
        }

        OnPropertyChanged(nameof(ViewRateFormatted));
        OnPropertyChanged(nameof(ViewDepositFormatted));
        OnPropertyChanged(nameof(ViewTotalCostFormatted));
        OnPropertyChanged(nameof(ViewStartDateFormatted));
        OnPropertyChanged(nameof(ViewDueDateFormatted));
        OnPropertyChanged(nameof(ViewReturnDateFormatted));
        OnPropertyChanged(nameof(ViewDepositStatusFormatted));

        IsViewModalOpen = true;
    }

    [RelayCommand]
    public void CloseViewModal()
    {
        IsViewModalOpen = false;
    }

    #endregion

    #region Filter Modal

    private sealed record FilterValues(
        string Status, string? CustomerId, string? ItemId,
        DateTimeOffset? StartDateFrom, DateTimeOffset? StartDateTo,
        DateTimeOffset? DueDateFrom, DateTimeOffset? DueDateTo)
    {
        public static readonly FilterValues Default = new("All", null, null, null, null, null, null);
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStatus, FilterCustomer?.Id, FilterItem?.Id,
            FilterStartDateFrom, FilterStartDateTo, FilterDueDateFrom, FilterDueDateTo),
        v =>
        {
            FilterStatus = v.Status;
            FilterCustomer = FilterCustomerOptions.FirstOrDefault(c => c.Id == v.CustomerId);
            FilterItem = FilterItemOptions.FirstOrDefault(i => i.Id == v.ItemId);
            FilterStartDateFrom = v.StartDateFrom;
            FilterStartDateTo = v.StartDateTo;
            FilterDueDateFrom = v.DueDateFrom;
            FilterDueDateTo = v.DueDateTo;
        });

    public bool HasFilterModalChanges => Filters.HasChanges;

    [RelayCommand]
    public void OpenFilterModal()
    {
        LoadFilterOptions();
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>
    /// Closes the filter modal, asking first and putting the filters back if they were changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    [RelayCommand]
    public void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        Filters.Reset();
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region Modal Helpers

    private void UpdateDropdownOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        AvailableItems.Clear();
        foreach (var rentalItem in companyData.RentalInventory.Where(i => i.Status == EntityStatus.Active))
        {
            var invItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == rentalItem.InventoryItemId);
            var product = invItem != null ? companyData.Products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
            var displayName = product?.Name ?? "Unknown Item";
            AvailableItems.Add(new RentalItemOption
            {
                Id = rentalItem.Id,
                Name = displayName,
                AvailableQuantity = (int)(invItem?.InStock ?? 0)
            });
        }
        // Sort by name after building
        var sorted = AvailableItems.OrderBy(i => i.Name).ToList();
        AvailableItems.Clear();
        foreach (var item in sorted)
            AvailableItems.Add(item);

        OptionLoader.Fill(AvailableCustomers, OptionLoader.Customers(companyData, activeOnly: true).AsOptions<CustomerOption>());
        OptionLoader.Fill(AvailableAccountants, OptionLoader.Accountants(companyData).AsOptions<AccountantOption>());
    }

    // Every customer and item, not only active ones, so past rentals of either can still be found.
    private void LoadFilterOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var customerId = FilterCustomer?.Id;
        var itemId = FilterItem?.Id;
        OptionLoader.Fill(FilterCustomerOptions, OptionLoader.Customers(companyData).AsOptions<CustomerOption>());
        OptionLoader.Fill(FilterItemOptions, companyData.RentalInventory
            .Select(i => new RentalItemOption { Id = i.Id, Name = RentalBookings.ItemName(companyData, i.Id) })
            .OrderBy(i => i.Name));
        FilterCustomer = FilterCustomerOptions.FirstOrDefault(c => c.Id == customerId);
        FilterItem = FilterItemOptions.FirstOrDefault(i => i.Id == itemId);
    }

    private void ClearModalFields()
    {
        ModalCustomer = null;
        ModalAccountant = null;
        ModalStartDate = DateTimeOffset.Now;
        ModalDueDate = DateTimeOffset.Now.AddDays(1);
        ModalNotes = string.Empty;
        RentalLineItems.Clear();
        AddRentalLineItem();
        ClearModalErrors();
    }

    private void ClearModalErrors()
    {
        ModalCustomerError = null;
        ModalLineItemsError = null;
        ModalDueDateError = null;
        foreach (var li in RentalLineItems)
        {
            li.HasItemError = false;
            li.ItemError = null;
            li.QuantityError = null;
            li.RateError = null;
        }
    }

    private bool ValidateModal()
    {
        ClearModalErrors();
        var isValid = true;

        if (ModalCustomer == null)
        {
            ModalCustomerError = "Please select a customer.".Translate();
            isValid = false;
        }

        if (RentalLineItems.Count == 0)
        {
            ModalLineItemsError = "Please add at least one line item.".Translate();
            isValid = false;
        }

        var start = ModalStartDate?.DateTime ?? DateTime.Today;
        var due = ModalDueDate?.DateTime ?? DateTime.Today.AddDays(1);
        if (due.Date < start.Date)
        {
            ModalDueDateError = "The due date can't be before the start date.".Translate();
            isValid = false;
        }

        foreach (var li in RentalLineItems)
        {
            if (li.SelectedItem == null)
            {
                li.HasItemError = true;
                li.ItemError = "Please select an item.".Translate();
                isValid = false;
            }

            if (!int.TryParse(li.Quantity, out var qty) || qty <= 0)
            {
                li.QuantityError = "Invalid quantity.".Translate();
                isValid = false;
            }
        }

        var companyData = App.CompanyManager?.CompanyData;
        if (!isValid || companyData == null)
            return false;

        var takesStock = _editingRecord != null ? RentalBookings.HoldsStock(_editingRecord) : start.Date <= DateTime.Today;
        var shortfalls = RentalBookings.FindShortfalls(companyData, BuildLines(), start, due, takesStock, _editingRecord);
        for (var i = 0; i < shortfalls.Length; i++)
        {
            if (shortfalls[i] is not { } available)
                continue;
            RentalLineItems[i].QuantityError = "Only {0} available for these dates.".TranslateFormat(available);
            isValid = false;
        }

        return isValid;
    }

    private List<RentalLineItem> BuildLines() => RentalLineItems.Select(li => li.ToLine()).ToList();

    private static IEnumerable<(string RentalItemId, int Units)> Units(IEnumerable<RentalLineItem> lines, int sign) =>
        lines.Select(li => (li.RentalItemId, sign * li.Quantity));

    /// <summary>
    /// Moves each item's units into stock (positive) or out of it (negative) and records the adjustments.
    /// </summary>
    private static List<StockAdjustment> MoveStock(CompanyData companyData,
        IEnumerable<(string RentalItemId, int Units)> changes, string reason, string rentalId)
    {
        var adjustments = new List<StockAdjustment>();
        foreach (var (rentalItemId, units) in changes)
        {
            var stock = RentalBookings.StockFor(companyData, rentalItemId);
            if (stock == null || units == 0)
                continue;

            var previous = stock.InStock;
            stock.InStock += units;
            stock.Status = stock.CalculateStatus();
            stock.LastUpdated = DateTime.UtcNow;
            App.CheckAndNotifyStockStatus(stock, previous);

            companyData.IdCounters.StockAdjustment++;
            var adjustment = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = stock.Id,
                AdjustmentType = units > 0 ? AdjustmentType.Add : AdjustmentType.Remove,
                Quantity = Math.Abs(units),
                PreviousStock = previous,
                NewStock = stock.InStock,
                Reason = reason,
                ReferenceNumber = rentalId,
                Timestamp = DateTime.UtcNow,
                IsAutoGenerated = true
            };
            adjustments.Add(adjustment);
            companyData.StockAdjustments.Add(adjustment);
        }

        return adjustments;
    }

    /// <summary>Takes back what <see cref="MoveStock"/> moved on undo, and moves it again on redo.</summary>
    private static void ReplayStock(CompanyData companyData, List<StockAdjustment> adjustments, bool undo)
    {
        foreach (var adjustment in undo ? Enumerable.Reverse(adjustments) : adjustments)
        {
            var stock = companyData.Inventory.FirstOrDefault(i => i.Id == adjustment.InventoryItemId);
            if (stock != null)
            {
                var previous = stock.InStock;
                var units = adjustment.AdjustmentType == AdjustmentType.Add ? adjustment.Quantity : -adjustment.Quantity;
                stock.InStock += undo ? -units : units;
                stock.Status = stock.CalculateStatus();
                stock.LastUpdated = DateTime.UtcNow;
                App.CheckAndNotifyStockStatus(stock, previous);
            }

            if (undo)
                companyData.StockAdjustments.Remove(adjustment);
            else
                companyData.StockAdjustments.Add(adjustment);
        }
    }

    #endregion

}

/// <summary>
/// Option model for rental item dropdown.
/// </summary>
public class RentalItemOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }

    public override string ToString() => $"{Name} ({AvailableQuantity} available)";
}

/// <summary>
/// ViewModel for a single line item in the rental Add/Edit modal.
/// </summary>
public partial class RentalModalLineItem : ObservableObject
{
    private readonly RentalRecordsModalsViewModel _parent;

    public RentalModalLineItem(RentalRecordsModalsViewModel parent)
    {
        _parent = parent;
    }

    [ObservableProperty]
    private RentalItemOption? _selectedItem;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _rateType = "Daily";

    [ObservableProperty]
    private string _rateAmount = string.Empty;

    [ObservableProperty]
    private string _securityDeposit = string.Empty;

    [ObservableProperty]
    private bool _hasItemError;

    [ObservableProperty]
    private string? _itemError;

    [ObservableProperty]
    private string? _quantityError;

    [ObservableProperty]
    private string? _rateError;

    public decimal Amount => RentalBookings.LineCost(ToLine(), _parent.ModalDays);

    public RentalLineItem ToLine() => new()
    {
        RentalItemId = SelectedItem?.Id ?? string.Empty,
        Quantity = int.TryParse(Quantity, out var q) ? q : 0,
        RateType = Enum.TryParse<Core.Enums.RateType>(RateType, out var type) ? type : Core.Enums.RateType.Daily,
        RateAmount = decimal.TryParse(RateAmount, out var r) ? r : 0,
        SecurityDeposit = decimal.TryParse(SecurityDeposit, out var d) ? d : 0
    };

    public void RefreshAmount()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountFormatted));
    }

    public string AmountFormatted => CurrencyService.Format(Amount);
    public string RateAmountDisplay => decimal.TryParse(RateAmount, out var r) ? CurrencyService.Format(r) : "-";
    public string SecurityDepositDisplay => decimal.TryParse(SecurityDeposit, out var d) ? CurrencyService.Format(d) : "-";

    partial void OnSelectedItemChanged(RentalItemOption? value)
    {
        if (value != null)
        {
            HasItemError = false;
            ItemError = null;

            // Auto-populate rate and deposit from the selected item
            var companyData = App.CompanyManager?.CompanyData;
            var item = companyData?.RentalInventory.FirstOrDefault(i => i.Id == value.Id);
            if (item != null)
            {
                RateAmount = RateType switch
                {
                    "Weekly" => item.WeeklyRate.ToString("0.00"),
                    "Monthly" => item.MonthlyRate.ToString("0.00"),
                    _ => item.DailyRate.ToString("0.00")
                };
                SecurityDeposit = item.SecurityDeposit.ToString("0.00");
                OnPropertyChanged(nameof(SecurityDepositDisplay));
            }
        }

        _parent.UpdateLineItemTotals();
    }

    partial void OnQuantityChanged(string value)
    {
        if (int.TryParse(value, out var qty) && qty > 0)
            QuantityError = null;

        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountFormatted));
        _parent.UpdateLineItemTotals();
    }

    partial void OnRateAmountChanged(string value)
    {
        if (decimal.TryParse(value, out var rate) && rate >= 0)
            RateError = null;

        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountFormatted));
        OnPropertyChanged(nameof(RateAmountDisplay));
        _parent.UpdateLineItemTotals();
    }

    partial void OnRateTypeChanged(string value)
    {
        if (SelectedItem != null)
        {
            var companyData = App.CompanyManager?.CompanyData;
            var item = companyData?.RentalInventory.FirstOrDefault(i => i.Id == SelectedItem.Id);
            if (item != null)
            {
                RateAmount = value switch
                {
                    "Weekly" => item.WeeklyRate.ToString("0.00"),
                    "Monthly" => item.MonthlyRate.ToString("0.00"),
                    _ => item.DailyRate.ToString("0.00")
                };
            }
        }

        RefreshAmount();
        _parent.UpdateLineItemTotals();
    }
}

/// <summary>
/// Display model for line items in the view modal.
/// </summary>
public class RentalViewLineItemDisplay
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string RateType { get; set; } = string.Empty;
    public decimal RateAmount { get; set; }
    public decimal SecurityDeposit { get; set; }
    public decimal Amount { get; set; }
    public string RateFormatted => $"{CurrencyService.Format(RateAmount)}/{RateType}";
    public string DepositFormatted => CurrencyService.Format(SecurityDeposit);
    public string AmountFormatted => CurrencyService.Format(Amount);
}
