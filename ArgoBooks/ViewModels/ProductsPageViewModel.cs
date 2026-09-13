using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Helpers;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Products/Services page.
/// </summary>
public partial class ProductsPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    /// <summary>
    /// Responsive header helper for adaptive layout.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public ProductsTableColumnWidths ColumnWidths => App.ProductsColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showNameColumn = ColumnVisibilityHelper.Load("Products", "Name", true);

    [ObservableProperty]
    private bool _showTypeColumn = ColumnVisibilityHelper.Load("Products", "Type", true);

    [ObservableProperty]
    private bool _showDescriptionColumn = ColumnVisibilityHelper.Load("Products", "Description", true);

    [ObservableProperty]
    private bool _showCategoryColumn = ColumnVisibilityHelper.Load("Products", "Category", true);

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnVisibilityHelper.Load("Products", "Supplier", true);

    [ObservableProperty]
    private bool _showReorderColumn = ColumnVisibilityHelper.Load("Products", "Reorder", false);

    [ObservableProperty]
    private bool _showOverstockColumn = ColumnVisibilityHelper.Load("Products", "Overstock", false);

    [ObservableProperty]
    private bool _showTrackInventoryColumn = ColumnVisibilityHelper.Load("Products", "TrackInventory", false);

    partial void OnShowNameColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Name", value); ColumnVisibilityHelper.Save("Products", "Name", value); }
    partial void OnShowTypeColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Type", value); ColumnVisibilityHelper.Save("Products", "Type", value); }
    partial void OnShowDescriptionColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Description", value); ColumnVisibilityHelper.Save("Products", "Description", value); }
    partial void OnShowCategoryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Category", value); ColumnVisibilityHelper.Save("Products", "Category", value); }
    partial void OnShowSupplierColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Supplier", value); ColumnVisibilityHelper.Save("Products", "Supplier", value); }
    partial void OnShowReorderColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reorder", value); ColumnVisibilityHelper.Save("Products", "Reorder", value); }
    partial void OnShowOverstockColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Overstock", value); ColumnVisibilityHelper.Save("Products", "Overstock", value); }
    partial void OnShowTrackInventoryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("TrackInventory", value); ColumnVisibilityHelper.Save("Products", "TrackInventory", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Products");
        ShowNameColumn = true;
        ShowTypeColumn = true;
        ShowDescriptionColumn = true;
        ShowCategoryColumn = true;
        ShowSupplierColumn = true;
        ShowReorderColumn = false;
        ShowOverstockColumn = false;
        ShowTrackInventoryColumn = false;
    }

    #endregion

    #region Tab Selection

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Gets whether the Expenses tab is selected (products/services purchased).
    /// </summary>
    public bool IsExpensesTabSelected => SelectedTabIndex == 0;

    /// <summary>
    /// Gets whether the Revenue tab is selected (products/services sold).
    /// </summary>
    public bool IsRevenueTabSelected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsExpensesTabSelected));
        OnPropertyChanged(nameof(IsRevenueTabSelected));
        OnPropertyChanged(nameof(CanAddProduct));
        ColumnWidths.SetTabMode(IsExpensesTabSelected);
        FilterProducts();
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterProducts();
        });

    [ObservableProperty]
    private string _filterItemType = "All";

    [ObservableProperty]
    private string? _filterCategory;

    [ObservableProperty]
    private string? _filterSupplier;

    #endregion

    #region Plan Status and Product Limits

    [ObservableProperty]
    private bool _hasPremium;

    [ObservableProperty]
    private int _expenseProductsCount;

    [ObservableProperty]
    private int _revenueProductsCount;

    /// <summary>
    /// Products are always unlimited, no free-tier limit.
    /// </summary>
    public bool CanAddProduct => true;

    #endregion

    #region Products Collection

    /// <summary>
    /// All products (unfiltered).
    /// </summary>
    private readonly List<Product> _allProducts = [];

    /// <summary>
    /// Expense products (purchased) for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> ExpenseProducts { get; } = [];

    /// <summary>
    /// Revenue products (sold) for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> RevenueProducts { get; } = [];

    /// <summary>
    /// Gets the current tab's products for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> CurrentProducts =>
        IsExpensesTabSelected ? ExpenseProducts : RevenueProducts;

    /// <summary>
    /// Available categories for filter/modal dropdown.
    /// </summary>
    public ObservableCollection<CategoryOption> AvailableCategories { get; } = [];

    /// <summary>
    /// Category items for the searchable category input (excludes "All Categories").
    /// </summary>
    public ObservableCollection<CategoryItem> CategoryItems { get; } = [];

    /// <summary>
    /// Gets whether there are any categories available.
    /// </summary>
    public bool HasCategories => CategoryItems.Count > 0;

    /// <summary>
    /// Available suppliers for filter/modal dropdown.
    /// </summary>
    public ObservableCollection<SupplierOption> AvailableSuppliers { get; } = [];

    /// <summary>
    /// Item type options for filter.
    /// </summary>
    public ObservableCollection<string> ItemTypeOptions { get; } = ["All", "Product", "Service"];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 products";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterProducts();

    #endregion

    #region Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalProductName = string.Empty;

    [ObservableProperty]
    private string _modalDescription = string.Empty;

    [ObservableProperty]
    private string _modalItemType = "Product";

    /// <summary>
    /// Gets whether a Product is selected (not Service) - used for showing threshold inputs.
    /// </summary>
    public bool IsProductSelected => ModalItemType == "Product";

    partial void OnModalItemTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsProductSelected));
    }

    [ObservableProperty]
    private CategoryOption? _modalCategory;

    [ObservableProperty]
    private string? _modalCategoryId;

    partial void OnModalCategoryIdChanged(string? value)
    {
        // Update ModalCategory when CategoryId changes
        ModalCategory = value != null ? AvailableCategories.FirstOrDefault(c => c.Id == value) : null;
    }

    [ObservableProperty]
    private SupplierOption? _modalSupplier;

    [ObservableProperty]
    private bool _modalTrackInventory;

    [ObservableProperty]
    private string _modalReorderPoint = string.Empty;

    [ObservableProperty]
    private string _modalOverstockThreshold = string.Empty;

    [ObservableProperty]
    private string _modalUnitPrice = string.Empty;

    [ObservableProperty]
    private string _modalCostPrice = string.Empty;

    [ObservableProperty]
    private string _modalSku = string.Empty;

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string? _modalProductNameError;

    [ObservableProperty]
    private string? _modalCategoryError;

    /// <summary>
    /// The product being edited (null for add).
    /// </summary>
    private Product? _editingProduct;

    /// <summary>
    /// The product being deleted.
    /// </summary>
    private ProductDisplayItem? _deletingProduct;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// Item types for dropdown.
    /// </summary>
    public ObservableCollection<string> ItemTypes { get; } = ["Product", "Service"];

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ProductsPageViewModel()
    {
        LoadProducts();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to product modal events to refresh data
        if (App.ProductModalsViewModel != null)
        {
            App.ProductModalsViewModel.ProductSaved += OnProductSaved;
            App.ProductModalsViewModel.ProductDeleted += OnProductDeleted;
            App.ProductModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ProductModalsViewModel.FiltersCleared += OnFiltersCleared;
        }

        // Subscribe to plan status changes so we update when user upgrades
        App.PlanStatusChanged += OnPlanStatusChanged;
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.ProductModalsViewModel != null)
        {
            App.ProductModalsViewModel.ProductSaved -= OnProductSaved;
            App.ProductModalsViewModel.ProductDeleted -= OnProductDeleted;
            App.ProductModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ProductModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
        App.PlanStatusChanged -= OnPlanStatusChanged;
    }

    /// <summary>
    /// Handles plan status changes by updating HasPremium.
    /// </summary>
    private void OnPlanStatusChanged(object? sender, PlanStatusChangedEventArgs e)
    {
        HasPremium = e.HasPremium;
    }

    /// <summary>
    /// Handles undo/redo state changes by refreshing the products.
    /// </summary>
    private bool _needsRefresh;

    /// <summary>
    /// The sidebar opens this page on a tab, under its own page name.
    /// </summary>
    private static bool IsThisPage(string? pageName) =>
        pageName is PageNames.Products or PageNames.ExpenseProducts or PageNames.RevenueProducts;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (!IsThisPage(App.NavigationService?.CurrentPageName))
        {
            _needsRefresh = true;
            return;
        }
        LoadProducts();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (IsThisPage(e.PageName) && _needsRefresh)
        {
            _needsRefresh = false;
            LoadProducts();
        }
    }

    private void OnProductSaved(object? sender, EventArgs e)
    {
        LoadProducts();
    }

    private void OnProductDeleted(object? sender, EventArgs e)
    {
        LoadProducts();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.ProductModalsViewModel;
        if (modals != null)
        {
            FilterItemType = modals.FilterItemType;
            FilterCategory = modals.FilterCategory;
            FilterSupplier = modals.FilterSupplier;
        }
        CurrentPage = 1;
        FilterProducts();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterItemType = "All";
        FilterCategory = null;
        FilterSupplier = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterProducts();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads products from the company data.
    /// </summary>
    private void LoadProducts()
    {
        _allProducts.Clear();
        ExpenseProducts.Clear();
        RevenueProducts.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null)
            return;

        _allProducts.AddRange(companyData.Products);
        UpdateStatistics();
        UpdateDropdownOptions();
        FilterProducts();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Count expense vs revenue products based on category type
        var expenseCategoryIds = companyData.Categories
            .Where(c => c.Type == CategoryType.Expense)
            .Select(c => c.Id)
            .ToHashSet();

        var revenueCategoryIds = companyData.Categories
            .Where(c => c.Type == CategoryType.Revenue)
            .Select(c => c.Id)
            .ToHashSet();

        ExpenseProductsCount = _allProducts.Count(p =>
            string.IsNullOrEmpty(p.CategoryId) || expenseCategoryIds.Contains(p.CategoryId));

        RevenueProductsCount = _allProducts.Count(p =>
            !string.IsNullOrEmpty(p.CategoryId) && revenueCategoryIds.Contains(p.CategoryId));
    }

    /// <summary>
    /// Updates the dropdown options from company data.
    /// </summary>
    private void UpdateDropdownOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Update categories
        AvailableCategories.Clear();
        AvailableCategories.Add(new CategoryOption { Id = null, Name = "All Categories" });

        var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
        var categories = companyData.Categories
            .Where(c => c.Type == targetType)
            .OrderBy(c => c.Name);

        foreach (var cat in categories)
        {
            AvailableCategories.Add(new CategoryOption { Id = cat.Id, Name = cat.Name });
        }

        // Update CategoryItems for the searchable input (excludes "All Categories")
        CategoryItems.Clear();
        foreach (var cat in categories)
        {
            CategoryItems.Add(new CategoryItem { Id = cat.Id, Name = cat.Name });
        }
        OnPropertyChanged(nameof(HasCategories));

        // Update suppliers
        AvailableSuppliers.Clear();
        AvailableSuppliers.Add(new SupplierOption { Id = null, Name = "All Suppliers" });

        foreach (var supplier in companyData.Suppliers.OrderBy(s => s.Name))
        {
            AvailableSuppliers.Add(new SupplierOption { Id = supplier.Id, Name = supplier.Name });
        }
    }

    /// <summary>
    /// Refreshes the products from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshProducts()
    {
        LoadProducts();
    }

    /// <summary>
    /// Filters products based on current tab, search query, and filters.
    /// </summary>
    private void FilterProducts()
    {
        var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
        var targetCollection = IsExpensesTabSelected ? ExpenseProducts : RevenueProducts;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            targetCollection.Clear();
            return;
        }

        // Get categories for the current tab type
        var categoryIds = companyData.Categories
            .Where(c => c.Type == targetType)
            .Select(c => c.Id)
            .ToHashSet();

        // Filter products by category type
        IEnumerable<Product> filtered = _allProducts
            .Where(p => string.IsNullOrEmpty(p.CategoryId) || categoryIds.Contains(p.CategoryId));

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, p => [p.Name, p.Sku, p.Description])
                .ToList();
        }

        // Apply item type filter on the product's own type
        if (FilterItemType != "All")
        {
            filtered = filtered.Where(p => p.ItemType == FilterItemType);
        }

        if (!string.IsNullOrWhiteSpace(FilterCategory) && FilterCategory != "All Categories")
        {
            var categoryOption = AvailableCategories.FirstOrDefault(c => c.Name == FilterCategory);
            if (categoryOption?.Id != null)
            {
                filtered = filtered.Where(p => p.CategoryId == categoryOption.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(FilterSupplier) && FilterSupplier != "All Suppliers")
        {
            var supplierOption = AvailableSuppliers.FirstOrDefault(s => s.Name == FilterSupplier);
            if (supplierOption?.Id != null)
            {
                filtered = filtered.Where(p => p.SupplierId == supplierOption.Id);
            }
        }

        var displayItems = filtered.Select(product =>
        {
            var category = companyData.Categories.FirstOrDefault(c => c.Id == product.CategoryId);
            var supplier = companyData.Suppliers.FirstOrDefault(s => s.Id == product.SupplierId);

            return new ProductDisplayItem
            {
                Id = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                Description = string.IsNullOrWhiteSpace(product.Description) ? "-" : product.Description,
                ItemType = product.ItemType,
                CategoryName = category?.Name ?? "-",
                SupplierName = supplier?.Name ?? "-",
                ReorderPoint = product.TrackInventory && product.ReorderPoint > 0 ? product.ReorderPoint.ToString() : "-",
                OverstockThreshold = product.TrackInventory && product.OverstockThreshold > 0 ? product.OverstockThreshold.ToString() : "-",
                UnitPrice = product.UnitPrice,
                CostPrice = product.CostPrice,
                TrackInventory = product.TrackInventory,
                IsHighlighted = product.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<ProductDisplayItem, object?>>
                {
                    ["Name"] = p => p.Name,
                    ["Type"] = p => p.ItemType,
                    ["Description"] = p => p.Description,
                    ["Category"] = p => p.CategoryName,
                    ["Supplier"] = p => p.SupplierName
                },
                p => p.Name);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedProducts = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        targetCollection.ReplaceAll(pagedProducts);

        OnPropertyChanged(nameof(CurrentProducts));
    }

    protected override void UpdatePageNumbers()
    {
        PageNumbers.Clear();
        var startPage = Math.Max(1, CurrentPage - 2);
        var endPage = Math.Min(TotalPages, startPage + 4);
        startPage = Math.Max(1, endPage - 4);

        for (var i = startPage; i <= endPage; i++)
        {
            PageNumbers.Add(i);
        }
    }

    private void UpdatePaginationText(int totalCount)
    {
        PaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, CurrentPage, PageSize, TotalPages, "product");
    }

    #endregion

    #region Add Product

    /// <summary>
    /// Opens the Add Product modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.ProductModalsViewModel?.OpenAddModal(IsExpensesTabSelected);
    }

    /// <summary>
    /// Closes the Add modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Saves a new product.
    /// </summary>
    [RelayCommand]
    private void SaveNewProduct()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Generate new ID
        companyData.IdCounters.Product++;
        var newId = $"PRD-{companyData.IdCounters.Product:D3}";

        var reorderPoint = decimal.TryParse(ModalReorderPoint, out var rp) ? rp : 0;
        var overstockThreshold = decimal.TryParse(ModalOverstockThreshold, out var ot) ? ot : 0;

        var newProduct = new Product
        {
            Id = newId,
            Name = ModalProductName.Trim(),
            Description = string.IsNullOrWhiteSpace(ModalDescription) ? string.Empty : ModalDescription.Trim(),
            Sku = string.IsNullOrWhiteSpace(ModalSku) ? newId : ModalSku.Trim(),
            CategoryId = ModalCategory?.Id,
            SupplierId = ModalSupplier?.Id,
            UnitPrice = decimal.TryParse(ModalUnitPrice, out var unitPrice) ? unitPrice : 0,
            CostPrice = decimal.TryParse(ModalCostPrice, out var costPrice) ? costPrice : 0,
            TrackInventory = ModalTrackInventory,
            ReorderPoint = reorderPoint,
            OverstockThreshold = overstockThreshold,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        companyData.Products.Add(newProduct);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ProductCreated);
        companyData.MarkAsModified();

        // Record undo action
        var productToUndo = newProduct;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add product '{newProduct.Name}'",
            () =>
            {
                companyData.Products.Remove(productToUndo);
                companyData.MarkAsModified();
                LoadProducts();
            },
            () =>
            {
                companyData.Products.Add(productToUndo);
                companyData.MarkAsModified();
                LoadProducts();
            }));

        // Reload and close
        LoadProducts();
        CloseAddModal();
    }

    #endregion

    #region Edit Product

    /// <summary>
    /// Opens the Edit Product modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(ProductDisplayItem? item)
    {
        App.ProductModalsViewModel?.OpenEditModal(item, IsExpensesTabSelected);
    }

    /// <summary>
    /// Closes the Edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingProduct = null;
        ClearModalFields();
    }

    /// <summary>
    /// Saves changes to an existing product.
    /// </summary>
    [RelayCommand]
    private void SaveEditedProduct()
    {
        if (!ValidateModal() || _editingProduct == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Store old values for undo
        var oldName = _editingProduct.Name;
        var oldDescription = _editingProduct.Description;
        var oldSku = _editingProduct.Sku;
        var oldCategoryId = _editingProduct.CategoryId;
        var oldSupplierId = _editingProduct.SupplierId;
        var oldUnitPrice = _editingProduct.UnitPrice;
        var oldCostPrice = _editingProduct.CostPrice;
        var oldTrackInventory = _editingProduct.TrackInventory;
        var oldReorderPoint = _editingProduct.ReorderPoint;
        var oldOverstockThreshold = _editingProduct.OverstockThreshold;

        // Store new values
        var newName = ModalProductName.Trim();
        var newDescription = string.IsNullOrWhiteSpace(ModalDescription) ? string.Empty : ModalDescription.Trim();
        var newSku = string.IsNullOrWhiteSpace(ModalSku) ? _editingProduct.Id : ModalSku.Trim();
        var newCategoryId = ModalCategory?.Id;
        var newSupplierId = ModalSupplier?.Id;
        var newUnitPrice = decimal.TryParse(ModalUnitPrice, out var unitPrice) ? unitPrice : 0;
        var newCostPrice = decimal.TryParse(ModalCostPrice, out var costPrice) ? costPrice : 0;
        var newReorderPoint = decimal.TryParse(ModalReorderPoint, out var rp) ? rp : 0;
        var newOverstockThreshold = decimal.TryParse(ModalOverstockThreshold, out var ot) ? ot : 0;
        var newTrackInventory = ModalTrackInventory;

        // Update the product
        var productToEdit = _editingProduct;
        productToEdit.Name = newName;
        productToEdit.Description = newDescription;
        productToEdit.Sku = newSku;
        productToEdit.CategoryId = newCategoryId;
        productToEdit.SupplierId = newSupplierId;
        productToEdit.UnitPrice = newUnitPrice;
        productToEdit.CostPrice = newCostPrice;
        productToEdit.TrackInventory = newTrackInventory;
        productToEdit.ReorderPoint = newReorderPoint;
        productToEdit.OverstockThreshold = newOverstockThreshold;
        productToEdit.UpdatedAt = DateTime.UtcNow;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit product '{newName}'",
            () =>
            {
                productToEdit.Name = oldName;
                productToEdit.Description = oldDescription;
                productToEdit.Sku = oldSku;
                productToEdit.CategoryId = oldCategoryId;
                productToEdit.SupplierId = oldSupplierId;
                productToEdit.UnitPrice = oldUnitPrice;
                productToEdit.CostPrice = oldCostPrice;
                productToEdit.TrackInventory = oldTrackInventory;
                productToEdit.ReorderPoint = oldReorderPoint;
                productToEdit.OverstockThreshold = oldOverstockThreshold;
                companyData.MarkAsModified();
                LoadProducts();
            },
            () =>
            {
                productToEdit.Name = newName;
                productToEdit.Description = newDescription;
                productToEdit.Sku = newSku;
                productToEdit.CategoryId = newCategoryId;
                productToEdit.SupplierId = newSupplierId;
                productToEdit.UnitPrice = newUnitPrice;
                productToEdit.CostPrice = newCostPrice;
                productToEdit.TrackInventory = newTrackInventory;
                productToEdit.ReorderPoint = newReorderPoint;
                productToEdit.OverstockThreshold = newOverstockThreshold;
                companyData.MarkAsModified();
                LoadProducts();
            }));

        // Reload and close
        LoadProducts();
        CloseEditModal();
    }

    #endregion

    #region Delete Product

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(ProductDisplayItem? item)
    {
        App.ProductModalsViewModel?.OpenDeleteConfirm(item);
    }

    /// <summary>
    /// Closes the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        _deletingProduct = null;
    }

    /// <summary>
    /// Confirms and deletes the product.
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (_deletingProduct == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var product = companyData.Products.FirstOrDefault(p => p.Id == _deletingProduct.Id);
        if (product != null)
        {
            var deletedProduct = product;
            companyData.Products.Remove(product);
            companyData.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete product '{deletedProduct.Name}'",
                () =>
                {
                    companyData.Products.Add(deletedProduct);
                    companyData.MarkAsModified();
                    LoadProducts();
                },
                () =>
                {
                    companyData.Products.Remove(deletedProduct);
                    companyData.MarkAsModified();
                    LoadProducts();
                }));
        }

        LoadProducts();
        CloseDeleteConfirm();
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.ProductModalsViewModel?.OpenFilterModal(IsExpensesTabSelected);
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
    /// Applies the current filters and closes the modal.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        CurrentPage = 1;
        FilterProducts();
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        FilterItemType = "All";
        FilterCategory = null;
        FilterSupplier = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterProducts();
        CloseFilterModal();
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalProductName = string.Empty;
        ModalDescription = string.Empty;
        ModalItemType = "Product";
        ModalCategory = null;
        ModalCategoryId = null;
        ModalSupplier = null;
        ModalReorderPoint = string.Empty;
        ModalOverstockThreshold = string.Empty;
        ModalUnitPrice = string.Empty;
        ModalCostPrice = string.Empty;
        ModalSku = string.Empty;
        ModalError = null;
        ModalProductNameError = null;
        ModalCategoryError = null;
    }

    private bool ValidateModal()
    {
        // Clear all errors first
        ModalError = null;
        ModalProductNameError = null;
        ModalCategoryError = null;

        var isValid = true;

        // Validate product name (required)
        if (string.IsNullOrWhiteSpace(ModalProductName))
        {
            ModalProductNameError = "Product name is required.".Translate();
            isValid = false;
        }
        else
        {
            // Check for duplicate names
            var existingWithSameName = _allProducts.Any(p =>
                p.Name.Equals(ModalProductName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingProduct == null || p.Id != _editingProduct.Id));

            if (existingWithSameName)
            {
                ModalProductNameError = "A product with this name already exists.".Translate();
                isValid = false;
            }
        }

        // Validate category (always required)
        if (string.IsNullOrEmpty(ModalCategoryId))
        {
            ModalCategoryError = HasCategories
                ? "Category is required."
                : "Please create a category first.";
            isValid = false;
        }

        return isValid;
    }

    #endregion
}

/// <summary>
/// Display model for products in the UI.
/// </summary>
public partial class ProductDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sku = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _itemType = "Product";

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private string _reorderPoint = string.Empty;

    [ObservableProperty]
    private string _overstockThreshold = string.Empty;

    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private decimal _costPrice;

    [ObservableProperty]
    private bool _trackInventory;

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// Category option for dropdown.
/// </summary>
public class CategoryOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// Supplier option for dropdown.
/// </summary>
public class SupplierOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
