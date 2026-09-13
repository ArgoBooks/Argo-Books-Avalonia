using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Stock Levels modals (Add Item, Adjust Stock).
/// </summary>
public partial class StockLevelsModalsViewModel : ViewModelBase
{
    #region Events

    /// <summary>
    /// Raised when an inventory item is saved (added or adjusted).
    /// </summary>
    public event EventHandler? ItemSaved;

    /// <summary>
    /// The Id of the inventory item most recently created via the Add-item modal. Lets a caller
    /// that opened "create inventory item" from another modal auto-select the new item after save.
    /// </summary>
    public string? LastSavedItemId { get; private set; }

    #endregion

    #region Adjust Stock Modal State

    [ObservableProperty]
    private bool _isAdjustStockModalOpen;

    [ObservableProperty]
    private string? _selectedItemId;

    [ObservableProperty]
    private string _selectedItemProductName = string.Empty;

    [ObservableProperty]
    private int _currentStock;

    [ObservableProperty]
    private string _adjustmentQuantity = string.Empty;

    [ObservableProperty]
    private string _adjustmentType = "Add";

    [ObservableProperty]
    private string _adjustmentReason = string.Empty;

    [ObservableProperty]
    private string? _adjustmentError;

    [ObservableProperty]
    private bool _hasAdjustmentQuantityError;

    /// <summary>
    /// Adjustment type options for dropdown.
    /// </summary>
    public ObservableCollection<string> AdjustmentTypes { get; } = new(AdjustmentTypeExtensions.GetAllNames());

    /// <summary>
    /// Calculated new stock level based on adjustment type and quantity.
    /// </summary>
    public string CalculatedNewStock
    {
        get
        {
            if (!int.TryParse(AdjustmentQuantity, out var qty))
                return CurrentStock.ToString();

            return AdjustmentType switch
            {
                "Add" => (CurrentStock + qty).ToString(),
                "Remove" => Math.Max(0, CurrentStock - qty).ToString(),
                "Set" => qty.ToString(),
                _ => CurrentStock.ToString()
            };
        }
    }

    partial void OnAdjustmentQuantityChanged(string value)
    {
        OnPropertyChanged(nameof(CalculatedNewStock));
        // Clear error when user starts typing
        if (!string.IsNullOrEmpty(value))
        {
            HasAdjustmentQuantityError = false;
        }
    }

    partial void OnAdjustmentTypeChanged(string value)
    {
        OnPropertyChanged(nameof(CalculatedNewStock));
    }

    #endregion

    #region Add Item Modal State

    [ObservableProperty]
    private bool _isAddItemModalOpen;

    [ObservableProperty]
    private Product? _selectedProduct;

    partial void OnSelectedProductChanged(Product? value)
    {
        if (value != null)
        {
            AddItemProductError = null;
        }
    }

    [ObservableProperty]
    private Location? _selectedLocation;

    partial void OnSelectedLocationChanged(Location? value)
    {
        if (value != null)
        {
            HasLocationError = false;
        }
    }

    [ObservableProperty]
    private string _addItemSku = string.Empty;

    partial void OnAddItemQuantityChanged(string value)
    {
        // Clear error when user starts typing
        if (!string.IsNullOrEmpty(value))
        {
            HasAddItemQuantityError = false;
        }
    }

    [ObservableProperty]
    private string _addItemQuantity = string.Empty;

    [ObservableProperty]
    private string _addItemReorderPoint = "10";

    [ObservableProperty]
    private string _addItemOverstockThreshold = "100";

    [ObservableProperty]
    private string? _addItemError;

    [ObservableProperty]
    private string? _addItemProductError;

    [ObservableProperty]
    private bool _hasLocationError;

    [ObservableProperty]
    private bool _hasAddItemQuantityError;

    /// <summary>
    /// Available products for Add Item modal.
    /// </summary>
    public ObservableCollection<Product> AvailableProducts { get; } = [];

    /// <summary>
    /// Available locations for Add Item modal.
    /// </summary>
    public ObservableCollection<Location> AvailableLocations { get; } = [];

    #endregion

    #region Adjust Stock Commands

    /// <summary>
    /// Opens the adjust stock modal for an item.
    /// </summary>
    public void OpenAdjustStockModal(string itemId, string productName, int currentStock)
    {
        SelectedItemId = itemId;
        SelectedItemProductName = productName;
        CurrentStock = currentStock;
        AdjustmentQuantity = string.Empty;
        AdjustmentType = "Add";
        AdjustmentReason = string.Empty;
        AdjustmentError = null;
        IsAdjustStockModalOpen = true;
    }

