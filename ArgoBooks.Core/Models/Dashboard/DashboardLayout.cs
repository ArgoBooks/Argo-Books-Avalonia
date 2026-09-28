namespace ArgoBooks.Core.Models.Dashboard;

public class DashboardLayout
{
    public List<DashboardRow> Rows { get; set; } = [];

    public static DashboardLayout CreateDefault()
    {
        return new DashboardLayout
        {
            Rows =
            [
                new DashboardRow(
                    new DashboardWidgetEntry(WidgetType.StatCardRevenue, WidgetSize.Tiny),
                    new DashboardWidgetEntry(WidgetType.StatCardExpenses, WidgetSize.Tiny),
                    new DashboardWidgetEntry(WidgetType.StatCardOutstandingInvoices, WidgetSize.Tiny),
                    new DashboardWidgetEntry(WidgetType.StatCardActiveRentals, WidgetSize.Tiny)),
                new DashboardRow(
                    new DashboardWidgetEntry(WidgetType.SetupChecklist, WidgetSize.Large)),
                new DashboardRow(
                    new DashboardWidgetEntry(WidgetType.QuickActions, WidgetSize.Large)),
                new DashboardRow(
                    new DashboardWidgetEntry(WidgetType.Chart, WidgetSize.Medium)
                        { Config = new() { ["ChartDataType"] = "TotalProfits" } },
                    new DashboardWidgetEntry(WidgetType.Chart, WidgetSize.Medium)
                        { Config = new() { ["ChartDataType"] = "RevenueVsExpenses" } }),
                new DashboardRow(
                    new DashboardWidgetEntry(WidgetType.RecentTransactions, WidgetSize.Medium),
                    new DashboardWidgetEntry(WidgetType.ActiveRentalsTable, WidgetSize.Medium)),
            ]
        };
    }

    public DashboardLayout Clone() => new()
    {
        Rows = Rows.Select(r => r.Clone()).ToList()
    };
}
