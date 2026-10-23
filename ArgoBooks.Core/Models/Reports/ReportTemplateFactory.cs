using ArgoBooks.Core.Enums;
using static ArgoBooks.Core.Models.Reports.TemplateLayoutHelper;

namespace ArgoBooks.Core.Models.Reports;

/// <summary>
/// Provides predefined report templates for common business reporting scenarios.
/// </summary>
public static class ReportTemplateFactory
{
    /// <summary>
    /// Template names constants.
    /// </summary>
    public static class TemplateNames
    {
        public const string Custom = "Custom Report";
        public const string MonthlyRevenue = "Revenue Overview";
        public const string FinancialOverview = "Financial Overview";
        public const string PerformanceAnalysis = "Performance Analysis";
        public const string ReturnsAnalysis = "Returns Analysis";
        public const string LossesAnalysis = "Losses Analysis";
        public const string GeographicAnalysis = "Geographic Analysis";
        public const string CustomerAnalysis = "Customer Analysis";
        public const string ExpenseBreakdown = "Expense Breakdown";
        public const string IncomeStatement = "Income Statement";
        public const string BalanceSheet = "Balance Sheet";
        public const string CashFlowStatement = "Cash Flow Statement";
        public const string GeneralLedger = "General Ledger";
        public const string ARaging = "Accounts Receivable Aging";
        public const string TaxSummary = "Tax Summary";
        public const string ProductSales = "Sales by Product";
        public const string PayrollRemittance = "Payroll Remittance";
        public const string CustomerStatement = "Customer Statement";
        public const string SalesByCustomer = "Sales by Customer";
        public const string ExpensesBySupplier = "Expenses by Supplier";
        public const string PayrollSummary = "Payroll Summary";
        public const string Rentals = "Rentals";
        public const string AssetRegister = "Asset Register";
    }

    /// <summary>
    /// Checks if the given template name is a built-in template.
    /// </summary>
    public static bool IsBuiltInTemplate(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return false;

        return templateName == TemplateNames.Custom ||
               templateName == TemplateNames.MonthlyRevenue ||
               templateName == TemplateNames.FinancialOverview ||
               templateName == TemplateNames.PerformanceAnalysis ||
               templateName == TemplateNames.ReturnsAnalysis ||
               templateName == TemplateNames.LossesAnalysis ||
               templateName == TemplateNames.GeographicAnalysis ||
               templateName == TemplateNames.CustomerAnalysis ||
               templateName == TemplateNames.ExpenseBreakdown ||
               templateName == TemplateNames.IncomeStatement ||
               templateName == TemplateNames.BalanceSheet ||
               templateName == TemplateNames.CashFlowStatement ||
               templateName == TemplateNames.GeneralLedger ||
               templateName == TemplateNames.ARaging ||
               templateName == TemplateNames.TaxSummary ||
               templateName == TemplateNames.ProductSales ||
               templateName == TemplateNames.PayrollRemittance ||
               templateName == TemplateNames.CustomerStatement ||
               templateName == TemplateNames.SalesByCustomer ||
               templateName == TemplateNames.ExpensesBySupplier ||
               templateName == TemplateNames.PayrollSummary ||
               templateName == TemplateNames.Rentals ||
               templateName == TemplateNames.AssetRegister;
    }

