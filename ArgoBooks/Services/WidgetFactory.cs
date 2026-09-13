using ArgoBooks.Controls.Dashboard.Widgets;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.ViewModels.Dashboard;
using Avalonia.Controls;

namespace ArgoBooks.Services;

public record WidgetDefinition(
    WidgetType Type,
    string Name,
    string Description,
    string Category,
    string Icon,
    WidgetSize DefaultSize,
    WidgetSize[] AvailableSizes,
    ChartDataType? ChartDataType = null
);

public static class WidgetFactory
{
    private static readonly Dictionary<WidgetType, WidgetDefinition> Definitions = new()
    {
        [WidgetType.StatCardRevenue] = new(WidgetType.StatCardRevenue, "Revenue", "Total revenue for the period", "Statistics", "💰", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardExpenses] = new(WidgetType.StatCardExpenses, "Expenses", "Total expenses for the period", "Statistics", "📉", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardOutstandingInvoices] = new(WidgetType.StatCardOutstandingInvoices, "Outstanding Invoices", "Unpaid invoice total", "Statistics", "📋", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardActiveRentals] = new(WidgetType.StatCardActiveRentals, "Active Rentals", "Currently active rental count", "Statistics", "🔑", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardNetProfit] = new(WidgetType.StatCardNetProfit, "Net Profit", "Revenue minus expenses for the period", "Statistics", "📈", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardTotalCustomers] = new(WidgetType.StatCardTotalCustomers, "Total Customers", "Number of customers", "Statistics", "👥", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardInventoryValue] = new(WidgetType.StatCardInventoryValue, "Inventory Value", "Total value of inventory on hand", "Statistics", "📦", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardOverdueInvoices] = new(WidgetType.StatCardOverdueInvoices, "Overdue Invoices", "Invoices past their due date", "Statistics", "🚨", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.StatCardPayrollRemittance] = new(WidgetType.StatCardPayrollRemittance, "Next Remittance Due", "What you owe CRA next, and when", "Statistics", "🏦", WidgetSize.Tiny, [WidgetSize.Tiny, WidgetSize.Small, WidgetSize.Medium]),
        [WidgetType.QuickActions] = new(WidgetType.QuickActions, "Quick Actions", "Shortcut buttons for common tasks", "Actions", "⚡", WidgetSize.Large, [WidgetSize.Medium, WidgetSize.MedLarge, WidgetSize.Large]),
        [WidgetType.RecentTransactions] = new(WidgetType.RecentTransactions, "Recent Transactions", "Latest revenue and expense entries", "Tables", "📝", WidgetSize.Medium, [WidgetSize.Medium, WidgetSize.MedLarge, WidgetSize.Large]),
        [WidgetType.ActiveRentalsTable] = new(WidgetType.ActiveRentalsTable, "Active Rentals Table", "Currently active and overdue rentals", "Tables", "📅", WidgetSize.Medium, [WidgetSize.Medium, WidgetSize.MedLarge, WidgetSize.Large]),
        [WidgetType.SetupChecklist] = new(WidgetType.SetupChecklist, "Setup Checklist", "Getting started guide for new users", "Onboarding", "✅", WidgetSize.Large, [WidgetSize.Medium, WidgetSize.MedLarge, WidgetSize.Large]),
        [WidgetType.TopCustomers] = new(WidgetType.TopCustomers, "Top Customers", "Highest revenue customers", "Tables", "👥", WidgetSize.Small, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.MedLarge]),
        [WidgetType.LowStockAlerts] = new(WidgetType.LowStockAlerts, "Low Stock Alerts", "Inventory items below threshold", "Inventory", "⚠️", WidgetSize.Small, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.MedLarge]),
        [WidgetType.UpcomingInvoiceDueDates] = new(WidgetType.UpcomingInvoiceDueDates, "Upcoming Due Dates", "Invoices due soon", "Invoices", "📆", WidgetSize.Small, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.MedLarge]),
        [WidgetType.OverdueRentals] = new(WidgetType.OverdueRentals, "Overdue Rentals", "Rentals past their due date", "Rentals", "🚨", WidgetSize.Small, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.MedLarge]),
    };

    private static readonly Dictionary<ChartDataType, WidgetDefinition> ChartDefinitions = BuildChartDefinitions();

    private static Dictionary<ChartDataType, WidgetDefinition> BuildChartDefinitions()
    {
        var defs = new Dictionary<ChartDataType, WidgetDefinition>();
        foreach (var type in Enum.GetValues<ChartDataType>())
        {
            if (type == ChartDataType.WorldMap) continue;
            defs[type] = new WidgetDefinition(
                WidgetType.Chart,
                type.GetDisplayName(),
                $"{type.GetChartCategory()} chart",
                "Charts",
                type.GetChartIcon(),
                WidgetSize.Medium,
                [WidgetSize.Small, WidgetSize.Medium, WidgetSize.MedLarge, WidgetSize.Large],
                type);
        }
        return defs;
    }

    private static ChartDataType? MapLegacyChartType(WidgetType type) => type switch
    {
        WidgetType.ProfitsChart => ChartDataType.TotalProfits,
        WidgetType.RevenueVsExpensesChart => ChartDataType.RevenueVsExpenses,
        WidgetType.ExpenseByCategory => ChartDataType.ExpensesDistribution,
        _ => null
    };

    public static bool IsKnownType(WidgetType type)
        => Definitions.ContainsKey(type) || type == WidgetType.Chart || MapLegacyChartType(type).HasValue;

    public static IReadOnlyList<WidgetDefinition> GetAllDefinitions()
        => Definitions.Values.Concat(ChartDefinitions.Values).ToList();

    public static WidgetHostViewModel CreateWidgetHost(DashboardWidgetEntry entry)
    {
        var legacyChart = MapLegacyChartType(entry.WidgetType);
        if (legacyChart.HasValue)
            entry.Config.TryAdd("ChartDataType", legacyChart.Value.ToString());

        if (entry.WidgetType == WidgetType.Chart || legacyChart.HasValue)
        {
            var chartDataType = ChartDataType.TotalProfits;
            if (entry.Config.TryGetValue("ChartDataType", out var typeStr)
                && Enum.TryParse<ChartDataType>(typeStr, out var parsed))
                chartDataType = parsed;

            var def = ChartDefinitions.TryGetValue(chartDataType, out var chartDef)
                ? chartDef
                : ChartDefinitions.Values.First();

            var viewModel = new UnifiedChartWidgetViewModel(chartDataType);
            return new WidgetHostViewModel(entry, viewModel, def.AvailableSizes);
        }

        var definition = Definitions.TryGetValue(entry.WidgetType, out var d) ? d : Definitions.Values.First();
        var vm = CreateViewModel(entry.WidgetType);
        return new WidgetHostViewModel(entry, vm, definition.AvailableSizes);
    }

    public static WidgetViewModelBase CreateViewModel(WidgetType type) => type switch
    {
        WidgetType.StatCardRevenue => new StatCardWidgetViewModel(StatCardKind.Revenue),
        WidgetType.StatCardExpenses => new StatCardWidgetViewModel(StatCardKind.Expenses),
        WidgetType.StatCardOutstandingInvoices => new StatCardWidgetViewModel(StatCardKind.OutstandingInvoices),
        WidgetType.StatCardActiveRentals => new StatCardWidgetViewModel(StatCardKind.ActiveRentals),
        WidgetType.StatCardNetProfit => new StatCardWidgetViewModel(StatCardKind.NetProfit),
        WidgetType.StatCardTotalCustomers => new StatCardWidgetViewModel(StatCardKind.TotalCustomers),
        WidgetType.StatCardInventoryValue => new StatCardWidgetViewModel(StatCardKind.InventoryValue),
        WidgetType.StatCardOverdueInvoices => new StatCardWidgetViewModel(StatCardKind.OverdueInvoices),
        WidgetType.StatCardPayrollRemittance => new StatCardWidgetViewModel(StatCardKind.PayrollRemittance),
        WidgetType.QuickActions => new QuickActionsWidgetViewModel(),
        WidgetType.Chart => new UnifiedChartWidgetViewModel(ChartDataType.TotalProfits),
        WidgetType.ProfitsChart => new UnifiedChartWidgetViewModel(ChartDataType.TotalProfits),
        WidgetType.RevenueVsExpensesChart => new UnifiedChartWidgetViewModel(ChartDataType.RevenueVsExpenses),
        WidgetType.RecentTransactions => new RecentTransactionsWidgetViewModel(),
        WidgetType.ActiveRentalsTable => new ActiveRentalsWidgetViewModel(),
        WidgetType.SetupChecklist => new SetupChecklistWidgetViewModel(),
        WidgetType.TopCustomers => new TopCustomersWidgetViewModel(),
        WidgetType.LowStockAlerts => new LowStockAlertsWidgetViewModel(),
        WidgetType.UpcomingInvoiceDueDates => new UpcomingInvoicesWidgetViewModel(),
        WidgetType.ExpenseByCategory => new UnifiedChartWidgetViewModel(ChartDataType.ExpensesDistribution),
        WidgetType.OverdueRentals => new OverdueRentalsWidgetViewModel(),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static Control CreateView(WidgetType type) => type switch
    {
        WidgetType.StatCardRevenue or
        WidgetType.StatCardExpenses or
        WidgetType.StatCardOutstandingInvoices or
        WidgetType.StatCardActiveRentals or
        WidgetType.StatCardNetProfit or
        WidgetType.StatCardTotalCustomers or
        WidgetType.StatCardInventoryValue or
        WidgetType.StatCardOverdueInvoices or
        WidgetType.StatCardPayrollRemittance => new StatCardWidget(),
        WidgetType.QuickActions => new QuickActionsWidget(),
        WidgetType.ProfitsChart or
        WidgetType.RevenueVsExpensesChart or
        WidgetType.ExpenseByCategory or
        WidgetType.Chart => new ChartWidget(),
        WidgetType.RecentTransactions => new RecentTransactionsWidget(),
        WidgetType.ActiveRentalsTable => new ActiveRentalsWidget(),
        WidgetType.SetupChecklist => new SetupChecklistWidget(),
        WidgetType.TopCustomers => new TopCustomersWidget(),
        WidgetType.LowStockAlerts => new LowStockAlertsWidget(),
        WidgetType.UpcomingInvoiceDueDates => new UpcomingInvoicesWidget(),
        WidgetType.OverdueRentals => new OverdueRentalsWidget(),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
