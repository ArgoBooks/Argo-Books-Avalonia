using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Lost/Damaged page displaying lost and damaged inventory records.
/// </summary>
public partial class LostDamagedPageViewModel : ViewModelBase, ICleanupViewModel
{
    #region Responsive Header

    /// <summary>
    /// Helper for responsive header layout adjustments.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public LostDamagedTableColumnWidths ColumnWidths => App.LostDamagedColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private bool _isColumnMenuOpen;

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showIdColumn = ColumnVisibilityHelper.Load("LostDamaged", "Id", true);

    [ObservableProperty]
    private bool _showProductColumn = ColumnVisibilityHelper.Load("LostDamaged", "Product", true);

    [ObservableProperty]
    private bool _showDateColumn = ColumnVisibilityHelper.Load("LostDamaged", "Date", true);

    [ObservableProperty]
    private bool _showReasonColumn = ColumnVisibilityHelper.Load("LostDamaged", "Reason", true);

    [ObservableProperty]
    private bool _showLossColumn = ColumnVisibilityHelper.Load("LostDamaged", "Loss", true);

    partial void OnShowIdColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Id", value); ColumnVisibilityHelper.Save("LostDamaged", "Id", value); }
    partial void OnShowProductColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Product", value); ColumnVisibilityHelper.Save("LostDamaged", "Product", value); }
    partial void OnShowDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Date", value); ColumnVisibilityHelper.Save("LostDamaged", "Date", value); }
    partial void OnShowReasonColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reason", value); ColumnVisibilityHelper.Save("LostDamaged", "Reason", value); }
    partial void OnShowLossColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Loss", value); ColumnVisibilityHelper.Save("LostDamaged", "Loss", value); }

    [RelayCommand]
    private void ToggleColumnMenu()
    {
        IsColumnMenuOpen = !IsColumnMenuOpen;
    }

