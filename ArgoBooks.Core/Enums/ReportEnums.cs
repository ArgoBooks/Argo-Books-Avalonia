namespace ArgoBooks.Core.Enums;

/// <summary>
/// Types of elements that can be included in a report.
/// </summary>
public enum ReportElementType
{
    Chart,
    Table,
    Label,
    Image,
    DateRange,
    Summary,
    AccountingTable
}

/// <summary>
/// Supported page sizes for reports.
/// </summary>
public enum PageSize
{
    A4,
    Letter,
    Legal,
    Tabloid,
    A3,
    Custom
}

/// <summary>
/// Page orientation options.
/// </summary>
public enum PageOrientation
{
    Portrait,
    Landscape
}

/// <summary>
/// Element alignment options for the report designer.
/// </summary>
public enum ElementAlignment
{
    Left,
    Right,
    Top,
    Bottom,
    CenterH,
    CenterV
}

/// <summary>
/// Distribution direction for spacing elements evenly.
/// </summary>
public enum DistributeDirection
{
    Horizontal,
    Vertical
}

/// <summary>
/// Size matching mode for making elements the same size.
/// </summary>
public enum MatchSizeMode
{
    Width,
    Height,
    Both
}

/// <summary>
/// Transaction type filter options.
/// </summary>
public enum TransactionType
{
    Revenue,
    Expenses,
    Invoices,
    Payments,
    RentalRecords,
    RentalItems,
    Inventory,
    PurchaseOrders,
    StockAdjustments,
    StockTransfers,
    Returns,
    LostDamaged,
    Receipts,
    Customers,
    Suppliers,
    Products,
    Categories,
    Locations,
    Accountants
}

/// <summary>
/// Supported export formats.
/// </summary>
public enum ExportFormat
{
    PDF,
    PNG,
    JPEG
}

/// <summary>
/// Image scaling modes for display.
/// </summary>
public enum ImageScaleMode
{
    Stretch,
    Fit,
    Fill,
    Center
}

/// <summary>
/// Table data selection modes.
/// </summary>
public enum TableDataSelection
{
    All,
    TopByAmount,
    BottomByAmount,
    ReturnsOnly,
    LossesOnly
}

/// <summary>
/// Table sorting options.
/// </summary>
public enum TableSortOrder
{
    DateDescending,
    DateAscending,
    AmountDescending,
    AmountAscending
}

/// <summary>
/// Chart data types available in reports.
/// </summary>
public enum ChartDataType
{
    // Revenue charts
    TotalRevenue,
    RevenueDistribution,

    // Expense charts
    TotalExpenses,
    ExpensesDistribution,

    // Financial charts
    TotalProfits,
    RevenueVsExpenses,

    // Transaction charts
    AverageTransactionValue,
    TotalTransactions,
    AverageShippingCosts,

    // Geographic charts
    WorldMap,
    CountriesOfOrigin,
    CountriesOfDestination,
    CompaniesOfOrigin,
    CompaniesOfDestination,

    // Accountant charts
    AccountantsTransactions,

    // Customer charts
    TopCustomersByRevenue,
    CustomerPaymentStatus,
    CustomerGrowth,
    CustomerLifetimeValue,
    ActiveVsInactiveCustomers,
    RentalsPerCustomer,

    // Returns charts
    ReturnsOverTime,
    ReturnReasons,
    ReturnFinancialImpact,
    ReturnsByCategory,
    ReturnsByProduct,
    ExpenseVsRevenueReturns,

    // Losses charts
    LossesOverTime,
    LossReasons,
    LossFinancialImpact,
    LossesByCategory,
    LossesByProduct,
    ExpenseVsRevenueLosses,

    // Taxes charts
    TaxCollectedVsPaid,
    TaxLiabilityTrend,
    TaxByCategory,
    TaxRateDistribution,
    TaxByProduct,
    ExpenseVsRevenueTax,

    // Products charts
    ProductRevenueTrend
}

/// <summary>
/// Visual chart style for rendering series in reports.
/// </summary>
public enum ReportChartStyle
{
    Bar,
    Line,
    StepLine,
    Area,
    Scatter
}

