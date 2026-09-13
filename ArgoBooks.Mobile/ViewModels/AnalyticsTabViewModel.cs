using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One tab of the 9-tab Analytics screen. The mobile snapshot only carries the dashboard totals
/// plus the Money/People/Inventory row lists (no per-tab analytics slices like the desktop's
/// Geographic/Performance/Taxes/Returns/Losses/Refunds reports), so only the tabs that map onto
/// data actually present in the snapshot (Dashboard, Customers, Products) render KPIs/rows; the
/// rest show an explicit empty state instead of crashing or faking numbers.
/// </summary>
public partial class AnalyticsTabViewModel : ObservableObject
{
    private readonly Action<RowDto> _onOpenRow;

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<AnalyticsKpiViewModel> Kpis { get; } = new();

    public ObservableCollection<RowItemViewModel> Rows { get; } = new();

    public string RowsTitle { get; private set; } = string.Empty;

    public AnalyticsTabViewModel(string name, Action<RowDto> onOpenRow)
    {
        Name = name;
        _onOpenRow = onOpenRow;
    }

    public void Update(MobileSnapshot? snapshot)
    {
        Kpis.Clear();
        Rows.Clear();
        HasData = false;
        RowsTitle = string.Empty;

        if (snapshot == null)
        {
            return;
        }

        switch (Name)
        {
            case "Dashboard":
                var d = snapshot.Dashboard;
                Kpis.Add(new AnalyticsKpiViewModel("Money in", d.MoneyIn.ToString("C2", CultureInfo.InvariantCulture)));
                Kpis.Add(new AnalyticsKpiViewModel("Money out", d.MoneyOut.ToString("C2", CultureInfo.InvariantCulture)));
                Kpis.Add(new AnalyticsKpiViewModel("Profit", d.Profit.ToString("C2", CultureInfo.InvariantCulture)));
                Kpis.Add(new AnalyticsKpiViewModel("Profit margin", d.ProfitMargin.ToString("P0", CultureInfo.InvariantCulture)));
                RowsTitle = "Recent revenue";
                foreach (var row in snapshot.Revenue.Take(5))
                {
                    Rows.Add(new RowItemViewModel(row, _onOpenRow));
                }
                HasData = true;
                break;

            case "Customers":
                Kpis.Add(new AnalyticsKpiViewModel("Total customers", snapshot.Customers.Count.ToString(CultureInfo.InvariantCulture)));
                RowsTitle = "Customers";
                foreach (var row in snapshot.Customers.Take(10))
                {
                    Rows.Add(new RowItemViewModel(row, _onOpenRow));
                }
                HasData = snapshot.Customers.Count > 0;
                break;

            case "Products":
                Kpis.Add(new AnalyticsKpiViewModel("Total products", snapshot.Products.Count.ToString(CultureInfo.InvariantCulture)));
                RowsTitle = "Products";
                foreach (var row in snapshot.Products.Take(10))
                {
                    Rows.Add(new RowItemViewModel(row, _onOpenRow));
                }
                HasData = snapshot.Products.Count > 0;
                break;

            default:
                // Geographic, Performance, Taxes, Returns, Losses, Refunds: the snapshot doesn't
                // carry these slices yet. Show the empty state rather than fabricate numbers.
                HasData = false;
                break;
        }
    }

    [RelayCommand]
    private void Select() => SelectRequested?.Invoke(this);

    /// <summary>Raised when this tab is tapped; the parent AnalyticsViewModel wires this to switch the selected tab.</summary>
    public event Action<AnalyticsTabViewModel>? SelectRequested;
}
