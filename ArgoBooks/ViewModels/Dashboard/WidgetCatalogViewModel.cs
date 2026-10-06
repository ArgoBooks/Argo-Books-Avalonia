using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels.Dashboard;

public partial class CatalogItem(WidgetDefinition definition) : ObservableObject
{
    public WidgetDefinition Definition { get; } = definition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWontFit))]
    private bool _isAlreadyAdded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWontFit))]
    private bool _cannotFitInRow;
    public bool ShowWontFit => CannotFitInRow && !IsAlreadyAdded;
}

public partial class WidgetCatalogViewModel : ObservableObject
{
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isRowFull;
    public ObservableCollection<CatalogItem> StatCards { get; } = [];
    public ObservableCollection<CatalogItem> Charts { get; } = [];
    public ObservableCollection<CatalogItem> Tables { get; } = [];
    public ObservableCollection<CatalogItem> Other { get; } = [];

    /// <summary>Every chart there is, which <see cref="Charts"/> is the searched view of.</summary>
    private readonly List<CatalogItem> _allCharts = [];

    [ObservableProperty]
    private string _chartSearch = string.Empty;

    /// <summary>Whether a search has been typed that nothing answers.</summary>
    public bool NoChartMatches => Charts.Count == 0 && !string.IsNullOrWhiteSpace(ChartSearch);

    partial void OnChartSearchChanged(string value) => ApplyChartSearch();

    private void ApplyChartSearch()
    {
        Charts.Clear();
        var matches = _allCharts.RankBySearch(ChartSearch, c => [c.Definition.Name, c.Definition.Description]);
        foreach (var item in matches)
            Charts.Add(item);

        OnPropertyChanged(nameof(NoChartMatches));
    }

    [RelayCommand]
    private void ClearChartSearch() => ChartSearch = string.Empty;

    public event EventHandler<WidgetDefinition>? WidgetAddRequested;

    private static readonly HashSet<string> StatCardCategories = ["Statistics"];
    private static readonly HashSet<string> ChartCategories = ["Charts", "Insights"];
    private static readonly HashSet<string> OtherCategories = ["Actions"];

    public void Refresh(IEnumerable<WidgetHostViewModel> currentWidgets, double remainingFraction = 1.0)
    {
        // Cleared first, so the list the search runs over is the one this refresh builds.
        ChartSearch = string.Empty;
        StatCards.Clear();
        _allCharts.Clear();
        Charts.Clear();
        Tables.Clear();
        Other.Clear();

        var placedTypes = currentWidgets.Select(w => w.WidgetType).ToHashSet();
        var placedChartTypes = currentWidgets
            .Where(w => w.WidgetViewModel is UnifiedChartWidgetViewModel)
            .Select(w => ((UnifiedChartWidgetViewModel)w.WidgetViewModel).ChartDataType)
            .ToHashSet();

        foreach (var d in WidgetFactory.GetAllDefinitions())
        {
            var item = new CatalogItem(d)
            {
                IsAlreadyAdded = d.ChartDataType.HasValue
                    ? placedChartTypes.Contains(d.ChartDataType.Value)
                    : placedTypes.Contains(d.Type),
                CannotFitInRow = d.AvailableSizes.Min(s => s.ToFraction()) > remainingFraction + 0.001
            };

            if (StatCardCategories.Contains(d.Category))
                StatCards.Add(item);
            else if (ChartCategories.Contains(d.Category))
                _allCharts.Add(item);
            else if (OtherCategories.Contains(d.Category))
                Other.Add(item);
            else
                Tables.Add(item);
        }

        ApplyChartSearch();

        // Show banner when every available (not-already-added) widget is too large
        var allItems = StatCards.Concat(_allCharts).Concat(Tables).Concat(Other);
        IsRowFull = allItems.Where(i => !i.IsAlreadyAdded).All(i => i.CannotFitInRow);
    }

    [RelayCommand]
    private void AddWidget(CatalogItem item)
    {
        if (item.IsAlreadyAdded || item.CannotFitInRow) return;
        WidgetAddRequested?.Invoke(this, item.Definition);
        IsOpen = false;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;
}