    /// <summary>
    /// Closes the adjust stock modal.
    /// </summary>
    [RelayCommand]
    private void CloseAdjustStockModal()
    {
        IsAdjustStockModalOpen = false;
        ClearAdjustmentFields();
    }

    /// <summary>
    /// Returns true if any data has been entered in the Adjust Stock modal.
    /// </summary>
    private bool HasAdjustStockEnteredData =>
        !string.IsNullOrWhiteSpace(AdjustmentQuantity) ||
        !string.IsNullOrWhiteSpace(AdjustmentReason);

    /// <summary>
    /// Requests to close the Adjust Stock modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseAdjustStockModalAsync()
    {
        if (HasAdjustStockEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAdjustStockModal();
    }

    /// <summary>
    /// Saves the stock adjustment.
    /// </summary>
    [RelayCommand]
    private void SaveAdjustment()
    {
        if (string.IsNullOrEmpty(SelectedItemId)) return;

        AdjustmentError = null;
        HasAdjustmentQuantityError = false;

        // Validate quantity
        if (!int.TryParse(AdjustmentQuantity, out var quantity) || quantity < 0)
        {
            HasAdjustmentQuantityError = true;
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;

        var inventoryItem = companyData?.Inventory.FirstOrDefault(i => i.Id == SelectedItemId);
        if (inventoryItem == null) return;

        // Store old values for undo
        var oldInStock = inventoryItem.InStock;
        var oldStatus = inventoryItem.Status;

        // Calculate new stock
        var newStock = AdjustmentType switch
        {
            "Add" => inventoryItem.InStock + quantity,
            "Remove" => Math.Max(0, inventoryItem.InStock - quantity),
            "Set" => quantity,
            _ => inventoryItem.InStock
        };

        // Apply the change
        inventoryItem.InStock = newStock;
        inventoryItem.Status = inventoryItem.CalculateStatus();
        inventoryItem.LastUpdated = DateTime.UtcNow;
        App.CheckAndNotifyStockStatus(inventoryItem, oldInStock);

        // Create stock adjustment record
        if (companyData != null)
        {
            companyData.IdCounters.StockAdjustment++;
            var adjustmentRecord = new StockAdjustment
            {
                Id = $"ADJ-{companyData.IdCounters.StockAdjustment:D5}",
                InventoryItemId = inventoryItem.Id,
                AdjustmentType = AdjustmentType switch
                {
                    "Add" => Core.Enums.AdjustmentType.Add,
                    "Remove" => Core.Enums.AdjustmentType.Remove,
                    "Set" => Core.Enums.AdjustmentType.Set,
                    _ => Core.Enums.AdjustmentType.Add
                },
                // Store the quantity that was actually applied. A "Remove" clamps NewStock at 0, so the
                // user-entered amount can exceed what was removed. InventoryValuationService.SignedDelta
                // rolls back a Remove by -Quantity, so an unclamped Quantity would corrupt every
                // historical valuation before this adjustment.
                Quantity = AdjustmentType == "Remove" ? oldInStock - newStock : quantity,
                PreviousStock = oldInStock,
                NewStock = newStock,
                Reason = AdjustmentReason,
                Timestamp = DateTime.UtcNow
            };

            companyData.StockAdjustments.Add(adjustmentRecord);
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.StockAdjusted);
            companyData.MarkAsModified();

            // Record undo action
            var productName = SelectedItemProductName;
            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Adjust stock for '{productName}'",
                () =>
                {
                    inventoryItem.InStock = oldInStock;
                    inventoryItem.Status = oldStatus;
                    App.CheckAndNotifyStockStatus(inventoryItem, newStock);
                    companyData.StockAdjustments.Remove(adjustmentRecord);
                    companyData.MarkAsModified();
                    ItemSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    inventoryItem.InStock = newStock;
                    inventoryItem.Status = inventoryItem.CalculateStatus();
                    App.CheckAndNotifyStockStatus(inventoryItem, oldInStock);
                    companyData.StockAdjustments.Add(adjustmentRecord);
                    companyData.MarkAsModified();
                    ItemSaved?.Invoke(this, EventArgs.Empty);
                }));
        }

