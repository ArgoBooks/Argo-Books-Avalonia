using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ReportTableDataService class.
/// </summary>
public class ReportTableDataServiceTests
{
    private static ReportFilters CreateDefaultFilters() => new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static TableReportElement CreateDefaultTableConfig() => new()
    {
        MaxRows = 10,
        SortOrder = TableSortOrder.DateDescending,
        DataSelection = TableDataSelection.All
    };

    #region Revenue Table Tests

    [Fact]
    public void GetRevenueTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetRevenueTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    [Fact]
    public void GetRevenueTableData_EmptyRevenues_ReturnsEmptyList()
    {
        var data = new CompanyData();
        var service = new ReportTableDataService(data, CreateDefaultFilters());

        var result = service.GetRevenueTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Expense Table Tests

    [Fact]
    public void GetExpensesTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetExpensesTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Invoice Table Tests

    [Fact]
    public void GetInvoicesTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetInvoicesTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Multi-currency amount tests

    // Report tables store USD-normalized amounts (the renderer converts them to the display currency
    // at each row's date).

    [Fact]
    public void GetInvoicesTableData_ForeignCurrencyInvoice_StoresUsdNormalizedAmounts()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-001",
            IssueDate = new DateTime(2024, 6, 1),
            OriginalCurrency = "EUR",
            Total = 100m,      // native EUR
            TotalUSD = 110m,   // USD
            Balance = 40m,     // native EUR
            BalanceUSD = 44m,  // USD
            Status = InvoiceStatus.Sent
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetInvoicesTableData(CreateDefaultTableConfig()));