    [RelayCommand]
    private void CloseColumnMenu()
    {
        IsColumnMenuOpen = false;
    }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("LostDamaged");
        ShowIdColumn = true;
        ShowProductColumn = true;
        ShowDateColumn = true;
        ShowReasonColumn = true;
        ShowLossColumn = true;
    }

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalLostDamaged;

    [ObservableProperty]
    private int _lostItems;

    [ObservableProperty]
    private int _damagedItems;

    [ObservableProperty]
    private string _totalLossValue = "$0.00";

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterItems();
        });

    #endregion

    #region Items Collection

    private readonly List<LostDamaged> _allItems = [];

    public BatchObservableCollection<LostDamagedDisplayItem> Items { get; } = [];

    #endregion

    #region Pagination

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    public ObservableCollection<int> PageSizeOptions { get; } = [5, 10, 15, 25, 50];

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        FilterItems();
    }

    [ObservableProperty]
    private string _paginationText = "0 items";

    public ObservableCollection<int> PageNumbers { get; } = [];

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        FilterItems();
    }

    [RelayCommand]
    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
            CurrentPage--;
    }

    [RelayCommand]
    private void GoToNextPage()
    {
        if (CanGoToNextPage)
            CurrentPage++;
    }

    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
            CurrentPage = page;
    }

    #endregion

    #region Constructor

    public LostDamagedPageViewModel()
    {
        ColumnVisibilityHelper.SyncToManager(this);
        LoadItems();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to modal events
        if (App.LostDamagedModalsViewModel != null)
        {
            App.LostDamagedModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.LostDamagedModalsViewModel.FiltersCleared += OnFiltersCleared;
            App.LostDamagedModalsViewModel.ItemUndone += OnItemUndone;
        }

        // Subscribe to language changes to refresh translated content
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Unsubscribes from app-level and singleton events so this page VM can be garbage collected when
    /// the company is switched. Called by ClearPageCaches via <see cref="ICleanupViewModel"/>.
    /// </summary>
    public void Cleanup()
    {
        CancelPendingSearch();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.LostDamagedModalsViewModel != null)
        {
            App.LostDamagedModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.LostDamagedModalsViewModel.FiltersCleared -= OnFiltersCleared;
            App.LostDamagedModalsViewModel.ItemUndone -= OnItemUndone;
        }
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        FilterItems();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        CurrentPage = 1;
        FilterItems();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        SearchQuery = null;
        CurrentPage = 1;
        FilterItems();
    }

    private void OnItemUndone(object? sender, EventArgs e)
    {
        LoadItems();
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.LostDamaged)
        {
            _needsRefresh = true;
            return;
        }
        LoadItems();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.LostDamaged && _needsRefresh)
        {
            _needsRefresh = false;
            LoadItems();
        }
    }

    #endregion

    #region Data Loading

    private void LoadItems()
    {
        _allItems.Clear();
        Items.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.LostDamaged == null)
            return;

        _allItems.AddRange(companyData.LostDamaged);
        UpdateStatistics();
        FilterItems();
    }

    private void UpdateStatistics()
    {
        TotalLostDamaged = _allItems.Count;
        LostItems = _allItems.Count(item => item.Reason == LostDamagedReason.Lost || item.Reason == LostDamagedReason.Stolen);
        DamagedItems = _allItems.Count(item => item.Reason == LostDamagedReason.Damaged || item.Reason == LostDamagedReason.Expired);
        var totalValue = _allItems.Sum(item => item.ValueLost);
        TotalLossValue = CurrencyService.Format(totalValue);
    }

    [RelayCommand]
    private void RefreshItems()
    {
        LoadItems();
    }

    private void FilterItems()
    {
        IEnumerable<LostDamaged> filtered = _allItems;

        // Get filter values from modals view model
        var modals = App.LostDamagedModalsViewModel;
        var filterType = modals?.FilterType ?? "All";
        var filterReason = modals?.FilterReason ?? "All";
        var filterDateFrom = modals?.FilterDateFrom;
        var filterDateTo = modals?.FilterDateTo;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(item =>
                item.Id.ToLowerInvariant().Contains(query) ||
                GetProductName(item.ProductId).ToLowerInvariant().Contains(query) ||
                item.Notes.ToLowerInvariant().Contains(query)
            );
        }

        if (filterType != "All")
        {
            filtered = filterType switch
            {
                "Lost" => filtered.Where(item =>
                    item.Reason == LostDamagedReason.Lost ||
                    item.Reason == LostDamagedReason.Stolen),
                "Damaged" => filtered.Where(item =>
                    item.Reason == LostDamagedReason.Damaged ||
                    item.Reason == LostDamagedReason.Expired ||
                    item.Reason == LostDamagedReason.Other),
                _ => filtered
            };
        }

        if (filterReason != "All")
        {
            var reason = Enum.TryParse<LostDamagedReason>(filterReason, out var r) ? r : LostDamagedReason.Other;
            filtered = filtered.Where(item => item.Reason == reason);
        }

        if (filterDateFrom.HasValue)
        {
            filtered = filtered.Where(item => item.DateDiscovered >= filterDateFrom.Value.DateTime);
        }
        if (filterDateTo.HasValue)
        {
            filtered = filtered.Where(item => item.DateDiscovered <= filterDateTo.Value.DateTime);
        }

        // Sort by date descending (newest first)
        filtered = filtered.OrderByDescending(item => item.DateDiscovered);

        var displayItems = filtered.Select(CreateDisplayItem).ToList();

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedItems = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Items.ReplaceAll(pagedItems);
    }

    private LostDamagedDisplayItem CreateDisplayItem(LostDamaged item)
    {
        var productName = GetProductName(item.ProductId);
        var itemType = GetItemType(item.Reason);

        return new LostDamagedDisplayItem
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductName = productName,
            ItemType = itemType,
            DateDiscovered = item.DateDiscovered,
            Reason = item.Reason.ToString(),
            ValueLost = item.ValueLost,
            Notes = item.Notes,
            Quantity = item.Quantity,
            InsuranceClaim = item.InsuranceClaim
        };
    }

    private string GetProductName(string productId)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var product = companyData?.GetProduct(productId);
        return product?.Name ?? "Unknown Product";
    }

    private static string GetItemType(LostDamagedReason reason)
    {
        return reason switch
        {
            LostDamagedReason.Lost or LostDamagedReason.Stolen => "Lost",
            LostDamagedReason.Damaged or LostDamagedReason.Expired or LostDamagedReason.Other => "Damaged",
            _ => "Unknown"
        };
    }

    private void UpdatePageNumbers()
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
            totalCount, CurrentPage, PageSize, TotalPages, "item");
    }

    #endregion

    #region Filter Modal Commands

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.LostDamagedModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Action Commands

    [RelayCommand]
    private void ViewItemDetails(LostDamagedDisplayItem? item)
    {
        if (item == null) return;

        App.LostDamagedModalsViewModel?.OpenViewDetailsModal(
            item.Id,
            item.ProductName,
            item.ItemType,
            item.Reason,
            item.Notes,
            item.DateFormatted,
            item.ValueLostFormatted,
            item.QuantityFormatted);
    }

    [RelayCommand]
    private void UndoItem(LostDamagedDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var lostDamagedRecord = companyData?.LostDamaged.FirstOrDefault(ld => ld.Id == item.Id);
        if (lostDamagedRecord != null)
        {
            App.LostDamagedModalsViewModel?.OpenUndoItemModal(lostDamagedRecord, $"{item.Id} - {item.ProductName}");
        }
    }

    #endregion
}

/// <summary>
/// Display model for lost/damaged items in the UI.
/// </summary>
public partial class LostDamagedDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private string _itemType = string.Empty;

    [ObservableProperty]
    private DateTime _dateDiscovered;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private decimal _valueLost;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private int _quantity;

    [ObservableProperty]
    private bool _insuranceClaim;

    // Computed properties for display
    public string DateFormatted => DateDiscovered.ToString("MMM d, yyyy");
    public string ValueLostFormatted => CurrencyService.Format(ValueLost);
    public string QuantityFormatted => $"{Quantity} unit(s)";

}
