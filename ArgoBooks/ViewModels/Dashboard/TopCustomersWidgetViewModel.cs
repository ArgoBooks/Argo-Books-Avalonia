using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public record TopCustomerItem(int Rank, string Name, string TotalRevenue, int TransactionCount);

public partial class TopCustomersWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.TopCustomers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomers))]
    [NotifyPropertyChangedFor(nameof(HasNoCustomers))]
    private ObservableCollection<TopCustomerItem> _customers = [];

    public bool HasCustomers => Customers.Count > 0;
    public bool HasNoCustomers => Customers.Count == 0;

    public override bool HasConfig => true;

    [ObservableProperty]
    private int _count = 5;

    [ObservableProperty]
    private string _sortBy = "revenue";

    public int[] CountOptions { get; } = [5, 10];

    public string[] SortByOptions { get; } = ["revenue", "count"];

    partial void OnCountChanged(int value) => LoadData();
    partial void OnSortByChanged(string value) => LoadData();

    public override void Initialize(Dictionary<string, string> config)
    {
        ApplyConfig(config);
    }

    public override void ApplyConfig(Dictionary<string, string> config)
    {
        if (config.TryGetValue("Count", out var countStr) && int.TryParse(countStr, out var count))
            Count = count;
        if (config.TryGetValue("SortBy", out var sortBy))
            SortBy = sortBy;
    }

    public override Dictionary<string, string> GetConfig()
    {
        return new Dictionary<string, string>
        {
            ["Count"] = Count.ToString(),
            ["SortBy"] = SortBy
        };
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        LoadTopCustomers(data);
    }

    private void LoadTopCustomers(CompanyData data)
    {
        // The same ranking as the Top Customers chart, over the dashboard's date range.
        var chartSettings = ChartSettingsService.Instance;
        var ranked = TopCustomers.Rank(data.Revenues, data.Payments, chartSettings.StartDate, chartSettings.EndDate,
            CurrencyService.GetDisplayAmount);

        var sorted = SortBy == "count"
            ? ranked.OrderByDescending(c => c.Customer.Sales.Count).ThenByDescending(c => c.Amount)
            : (IEnumerable<(CustomerRevenue Customer, decimal Amount)>)ranked;

        var items = sorted
            .Take(Count)
            .Select((c, i) =>
            {
                var customer = data.GetCustomer(c.Customer.CustomerId);
                var name = customer?.Name ?? customer?.CompanyName ?? "Unknown";
                var formatted = CurrencyService.TryComputeDisplay(c.Customer.Total, out var amount)
                    ? CurrencyService.Format(amount)
                    : CurrencyService.PendingMarker;
                return new TopCustomerItem(i + 1, name, formatted, c.Customer.Sales.Count);
            })
            .ToList();

        Customers = new ObservableCollection<TopCustomerItem>(items);
    }
}
