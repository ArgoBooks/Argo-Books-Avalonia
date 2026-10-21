using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Assets page, which lists what the business keeps for years and what each is worth on the books.
/// </summary>
public partial class AssetsPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private int _assetsInUse;

    [ObservableProperty]
    private string _totalCost = "$0";

    [ObservableProperty]
    private string _totalBookValue = "$0";

    [ObservableProperty]
    private string _depreciationThisYear = "$0";

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public AssetsTableColumnWidths ColumnWidths => App.AssetsColumnWidths;

    #endregion

    #region Column Visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Assets", new Dictionary<string, bool>
    {
        ["Asset"] = true,
        ["Type"] = true,
        ["Purchased"] = true,
        ["Cost"] = true,
        ["ThisYear"] = true,
        ["BookValue"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showAssetColumn = ColumnDefaults.Load("Asset");

    [ObservableProperty]
    private bool _showTypeColumn = ColumnDefaults.Load("Type");

    [ObservableProperty]
    private bool _showPurchasedColumn = ColumnDefaults.Load("Purchased");

    [ObservableProperty]
    private bool _showCostColumn = ColumnDefaults.Load("Cost");

    [ObservableProperty]
    private bool _showThisYearColumn = ColumnDefaults.Load("ThisYear");

    [ObservableProperty]
    private bool _showBookValueColumn = ColumnDefaults.Load("BookValue");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Search

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterAssets();
        });

    #endregion

    #region Assets Collection

    private readonly List<AssetDisplayItem> _allAssets = [];

    /// <summary>
    /// Assets for display in the table.
    /// </summary>
    public BatchObservableCollection<AssetDisplayItem> Assets { get; } = [];

    #endregion

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterAssets();

    public AssetsPageViewModel()
    {
        LoadAssets();

        EnableDeferredUndoRefresh(p => p == PageNames.Assets, LoadAssets);

        if (App.AssetModalsViewModel != null)
            App.AssetModalsViewModel.AssetsChanged += OnAssetsChanged;
    }

    /// <summary>
    /// Unsubscribes from the modals so this page stops reacting once another company is opened.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.AssetModalsViewModel != null)
            App.AssetModalsViewModel.AssetsChanged -= OnAssetsChanged;
    }

    private void OnAssetsChanged(object? sender, EventArgs e) => LoadAssets();

    #region Data Loading

    private void LoadAssets()
    {
        _allAssets.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            Assets.Clear();
            return;
        }

        var today = DateTime.Today;
        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = new DateTime(today.Year, 12, 31);

        foreach (var asset in companyData.Assets)
        {
            _allAssets.Add(new AssetDisplayItem
            {
                Id = asset.Id,
                Name = asset.Name,
                Type = asset.Type.ToDisplayName(),
                IsLand = asset.Type == AssetType.Land,
                PurchaseDate = asset.PurchaseDate,
                Currency = asset.Currency,
                Cost = asset.Cost,
                ThisYear = AssetDepreciation.Between(asset, yearStart, yearEnd),
                BookValue = AssetDepreciation.BookValueAsOf(asset, today),
                Status = asset.DisposalKind switch
                {
                    AssetDisposalKind.Sold => "Sold",
                    AssetDisposalKind.Scrapped => "Disposed",
                    _ => asset.IsDisposed ? "Disposed" : "In use"
                }
            });
        }

        UpdateStatistics(companyData.Assets, today, yearStart, yearEnd);
        FilterAssets();
    }

    private void UpdateStatistics(List<FixedAsset> assets, DateTime today, DateTime yearStart, DateTime yearEnd)
    {
        var currency = CurrencyService.CurrentCurrencyCode;

        // Assets bought in another currency are shown in the company's at their purchase date's rate; one without that rate yet is left out.
        decimal InCompanyCurrency(FixedAsset asset, decimal amount) =>
            DisplayCurrency.FromNative(amount, asset.Currency, currency, asset.PurchaseDate.Date) ?? 0m;

        var held = assets.Where(a => AssetDepreciation.IsHeldOn(a, today)).ToList();
        AssetsInUse = held.Count;
        TotalCost = CurrencyInfo.FormatAmount(held.Sum(a => InCompanyCurrency(a, a.Cost)), currency);
        TotalBookValue = CurrencyInfo.FormatAmount(held.Sum(a => InCompanyCurrency(a, AssetDepreciation.BookValueAsOf(a, today))), currency);
        DepreciationThisYear = CurrencyInfo.FormatAmount(assets.Sum(a => InCompanyCurrency(a, AssetDepreciation.Between(a, yearStart, yearEnd))), currency);
    }

    private void FilterAssets()
    {
        var filtered = _allAssets.ToList();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, a => [a.Name, a.Type, a.Id])
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            filtered = filtered.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<AssetDisplayItem, object?>>
                {
                    ["Asset"] = a => a.Name,
                    ["Type"] = a => a.Type,
                    ["Purchased"] = a => a.PurchaseDate,
                    ["Cost"] = a => a.Cost,
                    ["ThisYear"] = a => a.ThisYear,
                    ["BookValue"] = a => a.IsDisposed ? -1m : a.BookValue,
                    ["Status"] = a => a.Status
                },
                a => a.Name);
        }

        Assets.ReplaceAll(Paginate(filtered, "asset"));
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal()
    {
        App.AssetModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void OpenEditModal(AssetDisplayItem? item)
    {
        if (item == null) return;
        App.AssetModalsViewModel?.OpenEditModal(item);
    }

    [RelayCommand]
    private void OpenDisposeModal(AssetDisplayItem? item)
    {
        if (item == null) return;
        App.AssetModalsViewModel?.OpenDisposeModal(item);
    }

    [RelayCommand]
    private void ReverseDisposal(AssetDisplayItem? item)
    {
        if (item == null) return;
        App.AssetModalsViewModel?.ReverseDisposal(item);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(AssetDisplayItem? item)
    {
        if (item == null) return;
        App.AssetModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion
}

/// <summary>
/// Display model for assets in the UI.
/// </summary>
public class AssetDisplayItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public bool IsLand { get; init; }

    public DateTime PurchaseDate { get; init; }

    public string Currency { get; init; } = "USD";

    public decimal Cost { get; init; }

    public decimal ThisYear { get; init; }

    public decimal BookValue { get; init; }

    public string Status { get; init; } = "In use";

    public bool IsDisposed => Status != "In use";

    public string PurchaseDateFormatted => DateFormatService.Format(PurchaseDate);

    public string CostFormatted => CurrencyInfo.FormatAmount(Cost, Currency);

    public string ThisYearFormatted => IsLand ? "-" : CurrencyInfo.FormatAmount(ThisYear, Currency);

    public string BookValueFormatted => IsDisposed ? "-" : CurrencyInfo.FormatAmount(BookValue, Currency);
}