/// <summary>
/// Text horizontal alignment options.
/// </summary>
public enum HorizontalTextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Text vertical alignment options.
/// </summary>
public enum VerticalTextAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Types of accounting reports available.
/// </summary>
public enum AccountingReportType
{
    IncomeStatement,
    BalanceSheet,
    CashFlowStatement,
    GeneralLedger,
    AccountsReceivableAging,
    TaxSummary,
    ProductSales,
    PayrollRemittance
}

/// <summary>
/// Row types for structured accounting tables.
/// </summary>
public enum AccountingRowType
{
    SectionHeader,
    DataRow,
    SubtotalRow,
    TotalRow,
    GrandTotalRow,
    SeparatorLine,
    BlankRow
}

/// <summary>
/// Extension methods for report enums.
/// </summary>
public static class ReportEnumExtensions
{
    /// <summary>
    /// Gets a user-friendly display name for a transaction type.
    /// </summary>
    public static string GetDisplayName(this TransactionType transactionType)
    {
        return transactionType switch
        {
            TransactionType.Revenue => "Revenue",
            TransactionType.Expenses => "Expenses",
            TransactionType.Invoices => "Invoices",
            TransactionType.Payments => "Payments",
            TransactionType.RentalRecords => "Rental Records",
            TransactionType.RentalItems => "Rental Items",
            TransactionType.Inventory => "Inventory",
            TransactionType.PurchaseOrders => "Purchase Orders",
            TransactionType.StockAdjustments => "Stock Adjustments",
            TransactionType.StockTransfers => "Stock Transfers",
            TransactionType.Returns => "Returns",
            TransactionType.LostDamaged => "Lost / Damaged",
            TransactionType.Receipts => "Receipts",
            TransactionType.Customers => "Customers",
            TransactionType.Suppliers => "Suppliers",
            TransactionType.Products => "Products",
            TransactionType.Categories => "Categories",
            TransactionType.Locations => "Locations",
            TransactionType.Accountants => "Accountants",
            _ => transactionType.ToString()
        };
    }

    /// <summary>
    /// Returns true if the transaction type supports Include Returns/Losses options.
    /// </summary>
    public static bool SupportsReturnsAndLosses(this TransactionType transactionType)
    {
        return transactionType is TransactionType.Revenue or TransactionType.Expenses;
    }

    /// <summary>
    /// Gets a user-friendly display name for a table sort order.
    /// </summary>
    public static string GetDisplayName(this TableSortOrder sortOrder)
    {
        return sortOrder switch
        {
            TableSortOrder.DateDescending => "Date descending",
            TableSortOrder.DateAscending => "Date ascending",
            TableSortOrder.AmountDescending => "Amount descending",
            TableSortOrder.AmountAscending => "Amount ascending",
            _ => sortOrder.ToString()
        };
    }

