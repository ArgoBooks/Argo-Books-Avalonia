using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Suppliers page.
/// </summary>
public partial class SuppliersPageViewModel : SortablePageViewModelBase
{
    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public SuppliersTableColumnWidths ColumnWidths => App.SuppliersColumnWidths;

    #endregion

    #region Column Visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Suppliers", new Dictionary<string, bool>
    {
        ["Supplier"] = true,
        ["Email"] = true,
        ["Phone"] = true,
        ["Address"] = true,
        ["Country"] = true,
        ["Products"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnDefaults.Load("Supplier");

    [ObservableProperty]
    private bool _showEmailColumn = ColumnDefaults.Load("Email");

    [ObservableProperty]
    private bool _showPhoneColumn = ColumnDefaults.Load("Phone");

    [ObservableProperty]
    private bool _showAddressColumn = ColumnDefaults.Load("Address");

    [ObservableProperty]
    private bool _showCountryColumn = ColumnDefaults.Load("Country");

    [ObservableProperty]
    private bool _showProductsColumn = ColumnDefaults.Load("Products");

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterSuppliers();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCountry;

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterSuppliers();

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalSuppliers;

    [ObservableProperty]
    private int _activeSuppliers;

    [ObservableProperty]
    private int _totalCountries;

    [ObservableProperty]
    private int _totalProductsSupplied;

    #endregion

    #region Suppliers Collection

    /// <summary>
    /// All suppliers (unfiltered).
    /// </summary>
    private readonly List<Supplier> _allSuppliers = [];

    /// <summary>
    /// Filtered suppliers for display.
    /// </summary>
    public BatchObservableCollection<SupplierDisplayItem> Suppliers { get; } = [];

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public SuppliersPageViewModel()
    {
        LoadSuppliers();

        EnableDeferredUndoRefresh(p => p == PageNames.Suppliers, LoadSuppliers);

        // Subscribe to shared modal events to refresh data
        if (App.SupplierModalsViewModel != null)
        {
            App.SupplierModalsViewModel.SupplierSaved += OnSupplierModalClosed;
            App.SupplierModalsViewModel.SupplierDeleted += OnSupplierModalClosed;
            App.SupplierModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.SupplierModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.SupplierModalsViewModel != null)
        {
            App.SupplierModalsViewModel.SupplierSaved -= OnSupplierModalClosed;
            App.SupplierModalsViewModel.SupplierDeleted -= OnSupplierModalClosed;
            App.SupplierModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.SupplierModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles supplier modal closed events by refreshing the suppliers.
    /// </summary>
    private void OnSupplierModalClosed(object? sender, EventArgs e)
    {
        LoadSuppliers();
    }

    /// <summary>
    /// Handles filters applied event from shared modal.
    /// </summary>
    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        if (App.SupplierModalsViewModel != null)
        {
            FilterCountry = App.SupplierModalsViewModel.FilterCountry == "All" ? null : App.SupplierModalsViewModel.FilterCountry;
            FilterStatus = App.SupplierModalsViewModel.FilterStatus;
            ActiveFilterCount = App.SupplierModalsViewModel.ActiveFilterCount;
        }
        CurrentPage = 1;
        FilterSuppliers();
    }

    protected override void ClearTableFilters() => App.SupplierModalsViewModel?.ClearFiltersCommand.Execute(null);

    /// <summary>
    /// Handles filters cleared event from shared modal.
    /// </summary>
    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        FilterCountry = null;
        FilterStatus = "All";
        SearchQuery = null;
        CurrentPage = 1;
        FilterSuppliers();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads suppliers from the company data.
    /// </summary>
    private void LoadSuppliers()
    {
        _allSuppliers.Clear();
        Suppliers.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Suppliers == null)
            return;

        _allSuppliers.AddRange(companyData.Suppliers);
        UpdateStatistics();
        FilterSuppliers();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        TotalSuppliers = _allSuppliers.Count;

        // Count active suppliers (those with at least one product or used in purchases)
        var suppliersWithProducts = companyData.Products
            .Where(p => !string.IsNullOrEmpty(p.SupplierId))
            .Select(p => p.SupplierId)
            .Distinct()
            .ToHashSet();

        ActiveSuppliers = _allSuppliers.Count(s => suppliersWithProducts.Contains(s.Id));

        // Count unique countries
        TotalCountries = _allSuppliers
            .Select(s => s.Address.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        // Count products supplied
        TotalProductsSupplied = companyData.Products
            .Count(p => !string.IsNullOrEmpty(p.SupplierId));
    }

    /// <summary>
    /// Filters suppliers based on search query and filters.
    /// </summary>
    private void FilterSuppliers()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var filtered = _allSuppliers.AsEnumerable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, s => [s.Name, s.Id, s.Email, s.ContactPerson]);
        }

        if (!string.IsNullOrWhiteSpace(FilterCountry) && FilterCountry != "All Countries")
        {
            filtered = filtered.Where(s =>
                s.Address.Country.Equals(FilterCountry, StringComparison.OrdinalIgnoreCase));
        }

        if (FilterStatus != "All")
        {
            var suppliersWithProducts = companyData.Products
                .Where(p => !string.IsNullOrEmpty(p.SupplierId))
                .Select(p => p.SupplierId)
                .Distinct()
                .ToHashSet();

            filtered = FilterStatus == "Active"
                ? filtered.Where(s => suppliersWithProducts.Contains(s.Id))
                : filtered.Where(s => !suppliersWithProducts.Contains(s.Id));
        }

        // Pre-build product count lookup for O(1) access per supplier
        var productCountBySupplier = companyData.Products
            .Where(p => !string.IsNullOrEmpty(p.SupplierId))
            .GroupBy(p => p.SupplierId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Convert to a list and create display items with additional computed properties
        var displayItems = filtered.Select(supplier =>
        {
            var productCount = productCountBySupplier.GetValueOrDefault(supplier.Id);

            // Format address as comma-separated parts
            var addressParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(supplier.Address.Street))
                addressParts.Add(supplier.Address.Street);
            if (!string.IsNullOrWhiteSpace(supplier.Address.City))
                addressParts.Add(supplier.Address.City);
            if (!string.IsNullOrWhiteSpace(supplier.Address.State))
                addressParts.Add(supplier.Address.State);
            var addressString = addressParts.Count > 0 ? string.Join(", ", addressParts) : "-";

            var avatarBitmap = AvatarBitmapLoader.LoadSupplier(supplier);

            return new SupplierDisplayItem
            {
                Id = supplier.Id,
                Name = supplier.Name,
                ContactPerson = supplier.ContactPerson,
                Email = string.IsNullOrWhiteSpace(supplier.Email) ? "-" : supplier.Email,
                Phone = string.IsNullOrWhiteSpace(supplier.Phone) ? "-" : supplier.Phone,
                Address = addressString,
                Country = string.IsNullOrWhiteSpace(supplier.Address.Country) ? "-" : supplier.Address.Country,
                ProductCount = productCount,
                Initials = Helpers.InitialsHelper.From(supplier.Name),
                AvatarBitmap = avatarBitmap,
                HasAvatar = avatarBitmap != null,
                IsHighlighted = supplier.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<SupplierDisplayItem, object?>>
                {
                    ["Name"] = s => s.Name,
                    ["Email"] = s => s.Email,
                    ["Phone"] = s => s.Phone,
                    ["Address"] = s => s.Address,
                    ["Country"] = s => s.Country,
                    ["Products"] = s => s.ProductCount
                },
                s => s.Name);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedItems = Paginate(displayItems, "supplier");

        Suppliers.ReplaceAll(pagedItems);
    }

    #endregion

    #region Add Supplier

    /// <summary>
    /// Opens the Add Supplier modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.SupplierModalsViewModel?.OpenAddModal();
    }

    #endregion

    #region Edit Supplier

    /// <summary>
    /// Opens the Edit Supplier modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(SupplierDisplayItem? item)
    {
        App.SupplierModalsViewModel?.OpenEditModal(item);
    }

    #endregion

    #region Delete Supplier

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(SupplierDisplayItem? item)
    {
        App.SupplierModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.SupplierModalsViewModel?.OpenFilterModal();
    }

    #endregion
}

/// <summary>
/// Display model for suppliers in the UI.
/// </summary>
public partial class SupplierDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _contactPerson = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _country = string.Empty;

    [ObservableProperty]
    private int _productCount;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private bool _isHighlighted;
}
