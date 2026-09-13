using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Stock Adjustments modals (Add Adjustment, View Details, Delete).
/// </summary>
public partial class StockAdjustmentsModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when a stock adjustment is saved.
    /// </summary>
    public event EventHandler? AdjustmentSaved;

    /// <summary>
    /// Raised when a stock adjustment is deleted.
    /// </summary>
    public event EventHandler? AdjustmentDeleted;

    #endregion

    #region Add Adjustment Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private InventoryItemDisplayOption? _selectedInventoryOption;

    /// <summary>
    /// Gets the selected inventory item from the option.
    /// </summary>
    public InventoryItem? SelectedInventoryItem => SelectedInventoryOption?.InventoryItem;

    [ObservableProperty]
    private string _adjustmentType = "Add";

    [ObservableProperty]
    private string _adjustmentQuantity = string.Empty;

    [ObservableProperty]
    private string _adjustmentReason = string.Empty;

    [ObservableProperty]
    private string _referenceNumber = string.Empty;

    [ObservableProperty]
    private string? _addModalError;

    [ObservableProperty]
    private bool _hasInventoryError;

    [ObservableProperty]
    private bool _hasQuantityError;

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        SelectedInventoryOption != null ||
        AdjustmentType != "Add" ||
        !string.IsNullOrWhiteSpace(AdjustmentQuantity) ||
        !string.IsNullOrWhiteSpace(AdjustmentReason) ||
        !string.IsNullOrWhiteSpace(ReferenceNumber);

    /// <summary>
    /// Adjustment type options for dropdown.
    /// </summary>
    public ObservableCollection<string> AdjustmentTypes { get; } = new(AdjustmentTypeExtensions.GetAllNames());

    /// <summary>
    /// Available inventory items for selection.
    /// </summary>
    public ObservableCollection<InventoryItemDisplayOption> AvailableInventoryItems { get; } = [];

    /// <summary>
    /// Calculated new stock level based on adjustment type and quantity.
    /// </summary>
    public string CalculatedNewStock
    {
        get
        {
            if (SelectedInventoryItem == null) return "0";
            if (!int.TryParse(AdjustmentQuantity, out var qty)) return SelectedInventoryItem.InStock.ToString();

            return AdjustmentType switch
            {
                "Add" => (SelectedInventoryItem.InStock + qty).ToString(),
                "Remove" => (SelectedInventoryItem.InStock - qty).ToString(),
                "Set" => qty.ToString(),
                _ => SelectedInventoryItem.InStock.ToString()
            };
        }
    }

    /// <summary>
    /// Current stock of selected item.
    /// </summary>
    public int CurrentStock => SelectedInventoryItem?.InStock ?? 0;

    partial void OnAdjustmentQuantityChanged(string value)
    {
        OnPropertyChanged(nameof(CalculatedNewStock));
        // Clear error when user starts typing
        if (!string.IsNullOrEmpty(value))
        {
            HasQuantityError = false;
        }
    }
    partial void OnAdjustmentTypeChanged(string value) => OnPropertyChanged(nameof(CalculatedNewStock));
    partial void OnSelectedInventoryOptionChanged(InventoryItemDisplayOption? value)
    {
        OnPropertyChanged(nameof(SelectedInventoryItem));
        OnPropertyChanged(nameof(CurrentStock));
        OnPropertyChanged(nameof(CalculatedNewStock));
        // Clear error when user selects an inventory item
        if (value != null)
        {
            HasInventoryError = false;
        }
    }

    #endregion

    #region View Modal State

    [ObservableProperty]
    private bool _isViewModalOpen;

    [ObservableProperty]
    private StockAdjustmentDisplayItem? _viewingAdjustment;

    #endregion

    #region Delete Confirmation State

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private StockAdjustmentDisplayItem? _deletingAdjustment;

    #endregion

    #region Add Modal Commands

    /// <summary>
    /// Opens the add adjustment modal.
    /// </summary>
    [RelayCommand]
    public void OpenAddModal()
    {
        LoadInventoryItems();
        ClearAddModalFields();
        IsAddModalOpen = true;
    }

    /// <summary>
    /// Closes the add adjustment modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearAddModalFields();
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
    /// Opens the create inventory item modal on top of the current modal.
    /// </summary>
    // One-shot handler for the "create entity from this modal" flow. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _itemSavedHandler;

    [RelayCommand]
    private void OpenCreateInventoryItem()
    {
        var stockLevelsModals = App.StockLevelsModalsViewModel;
        if (stockLevelsModals == null) return;

        CreateModalSubscription.RearmOnce(ref _itemSavedHandler,
            h => stockLevelsModals.ItemSaved += h,
            h => stockLevelsModals.ItemSaved -= h,
            () =>
            {
                LoadInventoryItems();

                // Auto-select the inventory item the user just created.
                var newItem = AvailableInventoryItems.FirstOrDefault(o => o.InventoryItem?.Id == stockLevelsModals.LastSavedItemId);
                if (newItem != null)
                    SelectedInventoryOption = newItem;
            });
        stockLevelsModals.OpenAddItemModal();
    }

    /// <summary>
    /// Saves the new stock adjustment.
    /// </summary>
    [RelayCommand]
    private void SaveAdjustment()
    {
        AddModalError = null;
        HasInventoryError = false;
        HasQuantityError = false;

        // Validate all fields before returning
        var hasErrors = false;

        if (SelectedInventoryItem == null)
        {
            HasInventoryError = true;
            hasErrors = true;
        }

        if (!int.TryParse(AdjustmentQuantity, out var quantity) || quantity < 0)
        {
            HasQuantityError = true;
            hasErrors = true;
        }

        if (hasErrors) return;

        var companyData = App.CompanyManager?.CompanyData;

        var inventoryItem = companyData?.Inventory.FirstOrDefault(i => i.Id == SelectedInventoryItem!.Id);
        if (inventoryItem == null) return;

        // Store old values for undo
        var oldInStock = inventoryItem.InStock;
        var oldStatus = inventoryItem.Status;

        // Calculate new stock
        var adjustmentTypeEnum = AdjustmentType switch
        {
            "Add" => Core.Enums.AdjustmentType.Add,
            "Remove" => Core.Enums.AdjustmentType.Remove,
            "Set" => Core.Enums.AdjustmentType.Set,
            _ => Core.Enums.AdjustmentType.Add
        };

        var newStock = adjustmentTypeEnum switch
        {
            Core.Enums.AdjustmentType.Add => inventoryItem.InStock + quantity,
            Core.Enums.AdjustmentType.Remove => inventoryItem.InStock - quantity,
            Core.Enums.AdjustmentType.Set => quantity,
            _ => inventoryItem.InStock
        };

        if (newStock < 0)
        {
            AddModalError = "Cannot remove {0} items. Only {1} in stock.".TranslateFormat(quantity, inventoryItem.InStock);
            HasQuantityError = true;
            return;
        }

        // Apply the change
        inventoryItem.InStock = newStock;
        inventoryItem.Status = inventoryItem.CalculateStatus();
        inventoryItem.LastUpdated = DateTime.UtcNow;

        // Create stock adjustment record
        if (companyData != null)
        {
            companyData.IdCounters.StockAdjustment++;
            var adjustmentRecord = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = inventoryItem.Id,
                AdjustmentType = adjustmentTypeEnum,
                Quantity = quantity,
                PreviousStock = oldInStock,
                NewStock = newStock,
                Reason = AdjustmentReason,
                ReferenceNumber = string.IsNullOrWhiteSpace(ReferenceNumber) ? null : ReferenceNumber.Trim(),
                Timestamp = DateTime.UtcNow
            };

            companyData.StockAdjustments.Add(adjustmentRecord);
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.StockAdjusted);
            companyData.MarkAsModified();

            // Get product name for undo description
            var product = companyData.Products.FirstOrDefault(p => p.Id == inventoryItem.ProductId);
            var productName = product?.Name ?? "Unknown Product";

            // Record undo action
            var itemToUndo = inventoryItem;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Adjust stock for '{productName}'",
                () =>
                {
                    itemToUndo.InStock = oldInStock;
                    itemToUndo.Status = oldStatus;
                    companyData.StockAdjustments.Remove(adjustmentRecord);
                    companyData.MarkAsModified();
                    AdjustmentSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    itemToUndo.InStock = newStock;
                    itemToUndo.Status = itemToUndo.CalculateStatus();
                    companyData.StockAdjustments.Add(adjustmentRecord);
                    companyData.MarkAsModified();
                    AdjustmentSaved?.Invoke(this, EventArgs.Empty);
                    App.CheckAndNotifyStockStatus(itemToUndo);
                }));
        }

        // Notify and close
        AdjustmentSaved?.Invoke(this, EventArgs.Empty);
        App.CheckAndNotifyStockStatus(inventoryItem);
        CloseAddModal();
    }

    private void LoadInventoryItems()
    {
        AvailableInventoryItems.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var inventory = companyData.Inventory;
        var products = companyData.Products;
        var locations = companyData.Locations;

        foreach (var item in inventory)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);
            var location = locations.FirstOrDefault(l => l.Id == item.LocationId);

            AvailableInventoryItems.Add(new InventoryItemDisplayOption
            {
                InventoryItem = item,
                DisplayText = $"{product?.Name ?? "Unknown"} @ {location?.Name ?? "Default"}",
                ProductName = product?.Name ?? "Unknown",
                LocationName = location?.Name ?? "Default",
                CurrentStock = item.InStock
            });
        }
    }

    private void ClearAddModalFields()
    {
        SelectedInventoryOption = null;
        AdjustmentType = "Add";
        AdjustmentQuantity = string.Empty;
        AdjustmentReason = string.Empty;
        ReferenceNumber = string.Empty;
        AddModalError = null;
        HasInventoryError = false;
        HasQuantityError = false;
    }

    #endregion

    #region View Modal Commands

    /// <summary>
    /// Opens the view details modal.
    /// </summary>
    public void OpenViewModal(StockAdjustmentDisplayItem item)
    {
        ViewingAdjustment = item;
        IsViewModalOpen = true;
    }

    /// <summary>
    /// Closes the view details modal.
    /// </summary>
    [RelayCommand]
    private void CloseViewModal()
    {
        IsViewModalOpen = false;
        ViewingAdjustment = null;
    }

    #endregion

    #region Delete Confirmation Commands

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    public async void OpenDeleteConfirm(StockAdjustmentDisplayItem item)
    {
        try
        {
            // Auto-generated adjustments (from rentals/transactions) cannot be deleted
            if (item.IsAutoGenerated) return;

            var companyData = App.CompanyManager?.CompanyData;

            var adjustment = companyData?.StockAdjustments.FirstOrDefault(a => a.Id == item.Id);
            if (adjustment == null) return;

            // Find the inventory item and reverse the adjustment
            var inventoryItem = companyData?.Inventory.FirstOrDefault(i => i.Id == adjustment.InventoryItemId);

            // Adding an adjustment can't leave stock below zero, so taking one back can't either.
            var stockAfterDelete = inventoryItem?.InStock - (adjustment.NewStock - adjustment.PreviousStock);
            if (stockAfterDelete < 0)
            {
                await App.ShowWarningMessageBoxAsync(
                    "Cannot Delete".Translate(),
                    "This adjustment can't be deleted because it would leave {0} with {1} in stock.".TranslateFormat(item.ProductName, stockAfterDelete));
                return;
            }

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Stock Adjustment".Translate(),
                Message = "Are you sure you want to delete this stock adjustment?\n\nProduct: {0}\nQuantity: {1}".TranslateFormat(item.ProductName, item.Quantity),
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            // Store values for undo
            var oldInventoryStock = inventoryItem?.InStock;
            var oldInventoryStatus = inventoryItem?.Status;

            // Reverse the adjustment on inventory if item still exists
            if (inventoryItem != null)
            {
                // Reverse the adjustment's net effect from the live stock. Setting InStock to
                // PreviousStock is only correct if this is the most recent adjustment for the item;
                // with later adjustments present, that snapshot leaves stock and the ledger inconsistent.
                inventoryItem.InStock -= adjustment.NewStock - adjustment.PreviousStock;
                inventoryItem.Status = inventoryItem.CalculateStatus();
                inventoryItem.LastUpdated = DateTime.UtcNow;
            }

            // Remove the adjustment record
            companyData?.StockAdjustments.Remove(adjustment);
            companyData?.MarkAsModified();

            // Record undo action
            var adjustmentProductName = item.ProductName;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete adjustment for '{adjustmentProductName}'",
                () =>
                {
                    // Undo: restore the adjustment
                    companyData?.StockAdjustments.Add(adjustment);
                    if (inventoryItem != null && oldInventoryStock.HasValue)
                    {
                        inventoryItem.InStock = oldInventoryStock.Value;
                        inventoryItem.Status = oldInventoryStatus ?? inventoryItem.CalculateStatus();
                    }
                    companyData?.MarkAsModified();
                    AdjustmentDeleted?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    // Redo: delete again
                    companyData?.StockAdjustments.Remove(adjustment);
                    if (inventoryItem != null)
                    {
                        inventoryItem.InStock -= adjustment.NewStock - adjustment.PreviousStock;
                        inventoryItem.Status = inventoryItem.CalculateStatus();
                    }
                    companyData?.MarkAsModified();
                    AdjustmentDeleted?.Invoke(this, EventArgs.Empty);
                }));

            // Notify
            AdjustmentDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "StockAdjustment.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Filter Modal State

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler<AdjustmentsFilterAppliedEventArgs>? FiltersApplied;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDate;

    [ObservableProperty]
    private DateTimeOffset? _filterEndDate;

    [ObservableProperty]
    private string _filterProduct = "All";

    [ObservableProperty]
    private string _filterType = "All";

    /// <summary>
    /// Available products for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterProducts { get; } = ["All"];

    /// <summary>
    /// Adjustment type options for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterTypeOptions { get; } = new(AdjustmentTypeExtensions.GetFilterOptions());

    // Original filter values for change detection
    private DateTimeOffset? _originalFilterStartDate;
    private DateTimeOffset? _originalFilterEndDate;
    private string _originalFilterProduct = "All";
    private string _originalFilterType = "All";

    /// <summary>
    /// Returns true if any filter has been changed from its original value when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterStartDate != _originalFilterStartDate ||
        FilterEndDate != _originalFilterEndDate ||
        FilterProduct != _originalFilterProduct ||
        FilterType != _originalFilterType;

    /// <summary>
    /// Captures the current filter values as the original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterStartDate = FilterStartDate;
        _originalFilterEndDate = FilterEndDate;
        _originalFilterProduct = FilterProduct;
        _originalFilterType = FilterType;
    }

    /// <summary>
    /// Restores filter values to their original values when the modal was opened.
    /// </summary>
    private void RestoreOriginalFilterValues()
    {
        FilterStartDate = _originalFilterStartDate;
        FilterEndDate = _originalFilterEndDate;
        FilterProduct = _originalFilterProduct;
        FilterType = _originalFilterType;
    }

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal(IEnumerable<string> products,
        DateTimeOffset? startDate, DateTimeOffset? endDate,
        string currentProduct, string currentType)
    {
        FilterProducts.Clear();
        FilterProducts.Add("All");
        foreach (var prod in products.Where(p => p != "All"))
            FilterProducts.Add(prod);

        FilterStartDate = startDate;
        FilterEndDate = endDate;
        FilterProduct = currentProduct;
        FilterType = currentType;

        CaptureOriginalFilterValues();
        IsFilterModalOpen = true;
    }

    /// <summary>
    /// Closes the filter modal.
    /// </summary>
    [RelayCommand]
    private void CloseFilterModal()
    {
        IsFilterModalOpen = false;
    }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, new AdjustmentsFilterAppliedEventArgs(
            FilterStartDate, FilterEndDate, FilterProduct, FilterType));
        IsFilterModalOpen = false;
    }

    /// <summary>
    /// Resets filter values to their defaults.
    /// </summary>
    private void ResetFilterDefaults()
    {
        FilterStartDate = null;
        FilterEndDate = null;
        FilterProduct = "All";
        FilterType = "All";
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        ResetFilterDefaults();
        CloseFilterModal();
    }

    /// <summary>
    /// Requests to close the filter modal, showing confirmation if changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (HasFilterModalChanges)
        {
            if (!await ConfirmDiscardFiltersAsync())
                return;

            RestoreOriginalFilterValues();
        }

        CloseFilterModal();
    }

    #endregion
}

/// <summary>
/// Event args for filter applied events.
/// </summary>
public class AdjustmentsFilterAppliedEventArgs(
    DateTimeOffset? startDate,
    DateTimeOffset? endDate,
    string product,
    string type)
    : EventArgs
{
    public DateTimeOffset? StartDate { get; } = startDate;
    public DateTimeOffset? EndDate { get; } = endDate;
    public string Product { get; } = product;
    public string Type { get; } = type;
}

/// <summary>
/// Display option for inventory items in the dropdown.
/// </summary>
public class InventoryItemDisplayOption
{
    public InventoryItem? InventoryItem { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }

    public override string ToString() => DisplayText;
}