    /// <summary>
    /// Gets a user-friendly display name for a chart data type.
    /// </summary>
    public static string GetDisplayName(this ChartDataType chartType)
    {
        return chartType switch
        {
            // Revenue charts
            ChartDataType.TotalRevenue => "Total Revenue",
            ChartDataType.RevenueDistribution => "Revenue Distribution",

            // Expense charts
            ChartDataType.TotalExpenses => "Total Expenses",
            ChartDataType.ExpensesDistribution => "Expense Distribution",

            // Financial charts
            ChartDataType.TotalProfits => "Total Profits",
            ChartDataType.RevenueVsExpenses => "Expenses vs Revenue",

            // Transaction charts
            ChartDataType.AverageTransactionValue => "Average Transaction Value",
            ChartDataType.TotalTransactions => "Total Transactions",
            ChartDataType.AverageShippingCosts => "Average Shipping Costs",

            // Geographic charts
            ChartDataType.WorldMap => "Geographic Distribution",
            ChartDataType.CountriesOfOrigin => "Countries of Origin",
            ChartDataType.CountriesOfDestination => "Countries of Destination",
            ChartDataType.CompaniesOfOrigin => "Companies of Origin",
            ChartDataType.CompaniesOfDestination => "Companies of Destination",

            // Accountant charts
            ChartDataType.AccountantsTransactions => "Transactions by Accountant",

            // Customer charts
            ChartDataType.TopCustomersByRevenue => "Top Customers by Revenue",
            ChartDataType.CustomerPaymentStatus => "Customer Payment Status",
            ChartDataType.CustomerGrowth => "Customer Growth",
            ChartDataType.CustomerLifetimeValue => "Customer Lifetime Value",
            ChartDataType.ActiveVsInactiveCustomers => "Active vs Inactive Customers",
            ChartDataType.RentalsPerCustomer => "Rentals per Customer",

            // Returns charts
            ChartDataType.ReturnsOverTime => "Returns Over Time",
            ChartDataType.ReturnReasons => "Return Reasons",
            ChartDataType.ReturnFinancialImpact => "Financial Impact of Returns",
            ChartDataType.ReturnsByCategory => "Returns by Category",
            ChartDataType.ReturnsByProduct => "Returns by Product",
            ChartDataType.ExpenseVsRevenueReturns => "Expense vs Revenue Returns",

            // Losses charts
            ChartDataType.LossesOverTime => "Losses Over Time",
            ChartDataType.LossReasons => "Loss Reasons",
            ChartDataType.LossFinancialImpact => "Financial Impact of Losses",
            ChartDataType.LossesByCategory => "Losses by Category",
            ChartDataType.LossesByProduct => "Losses by Product",
            ChartDataType.ExpenseVsRevenueLosses => "Expense vs Revenue Losses",

            // Taxes charts
            ChartDataType.TaxCollectedVsPaid => "Tax Collected vs Paid",
            ChartDataType.TaxLiabilityTrend => "Net Tax Liability",
            ChartDataType.TaxByCategory => "Tax by Category",
            ChartDataType.TaxRateDistribution => "Tax Rate Distribution",
            ChartDataType.TaxByProduct => "Tax by Product",
            ChartDataType.ExpenseVsRevenueTax => "Expense vs Revenue Tax",

            // Products charts
            ChartDataType.ProductRevenueTrend => "Revenue Over Time",

            _ => chartType.ToString()
        };
    }

    /// <summary>
    /// Gets a user-friendly display name for an accounting report type.
    /// </summary>
    public static string GetDisplayName(this AccountingReportType reportType)
    {
        return reportType switch
        {
            AccountingReportType.IncomeStatement => "Income Statement",
            AccountingReportType.BalanceSheet => "Balance Sheet",
            AccountingReportType.CashFlowStatement => "Cash Flow Statement",
            AccountingReportType.GeneralLedger => "General Ledger",
            AccountingReportType.AccountsReceivableAging => "Accounts Receivable Aging",
            AccountingReportType.TaxSummary => "Tax Summary",
            AccountingReportType.ProductSales => "Sales by Product",
            AccountingReportType.PayrollRemittance => "Payroll Remittance",
            _ => reportType.ToString()
        };
    }

    /// <summary>
    /// Returns true if the chart type produces a distribution (pie/donut) style dataset.
    /// </summary>
    public static bool IsDistribution(this ChartDataType chartType) => chartType switch
    {
        ChartDataType.RevenueDistribution => true,
        ChartDataType.ExpensesDistribution => true,
        ChartDataType.CountriesOfOrigin => true,
        ChartDataType.CountriesOfDestination => true,
        ChartDataType.CompaniesOfOrigin => true,
        ChartDataType.CompaniesOfDestination => true,
        ChartDataType.AccountantsTransactions => true,
        ChartDataType.TopCustomersByRevenue => true,
        ChartDataType.CustomerPaymentStatus => true,
        ChartDataType.ActiveVsInactiveCustomers => true,
        ChartDataType.RentalsPerCustomer => true,
        ChartDataType.ReturnReasons => true,
        ChartDataType.ReturnsByCategory => true,
        ChartDataType.ReturnsByProduct => true,
        ChartDataType.LossReasons => true,
        ChartDataType.LossesByCategory => true,
        ChartDataType.LossesByProduct => true,
        ChartDataType.TaxByCategory => true,
        ChartDataType.TaxRateDistribution => true,
        ChartDataType.TaxByProduct => true,
        _ => false
    };

    /// <summary>
    /// Returns true if the chart type produces multiple series (e.g. grouped bar or multi-line).
    /// </summary>
    public static bool IsMultiSeries(this ChartDataType chartType) => chartType switch
    {
        ChartDataType.RevenueVsExpenses => true,
        ChartDataType.AverageTransactionValue => true,
        ChartDataType.TotalTransactions => true,
        ChartDataType.ExpenseVsRevenueReturns => true,
        ChartDataType.ExpenseVsRevenueLosses => true,
        ChartDataType.TaxCollectedVsPaid => true,
        ChartDataType.ExpenseVsRevenueTax => true,
        _ => false
    };