    /// <summary>
    /// Creates a report configuration from a template name.
    /// </summary>
    public static ReportConfiguration CreateFromTemplate(string templateName)
    {
        return templateName switch
        {
            TemplateNames.MonthlyRevenue => CreateMonthlyRevenueTemplate(),
            TemplateNames.FinancialOverview => CreateFinancialOverviewTemplate(),
            TemplateNames.PerformanceAnalysis => CreatePerformanceAnalysisTemplate(),
            TemplateNames.ReturnsAnalysis => CreateReturnsAnalysisTemplate(),
            TemplateNames.LossesAnalysis => CreateLossesAnalysisTemplate(),
            TemplateNames.GeographicAnalysis => CreateGeographicAnalysisTemplate(),
            TemplateNames.CustomerAnalysis => CreateCustomerAnalysisTemplate(),
            TemplateNames.ExpenseBreakdown => CreateExpenseBreakdownTemplate(),
            TemplateNames.IncomeStatement => CreateAccountingTemplate(AccountingReportType.IncomeStatement, "Income Statement", DatePresetNames.YearToDate),
            TemplateNames.BalanceSheet => CreateAccountingTemplate(AccountingReportType.BalanceSheet, "Balance Sheet", DatePresetNames.YearToDate),
            TemplateNames.CashFlowStatement => CreateAccountingTemplate(AccountingReportType.CashFlowStatement, "Cash Flow Statement", DatePresetNames.YearToDate),
            TemplateNames.GeneralLedger => CreateAccountingTemplate(AccountingReportType.GeneralLedger, "General Ledger", DatePresetNames.ThisMonth),
            TemplateNames.ARaging => CreateAccountingTemplate(AccountingReportType.AccountsReceivableAging, "Accounts Receivable Aging", DatePresetNames.AllTime),
            TemplateNames.TaxSummary => CreateAccountingTemplate(AccountingReportType.TaxSummary, "Tax Summary", DatePresetNames.YearToDate),
            TemplateNames.ProductSales => CreateAccountingTemplate(AccountingReportType.ProductSales, "Sales by Product", DatePresetNames.YearToDate),
            TemplateNames.PayrollRemittance => CreateAccountingTemplate(AccountingReportType.PayrollRemittance, "Payroll Remittance", DatePresetNames.ThisMonth),
            TemplateNames.CustomerStatement => CreateAccountingTemplate(AccountingReportType.CustomerStatement, "Customer Statement", DatePresetNames.LastMonth),
            TemplateNames.SalesByCustomer => CreateAccountingTemplate(AccountingReportType.SalesByCustomer, "Sales by Customer", DatePresetNames.YearToDate),
            TemplateNames.ExpensesBySupplier => CreateAccountingTemplate(AccountingReportType.ExpensesBySupplier, "Expenses by Supplier", DatePresetNames.YearToDate),
            TemplateNames.PayrollSummary => CreateAccountingTemplate(AccountingReportType.PayrollSummary, "Payroll Summary", DatePresetNames.YearToDate),
            TemplateNames.Rentals => CreateAccountingTemplate(AccountingReportType.Rentals, "Rentals", DatePresetNames.ThisMonth),
            TemplateNames.AssetRegister => CreateAccountingTemplate(AccountingReportType.AssetRegister, "Asset Register", DatePresetNames.YearToDate),
            _ => new ReportConfiguration()
        };
    }

