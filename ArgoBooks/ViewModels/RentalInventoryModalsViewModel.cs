using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for rental inventory modals.
/// </summary>
public partial class RentalInventoryModalsViewModel : ViewModelBase
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

    public string FormTitle => IsEditMode ? "Edit Rental Item".Translate() : "Add Rental Item".Translate();
    public string FormSaveText => IsEditMode ? "Save Changes".Translate() : "Add Item".Translate();

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
        if (IsEditMode) SaveEditedItem();
        else SaveNewItem();
    }

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private bool _isRentOutModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private InventoryItemOption? _modalInventoryItem;

    [ObservableProperty]
    private string _modalDailyRate = string.Empty;

    [ObservableProperty]
    private string _modalWeeklyRate = string.Empty;

    [ObservableProperty]
    private string _modalMonthlyRate = string.Empty;

    [ObservableProperty]
    private string _modalSecurityDeposit = string.Empty;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    [ObservableProperty]
    private string _modalStatus = "Active";

    [ObservableProperty]
    private string? _modalInventoryItemError;

    [ObservableProperty]
    private string? _modalDailyRateError;

    private RentalItem? _editingItem;

    private sealed record EditState(
        string? InventoryItemId, string DailyRate, string WeeklyRate, string MonthlyRate,
        string SecurityDeposit, string Notes, string Status);

    // The form as the edit modal opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(
        ModalInventoryItem?.Id, ModalDailyRate, ModalWeeklyRate, ModalMonthlyRate,
        ModalSecurityDeposit, ModalNotes, ModalStatus);

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        ModalInventoryItem != null ||
        !string.IsNullOrWhiteSpace(ModalDailyRate) ||
        !string.IsNullOrWhiteSpace(ModalWeeklyRate) ||
        !string.IsNullOrWhiteSpace(ModalMonthlyRate) ||
        !string.IsNullOrWhiteSpace(ModalSecurityDeposit) ||
        !string.IsNullOrWhiteSpace(ModalNotes) ||
        ModalStatus != "Active";

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges => Capture() != _original;

    private sealed record FilterValues(string Status, string Availability, string? DailyRateMin, string? DailyRateMax)
    {
        public static readonly FilterValues Default = new("All", "All", null, null);
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStatus, FilterAvailability, FilterDailyRateMin, FilterDailyRateMax),
        v =>
        {
            FilterStatus = v.Status;
            FilterAvailability = v.Availability;
            FilterDailyRateMin = v.DailyRateMin;
            FilterDailyRateMax = v.DailyRateMax;
        });

    public bool HasFilterModalChanges => Filters.HasChanges;

    #endregion

    #region Rent Out Modal Fields

    [ObservableProperty]
    private string _rentOutItemName = string.Empty;

    [ObservableProperty]
    private string _rentOutItemId = string.Empty;

    [ObservableProperty]
    private int _rentOutAvailableQuantity;

    [ObservableProperty]
    private CustomerOption? _rentOutCustomer;

    [ObservableProperty]
    private AccountantOption? _rentOutAccountant;

    [ObservableProperty]
    private string _rentOutQuantity = "1";

    [ObservableProperty]
    private string _rentOutRateType = "Daily";

    [ObservableProperty]
    private decimal _rentOutRateAmount;

    [ObservableProperty]
    private decimal _rentOutDeposit;

    [ObservableProperty]
    private DateTimeOffset? _rentOutStartDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset? _rentOutDueDate = DateTimeOffset.Now.AddDays(1);

    [ObservableProperty]
    private string _rentOutNotes = string.Empty;

    [ObservableProperty]
    private string? _rentOutCustomerError;

    [ObservableProperty]
    private string? _rentOutQuantityError;

    [ObservableProperty]
    private string? _rentOutDueDateError;

    private RentalItem? _rentingItem;

    public string RentOutEstimatedTotal =>
        int.TryParse(RentOutQuantity, out var qty) && qty > 0 && RentOutStartDate != null && RentOutDueDate != null
            ? CurrencyService.Format(RentalBookings.RentalCost([RentOutLine(qty)], RentOutStartDate.Value.DateTime, RentOutDueDate.Value.DateTime))
            : CurrencyService.Format(0);

    public string RentOutRateFormatted => CurrencyService.Format(RentOutRateAmount);

    public string RentOutDepositFormatted =>
        CurrencyService.Format(RentOutDeposit * (int.TryParse(RentOutQuantity, out var qty) && qty > 0 ? qty : 1));

    public bool RentOutStartsLater => RentOutStartDate?.Date > DateTime.Today;

    private RentalLineItem RentOutLine(int quantity) => new()
    {
        RentalItemId = _rentingItem?.Id ?? RentOutItemId,
        Quantity = quantity,
        RateType = Enum.TryParse<RateType>(RentOutRateType, out var type) ? type : RateType.Daily,
        RateAmount = RentOutRateAmount,
        SecurityDeposit = RentOutDeposit
    };

    partial void OnRentOutQuantityChanged(string value)
    {
        OnPropertyChanged(nameof(RentOutEstimatedTotal));
        OnPropertyChanged(nameof(RentOutDepositFormatted));
        if (int.TryParse(value, out var qty) && qty > 0)
        {
            RentOutQuantityError = null;
        }
    }

    partial void OnRentOutCustomerChanged(CustomerOption? value)
    {
        if (value != null)
        {
            RentOutCustomerError = null;
        }
    }

    partial void OnModalInventoryItemChanged(InventoryItemOption? value)
    {
        if (value != null)
        {
            ModalInventoryItemError = null;
        }
    }

    partial void OnModalDailyRateChanged(string value)
    {
        ModalDailyRateError = null;
    }

    partial void OnRentOutRateTypeChanged(string value)
    {
        UpdateRentOutRateAmount();
        OnPropertyChanged(nameof(RentOutEstimatedTotal));
    }

    partial void OnRentOutStartDateChanged(DateTimeOffset? value)
    {
        RentOutDueDateError = null;
        OnPropertyChanged(nameof(RentOutEstimatedTotal));
        OnPropertyChanged(nameof(RentOutStartsLater));
    }

    partial void OnRentOutDueDateChanged(DateTimeOffset? value)
    {
        RentOutDueDateError = null;
        OnPropertyChanged(nameof(RentOutEstimatedTotal));
    }

    partial void OnRentOutRateAmountChanged(decimal value) => OnPropertyChanged(nameof(RentOutRateFormatted));

    partial void OnRentOutDepositChanged(decimal value) => OnPropertyChanged(nameof(RentOutDepositFormatted));

    private void UpdateRentOutRateAmount()
    {
        if (_rentingItem == null) return;

        RentOutRateAmount = RentOutRateType switch
        {
            "Daily" => _rentingItem.DailyRate,
            "Weekly" => _rentingItem.WeeklyRate,
            "Monthly" => _rentingItem.MonthlyRate,
            _ => _rentingItem.DailyRate
        };
    }

    #endregion

    #region Filter Fields

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterDailyRateMin;

    [ObservableProperty]
    private string? _filterDailyRateMax;

    [ObservableProperty]
    private string _filterAvailability = "All";

    #endregion

    #region Dropdown Options

    public ObservableCollection<InventoryItemOption> AvailableInventoryItems { get; } = [];
    public ObservableCollection<CustomerOption> AvailableCustomers { get; } = [];
    public ObservableCollection<AccountantOption> AvailableAccountants { get; } = [];
    public ObservableCollection<string> StatusOptions { get; } = ["Active", "In Maintenance"];
    public ObservableCollection<string> FilterStatusOptions { get; } = ["All", "Available", "In Maintenance", "All Rented"];
    public ObservableCollection<string> AvailabilityOptions { get; } = ["All", "Available Only", "Unavailable Only"];
    public ObservableCollection<string> RateTypeOptions { get; } = new(RateTypeExtensions.GetAllNames());

    #endregion

    #region Events

    public event EventHandler? ItemSaved;

    /// <summary>
    /// The Id of the rental item most recently created via the Add modal. Lets a caller that
    /// opened "create rental item" from another modal auto-select the new item after save.
    /// </summary>
    public string? LastSavedItemId { get; private set; }
    public event EventHandler? ItemDeleted;
    public event EventHandler? FiltersApplied;
    public event EventHandler? FiltersCleared;
    public event EventHandler? RentalCreated;

    #endregion

    #region Add Item

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingItem = null;
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

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _supplierSavedHandler;
    private EventHandler? _customerSavedHandler;
    private EventHandler? _stockSavedHandler;

    [RelayCommand]
    private void OpenCreateSupplier()
    {
        var supplierModals = App.SupplierModalsViewModel;
        if (supplierModals == null) return;

        CreateModalSubscription.RearmOnce(ref _supplierSavedHandler,
            h => supplierModals.SupplierSaved += h,
            h => supplierModals.SupplierSaved -= h,
            () =>
            {
                UpdateDropdownOptions();
            });
        supplierModals.OpenAddModal();
    }

    // A rental item rents out a stock record, so with none yet it opens Stock Levels' Add Item form.
    [RelayCommand]
    private void OpenCreateInventoryItem()
    {
        var stockLevelsModals = App.StockLevelsModalsViewModel;
        if (stockLevelsModals == null) return;

        CreateModalSubscription.RearmOnce(ref _stockSavedHandler,
            h => stockLevelsModals.ItemSaved += h,
            h => stockLevelsModals.ItemSaved -= h,
            () =>
            {
                UpdateDropdownOptions();

                var newItem = AvailableInventoryItems.FirstOrDefault(i => i.Id == stockLevelsModals.LastSavedItemId);
                if (newItem != null)
                    ModalInventoryItem = newItem;
            });
        stockLevelsModals.OpenAddItemModal();
    }

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
                    RentOutCustomer = newCustomer;
            });
        customerModals.OpenAddModal();
    }

    [RelayCommand]
    public void SaveNewItem()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        companyData.IdCounters.RentalItem++;
        var newId = $"RNT-ITM-{companyData.IdCounters.RentalItem:D3}";

        var newItem = new RentalItem
        {
            Id = newId,
            InventoryItemId = ModalInventoryItem!.Id,
            DailyRate = decimal.TryParse(ModalDailyRate, out var daily) ? daily : 0,
            WeeklyRate = decimal.TryParse(ModalWeeklyRate, out var weekly) ? weekly : 0,
            MonthlyRate = decimal.TryParse(ModalMonthlyRate, out var monthly) ? monthly : 0,
            SecurityDeposit = decimal.TryParse(ModalSecurityDeposit, out var deposit) ? deposit : 0,
            Status = ModalStatus == "In Maintenance" ? EntityStatus.Inactive : EntityStatus.Active,
            Notes = ModalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.RentalInventory.Add(newItem);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.RentalItemCreated);
        companyData.MarkAsModified();

        // Resolve name for undo description
        var itemName = ResolveRentalItemName(companyData, newItem);

        var itemToUndo = newItem;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add rental item '{itemName}'",
            () =>
            {
                companyData.RentalInventory.Remove(itemToUndo);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.RentalInventory.Add(itemToUndo);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            }));

        LastSavedItemId = newItem.Id;
        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    #endregion

    #region Edit Item

    public void OpenEditModal(RentalItemDisplayItem? item)
    {
        if (item == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var rentalItem = companyData?.RentalInventory.FirstOrDefault(i => i.Id == item.Id);
        if (rentalItem == null)
            return;

        _editingItem = rentalItem;
        UpdateDropdownOptions();

        ModalInventoryItem = AvailableInventoryItems.FirstOrDefault(i => i.Id == rentalItem.InventoryItemId);
        ModalDailyRate = rentalItem.DailyRate.ToString("0.00");
        ModalWeeklyRate = rentalItem.WeeklyRate.ToString("0.00");
        ModalMonthlyRate = rentalItem.MonthlyRate.ToString("0.00");
        ModalSecurityDeposit = rentalItem.SecurityDeposit.ToString("0.00");
        ModalStatus = rentalItem.Status == EntityStatus.Inactive ? "In Maintenance" : "Active";
        ModalNotes = rentalItem.Notes;

        _original = Capture();

        ClearModalErrors();
        IsEditModalOpen = true;
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingItem = null;
        ClearModalFields();
    }

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
    public void SaveEditedItem()
    {
        if (!ValidateModal() || _editingItem == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var oldInventoryItemId = _editingItem.InventoryItemId;
        var oldDailyRate = _editingItem.DailyRate;
        var oldWeeklyRate = _editingItem.WeeklyRate;
        var oldMonthlyRate = _editingItem.MonthlyRate;
        var oldSecurityDeposit = _editingItem.SecurityDeposit;
        var oldStatus = _editingItem.Status;
        var oldNotes = _editingItem.Notes;

        var newInventoryItemId = ModalInventoryItem?.Id ?? string.Empty;
        var newDailyRate = decimal.TryParse(ModalDailyRate, out var daily) ? daily : 0;
        var newWeeklyRate = decimal.TryParse(ModalWeeklyRate, out var weekly) ? weekly : 0;
        var newMonthlyRate = decimal.TryParse(ModalMonthlyRate, out var monthly) ? monthly : 0;
        var newSecurityDeposit = decimal.TryParse(ModalSecurityDeposit, out var deposit) ? deposit : 0;
        var newStatus = ModalStatus == "In Maintenance" ? EntityStatus.Inactive : EntityStatus.Active;
        var newNotes = ModalNotes.Trim();

        var hasChanges = oldInventoryItemId != newInventoryItemId ||
                         oldDailyRate != newDailyRate ||
                         oldWeeklyRate != newWeeklyRate ||
                         oldMonthlyRate != newMonthlyRate ||
                         oldSecurityDeposit != newSecurityDeposit ||
                         oldStatus != newStatus ||
                         oldNotes != newNotes;

        if (!hasChanges)
        {
            CloseEditModal();
            return;
        }

        // Return, edit and delete find a rental's stock through this link when they run, so moving it
        // while units are out would put them back on the wrong item.
        var rentalItemId = _editingItem.Id;
        if (oldInventoryItemId != newInventoryItemId && companyData.Rentals.Any(r =>
                RentalBookings.HoldsStock(r) &&
                r.EffectiveLineItems().Any(li => li.RentalItemId == rentalItemId)))
        {
            ModalInventoryItemError = "This item is rented out. Link it to another inventory item once every rental of it is returned.".Translate();
            return;
        }

        var itemToEdit = _editingItem;
        var itemName = ResolveRentalItemName(companyData, itemToEdit);
        var changes = new Dictionary<string, FieldChange>();
        if (oldInventoryItemId != newInventoryItemId) changes["Inventory Item"] = new FieldChange { OldValue = oldInventoryItemId, NewValue = newInventoryItemId };
        if (oldDailyRate != newDailyRate) changes["Daily Rate"] = new FieldChange { OldValue = oldDailyRate.ToString("F2"), NewValue = newDailyRate.ToString("F2") };
        if (oldWeeklyRate != newWeeklyRate) changes["Weekly Rate"] = new FieldChange { OldValue = oldWeeklyRate.ToString("F2"), NewValue = newWeeklyRate.ToString("F2") };
        if (oldMonthlyRate != newMonthlyRate) changes["Monthly Rate"] = new FieldChange { OldValue = oldMonthlyRate.ToString("F2"), NewValue = newMonthlyRate.ToString("F2") };
        if (oldSecurityDeposit != newSecurityDeposit) changes["Security Deposit"] = new FieldChange { OldValue = oldSecurityDeposit.ToString("F2"), NewValue = newSecurityDeposit.ToString("F2") };
        if (oldStatus != newStatus) changes["Status"] = new FieldChange { OldValue = oldStatus.ToString(), NewValue = newStatus.ToString() };
        if (oldNotes != newNotes) changes["Notes"] = new FieldChange { OldValue = oldNotes, NewValue = newNotes };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);

        itemToEdit.InventoryItemId = newInventoryItemId;
        itemToEdit.DailyRate = newDailyRate;
        itemToEdit.WeeklyRate = newWeeklyRate;
        itemToEdit.MonthlyRate = newMonthlyRate;
        itemToEdit.SecurityDeposit = newSecurityDeposit;
        itemToEdit.Status = newStatus;
        itemToEdit.Notes = newNotes;
        itemToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit rental item '{itemName}'",
            () =>
            {
                itemToEdit.InventoryItemId = oldInventoryItemId;
                itemToEdit.DailyRate = oldDailyRate;
                itemToEdit.WeeklyRate = oldWeeklyRate;
                itemToEdit.MonthlyRate = oldMonthlyRate;
                itemToEdit.SecurityDeposit = oldSecurityDeposit;
                itemToEdit.Status = oldStatus;
                itemToEdit.Notes = oldNotes;
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                itemToEdit.InventoryItemId = newInventoryItemId;
                itemToEdit.DailyRate = newDailyRate;
                itemToEdit.WeeklyRate = newWeeklyRate;
                itemToEdit.MonthlyRate = newMonthlyRate;
                itemToEdit.SecurityDeposit = newSecurityDeposit;
                itemToEdit.Status = newStatus;
                itemToEdit.Notes = newNotes;
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            }));

        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Item

    public async void OpenDeleteConfirm(RentalItemDisplayItem? item)
    {
        try
        {
            if (item == null)
                return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            if (await BlockIfInUseAsync(
                    usages => "This rental item cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(usages),
                    (companyData.Rentals.Any(r => r.RentalItemId == item.Id || r.LineItems.Any(li => li.RentalItemId == item.Id)), "Rental Record".Translate())))
                return;

            if (!await ConfirmDeleteAsync("Delete Rental Item".Translate(),
                    "Are you sure you want to delete this rental item?\n\n{0}".TranslateFormat(item.Name)))
                return;

            var rentalItem = companyData.RentalInventory.FirstOrDefault(i => i.Id == item.Id);
            if (rentalItem == null)
            {
                ItemDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            RemoveWithUndo(companyData, companyData.RentalInventory, rentalItem, $"Delete rental item '{item.Name}'",
                () => ItemDeleted?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "RentalInventory.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal

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

    #region Rent Out Modal

    public void OpenRentOutModal(RentalItemDisplayItem? item)
    {
        if (item == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var rentalItem = companyData?.RentalInventory.FirstOrDefault(i => i.Id == item.Id);
        if (rentalItem == null)
            return;

        // With every unit out, a booking for later dates can still fit.
        var inventoryItem = companyData?.Inventory.FirstOrDefault(inv => inv.Id == rentalItem.InventoryItemId);
        if (inventoryItem == null || inventoryItem.InStock + RentalBookings.UnitsOut(companyData!.Rentals, rentalItem.Id) <= 0)
            return;

        _rentingItem = rentalItem;
        UpdateDropdownOptions();

        RentOutItemName = item.Name;
        RentOutItemId = rentalItem.Id;
        RentOutAvailableQuantity = (int)inventoryItem.InStock;
        RentOutCustomer = null;
        RentOutAccountant = null;
        RentOutQuantity = "1";
        RentOutRateType = "Daily";
        RentOutRateAmount = rentalItem.DailyRate;
        RentOutDeposit = rentalItem.SecurityDeposit;
        RentOutStartDate = DateTimeOffset.Now;
        RentOutDueDate = DateTimeOffset.Now.AddDays(1);
        RentOutNotes = string.Empty;

        ClearRentOutErrors();
        OnPropertyChanged(nameof(RentOutEstimatedTotal));
        IsRentOutModalOpen = true;
    }

    [RelayCommand]
    public void CloseRentOutModal()
    {
        IsRentOutModalOpen = false;
        _rentingItem = null;
    }

    [RelayCommand]
    public void ConfirmRentOut()
    {
        if (!ValidateRentOut() || _rentingItem == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        RentalRecordsModalsViewModel.CreateRental(companyData, RentOutCustomer!.Id!, RentOutAccountant?.Id,
            [RentOutLine(int.Parse(RentOutQuantity))],
            RentOutStartDate?.DateTime ?? DateTime.Today, RentOutDueDate?.DateTime ?? DateTime.Today.AddDays(1),
            RentOutNotes.Trim(),
            () =>
            {
                RentalCreated?.Invoke(this, EventArgs.Empty);
                ItemSaved?.Invoke(this, EventArgs.Empty);
            });

        RentalCreated?.Invoke(this, EventArgs.Empty);
        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseRentOutModal();
    }

    private bool ValidateRentOut()
    {
        ClearRentOutErrors();
        var isValid = true;

        if (RentOutCustomer == null)
        {
            RentOutCustomerError = "Customer is required.".Translate();
            isValid = false;
        }

        var start = RentOutStartDate?.DateTime ?? DateTime.Today;
        var due = RentOutDueDate?.DateTime ?? DateTime.Today.AddDays(1);
        if (due.Date < start.Date)
        {
            RentOutDueDateError = "The due date can't be before the start date.".Translate();
            isValid = false;
        }

        var companyData = App.CompanyManager?.CompanyData;
        if (!int.TryParse(RentOutQuantity, out var qty) || qty <= 0)
        {
            RentOutQuantityError = "Please enter a valid quantity.".Translate();
            isValid = false;
        }
        else if (companyData != null && _rentingItem != null &&
                 RentalBookings.FindShortfalls(companyData, [RentOutLine(qty)], start, due, start.Date <= DateTime.Today)[0] is { } available)
        {
            RentOutQuantityError = "Only {0} available for these dates.".TranslateFormat(available);
            isValid = false;
        }

        return isValid;
    }

    private void ClearRentOutErrors()
    {
        RentOutCustomerError = null;
        RentOutQuantityError = null;
        RentOutDueDateError = null;
    }

    #endregion

    #region Modal Helpers

    private void UpdateDropdownOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        AvailableInventoryItems.Clear();
        foreach (var invItem in companyData.Inventory.OrderBy(i => i.ProductId))
        {
            var product = companyData.Products.FirstOrDefault(p => p.Id == invItem.ProductId);
            var location = companyData.Locations.FirstOrDefault(l => l.Id == invItem.LocationId);
            var productName = product?.Name ?? "Unknown";
            var locationName = location?.Name ?? "Default";
            AvailableInventoryItems.Add(new InventoryItemOption
            {
                Id = invItem.Id,
                ProductName = productName,
                LocationName = locationName,
                InStock = (int)invItem.InStock,
                DisplayText = $"{productName} @ {locationName} ({invItem.InStock} in stock)"
            });
        }

        OptionLoader.Fill(AvailableCustomers, OptionLoader.Customers(companyData, activeOnly: true).AsOptions<CustomerOption>());
        OptionLoader.Fill(AvailableAccountants, OptionLoader.Accountants(companyData).AsOptions<AccountantOption>());
    }

    private void ClearModalFields()
    {
        ModalInventoryItem = null;
        ModalDailyRate = string.Empty;
        ModalWeeklyRate = string.Empty;
        ModalMonthlyRate = string.Empty;
        ModalSecurityDeposit = string.Empty;
        ModalNotes = string.Empty;
        ModalStatus = "Active";
        ClearModalErrors();
    }

    private void ClearModalErrors()
    {
        ModalInventoryItemError = null;
        ModalDailyRateError = null;
    }

    private bool ValidateModal()
    {
        ClearModalErrors();
        var isValid = true;

        if (ModalInventoryItem == null)
        {
            ModalInventoryItemError = "Inventory item is required.".Translate();
            isValid = false;
        }
        else
        {
            var companyData = App.CompanyManager?.CompanyData;
            var existingWithSameItem = companyData?.RentalInventory.Any(i =>
                i.InventoryItemId == ModalInventoryItem.Id &&
                (_editingItem == null || i.Id != _editingItem.Id)) ?? false;

            if (existingWithSameItem)
            {
                ModalInventoryItemError = "This inventory item is already in the rental inventory.".Translate();
                isValid = false;
            }
        }

        var hasDaily = decimal.TryParse(ModalDailyRate, out var daily) && daily > 0;
        var hasWeekly = decimal.TryParse(ModalWeeklyRate, out var weekly) && weekly > 0;
        var hasMonthly = decimal.TryParse(ModalMonthlyRate, out var monthly) && monthly > 0;

        if (!hasDaily && !hasWeekly && !hasMonthly)
        {
            ModalDailyRateError = "Please enter at least one rental rate.".Translate();
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Resolves a display name for a rental item by tracing InventoryItem → Product.
    /// </summary>
    public static string ResolveRentalItemName(CompanyData companyData, RentalItem rentalItem)
    {
        var inventoryItem = companyData.Inventory.FirstOrDefault(inv => inv.Id == rentalItem.InventoryItemId);
        if (inventoryItem == null) return "Unknown";
        var product = companyData.Products.FirstOrDefault(p => p.Id == inventoryItem.ProductId);
        return product?.Name ?? "Unknown";
    }

    #endregion

}

/// <summary>
/// Option model for inventory item dropdown in rental modals.
/// </summary>
public class InventoryItemOption
{
    public string Id { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int InStock { get; set; }
    public string DisplayText { get; set; } = string.Empty;

    public override string ToString() => DisplayText;
}

/// <summary>
/// Option model for accountant dropdown.
/// </summary>
public class AccountantOption : NamedOption
{
}
