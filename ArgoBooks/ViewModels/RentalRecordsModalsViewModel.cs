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
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText))]
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
    private DateTimeOffset? _modalStartDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset? _modalDueDate = DateTimeOffset.Now.AddDays(1);

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
    private string _totalSecurityDeposit = "$0.00";

    /// <summary>
    /// Total estimated amount across all line items.
    /// </summary>
    [ObservableProperty]
    private string _totalEstimatedAmount = "$0.00";

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
        var totalDeposit = RentalLineItems.Sum(li => decimal.TryParse(li.SecurityDeposit, out var d) ? d : 0);
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
    private decimal _returnTotalCost;

    [ObservableProperty]
    private decimal _returnDeposit;

    [ObservableProperty]
    private bool _returnRefundDeposit = true;

    [ObservableProperty]
    private string _returnNotes = string.Empty;

    [ObservableProperty]
    private bool _returnMarkAsPaid;

    private RentalRecord? _returningRecord;

    public string ReturnRateFormatted => $"{CurrencyService.Format(ReturnRateAmount)}/{ReturnRateType}";
    public string ReturnTotalCostFormatted => CurrencyService.Format(ReturnTotalCost);
    public string ReturnDepositFormatted => CurrencyService.Format(ReturnDeposit);
    public bool HasDeposit => ReturnDeposit > 0;

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
        ViewDepositRefundedAmount is > 0
            ? $"Refunded (${ViewDepositRefundedAmount.Value:N2})"
            : ViewStatus == "Returned"
                ? (ViewDepositRefundedAmount == 0 ? "Not Refunded" : "Refunded")
                : "Held";

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCustomer;

    [ObservableProperty]
    private string? _filterItem;

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
        if (companyData == null || ModalCustomer == null || RentalLineItems.Count == 0)
            return;

        // Build line items and check availability
        var lineItems = new List<RentalLineItem>();
        var inventoryChanges = new List<(InventoryItem InvItem, decimal OldInStock, int QtyChange)>();

        var hasAvailabilityIssue = false;
        var requestedByInvItem = new Dictionary<string, int>();
        foreach (var li in RentalLineItems)
        {
            if (li.SelectedItem == null)
            {
                li.HasItemError = true;
                li.ItemError = "Please select an item.".Translate();
                hasAvailabilityIssue = true;
                continue;
            }
            var rentQty = int.TryParse(li.Quantity, out var q) ? q : 1;
            var rentalItem = companyData.RentalInventory.FirstOrDefault(i => i.Id == li.SelectedItem.Id);
            var inventoryItem = rentalItem != null
                ? companyData.Inventory.FirstOrDefault(inv => inv.Id == rentalItem.InventoryItemId)
                : null;
            var alreadyRequested = inventoryItem != null ? requestedByInvItem.GetValueOrDefault(inventoryItem.Id) : 0;
            if (rentalItem == null || inventoryItem == null || inventoryItem.InStock - alreadyRequested < rentQty)
            {
                li.QuantityError = inventoryItem == null ? "Item not found." : $"Only {Math.Max(0, (int)inventoryItem.InStock - alreadyRequested)} available.";
                hasAvailabilityIssue = true;
                continue;
            }
            requestedByInvItem[inventoryItem.Id] = alreadyRequested + rentQty;

            lineItems.Add(new RentalLineItem
            {
                RentalItemId = li.SelectedItem.Id,
                Quantity = rentQty,
                RateType = li.RateType switch
                {
                    "Weekly" => RateType.Weekly,
                    "Monthly" => RateType.Monthly,
                    _ => RateType.Daily
                },
                RateAmount = decimal.TryParse(li.RateAmount, out var r) ? r : 0,
                SecurityDeposit = decimal.TryParse(li.SecurityDeposit, out var d) ? d : 0
            });

            inventoryChanges.Add((inventoryItem, inventoryItem.InStock, rentQty));
        }

        if (hasAvailabilityIssue)
            return;

        companyData.IdCounters.Rental++;
        var newId = $"RNT-{companyData.IdCounters.Rental:D3}";

        // Use first line item for top-level backward-compatible fields
        var firstLi = lineItems[0];
        var totalDeposit = lineItems.Sum(li => li.SecurityDeposit * li.Quantity);
        var totalQty = lineItems.Sum(li => li.Quantity);

        var newRecord = new RentalRecord
        {
            Id = newId,
            RentalItemId = firstLi.RentalItemId,
            CustomerId = ModalCustomer!.Id!,
            AccountantId = ModalAccountant?.Id,
            Quantity = totalQty,
            RateType = firstLi.RateType,
            RateAmount = firstLi.RateAmount,
            SecurityDeposit = totalDeposit,
            LineItems = lineItems,
            StartDate = ModalStartDate?.DateTime ?? DateTime.Today,
            DueDate = ModalDueDate?.DateTime ?? DateTime.Today.AddDays(1),
            Status = RentalStatus.Active,
            Notes = ModalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Update inventory quantities and create stock adjustments
        var stockAdjustments = new List<StockAdjustment>();
        foreach (var (invItem, _, qtyChange) in inventoryChanges)
        {
            var previousStock = invItem.InStock;
            invItem.InStock -= qtyChange;
            invItem.Status = invItem.CalculateStatus();
            invItem.LastUpdated = DateTime.UtcNow;
            App.CheckAndNotifyStockStatus(invItem, previousStock);

            companyData.IdCounters.StockAdjustment++;
            var adj = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = invItem.Id,
                AdjustmentType = AdjustmentType.Remove,
                Quantity = qtyChange,
                PreviousStock = previousStock,
                NewStock = invItem.InStock,
                Reason = "Rental",
                ReferenceNumber = newId,
                Timestamp = DateTime.UtcNow,
                IsAutoGenerated = true
            };
            stockAdjustments.Add(adj);
            companyData.StockAdjustments.Add(adj);
        }

        companyData.Rentals.Add(newRecord);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RentalRecordCreated);
        companyData.MarkAsModified();

        var recordToUndo = newRecord;
        var invChanges = inventoryChanges;
        var savedAdjustments = stockAdjustments;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Create rental '{newId}'",
            () =>
            {
                companyData.Rentals.Remove(recordToUndo);
                foreach (var (invItem, oldInStock, _) in invChanges)
                {
                    invItem.InStock = oldInStock;
                    invItem.Status = invItem.CalculateStatus();
                    invItem.LastUpdated = DateTime.UtcNow;
                }
                foreach (var adj in savedAdjustments)
                    companyData.StockAdjustments.RemoveAll(a => a.Id == adj.Id);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Rentals.Add(recordToUndo);
                foreach (var (invItem, _, qtyChange) in invChanges)
                {
                    var stockBeforeRedo = invItem.InStock;
                    invItem.InStock -= qtyChange;
                    invItem.Status = invItem.CalculateStatus();
                    invItem.LastUpdated = DateTime.UtcNow;
                    App.CheckAndNotifyStockStatus(invItem, stockBeforeRedo);
                }
                foreach (var adj in savedAdjustments)
                    companyData.StockAdjustments.Add(adj);
                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
                App.CheckAndNotifyRentalOverdue(recordToUndo);
            }));

        RecordSaved?.Invoke(this, EventArgs.Empty);
        App.CheckAndNotifyRentalOverdue(newRecord);
        CloseAddModal();
    }

    #endregion

    #region Edit Record

    public void OpenEditModal(RentalRecordDisplayItem? record)
    {
        if (record == null || !record.IsActive)
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

        // Populate line items from record
        RentalLineItems.Clear();
        if (rentalRecord.LineItems.Count > 0)
        {
            foreach (var li in rentalRecord.LineItems)
            {
                var lineItem = new RentalModalLineItem(this)
                {
                    SelectedItem = AvailableItems.FirstOrDefault(i => i.Id == li.RentalItemId),
                    Quantity = li.Quantity.ToString(),
                    RateType = li.RateType.ToString(),
                    RateAmount = li.RateAmount.ToString("0.00"),
                    SecurityDeposit = li.SecurityDeposit.ToString("0.00")
                };
                RentalLineItems.Add(lineItem);
            }
        }
        else
        {
            // Legacy single-item record
            var lineItem = new RentalModalLineItem(this)
            {
                SelectedItem = AvailableItems.FirstOrDefault(i => i.Id == rentalRecord.RentalItemId),
                Quantity = rentalRecord.Quantity.ToString(),
                RateType = rentalRecord.RateType.ToString(),
                RateAmount = rentalRecord.RateAmount.ToString("0.00"),
                SecurityDeposit = rentalRecord.SecurityDeposit.ToString("0.00")
            };
            RentalLineItems.Add(lineItem);
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
        if (!ValidateModal() || _editingRecord == null || ModalCustomer == null || RentalLineItems.Count == 0)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Save old values for undo
        var oldCustomerId = _editingRecord.CustomerId;
        var oldAccountantId = _editingRecord.AccountantId;
        var oldItemId = _editingRecord.RentalItemId;
        var oldQty = _editingRecord.Quantity;
        var oldRateType = _editingRecord.RateType;
        var oldRateAmount = _editingRecord.RateAmount;
        var oldDeposit = _editingRecord.SecurityDeposit;
        var oldLineItems = _editingRecord.LineItems.Select(li => new RentalLineItem
        {
            RentalItemId = li.RentalItemId, Quantity = li.Quantity,
            RateType = li.RateType, RateAmount = li.RateAmount, SecurityDeposit = li.SecurityDeposit
        }).ToList();
        var oldStartDate = _editingRecord.StartDate;
        var oldDueDate = _editingRecord.DueDate;
        var oldNotes = _editingRecord.Notes;

        var newLineItems = new List<RentalLineItem>();
        foreach (var li in RentalLineItems)
        {
            if (li.SelectedItem == null) return;
            newLineItems.Add(new RentalLineItem
            {
                RentalItemId = li.SelectedItem.Id,
                Quantity = int.TryParse(li.Quantity, out var q) ? q : 1,
                RateType = li.RateType switch
                {
                    "Weekly" => RateType.Weekly,
                    "Monthly" => RateType.Monthly,
                    _ => RateType.Daily
                },
                RateAmount = decimal.TryParse(li.RateAmount, out var r) ? r : 0,
                SecurityDeposit = decimal.TryParse(li.SecurityDeposit, out var d) ? d : 0
            });
        }

        // Calculate inventory changes via InventoryItem.InStock. A "Rent Out" record has no line
        // items, so the units it has out come from its top-level fields.
        var effectiveOldLineItems = GetEffectiveLineItems(_editingRecord);

        // Compute net quantity diff per InventoryItem
        var oldQtyByInvItem = new Dictionary<string, int>();
        foreach (var oldLi in effectiveOldLineItems)
        {
            var ri = companyData.RentalInventory.FirstOrDefault(i => i.Id == oldLi.RentalItemId);
            var invItem = ri != null ? companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
            if (invItem != null)
                oldQtyByInvItem[invItem.Id] = oldQtyByInvItem.GetValueOrDefault(invItem.Id) + oldLi.Quantity;
        }
        var newQtyByInvItem = new Dictionary<string, int>();
        foreach (var newLi in newLineItems)
        {
            var ri = companyData.RentalInventory.FirstOrDefault(i => i.Id == newLi.RentalItemId);
            var invItem = ri != null ? companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
            if (invItem != null)
                newQtyByInvItem[invItem.Id] = newQtyByInvItem.GetValueOrDefault(invItem.Id) + newLi.Quantity;
        }

        // Collect all affected InventoryItem IDs
        var allInvItemIds = oldQtyByInvItem.Keys.Union(newQtyByInvItem.Keys).ToList();
        var oldInStockSnapshot = new Dictionary<string, decimal>();
        var editAdjustments = new List<StockAdjustment>();

        // Guard before mutating anything: raising a rental's quantity must not drive an inventory item
        // negative. ValidateModal only runs the availability check for NEW records, so edits would
        // otherwise subtract past zero. Aborting here (no mutation has happened yet) is clean.
        foreach (var invItemId in allInvItemIds)
        {
            var invItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == invItemId);
            if (invItem == null) continue;

            var requiredExtra = newQtyByInvItem.GetValueOrDefault(invItemId) - oldQtyByInvItem.GetValueOrDefault(invItemId);
            if (requiredExtra > 0 && invItem.InStock < requiredExtra)
            {
                ModalLineItemsError = $"Only {invItem.InStock} more available to add to this rental.";
                return;
            }
        }

        foreach (var invItemId in allInvItemIds)
        {
            var invItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == invItemId);
            if (invItem == null) continue;

            oldInStockSnapshot[invItemId] = invItem.InStock;
            var oldQ = oldQtyByInvItem.GetValueOrDefault(invItemId);
            var newQ = newQtyByInvItem.GetValueOrDefault(invItemId);
            var netDiff = newQ - oldQ; // positive = need more stock removed

            if (netDiff == 0) continue;

            var previousStock = invItem.InStock;
            invItem.InStock -= netDiff;
            invItem.Status = invItem.CalculateStatus();
            invItem.LastUpdated = DateTime.UtcNow;
            App.CheckAndNotifyStockStatus(invItem, previousStock);

            companyData.IdCounters.StockAdjustment++;
            var adj = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = invItemId,
                AdjustmentType = netDiff > 0 ? AdjustmentType.Remove : AdjustmentType.Add,
                Quantity = Math.Abs(netDiff),
                PreviousStock = previousStock,
                NewStock = invItem.InStock,
                Reason = "Rental edited",
                ReferenceNumber = _editingRecord.Id,
                Timestamp = DateTime.UtcNow,
                IsAutoGenerated = true
            };
            editAdjustments.Add(adj);
            companyData.StockAdjustments.Add(adj);
        }

        // Update record
        var recordToEdit = _editingRecord;
        var firstLi = newLineItems[0];
        var newCustomerId = ModalCustomer!.Id;
        var newAccountantId = ModalAccountant?.Id;
        var newStartDate = ModalStartDate?.DateTime ?? DateTime.Today;
        var newDueDate = ModalDueDate?.DateTime ?? DateTime.Today.AddDays(1);
        var newNotes = ModalNotes.Trim();
        var totalDeposit = newLineItems.Sum(li => li.SecurityDeposit * li.Quantity);
        var totalQty = newLineItems.Sum(li => li.Quantity);

        recordToEdit.RentalItemId = firstLi.RentalItemId;
        recordToEdit.CustomerId = newCustomerId!;
        recordToEdit.AccountantId = newAccountantId;
        recordToEdit.Quantity = totalQty;
        recordToEdit.RateType = firstLi.RateType;
        recordToEdit.RateAmount = firstLi.RateAmount;
        recordToEdit.SecurityDeposit = totalDeposit;
        recordToEdit.LineItems = newLineItems;
        recordToEdit.StartDate = newStartDate;
        recordToEdit.DueDate = newDueDate;
        recordToEdit.Notes = newNotes;
        recordToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        var savedOldInStock = oldInStockSnapshot;
        var savedEditAdjustments = editAdjustments;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit rental '{recordToEdit.Id}'",
            () =>
            {
                recordToEdit.RentalItemId = oldItemId;
                recordToEdit.CustomerId = oldCustomerId;
                recordToEdit.AccountantId = oldAccountantId;
                recordToEdit.Quantity = oldQty;
                recordToEdit.RateType = oldRateType;
                recordToEdit.RateAmount = oldRateAmount;
                recordToEdit.SecurityDeposit = oldDeposit;
                recordToEdit.LineItems = oldLineItems;
                recordToEdit.StartDate = oldStartDate;
                recordToEdit.DueDate = oldDueDate;
                recordToEdit.Notes = oldNotes;

                // Restore old InStock values
                foreach (var (id, oldStock) in savedOldInStock)
                {
                    var invItem = companyData.Inventory.FirstOrDefault(i => i.Id == id);
                    if (invItem != null)
                    {
                        var stockBeforeUndo = invItem.InStock;
                        invItem.InStock = oldStock;
                        invItem.Status = invItem.CalculateStatus();
                        invItem.LastUpdated = DateTime.UtcNow;
                        App.CheckAndNotifyStockStatus(invItem, stockBeforeUndo);
                    }
                }
                foreach (var adj in savedEditAdjustments)
                    companyData.StockAdjustments.RemoveAll(a => a.Id == adj.Id);

                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                recordToEdit.RentalItemId = firstLi.RentalItemId;
                recordToEdit.CustomerId = newCustomerId!;
                recordToEdit.AccountantId = newAccountantId;
                recordToEdit.Quantity = totalQty;
                recordToEdit.RateType = firstLi.RateType;
                recordToEdit.RateAmount = firstLi.RateAmount;
                recordToEdit.SecurityDeposit = totalDeposit;
                recordToEdit.LineItems = newLineItems;
                recordToEdit.StartDate = newStartDate;
                recordToEdit.DueDate = newDueDate;
                recordToEdit.Notes = newNotes;

                // Re-apply inventory changes
                foreach (var invItemId in allInvItemIds)
                {
                    var invItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == invItemId);
                    if (invItem == null) continue;
                    var oldQ = oldQtyByInvItem.GetValueOrDefault(invItemId);
                    var newQ = newQtyByInvItem.GetValueOrDefault(invItemId);
                    var netDiff = newQ - oldQ;
                    if (netDiff == 0) continue;
                    var stockBeforeRedo = invItem.InStock;
                    invItem.InStock -= netDiff;
                    invItem.Status = invItem.CalculateStatus();
                    invItem.LastUpdated = DateTime.UtcNow;
                    App.CheckAndNotifyStockStatus(invItem, stockBeforeRedo);
                }
                foreach (var adj in savedEditAdjustments)
                    companyData.StockAdjustments.Add(adj);

                companyData.MarkAsModified();
                RecordSaved?.Invoke(this, EventArgs.Empty);
                App.CheckAndNotifyRentalOverdue(recordToEdit);
            }));

        RecordSaved?.Invoke(this, EventArgs.Empty);
        App.CheckAndNotifyRentalOverdue(recordToEdit);
        CloseEditModal();
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

            // Units still out go back into stock. The first removal records the stock each item
            // started from and writes the adjustments; a redo reapplies the same adjustments.
            var wasActive = rentalRecord.Status == RentalStatus.Active || rentalRecord.Status == RentalStatus.Overdue;
            var stockBefore = new Dictionary<string, decimal>();
            var adjustments = new List<StockAdjustment>();
            var isRedo = false;

            RemoveWithUndo(companyData, companyData.Rentals, rentalRecord, $"Delete rental '{rentalRecord.Id}'",
                () => RecordDeleted?.Invoke(this, EventArgs.Empty),
                onRemove: () =>
                {
                    if (!wasActive) return;

                    foreach (var li in GetEffectiveLineItems(rentalRecord))
                    {
                        var ri = companyData.RentalInventory.FirstOrDefault(i => i.Id == li.RentalItemId);
                        var invItem = ri != null ? companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
                        if (invItem == null) continue;

                        var previousStock = invItem.InStock;
                        invItem.InStock += li.Quantity;
                        invItem.Status = invItem.CalculateStatus();
                        invItem.LastUpdated = DateTime.UtcNow;
                        if (isRedo) continue;

                        stockBefore.TryAdd(invItem.Id, previousStock);
                        companyData.IdCounters.StockAdjustment++;
                        adjustments.Add(new StockAdjustment
                        {
                            Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                            InventoryItemId = invItem.Id,
                            AdjustmentType = AdjustmentType.Add,
                            Quantity = li.Quantity,
                            PreviousStock = previousStock,
                            NewStock = invItem.InStock,
                            Reason = "Rental deleted",
                            ReferenceNumber = rentalRecord.Id,
                            Timestamp = DateTime.UtcNow,
                            IsAutoGenerated = true
                        });
                    }

                    companyData.StockAdjustments.AddRange(adjustments);
                    isRedo = true;
                },
                onRestore: () =>
                {
                    if (!wasActive) return;

                    foreach (var (id, oldStock) in stockBefore)
                    {
                        var invItem = companyData.Inventory.FirstOrDefault(i => i.Id == id);
                        if (invItem == null) continue;

                        var stockBeforeUndo = invItem.InStock;
                        invItem.InStock = oldStock;
                        invItem.Status = invItem.CalculateStatus();
                        invItem.LastUpdated = DateTime.UtcNow;
                        App.CheckAndNotifyStockStatus(invItem, stockBeforeUndo);
                    }
                    foreach (var adj in adjustments)
                        companyData.StockAdjustments.RemoveAll(a => a.Id == adj.Id);
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

        ReturnRecordId = rentalRecord.Id;
        ReturnItemName = record.ItemName;
        ReturnCustomerName = record.CustomerName;
        ReturnQuantity = rentalRecord.Quantity;
        ReturnRateType = rentalRecord.RateType.ToString();
        ReturnRateAmount = rentalRecord.RateAmount;
        ReturnDate = DateTimeOffset.Now;
        ReturnDeposit = rentalRecord.SecurityDeposit;
        ReturnRefundDeposit = true;
        ReturnMarkAsPaid = false;
        ReturnNotes = string.Empty;

        ReturnTotalCost = CalculateReturnCost(rentalRecord, ReturnDate?.DateTime ?? DateTime.Today);

        OnPropertyChanged(nameof(ReturnRateFormatted));
        OnPropertyChanged(nameof(ReturnTotalCostFormatted));
        OnPropertyChanged(nameof(ReturnDepositFormatted));
        OnPropertyChanged(nameof(HasDeposit));
        IsReturnModalOpen = true;
    }

    partial void OnReturnDateChanged(DateTimeOffset? value)
    {
        if (_returningRecord == null)
            return;

        ReturnTotalCost = CalculateReturnCost(_returningRecord, value?.DateTime ?? DateTime.Today);
        OnPropertyChanged(nameof(ReturnTotalCostFormatted));
    }

    private static decimal CalculateReturnCost(RentalRecord record, DateTime returnDate)
    {
        var days = (returnDate - record.StartDate).Days;
        if (days < 1) days = 1;

        return GetEffectiveLineItems(record).Sum(li => li.RateType switch
        {
            RateType.Daily => li.RateAmount * days * li.Quantity,
            RateType.Weekly => li.RateAmount * (decimal)Math.Ceiling(days / 7.0) * li.Quantity,
            RateType.Monthly => li.RateAmount * (decimal)Math.Ceiling(days / 30.0) * li.Quantity,
            _ => 0
        });
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

        var oldStatus = _returningRecord.Status;
        var oldReturnDate = _returningRecord.ReturnDate;
        var oldTotalCost = _returningRecord.TotalCost;
        var oldDepositRefunded = _returningRecord.DepositRefunded;
        var oldPaid = _returningRecord.Paid;
        var oldNotes = _returningRecord.Notes;

        // Update rental record
        _returningRecord.Status = RentalStatus.Returned;
        _returningRecord.ReturnDate = ReturnDate?.DateTime;
        _returningRecord.TotalCost = ReturnTotalCost;
        _returningRecord.DepositRefunded = ReturnRefundDeposit ? _returningRecord.SecurityDeposit : 0;
        _returningRecord.Paid = ReturnMarkAsPaid;
        if (!string.IsNullOrWhiteSpace(ReturnNotes))
        {
            _returningRecord.Notes = string.IsNullOrWhiteSpace(_returningRecord.Notes)
                ? ReturnNotes.Trim()
                : $"{_returningRecord.Notes}\n\nReturn notes: {ReturnNotes.Trim()}";
        }
        _returningRecord.UpdatedAt = DateTime.UtcNow;

        var keptDeposit = ReturnRefundDeposit
            ? null
            : CreateKeptDepositRevenue(_returningRecord, companyData, _returningRecord.ReturnDate ?? DateTime.Now);
        if (keptDeposit != null)
            AddKeptDeposit(companyData, keptDeposit);

        // Update inventory for all line items via InventoryItem.InStock
        var returnInvSnapshot = new Dictionary<string, decimal>();
        var returnAdjustments = new List<StockAdjustment>();
        var effectiveItems = GetEffectiveLineItems(_returningRecord);
        foreach (var li in effectiveItems)
        {
            var ri = companyData.RentalInventory.FirstOrDefault(i => i.Id == li.RentalItemId);
            var invItem = ri != null ? companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
            if (invItem != null)
            {
                if (!returnInvSnapshot.ContainsKey(invItem.Id))
                    returnInvSnapshot[invItem.Id] = invItem.InStock;
                var previousStock = invItem.InStock;
                invItem.InStock += li.Quantity;
                invItem.Status = invItem.CalculateStatus();
                invItem.LastUpdated = DateTime.UtcNow;

                companyData.IdCounters.StockAdjustment++;
                var adj = new StockAdjustment
                {
                    Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                    InventoryItemId = invItem.Id,
                    AdjustmentType = AdjustmentType.Add,
                    Quantity = li.Quantity,
                    PreviousStock = previousStock,
                    NewStock = invItem.InStock,
                    Reason = "Rental return",
                    ReferenceNumber = _returningRecord.Id,
                    Timestamp = DateTime.UtcNow,
                    IsAutoGenerated = true
                };
                returnAdjustments.Add(adj);
                companyData.StockAdjustments.Add(adj);
            }
        }

        companyData.MarkAsModified();

        var recordToReturn = _returningRecord;
        var savedReturnInvSnapshot = returnInvSnapshot;
        var savedReturnAdjustments = returnAdjustments;
        var newNotes = _returningRecord.Notes;
        // Capture the applied return values as locals so redo restores exactly what was confirmed.
        // The live ReturnDate/ReturnTotalCost/ReturnRefundDeposit/ReturnMarkAsPaid properties get reset
        // every time the Return modal is reopened, so reading them in the redo lambda would re-apply
        // stale/reset values (or another record's values).
        var newReturnDate = _returningRecord.ReturnDate;
        var newTotalCost = _returningRecord.TotalCost;
        var newDepositRefunded = _returningRecord.DepositRefunded;
        var newPaid = _returningRecord.Paid;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Return rental '{recordToReturn.Id}'",
            () =>
            {
                recordToReturn.Status = oldStatus;
                recordToReturn.ReturnDate = oldReturnDate;
                recordToReturn.TotalCost = oldTotalCost;
                recordToReturn.DepositRefunded = oldDepositRefunded;
                recordToReturn.Paid = oldPaid;
                recordToReturn.Notes = oldNotes;
                if (keptDeposit != null)
                    RemoveKeptDeposit(companyData, keptDeposit);
                foreach (var (id, oldStock) in savedReturnInvSnapshot)
                {
                    var invItem = companyData.Inventory.FirstOrDefault(i => i.Id == id);
                    if (invItem != null)
                    {
                        var stockBeforeUndo = invItem.InStock;
                        invItem.InStock = oldStock;
                        invItem.Status = invItem.CalculateStatus();
                        invItem.LastUpdated = DateTime.UtcNow;
                        App.CheckAndNotifyStockStatus(invItem, stockBeforeUndo);
                    }
                }
                foreach (var adj in savedReturnAdjustments)
                    companyData.StockAdjustments.RemoveAll(a => a.Id == adj.Id);
                companyData.MarkAsModified();
                RecordReturned?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                recordToReturn.Status = RentalStatus.Returned;
                recordToReturn.ReturnDate = newReturnDate;
                recordToReturn.TotalCost = newTotalCost;
                recordToReturn.DepositRefunded = newDepositRefunded;
                recordToReturn.Paid = newPaid;
                recordToReturn.Notes = newNotes;
                if (keptDeposit != null)
                    AddKeptDeposit(companyData, keptDeposit);
                var returnItems = GetEffectiveLineItems(recordToReturn);
                foreach (var li in returnItems)
                {
                    var ri = companyData.RentalInventory.FirstOrDefault(i => i.Id == li.RentalItemId);
                    var invItem = ri != null ? companyData.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
                    if (invItem != null)
                    {
                        invItem.InStock += li.Quantity;
                        invItem.Status = invItem.CalculateStatus();
                        invItem.LastUpdated = DateTime.UtcNow;
                    }
                }
                foreach (var adj in savedReturnAdjustments)
                    companyData.StockAdjustments.Add(adj);
                companyData.MarkAsModified();
                RecordReturned?.Invoke(this, EventArgs.Empty);
            }));

        RecordReturned?.Invoke(this, EventArgs.Empty);
        CloseReturnModal();
    }

    /// <summary>
    /// A deposit the business keeps is earned, so it becomes revenue on the day the rental comes back
    /// (docs/Calculations.md §4). Only a deposit billed on an invoice is in the books, so a rental with
    /// no invoice has nothing to move. Priced at the invoice's rate, as the invoice's refunds are.
    /// </summary>
    private static Revenue? CreateKeptDepositRevenue(RentalRecord rental, CompanyData companyData, DateTime date)
    {
        var invoice = rental.InvoiceIds
            .Select(companyData.GetInvoice)
            .OfType<Invoice>()
            .Where(i => i.SecurityDeposit > 0)
            .OrderBy(i => i.IssueDate)
            .FirstOrDefault();
        if (invoice == null)
            return null;

        var amount = Math.Min(rental.SecurityDeposit,
            SecurityDeposits.StillHeld(invoice, companyData.Payments, companyData.Revenues));
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

    private static void AddKeptDeposit(CompanyData companyData, Revenue revenue)
    {
        companyData.Revenues.Add(revenue);
        if (!revenue.IsPendingConversion)
            return;

        // Dated on the return, but the money came in with the invoice, so it waits for the invoice's rate.
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

    private static void RemoveKeptDeposit(CompanyData companyData, Revenue revenue)
    {
        companyData.Revenues.Remove(revenue);
        if (companyData.PendingConversions.RemoveAll(p => p.TransactionId == revenue.Id) > 0)
            _ = PendingConversionService.Instance?.ForgetAsync([revenue.Id]);
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
        ViewItemName = GetItemDisplayName(rentalRecord, companyData);
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

        ViewLineItems.Clear();
        var effectiveItems = GetEffectiveLineItems(rentalRecord);
        ViewHasMultipleItems = effectiveItems.Count > 1;
        foreach (var li in effectiveItems)
        {
            var liRentalItem = companyData?.RentalInventory.FirstOrDefault(i => i.Id == li.RentalItemId);
            var liInvItem = liRentalItem != null ? companyData?.Inventory.FirstOrDefault(inv => inv.Id == liRentalItem.InventoryItemId) : null;
            var liProduct = liInvItem != null ? companyData?.Products.FirstOrDefault(p => p.Id == liInvItem.ProductId) : null;
            ViewLineItems.Add(new RentalViewLineItemDisplay
            {
                ItemName = liProduct?.Name ?? "Unknown Item",
                Quantity = li.Quantity,
                RateType = li.RateType.ToString(),
                RateAmount = li.RateAmount,
                SecurityDeposit = li.SecurityDeposit
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
        string Status, string? Customer, string? Item,
        DateTimeOffset? StartDateFrom, DateTimeOffset? StartDateTo,
        DateTimeOffset? DueDateFrom, DateTimeOffset? DueDateTo)
    {
        public static readonly FilterValues Default = new("All", null, null, null, null, null, null);
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStatus, FilterCustomer, FilterItem,
            FilterStartDateFrom, FilterStartDateTo, FilterDueDateFrom, FilterDueDateTo),
        v =>
        {
            FilterStatus = v.Status;
            FilterCustomer = v.Customer;
            FilterItem = v.Item;
            FilterStartDateFrom = v.StartDateFrom;
            FilterStartDateTo = v.StartDateTo;
            FilterDueDateFrom = v.DueDateFrom;
            FilterDueDateTo = v.DueDateTo;
        });

    public bool HasFilterModalChanges => Filters.HasChanges;

    [RelayCommand]
    public void OpenFilterModal()
    {
        UpdateDropdownOptions();
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

        var companyData = App.CompanyManager?.CompanyData;
        var requestedByInvItem = new Dictionary<string, int>();

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
            else if (_editingRecord == null && li.SelectedItem != null)
            {
                var rentalItem = companyData?.RentalInventory.FirstOrDefault(i => i.Id == li.SelectedItem.Id);
                var inventoryItem = rentalItem != null
                    ? companyData?.Inventory.FirstOrDefault(inv => inv.Id == rentalItem.InventoryItemId)
                    : null;
                if (inventoryItem == null)
                {
                    li.QuantityError = "Item not found in inventory.".Translate();
                    isValid = false;
                }
                else
                {
                    // Lines renting the same item share one stock count, so each line can only take
                    // what the lines above it left.
                    var alreadyRequested = requestedByInvItem.GetValueOrDefault(inventoryItem.Id);
                    requestedByInvItem[inventoryItem.Id] = alreadyRequested + qty;
                    var available = Math.Max(0, (int)inventoryItem.InStock - alreadyRequested);
                    if (qty > available)
                    {
                        li.QuantityError = $"Only {available} available.";
                        isValid = false;
                    }
                }
            }
        }

        return isValid;
    }

    /// <summary>
    /// Gets the effective line items from a rental record.
    /// </summary>
    public static List<RentalLineItem> GetEffectiveLineItems(RentalRecord record)
    {
        if (record.LineItems.Count > 0)
            return record.LineItems;

        // Legacy / "Rent Out" records populate only the top-level fields and leave LineItems empty.
        // Synthesize a single line item from them so return-cost, inventory restoration, and display
        // all work (mirrors RentalAvailabilityModalViewModel.QuantitiesForItem's fallback).
        if (!string.IsNullOrEmpty(record.RentalItemId))
        {
            return
            [
                new RentalLineItem
                {
                    RentalItemId = record.RentalItemId,
                    Quantity = record.Quantity,
                    RateType = record.RateType,
                    RateAmount = record.RateAmount,
                    SecurityDeposit = record.SecurityDeposit
                }
            ];
        }

        return record.LineItems;
    }

    /// <summary>
    /// Gets a display name for a rental record's items.
    /// Resolves through RentalItem -> InventoryItem -> Product.
    /// Shows item name for single-item, or "Item1, Item2, ..." for multi-item.
    /// </summary>
    public static string GetItemDisplayName(RentalRecord record, CompanyData? companyData)
    {
        string ResolveName(string rentalItemId)
        {
            var ri = companyData?.RentalInventory.FirstOrDefault(i => i.Id == rentalItemId);
            var invItem = ri != null ? companyData?.Inventory.FirstOrDefault(inv => inv.Id == ri.InventoryItemId) : null;
            var product = invItem != null ? companyData?.Products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
            return product?.Name ?? "Unknown Item";
        }

        var effectiveItems = GetEffectiveLineItems(record);
        if (effectiveItems.Count == 1)
            return ResolveName(effectiveItems[0].RentalItemId);

        var names = effectiveItems.Select(li => ResolveName(li.RentalItemId));
        return string.Join(", ", names);
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

    public decimal Amount
    {
        get
        {
            var qty = int.TryParse(Quantity, out var q) ? q : 0;
            var rate = decimal.TryParse(RateAmount, out var r) ? r : 0;
            return qty * rate;
        }
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
    public string RateFormatted => $"{CurrencyService.Format(RateAmount)}/{RateType}";
    public string DepositFormatted => CurrencyService.Format(SecurityDeposit);
    public string AmountFormatted => CurrencyService.Format(Quantity * RateAmount);
}