        // Notify and close
        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseAdjustStockModal();
    }

    private void ClearAdjustmentFields()
    {
        SelectedItemId = null;
        SelectedItemProductName = string.Empty;
        CurrentStock = 0;
        AdjustmentQuantity = string.Empty;
        AdjustmentType = "Add";
        AdjustmentReason = string.Empty;
        AdjustmentError = null;
        HasAdjustmentQuantityError = false;
    }

    #endregion

    #region Add Item Commands

    /// <summary>
    /// Opens the create location modal on top of the current modal.
    /// </summary>
    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _locationSavedHandler;
    private EventHandler? _productSavedHandler;

    [RelayCommand]
    private void OpenCreateLocation()
    {
        var locationModals = App.LocationsModalsViewModel;
        if (locationModals == null) return;

        CreateModalSubscription.RearmOnce(ref _locationSavedHandler,
            h => locationModals.LocationSaved += h,
            h => locationModals.LocationSaved -= h,
            () =>
            {
                ReloadAvailableLocations();

                // Auto-select the location the user just created.
                var newLocation = AvailableLocations.FirstOrDefault(l => l.Id == locationModals.LastSavedLocationId);
                if (newLocation != null)
                    SelectedLocation = newLocation;
            });
        locationModals.OpenAddModal();
    }

    /// <summary>
    /// Opens the create product modal on top of the current modal.
    /// </summary>
    [RelayCommand]
    private void OpenCreateProduct()
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                ReloadAvailableProducts();

                // Auto-select the product the user just created.
                var newProduct = AvailableProducts.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                if (newProduct != null)
                    SelectedProduct = newProduct;
            });
        productModals.OpenAddModal();
    }

    private void ReloadAvailableProducts()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;
        AvailableProducts.Clear();
        foreach (var product in companyData.Products.Where(p => p.TrackInventory))
            AvailableProducts.Add(product);
    }

    private void ReloadAvailableLocations()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;
        AvailableLocations.Clear();
        foreach (var location in companyData.Locations)
            AvailableLocations.Add(location);
    }

    /// <summary>
    /// Opens the add item modal.
    /// </summary>
    [RelayCommand]
    public void OpenAddItemModal()
    {
        // Load available products and locations
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        AvailableProducts.Clear();
        foreach (var product in companyData.Products.Where(p => p.TrackInventory))
        {
            AvailableProducts.Add(product);
        }

        AvailableLocations.Clear();
        foreach (var location in companyData.Locations)
        {
            AvailableLocations.Add(location);
        }

        // Set defaults
        SelectedProduct = null;
        SelectedLocation = AvailableLocations.FirstOrDefault();
        AddItemSku = string.Empty;
        AddItemQuantity = "0";
        AddItemReorderPoint = "10";
        AddItemOverstockThreshold = "100";
        AddItemError = null;
        AddItemProductError = null;
        HasLocationError = false;
        HasAddItemQuantityError = false;

        IsAddItemModalOpen = true;
    }

    /// <summary>
    /// Closes the add item modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddItemModal()
    {
        IsAddItemModalOpen = false;
        ClearAddItemFields();
    }

    /// <summary>
    /// Returns true if any data has been entered in the Add Item modal.
    /// </summary>
    private bool HasAddItemEnteredData =>
        SelectedProduct != null ||
        (!string.IsNullOrWhiteSpace(AddItemQuantity) && AddItemQuantity != "0");

    /// <summary>
    /// Requests to close the Add Item modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseAddItemModalAsync()
    {
        if (HasAddItemEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddItemModal();
    }

    /// <summary>
    /// Saves a new inventory item.
    /// </summary>
    [RelayCommand]
    private void SaveNewItem()
    {
        AddItemError = null;
        AddItemProductError = null;
        HasLocationError = false;
        HasAddItemQuantityError = false;

        // Validate all fields before returning
        var hasErrors = false;

        if (SelectedProduct == null)
        {
            AddItemProductError = "Please select a product.".Translate();
            hasErrors = true;
        }

        if (SelectedLocation == null)
        {
            HasLocationError = true;
            hasErrors = true;
        }

        if (!int.TryParse(AddItemQuantity, out var quantity) || quantity < 0)
        {
            HasAddItemQuantityError = true;
            hasErrors = true;
        }

        if (hasErrors) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        // Check if item already exists for this product/location
        var existingItem = companyData.Inventory.FirstOrDefault(i =>
            i.ProductId == SelectedProduct!.Id && i.LocationId == SelectedLocation!.Id);

        if (existingItem != null)
        {
            AddItemError = "An inventory item already exists for this product and location.".Translate();
            return;
        }

        // Generate new ID
        companyData.IdCounters.InventoryItem++;
        var newId = $"INV-ITM-{companyData.IdCounters.InventoryItem:D5}";

        // Parse thresholds
        int.TryParse(AddItemReorderPoint, out var reorderPoint);
        int.TryParse(AddItemOverstockThreshold, out var overstockThreshold);

        var newItem = new InventoryItem
        {
            Id = newId,
            ProductId = SelectedProduct!.Id,
            Sku = AddItemSku.Trim(),
            LocationId = SelectedLocation!.Id,
            InStock = quantity,
            Reserved = 0,
            ReorderPoint = reorderPoint,
            OverstockThreshold = overstockThreshold,
            UnitCost = SelectedProduct.CostPrice,
            LastUpdated = DateTime.UtcNow
        };
        newItem.Status = newItem.CalculateStatus();

        companyData.Inventory.Add(newItem);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add inventory item for '{SelectedProduct.Name}'",
            () =>
            {
                companyData.Inventory.Remove(newItem);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Inventory.Add(newItem);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            }));

        // Notify and close
        LastSavedItemId = newItem.Id;
        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseAddItemModal();
    }

    private void ClearAddItemFields()
    {
        SelectedProduct = null;
        SelectedLocation = null;
        AddItemSku = string.Empty;
        AddItemQuantity = string.Empty;
        AddItemReorderPoint = "10";
        AddItemOverstockThreshold = "100";
        AddItemError = null;
        AddItemProductError = null;
        HasLocationError = false;
        HasAddItemQuantityError = false;
    }

    #endregion

    #region Filter Modal State

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler<FilterAppliedEventArgs>? FiltersApplied;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private string _filterCategory = "All";

    [ObservableProperty]
    private string _filterLocation = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>
    /// Available categories for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterCategories { get; } = ["All"];

    /// <summary>
    /// Available locations for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterLocations { get; } = ["All"];

    /// <summary>
    /// Status options for filter dropdown.
    /// </summary>
    public ObservableCollection<string> FilterStatusOptions { get; } = new(InventoryStatusExtensions.GetFilterOptions());

    // Original filter values for change detection
    private string _originalFilterCategory = "All";
    private string _originalFilterLocation = "All";
    private string _originalFilterStatus = "All";

    /// <summary>
    /// Returns true if any filter has been changed from its original value when the modal was opened.
    /// </summary>
    public bool HasFilterModalChanges =>
        FilterCategory != _originalFilterCategory ||
        FilterLocation != _originalFilterLocation ||
        FilterStatus != _originalFilterStatus;

    /// <summary>
    /// Captures the current filter values as the original values for change detection.
    /// </summary>
    private void CaptureOriginalFilterValues()
    {
        _originalFilterCategory = FilterCategory;
        _originalFilterLocation = FilterLocation;
        _originalFilterStatus = FilterStatus;
    }

    /// <summary>
    /// Restores filter values to their original values when the modal was opened.
    /// </summary>
    private void RestoreOriginalFilterValues()
    {
        FilterCategory = _originalFilterCategory;
        FilterLocation = _originalFilterLocation;
        FilterStatus = _originalFilterStatus;
    }

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal(IEnumerable<string> categories, IEnumerable<string> locations,
        string currentCategory, string currentLocation, string currentStatus)
    {
        FilterCategories.Clear();
        FilterCategories.Add("All");
        foreach (var cat in categories.Where(c => c != "All"))
            FilterCategories.Add(cat);

        FilterLocations.Clear();
        FilterLocations.Add("All");
        foreach (var loc in locations.Where(l => l != "All"))
            FilterLocations.Add(loc);

        FilterCategory = currentCategory;
        FilterLocation = currentLocation;
        FilterStatus = currentStatus;

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
    /// Requests to close the Filter modal, showing confirmation if changes were made.
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

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        FiltersApplied?.Invoke(this, new FilterAppliedEventArgs(FilterCategory, FilterLocation, FilterStatus));
        IsFilterModalOpen = false;
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
    /// Resets filter values to their defaults.
    /// </summary>
    private void ResetFilterDefaults()
    {
        FilterCategory = "All";
        FilterLocation = "All";
        FilterStatus = "All";
    }

    #endregion
}

/// <summary>
/// Event args for filter applied events.
/// </summary>
public class FilterAppliedEventArgs(string category, string location, string status) : EventArgs
{
    public string Category { get; } = category;
    public string Location { get; } = location;
    public string Status { get; } = status;
}
