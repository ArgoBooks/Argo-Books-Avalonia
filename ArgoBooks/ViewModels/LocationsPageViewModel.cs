using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Locations page.
/// Displays warehouse and storage locations.
/// </summary>
public partial class LocationsPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalLocations;

    [ObservableProperty]
    private decimal _totalStockItems;

    [ObservableProperty]
    private string _totalInventoryValue = "$0";

    [ObservableProperty]
    private string _averageCapacityUsed = "0%";

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public LocationsTableColumnWidths ColumnWidths => App.LocationsColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showLocationColumn = ColumnVisibilityHelper.Load("Locations", "Location", true);

    [ObservableProperty]
    private bool _showTypeColumn = ColumnVisibilityHelper.Load("Locations", "Type", true);

    [ObservableProperty]
    private bool _showAddressColumn = ColumnVisibilityHelper.Load("Locations", "Address", true);

    [ObservableProperty]
    private bool _showManagerColumn = ColumnVisibilityHelper.Load("Locations", "Manager", true);

    partial void OnShowLocationColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Location", value); ColumnVisibilityHelper.Save("Locations", "Location", value); }
    partial void OnShowTypeColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Type", value); ColumnVisibilityHelper.Save("Locations", "Type", value); }
    partial void OnShowAddressColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Address", value); ColumnVisibilityHelper.Save("Locations", "Address", value); }
    partial void OnShowManagerColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Manager", value); ColumnVisibilityHelper.Save("Locations", "Manager", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Locations");
        ShowLocationColumn = true;
        ShowTypeColumn = true;
        ShowAddressColumn = true;
        ShowManagerColumn = true;
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterLocations();
        });

    [ObservableProperty]
    private string _filterType = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    #endregion

    #region Locations Collection

    /// <summary>
    /// All locations (unfiltered).
    /// </summary>
    private readonly List<Location> _allLocations = [];

    /// <summary>
    /// Locations for display in the table.
    /// </summary>
    public BatchObservableCollection<LocationDisplayItem> Locations { get; } = [];

    /// <summary>
    /// Location type options for filter.
    /// </summary>
    public ObservableCollection<string> TypeOptions { get; } = ["All", "Warehouse", "Storage Facility", "Factory", "Retail Store", "Distribution Center"];

    /// <summary>
    /// Status options for filter.
    /// </summary>
    public ObservableCollection<string> StatusOptions { get; } = ["All", "Active", "Inactive"];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 locations";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterLocations();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public LocationsPageViewModel()
    {
        LoadLocations();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to modal events to refresh when locations are saved
        if (App.LocationsModalsViewModel != null)
        {
            App.LocationsModalsViewModel.LocationSaved += OnModalLocationSaved;
            App.LocationsModalsViewModel.LocationDeleted += OnModalLocationDeleted;
            App.LocationsModalsViewModel.FiltersApplied += OnFiltersApplied;
        }
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
        if (App.LocationsModalsViewModel != null)
        {
            App.LocationsModalsViewModel.LocationSaved -= OnModalLocationSaved;
            App.LocationsModalsViewModel.LocationDeleted -= OnModalLocationDeleted;
            App.LocationsModalsViewModel.FiltersApplied -= OnFiltersApplied;
        }
    }

    /// <summary>
    /// Handles filter applied events from the modals.
    /// </summary>
    private void OnFiltersApplied(object? sender, LocationsFilterAppliedEventArgs e)
    {
        FilterType = e.Type;
        FilterStatus = e.Status;
        CurrentPage = 1;
        FilterLocations();
    }

    /// <summary>
    /// Handles undo/redo state changes by refreshing the locations.
    /// </summary>
    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.Locations)
        {
            _needsRefresh = true;
            return;
        }
        LoadLocations();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.Locations && _needsRefresh)
        {
            _needsRefresh = false;
            LoadLocations();
        }
    }

    /// <summary>
    /// Handles location saved events from the modals.
    /// </summary>
    private void OnModalLocationSaved(object? sender, EventArgs e)
    {
        LoadLocations();
    }

    /// <summary>
    /// Handles location deleted events from the modals.
    /// </summary>
    private void OnModalLocationDeleted(object? sender, EventArgs e)
    {
        LoadLocations();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads locations from the company data.
    /// </summary>
    private void LoadLocations()
    {
        _allLocations.Clear();
        Locations.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Locations == null)
            return;

        _allLocations.AddRange(companyData.Locations);
        UpdateStatistics();
        FilterLocations();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        var companyData = App.CompanyManager?.CompanyData;

        TotalLocations = _allLocations.Count;

        // Calculate total stock items across all locations
        var inventory = companyData?.Inventory ?? [];
        TotalStockItems = inventory.Sum(i => i.InStock);

        // Calculate total inventory value
        var totalValue = inventory.Sum(i => i.InStock * i.UnitCost);
        TotalInventoryValue = $"${totalValue:N0}";

        // Calculate average capacity used
        if (_allLocations.Count > 0)
        {
            var totalCapacity = _allLocations.Sum(l => l.Capacity);
            var totalUtilization = _allLocations.Sum(l => l.CurrentUtilization);
            var avgPercentage = totalCapacity > 0 ? (double)totalUtilization / totalCapacity * 100 : 0;
            AverageCapacityUsed = $"{avgPercentage:N0}%";
        }
        else
        {
            AverageCapacityUsed = "0%";
        }
    }

    /// <summary>
    /// Refreshes the locations from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshLocations()
    {
        LoadLocations();
    }

    /// <summary>
    /// Filters locations based on search query and filters.
    /// </summary>
    private void FilterLocations()
    {
        var filtered = _allLocations.ToList();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .Select(l => new
                {
                    Location = l,
                    NameScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, l.Name),
                    AddressScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, l.Address.City),
                    ContactScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, l.ContactPerson),
                    IdScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, l.Id)
                })
                .Where(x => x.NameScore >= 0 || x.AddressScore >= 0 || x.ContactScore >= 0 || x.IdScore >= 0)
                .OrderByDescending(x => Math.Max(Math.Max(x.NameScore, x.AddressScore), Math.Max(x.ContactScore, x.IdScore)))
                .Select(x => x.Location)
                .ToList();
        }

        // Apply type filter (using metadata or naming convention)
        if (FilterType != "All")
        {
            // For now, filter by name pattern until we add a Type field to Location
            filtered = filtered.Where(l => GetLocationType(l) == FilterType).ToList();
        }

        if (FilterStatus != "All")
        {
            var isActive = FilterStatus == "Active";
            // Location has no Status field, so every location counts as active and
            // selecting "Inactive" always yields nothing. The branch is kept so the filter
            // starts working on its own if a Status field is added.
            if (!isActive)
            {
                filtered = [];
            }
        }

        var displayItems = filtered.Select(location =>
        {
            var addressParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(location.Address.Street))
                addressParts.Add(location.Address.Street);
            if (!string.IsNullOrWhiteSpace(location.Address.City))
                addressParts.Add(location.Address.City);
            if (!string.IsNullOrWhiteSpace(location.Address.State))
                addressParts.Add(location.Address.State);
            var addressString = addressParts.Count > 0 ? string.Join(", ", addressParts) : "-";

            return new LocationDisplayItem
            {
                Id = location.Id,
                Name = location.Name,
                Type = GetLocationType(location),
                Address = addressString,
                Manager = string.IsNullOrWhiteSpace(location.ContactPerson) ? "-" : location.ContactPerson,
                Phone = location.Phone,
                Capacity = location.Capacity,
                CurrentUtilization = location.CurrentUtilization,
                UtilizationPercentage = location.UtilizationPercentage,
                IsActive = true, // All locations are active for now
                CreatedAt = location.CreatedAt
            };
        }).ToList();

        // Apply sorting
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<LocationDisplayItem, object?>>
                {
                    ["Location"] = l => l.Name,
                    ["Type"] = l => l.Type,
                    ["Address"] = l => l.Address,
                    ["Manager"] = l => l.Manager
                },
                l => l.Name);
        }

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedLocations = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Locations.ReplaceAll(pagedLocations);
    }

    /// <summary>
    /// Gets the location type based on naming convention.
    /// </summary>
    private static string GetLocationType(Location location)
    {
        var name = location.Name.ToLowerInvariant();
        if (name.Contains("warehouse")) return "Warehouse";
        if (name.Contains("storage")) return "Storage Facility";
        if (name.Contains("factory")) return "Factory";
        if (name.Contains("retail") || name.Contains("store")) return "Retail Store";
        if (name.Contains("distribution")) return "Distribution Center";
        return "Warehouse"; // Default
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
            totalCount, CurrentPage, PageSize, TotalPages, "location");
    }

    #endregion

    #region Modal Commands

    /// <summary>
    /// Opens the Add Location modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.LocationsModalsViewModel?.OpenAddModal();
    }

    /// <summary>
    /// Opens the Edit Location modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(LocationDisplayItem? item)
    {
        if (item == null) return;
        App.LocationsModalsViewModel?.OpenEditModal(item);
    }

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(LocationDisplayItem? item)
    {
        if (item == null) return;
        App.LocationsModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal via the modals ViewModel.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.LocationsModalsViewModel?.OpenFilterModal();
    }

    #endregion
}

/// <summary>
/// Display model for locations in the UI.
/// </summary>
public partial class LocationDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _manager = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private int _capacity;

    [ObservableProperty]
    private int _currentUtilization;

    [ObservableProperty]
    private double _utilizationPercentage;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private DateTime _createdAt;

    /// <summary>
    /// Gets the initials from the location name for avatar display.
    /// </summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            if (parts is [{ Length: >= 2 }])
                return parts[0][..2].ToUpperInvariant();
            if (parts is [{ Length: 1 }])
                return parts[0].ToUpperInvariant();
            return "?";
        }
    }

    public string StatusText => IsActive ? "Active" : "Inactive";

    /// <summary>
    /// Gets the status color (green for active).
    /// </summary>
    public string StatusColor => IsActive ? AppColors.Success : AppColors.GrayMedium;

    /// <summary>
    /// Gets the status background color.
    /// </summary>
    public string StatusBackground => IsActive ? AppColors.SuccessLight : AppColors.GrayLightest;
}
