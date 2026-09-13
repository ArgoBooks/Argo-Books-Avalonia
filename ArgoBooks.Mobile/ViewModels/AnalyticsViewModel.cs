using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Analytics tab: the same 9 report tabs as the desktop (Dashboard, Products, Geographic,
/// Performance, Customers, Taxes, Returns, Losses, Refunds). See <see cref="AnalyticsTabViewModel"/>
/// for which tabs the current snapshot shape can actually render vs. show empty.
/// </summary>
public partial class AnalyticsViewModel : ViewModelBase
{
    public static readonly string[] TabNames =
    {
        "Dashboard", "Products", "Geographic", "Performance", "Customers",
        "Taxes", "Returns", "Losses", "Refunds",
    };

    private readonly Action<RowDto> _onOpenRow;
    private MobileSnapshot? _snapshot;

    public ObservableCollection<AnalyticsTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private AnalyticsTabViewModel _selectedTab = null!;

    public AnalyticsViewModel(Action<RowDto> onOpenRow)
    {
        _onOpenRow = onOpenRow;

        foreach (var name in TabNames)
        {
            var tab = new AnalyticsTabViewModel(name, _onOpenRow);
            tab.SelectRequested += SelectTab;
            Tabs.Add(tab);
        }

        SelectedTab = Tabs[0];
        SelectedTab.IsSelected = true;
    }

    public void UpdateSnapshot(MobileSnapshot? snapshot)
    {
        _snapshot = snapshot;
        foreach (var tab in Tabs)
        {
            tab.Update(_snapshot);
        }
    }

    private void SelectTab(AnalyticsTabViewModel tab)
    {
        if (ReferenceEquals(SelectedTab, tab))
        {
            return;
        }

        SelectedTab.IsSelected = false;
        SelectedTab = tab;
        SelectedTab.IsSelected = true;
    }
}
