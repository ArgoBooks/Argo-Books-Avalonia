using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Controls;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Lost/Damaged page displaying lost and damaged inventory records.
/// </summary>
public partial class LostDamagedPageViewModel : SortablePageViewModelBase
{
    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public LostDamagedTableColumnWidths ColumnWidths => App.LostDamagedColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("LostDamaged", new Dictionary<string, bool>
    {
        ["Id"] = true,
        ["Product"] = true,
        ["Date"] = true,
        ["Reason"] = true,
        ["Loss"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showIdColumn = ColumnDefaults.Load("Id");

    [ObservableProperty]
    private bool _showProductColumn = ColumnDefaults.Load("Product");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showReasonColumn = ColumnDefaults.Load("Reason");

    [ObservableProperty]
    private bool _showLossColumn = ColumnDefaults.Load("Loss");

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

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterItems();

    #endregion

    #region Constructor

    public LostDamagedPageViewModel()
    {
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadItems();

        EnableDeferredUndoRefresh(p => p == PageNames.LostDamaged, LoadItems);

        // Subscribe to modal events
        if (App.LostDamagedModalsViewModel != null)
        {
            App.LostDamagedModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.LostDamagedModalsViewModel.FiltersCleared += OnFiltersCleared;
            App.LostDamagedModalsViewModel.ItemUndone += OnItemUndone;
        }
    }

    /// <summary>
    /// Unsubscribes from app-level and singleton events so this page VM can be garbage collected when
    /// the company is switched. Called by ClearPageCaches via <see cref="ICleanupViewModel"/>.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.LostDamagedModalsViewModel != null)
        {
            App.LostDamagedModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.LostDamagedModalsViewModel.FiltersCleared -= OnFiltersCleared;
            App.LostDamagedModalsViewModel.ItemUndone -= OnItemUndone;
        }
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        ActiveFilterCount = App.LostDamagedModalsViewModel?.ActiveFilterCount ?? 0;
        CurrentPage = 1;
        FilterItems();
    }

    protected override void ClearTableFilters() => App.LostDamagedModalsViewModel?.ClearFiltersCommand.Execute(null);

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        SearchQuery = null;
        CurrentPage = 1;
        FilterItems();
    }

    private void OnItemUndone(object? sender, EventArgs e)
    {
        LoadItems();
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
        // Each loss is in its sale's or purchase's currency (Calculations.md §10).
        if (App.CompanyManager?.CompanyData is not { } companyData)
            return;
        var complete = ReturnLossAmounts.TrySumDisplay(
            _allItems, l => l.ValueLost, l => ReturnLossAmounts.CurrencyOf(companyData, l), l => l.DateDiscovered,
            CurrencyService.GetDisplayAmountFromNative, out var totalValue);
        TotalLossValue = complete ? CurrencyService.Format(totalValue) : CurrencyService.PendingMarker;
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
            filtered = filtered.RankBySearch(SearchQuery, item => [item.Id, GetProductName(item.ProductId), item.Notes]);
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

        var displayItems = filtered.Select(CreateDisplayItem).ToList();

        displayItems = displayItems.ApplySort(
            SortColumn,
            SortDirection,
            new Dictionary<string, Func<LostDamagedDisplayItem, object?>>
            {
                ["Id"] = i => i.Id,
                ["Product"] = i => i.ProductName,
                ["Date"] = i => i.DateDiscovered,
                ["Reason"] = i => i.Reason,
                ["Loss"] = i => i.ValueLost
            },
            i => i.DateDiscovered);

        var pagedItems = Paginate(displayItems, "item");

        Items.ReplaceAll(pagedItems);
    }

    private LostDamagedDisplayItem CreateDisplayItem(LostDamaged item)
    {
        var productName = GetProductName(item.ProductId);
        var itemType = GetItemType(item.Reason);
        var companyData = App.CompanyManager?.CompanyData;
        var value = companyData == null
            ? item.ValueLost
            : CurrencyService.GetDisplayAmountFromNative(
                item.ValueLost, ReturnLossAmounts.CurrencyOf(companyData, item), item.DateDiscovered);

        return new LostDamagedDisplayItem
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductName = productName,
            ItemType = itemType,
            DateDiscovered = item.DateDiscovered,
            Reason = item.Reason.ToString(),
            ValueLost = value ?? 0m,
            ValueLostFormatted = value.HasValue ? CurrencyService.Format(value.Value) : CurrencyService.PendingMarker,
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
    public string ValueLostFormatted { get; init; } = string.Empty;
    public string QuantityFormatted => $"{Quantity} unit(s)";

}
