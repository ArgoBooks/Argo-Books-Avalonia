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
/// Tests for the AccountingReportDataService class.
/// </summary>
public class AccountingReportDataServiceTests
{
    private static ReportFilters CreateDefaultFilters() => new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullCompanyData_CreatesInstance()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithEmptyCompanyData_CreatesInstance()
    {
        var data = new CompanyData();
        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        Assert.NotNull(service);
    }

    #endregion

    #region Income Statement Tests

    [Fact]
    public void GetReportData_IncomeStatement_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.IncomeStatement);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetReportData_IncomeStatement_EmptyData_ReturnsValidResult()
    {
        var data = new CompanyData();
        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.IncomeStatement);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetReportData_IncomeStatement_IncludesEndDayTransactionEnteredWithATime()
    {
        var data = new CompanyData();
        // A revenue entered at 14:30 on the last day of a custom range whose end date is stored at
        // midnight (as the date picker does). It must still be counted, not dropped by a time compare.
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1",
            Date = new DateTime(2024, 6, 30, 14, 30, 0),
            Total = 100m
        });
        var filters = new ReportFilters
        {
            StartDate = new DateTime(2024, 6, 1),
            EndDate = new DateTime(2024, 6, 30) // midnight
        };

        var result = new AccountingReportDataService(data, filters).GetReportData(AccountingReportType.IncomeStatement);

        var totalRevenue = result.Rows.Find(r => r.Label == "Total Revenue");
        Assert.NotNull(totalRevenue);
        Assert.Contains("100", totalRevenue.Values[0]);
    }

    #endregion

    #region Balance Sheet Tests

    [Fact]
    public void GetReportData_BalanceSheet_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.BalanceSheet);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetReportData_BalanceSheet_WithInventory_AddsInventoryRow()
    {
        var data = new CompanyData();
        // 100 units @ $5 via a single manual Add adjustment dated mid-period.
        // No revenue/expense rows => Cash = 0 and AR = 0, so Total Current
        // Assets equals the inventory value alone.
        data.Inventory.Add(new InventoryItem { Id = "I1", InStock = 100, UnitCost = 5m });
        data.StockAdjustments.Add(new StockAdjustment
        {
            InventoryItemId = "I1",
            AdjustmentType = AdjustmentType.Add,
            Quantity = 100,
            PreviousStock = 0,
            NewStock = 100,
            Timestamp = new DateTime(2024, 6, 1)
        });
        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.BalanceSheet);

        var inventoryRow = result.Rows.Find(r => r.Label == "Inventory");
        Assert.NotNull(inventoryRow);
        var totalCurrentAssetsRow = result.Rows.Find(r => r.Label == "Total Current Assets");
        Assert.NotNull(totalCurrentAssetsRow);
        // Cash and AR are zero, so the subtotal must equal the inventory value.
        Assert.Equal(inventoryRow.Values[0], totalCurrentAssetsRow.Values[0]);
    }

    // $500 rent + $65 tax + $200 deposit = $765, paid and refunded in full, deposit included. The
    // $65 of tax is handed back; the deposit part of the refund carried none.
    [Fact]
    public void GetReportData_BalanceSheet_TaxPayableTakesOffTheTaxOnARefund()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-D", InvoiceNumber = "INV-D", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), Subtotal = 500m, TaxAmount = 65m, SecurityDeposit = 200m,
            Total = 765m, TotalUSD = 765m, Status = InvoiceStatus.Refunded
        });
        data.Revenues.Add(new Revenue
        {
            Id = "REV-D", InvoiceId = "INV-D", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD",
            Subtotal = 500m, TaxAmount = 65m, TaxAmountUSD = 65m, Total = 565m, TotalUSD = 565m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-D1", InvoiceId = "INV-D", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 1),
            Amount = 765m, AmountUSD = 765m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-D2", InvoiceId = "INV-D", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 10),
            Amount = -765m, AmountUSD = -765m, IsRefund = true, DepositAmount = 200m
        });

        var result = new AccountingReportDataService(data, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);

        Assert.Equal(0m, AmountOf(result, "TOTAL LIABILITIES"));
    }

    // $500 rent + $200 deposit = $700, paid Mar 1. The deposit is owed back to the customer until it
    // is given back or kept, so only the $500 of rent is retained earnings.
    [Theory]
    [InlineData(null, false, 200, 500)]          // still held at the end date
    [InlineData("2025-01-15", false, 200, 500)]  // given back after the end date, so held on it
    [InlineData("2024-12-01", false, 0, 500)]    // given back before the end date
    [InlineData("2024-12-01", true, 0, 700)]     // kept, which made it revenue
    public void GetReportData_BalanceSheet_OwesBackTheSecurityDepositsItStillHolds(
        string? settledOn, bool kept, int owed, int retained)
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-S", InvoiceNumber = "INV-S", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), Subtotal = 500m, SecurityDeposit = 200m,
            Total = 700m, TotalUSD = 700m, Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        data.Revenues.Add(new Revenue
        {
            Id = "REV-S", InvoiceId = "INV-S", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD",
            Subtotal = 500m, Total = 500m, TotalUSD = 500m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-S1", InvoiceId = "INV-S", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 1),
            Amount = 700m, AmountUSD = 700m
        });
        if (settledOn != null && kept)
        {
            data.Revenues.Add(new Revenue
            {
                Id = "REV-K", InvoiceId = "INV-S", IsKeptDeposit = true, Date = DateTime.Parse(settledOn),
                OriginalCurrency = "USD", Subtotal = 200m, Total = 200m, TotalUSD = 200m
            });
        }
        else if (settledOn != null)
        {
            data.Payments.Add(new Payment
            {
                Id = "PAY-S2", InvoiceId = "INV-S", OriginalCurrency = "USD", Date = DateTime.Parse(settledOn),
                Amount = -200m, AmountUSD = -200m, IsRefund = true, DepositAmount = 200m
            });
        }
        InvoiceTotalsService.Recalculate(invoice, data.Payments);

        var result = new AccountingReportDataService(data, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);

        Assert.Equal(owed, AmountOf(result, "TOTAL LIABILITIES"));
        Assert.Equal(retained, AmountOf(result, "Retained Earnings"));
    }

    [Fact]
    public void GetReportData_BalanceSheet_NoInventory_OmitsInventoryRow()
    {
        var data = new CompanyData();
        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.BalanceSheet);

        Assert.Null(result.Rows.Find(r => r.Label == "Inventory"));
    }

    #endregion

    #region AR Aging Tests

    [Fact]
    public void GetReportData_ARaging_ExcludesInvoicesIssuedAfterTheEndDate_AndAgesAsOfEndDate()
    {
        var data = new CompanyData();
        // Open invoice within the report window.
        data.Invoices.Add(new Invoice
        {
            Id = "INV-A", InvoiceNumber = "INV-A", CustomerId = "C1",
            IssueDate = new DateTime(2024, 6, 1), DueDate = new DateTime(2024, 7, 1),
            Total = 100m, Balance = 100m, Status = InvoiceStatus.Sent
        });
        // Open invoice issued AFTER the report's end date - must not appear on an "as of 2024-12-31" aging.
        data.Invoices.Add(new Invoice
        {
            Id = "INV-B", InvoiceNumber = "INV-B", CustomerId = "C1",
            IssueDate = new DateTime(2025, 1, 1), DueDate = new DateTime(2025, 2, 1),
            Total = 600m, Balance = 600m, Status = InvoiceStatus.Sent
        });
        var filters = new ReportFilters { StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) };

        var result = new AccountingReportDataService(data, filters).GetReportData(AccountingReportType.AccountsReceivableAging);

        var totalRow = result.Rows.Find(r => r.Label == "TOTAL");
        Assert.NotNull(totalRow);
        var grandTotal = totalRow.Values[^1];
        Assert.Contains("100", grandTotal);       // only INV-A counts
        Assert.DoesNotContain("600", grandTotal); // INV-B (issued after the end date) excluded
    }

    #endregion

    #region Cash Flow Tests

    [Fact]
    public void GetReportData_CashFlow_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.CashFlowStatement);

        Assert.NotNull(result);
    }

    #endregion

    #region General Ledger Tests

    [Fact]
    public void GetReportData_GeneralLedger_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.GeneralLedger);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetReportData_GeneralLedger_RefundPayment_ShowsAmountInLedgerRow()
    {
        // A refund is stored as a Payment with a negative Amount. The ledger records every payment as
        // a Debit and only renders a column when its value is > 0, so a refund's negative debit shows
        // blank in BOTH columns even though the running balance still drops. The amount should be
        // visible (it belongs in the Credit column).
        var data = new CompanyData();
        data.Payments.Add(new Payment
        {
            Id = "PMT-1",
            CustomerId = "CUST-1",
            Date = new DateTime(2024, 6, 1),
            Amount = 250m,
            OriginalCurrency = "USD"
        });
        data.Payments.Add(new Payment
        {
            Id = "PMT-2",
            CustomerId = "CUST-1",
            Date = new DateTime(2024, 6, 15),
            Amount = -50m,        // refunds are stored negative
            OriginalCurrency = "USD",
            IsRefund = true,
            RefundedFromPaymentId = "PMT-1"
        });

        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.GeneralLedger);

        // The refund's ledger row is keyed by its payment id in the Reference column (Values[1]).
        var refundRow = result.Rows.Find(r => r.Values.Count >= 4 && r.Values[1] == "PMT-2");
        Assert.NotNull(refundRow);
        var debit = refundRow.Values[2];
        var credit = refundRow.Values[3];
        Assert.True(
            debit.Contains("50") || credit.Contains("50"),
            $"Refund amount missing from the ledger row. Debit='{debit}', Credit='{credit}'.");
    }

    #endregion

    #region AR/AP Aging Tests

    [Fact]
    public void GetReportData_ARAging_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.AccountsReceivableAging);

        Assert.NotNull(result);
    }

    #endregion

    #region Tax Summary Tests

    [Fact]
    public void GetReportData_TaxSummary_NullCompanyData_ReturnsValidResult()
    {
        var service = new AccountingReportDataService(null, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.TaxSummary);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetReportData_TaxSummary_CountsTransactionLevelTaxWhenLineItemsHaveNoRate()
    {
        // Regression: manually-entered transactions always carry a line item with TaxRate 0 but
        // record their tax at the transaction level.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-2024-00001",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "USD",
            Amount = 100m,
            TaxRate = 8m,        // stored as a percentage (8%)
            TaxAmount = 8m,
            TaxAmountUSD = 8m,
            Total = 108m,
            TotalUSD = 108m,
            LineItems = [new LineItem { Description = "Sale", Quantity = 1, UnitPrice = 100m, TaxRate = 0 }]
        });
        data.Expenses.Add(new Expense
        {
            Id = "EXP-2024-00001",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "USD",
            Amount = 50m,
            TaxRate = 10m,
            TaxAmount = 5m,
            TaxAmountUSD = 5m,
            Total = 55m,
            TotalUSD = 55m,
            LineItems = [new LineItem { Description = "Supplies", Quantity = 1, UnitPrice = 50m, TaxRate = 0 }]
        });

        var service = new AccountingReportDataService(data, CreateDefaultFilters());

        var result = service.GetReportData(AccountingReportType.TaxSummary);

        // The two subtotal rows are "Total Tax Collected" then "Total Tax Paid".
        var subtotals = result.Rows.FindAll(r => r.RowType == AccountingRowType.SubtotalRow);
        Assert.Equal(2, subtotals.Count);
        Assert.Contains("8", subtotals[0].Values[0]);   // tax collected = $8
        Assert.Contains("5", subtotals[1].Values[0]);   // tax paid = $5
    }

    // An invoice's revenue copies each line's own rate, but its tax comes from the invoice rate. The
    // summary totals the recorded tax, as the Balance Sheet does, split across the line rates.
    [Fact]
    public void GetReportData_TaxSummary_TotalsTheRecordedTax_SplitByLineRate()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-2024-00001", Date = new DateTime(2024, 6, 1), OriginalCurrency = "USD",
            TaxRate = 13m, TaxAmount = 26m, TaxAmountUSD = 26m, Total = 226m, TotalUSD = 226m,
            LineItems =
            [
                new LineItem { Description = "A", Quantity = 1, UnitPrice = 100m, TaxRate = 0.05m },
                new LineItem { Description = "B", Quantity = 1, UnitPrice = 100m, TaxRate = 0.15m }
            ]
        });

        var result = new AccountingReportDataService(data, CreateDefaultFilters())
            .GetReportData(AccountingReportType.TaxSummary);

        var collected = result.Rows.First(r => r.RowType == AccountingRowType.SubtotalRow);
        Assert.Contains("26", collected.Values[0]);
        Assert.Equal(26m, AmountOf(result, "NET TAX LIABILITY"));
    }

    // docs/Calculations.md §8: $86.91 + $32.09 tax = $119, paid Mar 1 and refunded in full Mar 10.
    private static CompanyData RefundedSale()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-T", InvoiceNumber = "INV-T", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), Subtotal = 86.91m, TaxAmount = 32.09m,
            Total = 119m, TotalUSD = 119m, Status = InvoiceStatus.Refunded
        });
        data.Revenues.Add(new Revenue
        {
            Id = "REV-T", InvoiceId = "INV-T", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD",
            Subtotal = 86.91m, TaxRate = 36.92m, TaxAmount = 32.09m, TaxAmountUSD = 32.09m, Total = 119m, TotalUSD = 119m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-T1", InvoiceId = "INV-T", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 1),
            Amount = 119m, AmountUSD = 119m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-T2", InvoiceId = "INV-T", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 10),
            Amount = -119m, AmountUSD = -119m, IsRefund = true
        });
        return data;
    }

    // Tax owed drops by the $32.09 handed back.
    [Fact]
    public void GetReportData_TaxSummary_TakesOffTheTaxOnARefund()
    {
        var result = new AccountingReportDataService(RefundedSale(), CreateDefaultFilters())
            .GetReportData(AccountingReportType.TaxSummary);

        Assert.Equal(0m, AmountOf(result, "NET TAX LIABILITY"));
    }

    // The $86.91 of revenue comes back off on the refund's date, so nothing is left as income.
    [Fact]
    public void GetReportData_IncomeStatement_TakesOffARefundBeforeTax()
    {
        var result = new AccountingReportDataService(RefundedSale(), CreateDefaultFilters())
            .GetReportData(AccountingReportType.IncomeStatement);

        Assert.Equal(0m, AmountOf(result, "Total Revenue"));
        Assert.Equal(0m, AmountOf(result, "NET INCOME"));
    }

    [Fact]
    public void GetReportData_GeneralLedger_TakesARefundBackOffRevenueBeforeTax()
    {
        var result = new AccountingReportDataService(RefundedSale(), CreateDefaultFilters())
            .GetReportData(AccountingReportType.GeneralLedger);

        var revenueRows = result.Rows
            .SkipWhile(r => r.Label != "REVENUE").Skip(1)
            .TakeWhile(r => r.RowType == AccountingRowType.DataRow)
            .ToList();
        Assert.Equal(0m, ParseAmount(revenueRows[^1].Values[4]));
    }

    #endregion

    #region Date-Filtering Regressions

    [Fact]
    public void GetReportData_BalanceSheet_ExcludesInvoicesIssuedAfterEndDate()
    {
        // AR on the Balance Sheet is an "as of the end date" balance, so an open invoice issued AFTER
        // the report end date must not be counted. Every other current-asset/liability line is date
        // gated via IsOnOrBeforeEndDate.
        static Invoice MakeInvoice(string id, DateTime issue, decimal amount) => new()
        {
            Id = id,
            IssueDate = issue,
            Total = amount,
            Balance = amount,
            Status = InvoiceStatus.Sent,
            OriginalCurrency = "USD"
        };

        static string TotalCurrentAssets(AccountingTableData r) =>
            r.Rows.Find(x => x.Label == "Total Current Assets")!.Values[0];

        // Only the in-period invoice (issued mid-2024, within the 2024 report window).
        var dataInPeriodOnly = new CompanyData();
        dataInPeriodOnly.Invoices.Add(MakeInvoice("INV-IN", new DateTime(2024, 6, 1), 1000m));

        // Same invoice plus one issued in 2025, after the 2024-12-31 end date.
        var dataWithFuture = new CompanyData();
        dataWithFuture.Invoices.Add(MakeInvoice("INV-IN", new DateTime(2024, 6, 1), 1000m));
        dataWithFuture.Invoices.Add(MakeInvoice("INV-FUTURE", new DateTime(2025, 3, 1), 5000m));

        var without = new AccountingReportDataService(dataInPeriodOnly, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);
        var with = new AccountingReportDataService(dataWithFuture, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);

        // The future invoice is outside the report window, so AR (and thus Total Current Assets,
        // with cash and inventory both zero) must be identical with or without it.
        Assert.Equal(TotalCurrentAssets(without), TotalCurrentAssets(with));
    }

    [Fact]
    public void GetReportData_BalanceSheet_ExcludesPurchaseOrdersOrderedAfterEndDate()
    {
        // AP mirrors AR: an open purchase order placed AFTER the report end date must not count toward
        // "as of" Accounts Payable. The AP line lacked the IsOnOrBeforeEndDate gate its neighbors have.
        static PurchaseOrder MakePO(string id, DateTime order, decimal amount) => new()
        {
            Id = id,
            OrderDate = order,
            Total = amount,
            Status = PurchaseOrderStatus.Sent,
            OriginalCurrency = "USD"
        };

        static string TotalLiabilities(AccountingTableData r) =>
            r.Rows.Find(x => x.Label == "TOTAL LIABILITIES")!.Values[0];

        var dataInPeriodOnly = new CompanyData();
        dataInPeriodOnly.PurchaseOrders.Add(MakePO("PO-IN", new DateTime(2024, 6, 1), 1000m));

        var dataWithFuture = new CompanyData();
        dataWithFuture.PurchaseOrders.Add(MakePO("PO-IN", new DateTime(2024, 6, 1), 1000m));
        dataWithFuture.PurchaseOrders.Add(MakePO("PO-FUTURE", new DateTime(2025, 1, 5), 3000m));

        var without = new AccountingReportDataService(dataInPeriodOnly, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);
        var with = new AccountingReportDataService(dataWithFuture, CreateDefaultFilters())
            .GetReportData(AccountingReportType.BalanceSheet);

        // No revenue/expense => sales tax payable is zero, so Total Liabilities equals AP. The future
        // PO is outside the window and must not change it.
        Assert.Equal(TotalLiabilities(without), TotalLiabilities(with));
    }

    [Fact]
    public void GetReportData_IncomeStatement_CountsTransactionWhenAllLineItemsNetToZero()
    {
        // A fully-discounted sale has line items that net to a $0 subtotal but still carries a
        // transaction-level Total. The category allocator divides each line item's share by the sum
        // of line-item subtotals; when that sum is 0 it returned $0 for the whole sale, dropping it
        // from Total Revenue (and the General Ledger) entirely.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-DISC",
            Date = new DateTime(2024, 6, 1),
            OriginalCurrency = "USD",
            Total = 4321m,
            TaxAmount = 0m,
            LineItems = [new LineItem { Description = "Item", Quantity = 1, UnitPrice = 4321m, Discount = 4321m }]
        });

        var result = new AccountingReportDataService(data, CreateDefaultFilters())
            .GetReportData(AccountingReportType.IncomeStatement);

        // The first subtotal row is "Total Revenue". Strip formatting to compare digits only.
        var totalRevenue = result.Rows.Find(r => r.RowType == AccountingRowType.SubtotalRow)!.Values[0];
        var digits = new string(totalRevenue.Where(char.IsDigit).ToArray());
        Assert.Contains("4321", digits);
    }

    #endregion

    #region Receivables As Of The End Date

    private static readonly ReportFilters January2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 1, 31)
    };

    // $1,000 invoiced Jan 10, $400 paid Jan 20, the other $600 Feb 5. Today it reads Paid with no
    // balance, but at Jan 31 the customer still owed $600.
    private static CompanyData InvoicePaidOffInFebruary()
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 1, 10), DueDate = new DateTime(2024, 2, 9),
            Subtotal = 1000m, Total = 1000m, TotalUSD = 1000m, Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", InvoiceId = "INV-1", CustomerId = "C1", Date = new DateTime(2024, 1, 10),
            OriginalCurrency = "USD", Subtotal = 1000m, Total = 1000m, TotalUSD = 1000m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", CustomerId = "C1", OriginalCurrency = "USD",
            Date = new DateTime(2024, 1, 20, 11, 0, 0), Amount = 400m, AmountUSD = 400m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-1", CustomerId = "C1", OriginalCurrency = "USD",
            Date = new DateTime(2024, 2, 5, 11, 0, 0), Amount = 600m, AmountUSD = 600m
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        return data;
    }

    [Fact]
    public void GetReportData_BalanceSheet_ReceivablesAreWhatWasOwedAtTheEndDate()
    {
        var data = InvoicePaidOffInFebruary();
        Assert.Equal(InvoiceStatus.Paid, data.Invoices[0].Status);

        var result = new AccountingReportDataService(data, January2024).GetReportData(AccountingReportType.BalanceSheet);

        // Cash is the $400 received by Jan 31; the $600 still owed is a receivable.
        Assert.Equal(600m, AmountOf(result, "Accounts Receivable"));
        Assert.Equal(1000m, AmountOf(result, "TOTAL ASSETS"));
    }

    [Fact]
    public void GetReportData_ARAging_AgesWhatWasOwedAtTheEndDate()
    {
        var result = new AccountingReportDataService(InvoicePaidOffInFebruary(), January2024)
            .GetReportData(AccountingReportType.AccountsReceivableAging);

        var total = result.Rows.Find(r => r.Label == "TOTAL")!;
        Assert.Equal(600m, ParseAmount(total.Values[^1]));
    }

    [Fact]
    public void GetReportData_ARAging_LeavesOutAFullyRefundedInvoice()
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-R", InvoiceNumber = "INV-R", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), DueDate = new DateTime(2024, 3, 31),
            Total = 500m, TotalUSD = 500m, Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        data.Payments.Add(new Payment
        {
            Id = "PAY-R1", InvoiceId = "INV-R", CustomerId = "C1", OriginalCurrency = "USD",
            Date = new DateTime(2024, 3, 5), Amount = 500m, AmountUSD = 500m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-R2", InvoiceId = "INV-R", CustomerId = "C1", OriginalCurrency = "USD",
            Date = new DateTime(2024, 3, 10), Amount = -500m, AmountUSD = -500m, IsRefund = true
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);

        var result = new AccountingReportDataService(data, CreateDefaultFilters())
            .GetReportData(AccountingReportType.AccountsReceivableAging);

        Assert.DoesNotContain(result.Rows, r => r.RowType == AccountingRowType.DataRow);
    }

    #endregion

    #region Payroll Remittance Tests

    private static ReportFilters PayrollFilters() => new()
    {
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2026, 12, 31)
    };

    private static CompanyData PayrollData(string province, decimal qpip = 0m)
    {
        var data = new CompanyData();

        data.PayRuns.Add(new Core.Models.Payroll.PayRun
        {
            Id = "PR-0001",
            PayDate = new DateTime(2026, 7, 3),
            Status = Core.Models.Payroll.PayRunStatus.Approved,
            Lines =
            {
                new Core.Models.Payroll.PayRunLine
                {
                    EmployeeId = "EMP-001",
                    Province = province,
                    GrossPay = 2000m,
                    CppEmployee = 100m,
                    CppEmployer = 100m,
                    EiEmployee = 30m,
                    EiEmployer = 42m,
                    QpipEmployee = qpip,
                    QpipEmployer = qpip,
                    FederalTax = 200m,
                    ProvincialTax = 90m,
                },
            },
        });

        return data;
    }

    private static decimal AmountOf(AccountingTableData data, string label) =>
        ParseAmount(data.Rows.Find(r => r.Label == label)!.Values[0]);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    [Fact]
    public void PayrollRemittance_OutsideQuebec_TotalsEverythingForCra()
    {
        var result = new AccountingReportDataService(PayrollData("ON"), PayrollFilters())
            .GetReportData(AccountingReportType.PayrollRemittance);

        // 200 federal + 90 provincial + 100 CPP + 100 employer CPP + 30 EI + 42 employer EI.
        Assert.Equal(562m, AmountOf(result, "Total to remit"));
        Assert.DoesNotContain(result.Rows, r => r.Label.Contains("Revenu Quebec"));
    }

    [Fact]
    public void PayrollRemittance_ForAQuebecEmployee_SplitsTheTwoAgenciesApart()
    {
        // Revenu Quebec collects Quebec income tax, QPP and QPIP; CRA collects federal income
        // tax and EI. A single combined total is owed to nobody: it overstates the CRA payment
        // by the whole Quebec side, and never mentions the payment Revenu Quebec is waiting for.
        var result = new AccountingReportDataService(PayrollData("QC", qpip: 9m), PayrollFilters())
            .GetReportData(AccountingReportType.PayrollRemittance);

        // CRA: federal tax 200 + EI 30 + employer EI 42. No Quebec tax, and no QPP.
        Assert.Equal(272m, AmountOf(result, "Total to remit to CRA"));

        // Revenu Quebec: Quebec tax 90 + QPP 100 + employer QPP 100 + QPIP 9 + employer QPIP 9.
        Assert.Equal(308m, AmountOf(result, "Total to remit to Revenu Quebec"));

        // The health services fund is a real employer contribution this app does not calculate,
        // so a total that looked complete would be trusted.
        Assert.Contains(result.Rows, r => r.Label.Contains("health services fund"));
    }

    #endregion
}