        Assert.Equal(110m, row.Total);      // USD-normalized, not the native 100
        Assert.Equal(44m, row.Balance);     // USD-normalized, not the native 40
        Assert.Equal(66m, row.AmountPaid);  // Total - Balance, in USD
    }

    [Fact]
    public void GetPaymentsTableData_ForeignCurrencyPayment_StoresUsdNormalizedAmount()
    {
        var data = new CompanyData();
        data.Payments.Add(new Payment
        {
            Id = "PAY-001",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "EUR",
            Amount = 50m,     // native EUR
            AmountUSD = 55m   // USD
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetPaymentsTableData(CreateDefaultTableConfig()));

        Assert.Equal(55m, row.Amount);
    }

    [Fact]
    public void GetPurchaseOrdersTableData_ForeignCurrencyOrder_StoresUsdNormalizedTotal()
    {
        var data = new CompanyData();
        data.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = "PO-001",
            OrderDate = new DateTime(2024, 6, 1),
            OriginalCurrency = "EUR",
            Total = 200m,     // native EUR
            TotalUSD = 220m   // USD
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetPurchaseOrdersTableData(CreateDefaultTableConfig()));

        Assert.Equal(220m, row.Total);
    }

    #endregion

    #region Payment Table Tests

    [Fact]
    public void GetPaymentsTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetPaymentsTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Inventory Table Tests

    [Fact]
    public void GetInventoryTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetInventoryTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Entity Table Tests

    [Fact]
    public void GetCustomersTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetCustomersTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    [Fact]
    public void GetSuppliersTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetSuppliersTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    [Fact]
    public void GetProductsTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetProductsTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Returns/Losses Table Tests

    [Fact]
    public void GetReturnsTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetReturnsTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    [Fact]
    public void GetLossesTableData_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportTableDataService(null, CreateDefaultFilters());

        var result = service.GetLossesTableData(CreateDefaultTableConfig());

        Assert.Empty(result);
    }

    #endregion

    #region Shipping

    // The Shipping cell sits beside Total, which is converted from the USD base at the row's date.

    [Fact]
    public void GetRevenueTableData_ForeignCurrencyShipping_StoresUsdNormalizedShipping()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "EUR",
            Total = 100m,
            TotalUSD = 110m,
            ShippingCost = 20m,
            ShippingCostUSD = 22m
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetRevenueTableData(CreateDefaultTableConfig()));

        Assert.Equal(22m, row.ShippingCost);
    }

    [Fact]
    public void GetExpensesTableData_ForeignCurrencyShipping_StoresUsdNormalizedShipping()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense
        {
            Id = "EXP-1",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "EUR",
            Total = 100m,
            TotalUSD = 110m,
            ShippingCost = 20m,
            ShippingCostUSD = 22m
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetExpensesTableData(CreateDefaultTableConfig()));

        Assert.Equal(22m, row.ShippingCost);
    }

    #endregion

    #region Invoice status

    // The invoices list derives Overdue and works the refund status out afresh (docs/Calculations.md
    // §6); the report printed the stored status, so the two disagreed.

    [Fact]
    public void GetInvoicesTableData_SentAndPastDue_ShowsOverdue()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            IssueDate = new DateTime(2024, 6, 1),
            DueDate = new DateTime(2024, 7, 1),
            OriginalCurrency = "USD",
            Total = 100m,
            Balance = 100m,
            Status = InvoiceStatus.Sent
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetInvoicesTableData(CreateDefaultTableConfig()));

        Assert.Equal("Overdue", row.Status);
    }

    [Fact]
    public void GetInvoicesTableData_RefundedInFullWithAStaleStatus_ShowsRefunded()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            IssueDate = new DateTime(2024, 6, 1),
            DueDate = new DateTime(2024, 7, 1),
            OriginalCurrency = "USD",
            Total = 100m,
            AmountPaid = 100m,
            AmountRefunded = 100m,
            Balance = 0m,
            Status = InvoiceStatus.Paid
        });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetInvoicesTableData(CreateDefaultTableConfig()));

        Assert.Equal("Refunded", row.Status);
    }

    #endregion

    #region Sort by amount

    // ¥500,000 is about $3,400. Ranking by the native figure put it above a $5,000 row, and Max Rows
    // then cut the real largest.

    private static TableReportElement LargestOnly() => new()
    {
        MaxRows = 1,
        SortOrder = TableSortOrder.AmountDescending
    };

    [Fact]
    public void GetInvoicesTableData_LargestFirst_RanksByUsdNotNativeAmount()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice { Id = "YEN", InvoiceNumber = "YEN", IssueDate = new DateTime(2024, 6, 1), OriginalCurrency = "JPY", Total = 500_000m, TotalUSD = 3_400m });
        data.Invoices.Add(new Invoice { Id = "USD", InvoiceNumber = "USD", IssueDate = new DateTime(2024, 6, 2), OriginalCurrency = "USD", Total = 5_000m });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters()).GetInvoicesTableData(LargestOnly()));

        Assert.Equal("USD", row.InvoiceNumber);
    }

    [Fact]
    public void GetPaymentsTableData_LargestFirst_RanksByUsdNotNativeAmount()
    {
        var data = new CompanyData();
        data.Payments.Add(new Payment { Id = "YEN", Date = new DateTime(2024, 6, 1), OriginalCurrency = "JPY", Amount = 500_000m, AmountUSD = 3_400m });
        data.Payments.Add(new Payment { Id = "USD", Date = new DateTime(2024, 6, 2), OriginalCurrency = "USD", Amount = 5_000m });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters()).GetPaymentsTableData(LargestOnly()));

        Assert.Equal("USD", row.Id);
    }

    [Fact]
    public void GetPurchaseOrdersTableData_LargestFirst_RanksByUsdNotNativeAmount()
    {
        var data = new CompanyData();
        data.PurchaseOrders.Add(new PurchaseOrder { Id = "YEN", PoNumber = "YEN", OrderDate = new DateTime(2024, 6, 1), OriginalCurrency = "JPY", Total = 500_000m, TotalUSD = 3_400m });
        data.PurchaseOrders.Add(new PurchaseOrder { Id = "USD", PoNumber = "USD", OrderDate = new DateTime(2024, 6, 2), OriginalCurrency = "USD", Total = 5_000m });

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters()).GetPurchaseOrdersTableData(LargestOnly()));

        Assert.Equal("USD", row.PoNumber);
    }

    #endregion

    #region Quantity

    [Fact]
    public void GetRevenueTableData_FractionalQuantity_IsKept()
    {
        var data = new CompanyData();
        var revenue = new Revenue
        {
            Id = "REV-1",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "USD",
            Total = 60m
        };
        revenue.LineItems.Add(new LineItem { Description = "Consulting", Quantity = 1.5m, UnitPrice = 40m });
        data.Revenues.Add(revenue);

        var row = Assert.Single(new ReportTableDataService(data, CreateDefaultFilters())
            .GetRevenueTableData(CreateDefaultTableConfig()));

        Assert.Equal(1.5m, row.Quantity);
    }

    #endregion
}