    /// <summary>
    /// Returns true if the chart type's values are counts, not money, so they are never converted
    /// to the display currency or shown with a currency symbol.
    /// </summary>
    public static bool IsCount(this ChartDataType chartType) => chartType switch
    {
        ChartDataType.TotalTransactions => true,
        ChartDataType.AccountantsTransactions => true,
        ChartDataType.CustomerPaymentStatus => true,
        ChartDataType.CustomerGrowth => true,
        ChartDataType.CustomerLifetimeValue => true,
        ChartDataType.ActiveVsInactiveCustomers => true,
        ChartDataType.RentalsPerCustomer => true,
        ChartDataType.ReturnsOverTime => true,
        ChartDataType.ReturnReasons => true,
        ChartDataType.ReturnsByCategory => true,
        ChartDataType.ReturnsByProduct => true,
        ChartDataType.ExpenseVsRevenueReturns => true,
        ChartDataType.LossesOverTime => true,
        ChartDataType.LossReasons => true,
        ChartDataType.LossesByCategory => true,
        ChartDataType.LossesByProduct => true,
        ChartDataType.ExpenseVsRevenueLosses => true,
        ChartDataType.TaxRateDistribution => true,
        _ => false
    };

    /// <summary>
    /// Returns the high-level category name for a chart data type.
    /// </summary>
    public static string GetChartCategory(this ChartDataType chartType) => chartType switch
    {
        ChartDataType.TotalRevenue or ChartDataType.RevenueDistribution => "Revenue",
        ChartDataType.TotalExpenses or ChartDataType.ExpensesDistribution => "Expenses",
        ChartDataType.TotalProfits or ChartDataType.RevenueVsExpenses => "Financial",
        ChartDataType.AverageTransactionValue or ChartDataType.TotalTransactions
            or ChartDataType.AverageShippingCosts => "Transactions",
        ChartDataType.CountriesOfOrigin or ChartDataType.CountriesOfDestination
            or ChartDataType.CompaniesOfOrigin or ChartDataType.CompaniesOfDestination => "Geographic",
        ChartDataType.AccountantsTransactions => "Accountant",
        ChartDataType.TopCustomersByRevenue or ChartDataType.CustomerPaymentStatus
            or ChartDataType.CustomerGrowth or ChartDataType.CustomerLifetimeValue
            or ChartDataType.ActiveVsInactiveCustomers or ChartDataType.RentalsPerCustomer => "Customer",
        ChartDataType.ReturnsOverTime or ChartDataType.ReturnReasons
            or ChartDataType.ReturnFinancialImpact or ChartDataType.ReturnsByCategory
            or ChartDataType.ReturnsByProduct or ChartDataType.ExpenseVsRevenueReturns => "Returns",
        ChartDataType.LossesOverTime or ChartDataType.LossReasons
            or ChartDataType.LossFinancialImpact or ChartDataType.LossesByCategory
            or ChartDataType.LossesByProduct or ChartDataType.ExpenseVsRevenueLosses => "Losses",
        ChartDataType.TaxCollectedVsPaid or ChartDataType.TaxLiabilityTrend
            or ChartDataType.TaxByCategory or ChartDataType.TaxRateDistribution
            or ChartDataType.TaxByProduct or ChartDataType.ExpenseVsRevenueTax => "Taxes",
        ChartDataType.ProductRevenueTrend => "Products",
        _ => "Charts"
    };

    /// <summary>
    /// Returns an emoji icon representing the category of the chart data type.
    /// </summary>
    public static string GetChartIcon(this ChartDataType chartType)
    {
        var category = chartType.GetChartCategory();
        return category switch
        {
            "Revenue" => "💰",
            "Expenses" => "📉",
            "Financial" => "📊",
            "Transactions" => "📝",
            "Geographic" => "🌍",
            "Accountant" => "🧾",
            "Customer" => "👥",
            "Returns" => "↩️",
            "Losses" => "⚠️",
            "Taxes" => "🏛️",
            _ => "📈"
        };
    }
}
