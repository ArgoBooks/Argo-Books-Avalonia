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
            if (!decimal.TryParse(AdjustmentQuantity, out var qty)) return StockUnits.Format(SelectedInventoryItem.InStock);

            return AdjustmentType switch
            {
                "Add" => StockUnits.Format(SelectedInventoryItem.InStock + qty),
                "Remove" => StockUnits.Format(SelectedInventoryItem.InStock - qty),
                "Set" => StockUnits.Format(qty),
                _ => StockUnits.Format(SelectedInventoryItem.InStock)
            };
        }
    }

    /// <summary>
    /// Current stock of selected item.
    /// </summary>
    public decimal CurrentStock => SelectedInventoryItem?.InStock ?? 0;

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

        if (!decimal.TryParse(AdjustmentQuantity, out var quantity) || quantity < 0)
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
            var adjustmentRecord = new StockAdjustment
            {
                Id = new Core.Data.IdGenerator(companyData).NextStockAdjustmentId(),
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
                DisplayText = $"{product?.Name ?? "Unknown"} @ {location?.Name ?? "Unknown"}",
                ProductName = product?.Name ?? "Unknown",
                LocationName = location?.Name ?? "Unknown",
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
            if (companyData == null || adjustment == null) return;

            var inventoryItem = companyData.Inventory.FirstOrDefault(i => i.Id == adjustment.InventoryItemId);

            // Adding an adjustment can't leave stock below zero, so taking one back can't either.
            var stockAfterDelete = inventoryItem?.InStock - (adjustment.NewStock - adjustment.PreviousStock);
            if (stockAfterDelete < 0)
            {
                await App.ShowWarningDialogAsync(
                    "Cannot Delete".Translate(),
                    "This adjustment can't be deleted because it would leave {0} with {1} in stock.".TranslateFormat(item.ProductName, stockAfterDelete));
                return;
            }

            if (!await ConfirmDeleteAsync("Delete Stock Adjustment".Translate(),
                    "Are you sure you want to delete this stock adjustment?\n\nProduct: {0}\nQuantity: {1}".TranslateFormat(item.ProductName, item.Quantity)))
                return;

            var oldInventoryStock = inventoryItem?.InStock;
            var oldInventoryStatus = inventoryItem?.Status;

            RemoveWithUndo(companyData, companyData.StockAdjustments, adjustment, $"Delete adjustment for '{item.ProductName}'",
                () => AdjustmentDeleted?.Invoke(this, EventArgs.Empty),
                onRemove: () =>
                {
                    if (inventoryItem == null) return;

                    // Reverse the adjustment's net effect from the live stock. Setting InStock to
                    // PreviousStock is only correct if this is the most recent adjustment for the item;
                    // with later adjustments present, that snapshot leaves stock and the ledger inconsistent.
                    inventoryItem.InStock -= adjustment.NewStock - adjustment.PreviousStock;
                    inventoryItem.Status = inventoryItem.CalculateStatus();
                    inventoryItem.LastUpdated = DateTime.UtcNow;
                },
                onRestore: () =>
                {
                    if (inventoryItem == null || !oldInventoryStock.HasValue) return;

                    inventoryItem.InStock = oldInventoryStock.Value;
                    inventoryItem.Status = oldInventoryStatus ?? inventoryItem.CalculateStatus();
                });
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

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    private sealed record FilterValues(DateTimeOffset? StartDate, DateTimeOffset? EndDate, string Product, string Type)
    {
        public static readonly FilterValues Default = new(null, null, "All", "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStartDate, FilterEndDate, FilterProduct, FilterType),
        v =>
        {
            FilterStartDate = v.StartDate;
            FilterEndDate = v.EndDate;
            FilterProduct = v.Product;
            FilterType = v.Type;
        });

    /// <summary>
    /// Opens the filter modal seeded with the page's current filters.
    /// </summary>
    public void OpenFilterModal(IEnumerable<string> products,
        DateTimeOffset? startDate, DateTimeOffset? endDate,
        string currentProduct, string currentType)
    {
        FilterProducts.Clear();
        FilterProducts.Add("All");
        foreach (var prod in products.Where(p => p != "All"))
            FilterProducts.Add(prod);

        Filters.Set(new(startDate, endDate, currentProduct, currentType));
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, new AdjustmentsFilterAppliedEventArgs(
            FilterStartDate, FilterEndDate, FilterProduct, FilterType));
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

    /// <summary>
    /// Closes the filter modal, asking first and putting the filters back if they were changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
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
    public decimal CurrentStock { get; set; }

    public override string ToString() => DisplayText;
}
