using System.Collections.ObjectModel;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Data;
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
    private decimal _currentStock;

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
            if (!decimal.TryParse(AdjustmentQuantity, out var qty))
                return StockUnits.Format(CurrentStock);

            return AdjustmentType switch
            {
                "Add" => StockUnits.Format(CurrentStock + qty),
                "Remove" => StockUnits.Format(Math.Max(0, CurrentStock - qty)),
                "Set" => StockUnits.Format(qty),
                _ => StockUnits.Format(CurrentStock)
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

    public string CurrentStockText => StockUnits.Format(CurrentStock);

    partial void OnCurrentStockChanged(decimal value) => OnPropertyChanged(nameof(CurrentStockText));

    #endregion

    #region Transfer Stock Modal

    [ObservableProperty]
    private bool _isTransferStockModalOpen;

    [ObservableProperty]
    private string _transferProductName = string.Empty;

    [ObservableProperty]
    private string _transferFromLocationName = string.Empty;

    [ObservableProperty]
    private string _transferAvailableText = string.Empty;

    [ObservableProperty]
    private Location? _transferToLocation;

    [ObservableProperty]
    private string _transferQuantity = string.Empty;

    [ObservableProperty]
    private string _transferNotes = string.Empty;

    [ObservableProperty]
    private string? _transferError;

    /// <summary>Locations the stock can move to: every location except the one it is at.</summary>
    public ObservableCollection<Location> TransferLocations { get; } = [];

    private string? _transferItemId;

    partial void OnTransferToLocationChanged(Location? value)
    {
        if (value != null)
            TransferError = null;
    }

    /// <summary>
    /// Opens the transfer modal for a stock record.
    /// </summary>
    public void OpenTransferStockModal(string itemId)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var item = companyData?.Inventory.FirstOrDefault(i => i.Id == itemId);
        if (companyData == null || item == null) return;

        _transferItemId = itemId;
        TransferProductName = companyData.GetProduct(item.ProductId)?.Name ?? "Unknown Product".Translate();
        TransferFromLocationName = companyData.GetLocation(item.LocationId)?.Name ?? "Unknown".Translate();
        TransferAvailableText = StockUnits.Format(item.InStock, item.UnitOfMeasure);

        TransferLocations.Clear();
        foreach (var location in companyData.Locations.Where(l => l.Id != item.LocationId).OrderBy(l => l.Name))
            TransferLocations.Add(location);

        TransferToLocation = null;
        TransferQuantity = string.Empty;
        TransferNotes = string.Empty;
        TransferError = TransferLocations.Count == 0
            ? "Add another location before moving stock.".Translate()
            : null;
        IsTransferStockModalOpen = true;
    }

    [RelayCommand]
    private void CloseTransferStockModal()
    {
        IsTransferStockModalOpen = false;
        _transferItemId = null;
    }

    /// <summary>
    /// Moves the stock, recording the transfer and an adjustment at each end, as one undo step.
    /// </summary>
    [RelayCommand]
    private void SaveTransfer()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var item = companyData?.Inventory.FirstOrDefault(i => i.Id == _transferItemId);
        if (companyData == null || item == null) return;

        if (TransferToLocation == null)
        {
            TransferError = "Pick where the stock is going.".Translate();
            return;
        }

        if (!decimal.TryParse(TransferQuantity, out var quantity) || quantity <= 0)
        {
            TransferError = "Please enter a valid quantity.".Translate();
            return;
        }

        if (quantity > item.InStock)
        {
            TransferError = "Only {0} in stock here.".TranslateFormat(StockUnits.Format(item.InStock, item.UnitOfMeasure));
            return;
        }

        var destinationId = TransferToLocation.Id;
        var notes = TransferNotes.Trim();
        var result = InventoryStockService.Transfer(companyData, item, destinationId, quantity, notes);
        App.CheckAndNotifyStockStatus(item, result.SourceOldStock);
        companyData.MarkAsModified();

        var productName = TransferProductName;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Transfer stock for '{productName}'",
            () =>
            {
                InventoryStockService.RevertTransfer(companyData, result);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                result = InventoryStockService.Transfer(companyData, item, destinationId, quantity, notes);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            }));

        ItemSaved?.Invoke(this, EventArgs.Empty);
        CloseTransferStockModal();
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
    public void OpenAdjustStockModal(string itemId, string productName, decimal currentStock)
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
        if (!decimal.TryParse(AdjustmentQuantity, out var quantity) || quantity < 0)
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
            var adjustmentRecord = new StockAdjustment
            {
                Id = new Core.Data.IdGenerator(companyData).NextStockAdjustmentId(),
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
                    companyData.StockAdjustments.RemoveRecord(adjustmentRecord);
                    companyData.MarkAsModified();
                    ItemSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    inventoryItem.InStock = newStock;
                    inventoryItem.Status = inventoryItem.CalculateStatus();
                    App.CheckAndNotifyStockStatus(inventoryItem, oldInStock);
                    companyData.StockAdjustments.RestoreRecord(adjustmentRecord);
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
        productModals.OpenAddInventoryProductModal();
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
        _addItemSession++;
        IsAddItemModalOpen = false;
        ClearAddItemFields();
    }

    // Changes whenever the modal closes, so a save still waiting on the rate knows it was abandoned.
    private int _addItemSession;

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
    private async Task SaveNewItem()
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

        if (!decimal.TryParse(AddItemQuantity, out var quantity) || quantity < 0)
        {
            HasAddItemQuantityError = true;
            hasErrors = true;
        }

        if (hasErrors) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        // Read the form before the rate fetch below: closing the modal while it runs clears the fields.
        var product = SelectedProduct!;
        var location = SelectedLocation!;
        var sku = AddItemSku.Trim();
        decimal.TryParse(AddItemReorderPoint, out var reorderPoint);
        decimal.TryParse(AddItemOverstockThreshold, out var overstockThreshold);

        bool AlreadyStocked() => companyData.Inventory.Any(i => i.ProductId == product.Id && i.LocationId == location.Id);
        if (AlreadyStocked())
        {
            AddItemError = "An inventory item already exists for this product and location.".Translate();
            return;
        }

        // The starting unit cost converts the product's cost price to USD at today's rate. The Add Item
        // button stays disabled while this command runs, so the item can't be added twice.
        var session = _addItemSession;
        await CurrencyService.WarmRateForDateAsync(DateTime.Today);
        if (session != _addItemSession || !IsAddItemModalOpen)
            return;
        if (AlreadyStocked())
        {
            AddItemError = "An inventory item already exists for this product and location.".Translate();
            return;
        }

        var newItem = new InventoryItem
        {
            Id = new Core.Data.IdGenerator(companyData).NextInventoryItemId(),
            ProductId = product.Id,
            Sku = sku,
            LocationId = location.Id,
            InStock = quantity,
            Reserved = 0,
            ReorderPoint = reorderPoint,
            OverstockThreshold = overstockThreshold,
            UnitOfMeasure = product.UnitOfMeasure,
            LastUpdated = DateTime.UtcNow
        };
        newItem.Status = newItem.CalculateStatus();

        companyData.Inventory.Add(newItem);
        InventoryStockService.StartAtCostPrice(companyData, newItem, product, DateTime.Today);
        var pendingCost = UsdConversion.Queued(companyData, UsdConversion.KeyOf(newItem));
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add inventory item for '{product.Name}'",
            () =>
            {
                companyData.Inventory.RemoveRecord(newItem);
                UsdConversion.Set(companyData, UsdConversion.KeyOf(newItem), null);
                companyData.MarkAsModified();
                ItemSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Inventory.RestoreRecord(newItem);
                UsdConversion.Set(companyData, UsdConversion.KeyOf(newItem), newItem.IsPendingConversion ? pendingCost : null);
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

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    private sealed record FilterValues(string Category, string Location, string Status)
    {
        public static readonly FilterValues Default = new("All", "All", "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterCategory, FilterLocation, FilterStatus),
        v =>
        {
            FilterCategory = v.Category;
            FilterLocation = v.Location;
            FilterStatus = v.Status;
        });

    /// <summary>
    /// Opens the filter modal seeded with the page's current filters.
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

        Filters.Set(new(currentCategory, currentLocation, currentStatus));
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

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, new FilterAppliedEventArgs(FilterCategory, FilterLocation, FilterStatus));
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