    /// <summary>
    /// Creates a monthly revenue report template.
    /// </summary>
    public static ReportConfiguration CreateMonthlyRevenueTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Revenue Overview",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                DatePresetName = DatePresetNames.ThisMonth
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.TotalRevenue,
            ChartDataType.RevenueDistribution,
            ChartDataType.CustomerGrowth,
            ChartDataType.AverageTransactionValue
        ]);

        AddSalesReportElements(config);
        return config;
    }

    /// <summary>
    /// Creates a financial overview template.
    /// </summary>
    public static ReportConfiguration CreateFinancialOverviewTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Financial Overview",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                DatePresetName = DatePresetNames.ThisQuarter
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.TotalRevenue,
            ChartDataType.TotalExpenses,
            ChartDataType.RevenueVsExpenses,
            ChartDataType.TotalProfits
        ]);

        AddFinancialOverviewElements(config);
        return config;
    }

    /// <summary>
    /// Creates a performance analysis template.
    /// </summary>
    public static ReportConfiguration CreatePerformanceAnalysisTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Performance Analysis",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                DatePresetName = DatePresetNames.Last30Days
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.CustomerGrowth,
            ChartDataType.AverageTransactionValue,
            ChartDataType.TotalTransactions,
            ChartDataType.ReturnsOverTime
        ]);

        AddPerformanceAnalysisElements(config);
        return config;
    }

    /// <summary>
    /// Creates a returns analysis template.
    /// </summary>
    public static ReportConfiguration CreateReturnsAnalysisTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Returns Analysis",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                IncludeReturns = true,
                IncludeLosses = false,
                DatePresetName = DatePresetNames.YearToDate
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.ReturnReasons,
            ChartDataType.ReturnFinancialImpact,
            ChartDataType.ReturnsByCategory,
            ChartDataType.ReturnsByProduct
        ]);

        AddReturnsAnalysisElements(config);
        return config;
    }

    /// <summary>
    /// Creates a losses analysis template.
    /// </summary>
    public static ReportConfiguration CreateLossesAnalysisTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Losses Analysis",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                IncludeReturns = false,
                IncludeLosses = true,
                DatePresetName = DatePresetNames.YearToDate
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.LossReasons,
            ChartDataType.LossFinancialImpact,
            ChartDataType.LossesByCategory,
            ChartDataType.LossesByProduct
        ]);

        AddLossesAnalysisElements(config);
        return config;
    }

    /// <summary>
    /// Creates a geographic analysis template.
    /// </summary>
    public static ReportConfiguration CreateGeographicAnalysisTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Geographic Analysis",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                DatePresetName = DatePresetNames.Last30Days
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.WorldMap,
            ChartDataType.CountriesOfOrigin,
            ChartDataType.CountriesOfDestination,
            ChartDataType.CompaniesOfOrigin
        ]);

        AddGeographicAnalysisElements(config);
        return config;
    }

    /// <summary>
    /// Creates a customer analysis template.
    /// </summary>
    public static ReportConfiguration CreateCustomerAnalysisTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Customer Analysis",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Revenue,
                DatePresetName = DatePresetNames.YearToDate
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.TopCustomersByRevenue,
            ChartDataType.CustomerGrowth,
            ChartDataType.CustomerLifetimeValue,
            ChartDataType.ActiveVsInactiveCustomers
        ]);

        AddCustomerAnalysisElements(config);
        return config;
    }

    /// <summary>
    /// Creates an expense breakdown template.
    /// </summary>
    public static ReportConfiguration CreateExpenseBreakdownTemplate()
    {
        var config = new ReportConfiguration
        {
            Title = "Expense Breakdown",
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Landscape,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters =
            {
                TransactionType = TransactionType.Expenses,
                DatePresetName = DatePresetNames.ThisMonth
            }
        };

        config.Filters.SelectedChartTypes.AddRange(
        [
            ChartDataType.TotalExpenses,
            ChartDataType.ExpensesDistribution,
            ChartDataType.AverageTransactionValue,
            ChartDataType.RevenueVsExpenses
        ]);

        AddExpenseBreakdownElements(config);
        return config;
    }

    #region Element Addition Methods

    private static void AddSalesReportElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create a 2x2 grid for charts below the date range
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TotalRevenue,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.RevenueDistribution,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CustomerGrowth,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.AverageTransactionValue,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddFinancialOverviewElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create 2x2 grid for charts below the date range
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.RevenueVsExpenses,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TotalProfits,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TotalRevenue,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TotalExpenses,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddPerformanceAnalysisElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create vertical stack: summary (20%), then 3 charts below the date range
        var stack = CreateVerticalStack(context, 0.20, 0.27, 0.27, 0.26);

        config.AddElement(new SummaryReportElement
        {
            X = stack[0].X,
            Y = stack[0].Y,
            Width = stack[0].Width,
            Height = stack[0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CustomerGrowth,
            X = stack[1].X,
            Y = stack[1].Y,
            Width = stack[1].Width,
            Height = stack[1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.AverageTransactionValue,
            X = stack[2].X,
            Y = stack[2].Y,
            Width = stack[2].Width,
            Height = stack[2].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ReturnsOverTime,
            X = stack[3].X,
            Y = stack[3].Y,
            Width = stack[3].Width,
            Height = stack[3].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddReturnsAnalysisElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // 2x2 grid layout for 4 charts
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ReturnReasons,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ReturnFinancialImpact,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ReturnsByCategory,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ReturnsByProduct,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddLossesAnalysisElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // 2x2 grid layout for 4 charts
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.LossReasons,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.LossFinancialImpact,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.LossesByCategory,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.LossesByProduct,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddGeographicAnalysisElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create vertical split: map (50%), then bottom grid (50%) below the date range
        var stack = CreateVerticalStack(context, 0.5, 0.5);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.WorldMap,
            X = stack[0].X,
            Y = stack[0].Y,
            Width = stack[0].Width,
            Height = stack[0].Height
        });

        // Split bottom area into 2 columns
        var columns = SplitHorizontally(stack[1], [0.5, 0.5], context.ElementSpacing);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CountriesOfOrigin,
            X = columns[0].X,
            Y = columns[0].Y,
            Width = columns[0].Width,
            Height = columns[0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CountriesOfDestination,
            X = columns[1].X,
            Y = columns[1].Y,
            Width = columns[1].Width,
            Height = columns[1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddCustomerAnalysisElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create 2x2 grid for charts
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TopCustomersByRevenue,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CustomerGrowth,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.CustomerLifetimeValue,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ActiveVsInactiveCustomers,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static void AddExpenseBreakdownElements(ReportConfiguration config)
    {
        var context = new LayoutContext(config);

        // Create 2x2 grid for charts
        var grid = CreateGrid(context, 2, 2);

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.TotalExpenses,
            X = grid[0, 0].X,
            Y = grid[0, 0].Y,
            Width = grid[0, 0].Width,
            Height = grid[0, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.ExpensesDistribution,
            X = grid[0, 1].X,
            Y = grid[0, 1].Y,
            Width = grid[0, 1].Width,
            Height = grid[0, 1].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.AverageTransactionValue,
            X = grid[1, 0].X,
            Y = grid[1, 0].Y,
            Width = grid[1, 0].Width,
            Height = grid[1, 0].Height
        });

        config.AddElement(new ChartReportElement
        {
            ChartType = ChartDataType.RevenueVsExpenses,
            X = grid[1, 1].X,
            Y = grid[1, 1].Y,
            Width = grid[1, 1].Width,
            Height = grid[1, 1].Height
        });

        // Date range element - added last so it renders on top (highest ZOrder)
        var dateRangeBounds = GetDateRangeBounds(context);
        config.AddElement(new DateRangeReportElement
        {
            X = context.MarginLeft + (context.ContentWidth - 200) / 2,
            Y = dateRangeBounds.Y,
            Height = dateRangeBounds.Height
        });
    }

    private static ReportConfiguration CreateAccountingTemplate(AccountingReportType reportType, string title, string datePreset)
    {
        var config = new ReportConfiguration
        {
            Title = title,
            PageSize = PageSize.Letter,
            PageOrientation = PageOrientation.Portrait,
            ShowHeader = true,
            ShowFooter = true,
            ShowPageNumbers = true,
            ShowCompanyDetails = true,
            Filters = { DatePresetName = datePreset }
        };

        var context = new LayoutContext(config);

        // AR Aging and the Customer Statement give their own dates in the table, and the Balance Sheet gets an "As of" date.
        var datesInTable = reportType is AccountingReportType.AccountsReceivableAging or AccountingReportType.CustomerStatement;

        if (!datesInTable)
        {
            var dateRangeBounds = GetDateRangeBounds(context);
            config.AddElement(new DateRangeReportElement
            {
                X = context.MarginLeft + (context.ContentWidth - 200) / 2,
                Y = dateRangeBounds.Y,
                Height = dateRangeBounds.Height,
                IsAsOfDate = reportType == AccountingReportType.BalanceSheet
            });
        }

        // Accounting table filling the content area
        config.AddElement(new AccountingTableReportElement
        {
            ReportType = reportType,
            X = context.MarginLeft,
            Y = context.ContentTop,
            Width = context.ContentWidth,
            Height = context.ContentHeight
        });

        return config;
    }

    #endregion
}
