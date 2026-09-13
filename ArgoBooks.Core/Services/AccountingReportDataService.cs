using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for computing accounting report data from transaction records.
/// Aggregates from Revenue, Expense, Invoice, Payment, PurchaseOrder, and InventoryItem data
/// without using a Chart of Accounts or double-entry bookkeeping.
/// </summary>
public class AccountingReportDataService(CompanyData? companyData, ReportFilters filters)
{
    /// <summary>
    /// Gets the company's configured currency code, defaulting to USD.
    /// </summary>
    private string GetCurrencyCode()
    {
        return companyData?.Settings.Localization.Currency ?? "USD";
    }

    // Lazily computed, report-wide display currency decision (see DisplayCode).
    private string? _displayCode;

    /// <summary>
    /// The single currency the whole report is rendered in (docs/Calculations.md §3a Phase 2).
    ///
    /// Chosen ONCE per report so a printed document is never a mix of currencies:
    /// <list type="bullet">
    ///   <item>USD company => "USD" (identity, no conversion).</item>
    ///   <item>Non-USD company AND an exact-date USD->code rate is cached for EVERY date the report
    ///         converts => the company currency, converting each value at its OWN date.</item>
    ///   <item>Otherwise (any needed exact-date rate missing, or no rate service) => "USD" fallback,
    ///         showing the entire report in USD.</item>
    /// </list>
    /// </summary>
    private string DisplayCode => _displayCode ??= ResolveDisplayCode();

    private Dictionary<string, Invoice>? _invoicesById;

    private IReadOnlyDictionary<string, Invoice> InvoicesById =>
        _invoicesById ??= ProfitCalculator.BuildInvoiceLookup(companyData!.Invoices);

    private string ResolveDisplayCode()
    {
        var code = GetCurrencyCode();

        // USD company (the default): identity, never convert.
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase))
            return "USD";

        var rates = ExchangeRateService.Instance;
        if (rates == null || companyData == null)
            return "USD"; // No way to convert -> show USD so the document stays single-currency.

        // Convert at the company currency only if an exact-date USD->code rate is available for
        // EVERY date the report needs to convert. Any single miss -> whole report falls back to USD.
        foreach (var date in GetConversionDates())
        {
            if (!rates.TryConvertFromUSD(1m, code, date, out _))
                return "USD";
        }

        return code;
    }

    /// <summary>
    /// The distinct set of dates the report converts at: every transaction the report sums or needs
    /// (revenues, expenses, payments, purchase orders, and relevant invoices) PLUS the report end
    /// date (used for point-in-time valuations such as inventory and AR aging). If ANY of these
    /// lacks an exact-date rate for the company currency, the report falls back to USD.
    /// </summary>
    private IEnumerable<DateTime> GetConversionDates()
    {
        var dates = new HashSet<DateTime>();
        if (companyData != null)
        {
            // Revenues and expenses summed across Income Statement, Cash Flow, Balance Sheet,
            // General Ledger, Tax Summary and Product Sales: gate on every recorded date.
            foreach (var r in companyData.Revenues)
                dates.Add(r.Date.Date);
            foreach (var e in companyData.Expenses)
                dates.Add(e.Date.Date);

            // Payments (Cash Flow, Balance Sheet cash, General Ledger).
            foreach (var p in companyData.Payments)
                dates.Add(p.Date.Date);

            // Purchase orders (Balance Sheet accounts payable).
            foreach (var po in companyData.PurchaseOrders)
                dates.Add(po.OrderDate.Date);

            // Invoices (Balance Sheet AR, AR aging) are converted at their issue date.
            foreach (var i in companyData.Invoices)
                dates.Add(i.IssueDate.Date);
        }

        // Point-in-time valuations (inventory, balances "as of") use the end date.
        dates.Add((filters.EndDate ?? DateTime.Today).Date);

        return dates;
    }

    /// <summary>
    /// Converts a USD amount to <see cref="DisplayCode"/> at the given transaction date.
    /// Returns the USD amount unchanged when the report is in USD (USD company or fallback).
    /// Per docs/Calculations.md §3a, conversion happens at PRODUCTION (before aggregation), so a
    /// total equals the sum of its rows converted at each row's own date.
    /// </summary>
    private decimal ToDisplay(decimal amountUSD, DateTime date)
    {
        if (string.Equals(DisplayCode, "USD", StringComparison.OrdinalIgnoreCase))
            return amountUSD;

        // DisplayCode is only set to a non-USD code when every conversion date has an exact-date
        // rate, so this lookup is expected to succeed; fall back to the USD amount defensively.
        return ExchangeRateService.Instance != null
               && ExchangeRateService.Instance.TryConvertFromUSD(amountUSD, DisplayCode, date, out var converted)
            ? converted
            : amountUSD;
    }

    /// <summary>
    /// The end date used for point-in-time valuations (inventory, balances "as of").
    /// </summary>
    private DateTime EndDateForValuation => (filters.EndDate ?? DateTime.Today).Date;

    /// <summary>
    /// Gets a subtitle indicating the currency the report is rendered in (the resolved DisplayCode,
    /// so the fallback case correctly reads "Amounts in USD").
    /// </summary>
    private string GetCurrencySubtitle()
    {
        return $"Amounts in {DisplayCode}";
    }

    /// <summary>
    /// Formats a currency amount that is ALREADY in <see cref="DisplayCode"/> (conversion happens at
    /// production via <see cref="ToDisplay"/>), so this only formats; it does not convert.
    /// </summary>
    private string FormatCurrency(decimal amount)
    {
        return CurrencyInfo.FormatAmount(amount, DisplayCode);
    }

    /// <summary>
    /// Formats a currency amount, wrapping negative values in parentheses.
    /// </summary>
    private string FormatCurrencyWithSign(decimal amount)
    {
        if (amount < 0)
            return $"({FormatCurrency(Math.Abs(amount))})";
        return FormatCurrency(amount);
    }

    /// <summary>
    /// Checks whether a date falls within the configured filter range.
    /// </summary>
    private bool IsInDateRange(DateTime date)
    {
        // Compare at day granularity: a report date range selects whole days, but a transaction
        // carries a real time-of-day (DateTimeOffset.Now) and a custom range's end date is midnight,
        // so a raw `date > EndDate` would drop a transaction entered later on the end day itself.
        if (filters.StartDate.HasValue && date.Date < filters.StartDate.Value.Date)
            return false;
        if (filters.EndDate.HasValue && date.Date > filters.EndDate.Value.Date)
            return false;
        return true;
    }

    /// <summary>
    /// Checks whether a date falls on or before the end date filter.
    /// Used for cumulative/balance calculations.
    /// </summary>
    private bool IsOnOrBeforeEndDate(DateTime date)
    {
        if (filters.EndDate.HasValue && date.Date > filters.EndDate.Value.Date)
            return false;
        return true;
    }

    /// <summary>
    /// Issued invoices with money still owed at the end date, and how much (USD). The balance is
    /// rebuilt from the payments dated on or before that date, because an invoice's stored status
    /// and balance are today's: one paid after the end date was still owed on it. An invoice with
    /// no payment rows (imported with only a paid amount) has no dates to rebuild from, so its
    /// stored figures stand.
    /// </summary>
    private List<(Invoice Invoice, decimal BalanceUSD)> GetReceivablesAsOfEndDate()
    {
        var paymentsByInvoice = companyData!.Payments
            .Where(p => !p.IsRefund && p.Amount > 0 && !string.IsNullOrEmpty(p.InvoiceId))
            .ToLookup(p => p.InvoiceId);

        var receivables = new List<(Invoice, decimal)>();
        foreach (var invoice in companyData.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled
                                                                && i.Status != InvoiceStatus.Draft
                                                                && IsOnOrBeforeEndDate(i.IssueDate)))
        {
            var payments = paymentsByInvoice[invoice.Id].ToList();
            var balanceUSD = payments.Count == 0
                ? (invoice.Status == InvoiceStatus.Paid ? 0m : invoice.EffectiveBalanceUSD)
                : Math.Max(0m, invoice.EffectiveTotalUSD
                               - payments.Where(p => IsOnOrBeforeEndDate(p.Date)).Sum(p => p.EffectiveAmountUSD));

            if (Math.Round(balanceUSD, 2) > 0)
                receivables.Add((invoice, balanceUSD));
        }
        return receivables;
    }

    /// <summary>
    /// Dispatches to the appropriate report generation method based on report type.
    /// </summary>
    public AccountingTableData GetReportData(AccountingReportType reportType)
    {
        return reportType switch
        {
            AccountingReportType.IncomeStatement => GetIncomeStatementData(),
            AccountingReportType.BalanceSheet => GetBalanceSheetData(),
            AccountingReportType.CashFlowStatement => GetCashFlowData(),
            AccountingReportType.GeneralLedger => GetGeneralLedgerData(),
            AccountingReportType.AccountsReceivableAging => GetARAgingData(),
            AccountingReportType.TaxSummary => GetTaxSummaryData(),
            AccountingReportType.PayrollRemittance => GetPayrollRemittanceData(),
            AccountingReportType.ProductSales => GetProductSalesData(),
            _ => new AccountingTableData { Title = "Unknown Report" }
        };
    }

    /// <summary>
    /// Resolves a category name from a product ID by looking up the product's category.
    /// </summary>
    private string GetCategoryNameForProduct(string? productId)
    {
        if (string.IsNullOrEmpty(productId) || companyData == null)
            return "Uncategorized";

        var product = companyData.GetProduct(productId);
        if (product == null || string.IsNullOrEmpty(product.CategoryId))
            return "Uncategorized";

        var category = companyData.GetCategory(product.CategoryId);
        return category?.Name ?? "Uncategorized";
    }

    /// <summary>
    /// Gets the USD conversion ratio for a transaction's original currency amounts.
    /// Returns the multiplier to convert original currency values to USD equivalents.
    /// </summary>
    private static decimal GetUSDRatio(Transaction txn)
    {
        if (txn.IsPendingConversion) return 0;
        if (string.Equals(txn.OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            return 1m; // Already in USD (including legacy data)
        if (txn.Total != 0)
            return txn.TotalUSD / txn.Total;
        return 0m; // Zero-amount non-USD transaction
    }

    /// <summary>
    /// Groups transaction pre-tax totals by category, derived from line items' product IDs.
    /// Uses Subtotal (pre-tax) because sales tax is a liability, not revenue/expense.
    /// Each transaction's USD amounts are converted to DisplayCode at the transaction's OWN date
    /// (docs/Calculations.md §3a Phase 2) before being summed, so the result is already in
    /// DisplayCode.
    /// </summary>
    private Dictionary<string, decimal> GroupTransactionsByCategory(
        IEnumerable<Transaction> transactions)
    {
        var result = new Dictionary<string, decimal>();

        foreach (var txn in transactions)
        {
            var lineItemsTotal = txn.LineItems.Sum(li => li.Subtotal);

            // Proportional allocation needs a non-zero line-item subtotal to divide by.
            if (txn.LineItems.Count > 0 && lineItemsTotal != 0)
            {
                // Convert line item amounts to USD using the transaction's conversion ratio
                var subtotalUSD = txn.EffectiveSubtotalUSD;

                foreach (var lineItem in txn.LineItems)
                {
                    var categoryName = GetCategoryNameForProduct(lineItem.ProductId);
                    result.TryAdd(categoryName, 0);
                    // Proportionally allocate USD subtotal across line items, then convert at the
                    // transaction's own date.
                    var lineItemUSD = Math.Round(lineItem.Subtotal / lineItemsTotal * subtotalUSD, 2);
                    result[categoryName] += ToDisplay(lineItemUSD, txn.Date);
                }
            }
            else
            {
                // No line items, or every line item nets to a zero subtotal (e.g. a 100% discount):
                // post the transaction-level pre-tax amount so the transaction is not dropped.
                var categoryName = "Uncategorized";
                result.TryAdd(categoryName, 0);
                result[categoryName] += ToDisplay(txn.EffectiveSubtotalUSD, txn.Date);
            }
        }

        return result;
    }

    #region Income Statement

    /// <summary>
    /// Generates Income Statement data showing revenue, expenses, and net income.
    /// </summary>
    private AccountingTableData GetIncomeStatementData()
    {
        var t = GetAccountingTerms();
        var data = new AccountingTableData
        {
            Title = t.IncomeStatementTitle,
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [],
            ColumnWidthRatios = [0.65, 0.35]
        };

        if (companyData == null)
        {
            AddEmptyIncomeStatement(data, t);
            return data;
        }

        // Filter revenues and expenses by date
        var revenues = companyData.Revenues
            .Where(r => IsInDateRange(r.Date))
            .ToList();

        var expenses = companyData.Expenses
            .Where(e => IsInDateRange(e.Date))
            .ToList();

        var revenueByCategory = GroupTransactionsByCategory(revenues);
        var expenseByCategory = GroupTransactionsByCategory(expenses);

        // Refunds come off revenue on their own date, before tax (docs/Calculations.md §8).
        var refunds = companyData.Payments
            .Where(p => p.IsRefund && IsInDateRange(p.Date))
            .Sum(p => ToDisplay(RefundAggregator.PreTaxPortionUSD(p, InvoicesById), p.Date));

        var totalRevenue = revenueByCategory.Values.Sum() - refunds;
        var totalExpenses = expenseByCategory.Values.Sum();
        var netIncome = totalRevenue - totalExpenses;

        // Revenue section
        data.Rows.Add(new AccountingRow
        {
            Label = t.Revenue,
            RowType = AccountingRowType.SectionHeader,
            Values = ["Amount"]
        });

        foreach (var kvp in revenueByCategory.OrderBy(k => k.Key))
        {
            data.Rows.Add(new AccountingRow
            {
                Label = kvp.Key,
                Values = [FormatCurrency(kvp.Value)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        if (refunds != 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Refunds",
                Values = [FormatCurrencyWithSign(-refunds)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = t.TotalRevenue,
            Values = [FormatCurrencyWithSign(totalRevenue)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Expenses section
        data.Rows.Add(new AccountingRow
        {
            Label = t.OperatingExpenses,
            RowType = AccountingRowType.SectionHeader,
            Values = [""]
        });

        foreach (var kvp in expenseByCategory.OrderBy(k => k.Key))
        {
            data.Rows.Add(new AccountingRow
            {
                Label = kvp.Key,
                Values = [FormatCurrency(kvp.Value)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = t.TotalOperatingExpenses,
            Values = [FormatCurrency(totalExpenses)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Net Income
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = t.NetIncome,
            Values = [FormatCurrencyWithSign(netIncome)],
            RowType = AccountingRowType.GrandTotalRow
        });

        return data;
    }

    private void AddEmptyIncomeStatement(AccountingTableData data, AccountingTerms t)
    {
        data.Rows.Add(new AccountingRow { Label = t.Revenue, RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.TotalRevenue, Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.OperatingExpenses, RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.TotalOperatingExpenses, Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.NetIncome, Values = [FormatCurrency(0)], RowType = AccountingRowType.GrandTotalRow });
    }

    #endregion

    #region Balance Sheet

    /// <summary>
    /// Generates Balance Sheet data showing assets, liabilities, and equity.
    /// </summary>
    private AccountingTableData GetBalanceSheetData()
    {
        var t = GetAccountingTerms();
        var data = new AccountingTableData
        {
            Title = t.BalanceSheetTitle,
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [],
            ColumnWidthRatios = [0.65, 0.35],
            Footnote = "Cash balance estimated from recorded transactions. "
                + "Inventory valued at each item's current unit cost (cost "
                + "history is not tracked) applied to stock levels "
                + "reconstructed as of the report date."
        };

        if (companyData == null)
        {
            AddEmptyBalanceSheet(data, t);
            return data;
        }

        // Cash = Revenue (Paid, no invoice) + Payments - Expenses, all filtered by date.
        // Uses post-tax (total) amounts because cash includes tax collected/paid.
        // Each component is converted at its OWN date, then combined (a derived figure: do not
        // convert the combined result). See docs/Calculations.md §3a Phase 2.
        var cashFromRevenue = companyData.Revenues
            .Where(r => RevenueAggregator.IsCollected(r)
                        && string.IsNullOrEmpty(r.InvoiceId)
                        && IsOnOrBeforeEndDate(r.Date))
            .Sum(r => ToDisplay(r.EffectiveTotalUSD, r.Date));

        // Everything except revenue-linked, which is already counted by its own Revenue row.
        // Tested on RevenueId rather than on HAVING an InvoiceId: an imported payment whose
        // invoice was not in the file has neither id, and is still real cash.
        var cashFromPayments = companyData.Payments
            .Where(p => string.IsNullOrEmpty(p.RevenueId) && IsOnOrBeforeEndDate(p.Date))
            .Sum(p => ToDisplay(p.EffectiveAmountUSD, p.Date));

        var cashPaidForExpenses = companyData.Expenses
            .Where(e => IsOnOrBeforeEndDate(e.Date))
            .Sum(e => ToDisplay(e.EffectiveTotalUSD, e.Date));

        var cash = cashFromRevenue + cashFromPayments - cashPaidForExpenses;

        // Accounts Receivable = what was still owed at the end date, each balance converted at the
        // invoice's issue date.
        var accountsReceivable = GetReceivablesAsOfEndDate()
            .Sum(r => ToDisplay(r.BalanceUSD, r.Invoice.IssueDate));

        // Inventory valued at current unit cost, using stock levels
        // reconstructed as of the report end date. See docs/Calculations.md §10.
        // A point-in-time valuation: convert the USD value at the end date.
        var inventoryValue = ToDisplay(
            InventoryValuationService.TotalValueAsOf(companyData, EndDateForValuation),
            EndDateForValuation);

        var totalCurrentAssets = cash + accountsReceivable + inventoryValue;
        var totalAssets = totalCurrentAssets;

        // Accounts Payable = purchase orders not received and not cancelled, each converted at the
        // order date.
        var accountsPayable = companyData.PurchaseOrders
            .Where(po => po.Status != PurchaseOrderStatus.Received
                         && po.Status != PurchaseOrderStatus.Cancelled
                         && IsOnOrBeforeEndDate(po.OrderDate))
            .Sum(po => ToDisplay(po.EffectiveTotalUSD, po.OrderDate));

        // Sales Tax Payable = tax collected on all revenue, less tax handed back on refunds
        // (docs/Calculations.md §8), minus input tax credits from expenses.
        // Components converted at each transaction's own date, then combined (derived figure).
        var taxCollected = companyData.Revenues
            .Where(r => IsOnOrBeforeEndDate(r.Date))
            .Sum(r => ToDisplay(r.EffectiveTotalUSD - r.EffectiveSubtotalUSD, r.Date));
        var taxRefunded = companyData.Payments
            .Where(p => p.IsRefund && IsOnOrBeforeEndDate(p.Date))
            .Sum(p => ToDisplay(RefundAggregator.TaxPortionUSD(p, InvoicesById), p.Date));
        var taxPaidOnExpenses = companyData.Expenses
            .Where(e => IsOnOrBeforeEndDate(e.Date))
            .Sum(e => ToDisplay(e.EffectiveTotalUSD - e.EffectiveSubtotalUSD, e.Date));
        var salesTaxPayable = taxCollected - taxRefunded - taxPaidOnExpenses;

        // A security deposit isn't earned (docs/Calculations.md §4), but the invoice total carrying it
        // is in cash or receivables from the day it is issued, so it is owed back until it is given
        // back or kept. Priced at the invoice's rate, as its refunds and kept deposit are.
        var paymentsToDate = companyData.Payments.Where(p => IsOnOrBeforeEndDate(p.Date)).ToList();
        var revenuesToDate = companyData.Revenues.Where(r => IsOnOrBeforeEndDate(r.Date)).ToList();
        var securityDeposits = companyData.Invoices
            .Where(i => i.SecurityDeposit > 0 && i.Total > 0
                        && i.Status != InvoiceStatus.Cancelled
                        && i.Status != InvoiceStatus.Draft
                        && IsOnOrBeforeEndDate(i.IssueDate))
            .Sum(i => ToDisplay(
                SecurityDeposits.StillHeld(i, paymentsToDate, revenuesToDate) * i.EffectiveTotalUSD / i.Total,
                i.IssueDate));

        var totalLiabilities = accountsPayable + salesTaxPayable + securityDeposits;

        // Retained Earnings derived as balancing figure so Assets = Liabilities + Equity.
        // This is standard for simplified bookkeeping systems without full double-entry.
        var retainedEarnings = totalAssets - totalLiabilities;
        var totalEquity = retainedEarnings;

        // ASSETS
        data.Rows.Add(new AccountingRow
        {
            Label = "ASSETS",
            RowType = AccountingRowType.SectionHeader,
            Values = ["Amount"]
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Current Assets",
            RowType = AccountingRowType.SectionHeader,
            IndentLevel = 0,
            Values = [""]
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Cash (Estimated)",
            Values = [FormatCurrencyWithSign(cash)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = t.AccountsReceivable,
            Values = [FormatCurrency(accountsReceivable)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        if (companyData.Inventory.Count > 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Inventory",
                Values = [FormatCurrency(inventoryValue)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = "Total Current Assets",
            Values = [FormatCurrencyWithSign(totalCurrentAssets)],
            IndentLevel = 0,
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL ASSETS",
            Values = [FormatCurrencyWithSign(totalAssets)],
            RowType = AccountingRowType.TotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // LIABILITIES
        data.Rows.Add(new AccountingRow
        {
            Label = "LIABILITIES",
            RowType = AccountingRowType.SectionHeader,
            Values = [""]
        });

        data.Rows.Add(new AccountingRow
        {
            Label = t.AccountsPayable,
            Values = [FormatCurrency(accountsPayable)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        if (salesTaxPayable != 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = t.TaxPayableLabel,
                Values = [FormatCurrencyWithSign(salesTaxPayable)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        if (securityDeposits != 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Security Deposits",
                Values = [FormatCurrency(securityDeposits)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL LIABILITIES",
            Values = [FormatCurrencyWithSign(totalLiabilities)],
            RowType = AccountingRowType.TotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // EQUITY
        data.Rows.Add(new AccountingRow
        {
            Label = "EQUITY",
            RowType = AccountingRowType.SectionHeader,
            Values = [""]
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Retained Earnings",
            Values = [FormatCurrencyWithSign(retainedEarnings)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL EQUITY",
            Values = [FormatCurrencyWithSign(totalEquity)],
            RowType = AccountingRowType.TotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // TOTAL LIABILITIES & EQUITY
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL LIABILITIES & EQUITY",
            Values = [FormatCurrencyWithSign(totalLiabilities + totalEquity)],
            RowType = AccountingRowType.GrandTotalRow
        });

        return data;
    }

    private void AddEmptyBalanceSheet(AccountingTableData data, AccountingTerms t)
    {
        data.Rows.Add(new AccountingRow { Label = "ASSETS", RowType = AccountingRowType.SectionHeader, Values = ["Amount"] });
        data.Rows.Add(new AccountingRow { Label = "Current Assets", RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "Cash (Estimated)", Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = t.AccountsReceivable, Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "Total Current Assets", Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "TOTAL ASSETS", Values = [FormatCurrency(0)], RowType = AccountingRowType.TotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "LIABILITIES", RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.AccountsPayable, Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "TOTAL LIABILITIES", Values = [FormatCurrency(0)], RowType = AccountingRowType.TotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "EQUITY", RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "Retained Earnings", Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "TOTAL EQUITY", Values = [FormatCurrency(0)], RowType = AccountingRowType.TotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "TOTAL LIABILITIES & EQUITY", Values = [FormatCurrency(0)], RowType = AccountingRowType.GrandTotalRow });
    }

    #endregion

    #region Cash Flow Statement

    /// <summary>
    /// Generates Cash Flow Statement data showing operating activities.
    /// </summary>
    private AccountingTableData GetCashFlowData()
    {
        var data = new AccountingTableData
        {
            Title = "Cash Flow Statement",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [],
            ColumnWidthRatios = [0.65, 0.35]
        };

        if (companyData == null)
        {
            AddEmptyCashFlow(data);
            return data;
        }

        // Operating Activities
        // Exclude invoice-linked revenue to avoid double counting with Payments.
        // Uses post-tax (total) amounts because cash includes tax collected/paid,
        // consistent with Balance Sheet cash calculation.
        var cashFromSales = companyData.Revenues
            .Where(r => RevenueAggregator.IsCollected(r)
                        && string.IsNullOrEmpty(r.InvoiceId)
                        && IsInDateRange(r.Date))
            .Sum(r => ToDisplay(r.EffectiveTotalUSD, r.Date));

        // Everything except revenue-linked, matching cashFromSales above and the balance
        // sheet's cashFromPayments. See the note there on why this tests RevenueId.
        var cashFromInvoicePayments = companyData.Payments
            .Where(p => string.IsNullOrEmpty(p.RevenueId) && IsInDateRange(p.Date))
            .Sum(p => ToDisplay(p.EffectiveAmountUSD, p.Date));

        var cashPaidForExpenses = companyData.Expenses
            .Where(e => IsInDateRange(e.Date))
            .Sum(e => ToDisplay(e.EffectiveTotalUSD, e.Date));

        var totalOperating = cashFromSales + cashFromInvoicePayments - cashPaidForExpenses;

        var netChange = totalOperating;

        // Operating section
        data.Rows.Add(new AccountingRow
        {
            Label = "OPERATING ACTIVITIES",
            RowType = AccountingRowType.SectionHeader,
            Values = ["Amount"]
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Cash from Sales",
            Values = [FormatCurrency(cashFromSales)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Cash from Invoice Payments",
            Values = [FormatCurrency(cashFromInvoicePayments)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Cash Paid for Expenses",
            Values = [FormatCurrencyWithSign(-cashPaidForExpenses)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Net Cash from Operating Activities",
            Values = [FormatCurrencyWithSign(totalOperating)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Net change
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "NET CHANGE IN CASH",
            Values = [FormatCurrencyWithSign(netChange)],
            RowType = AccountingRowType.GrandTotalRow
        });

        return data;
    }

    private void AddEmptyCashFlow(AccountingTableData data)
    {
        data.Rows.Add(new AccountingRow { Label = "OPERATING ACTIVITIES", RowType = AccountingRowType.SectionHeader, Values = ["Amount"] });
        data.Rows.Add(new AccountingRow { Label = "Cash from Sales", Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "Cash from Invoice Payments", Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "Cash Paid for Expenses", Values = [FormatCurrency(0)], IndentLevel = 1, RowType = AccountingRowType.DataRow });
        data.Rows.Add(new AccountingRow { Label = "Net Cash from Operating Activities", Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = "NET CHANGE IN CASH", Values = [FormatCurrency(0)], RowType = AccountingRowType.GrandTotalRow });
    }

    #endregion

    #region General Ledger

    /// <summary>
    /// Generates General Ledger data showing all transactions chronologically, grouped by category.
    /// </summary>
    private AccountingTableData GetGeneralLedgerData()
    {
        var t = GetAccountingTerms();
        var data = new AccountingTableData
        {
            Title = "General Ledger",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Date", "Description", "Ref", "Debit", "Credit", "Balance"],
            ColumnWidthRatios = [0.12, 0.3, 0.14, 0.14, 0.14, 0.16]
        };

        if (companyData == null)
            return data;

        // Build a list of all ledger entries grouped by category
        var entries = new Dictionary<string, List<LedgerEntry>>();

        // Revenue transactions (credits), all amounts in USD
        foreach (var rev in companyData.Revenues.Where(r => IsInDateRange(r.Date)))
        {
            var lineItemsTotal = rev.LineItems.Sum(li => li.Subtotal);
            if (rev.LineItems.Count > 0 && lineItemsTotal != 0)
            {
                var subtotalUSD = rev.EffectiveSubtotalUSD;

                foreach (var li in rev.LineItems)
                {
                    var catName = GetCategoryNameForProduct(li.ProductId);
                    var lineItemUSD = Math.Round(li.Subtotal / lineItemsTotal * subtotalUSD, 2);
                    AddLedgerEntry(entries, catName, new LedgerEntry
                    {
                        Date = rev.Date,
                        Description = li.Description.Length > 0 ? li.Description : rev.Description,
                        Reference = rev.Id,
                        Debit = 0,
                        Credit = ToDisplay(lineItemUSD, rev.Date)
                    });
                }
            }
            else
            {
                // No line items, or every line item nets to a zero subtotal (e.g. a 100% discount):
                // post the transaction-level amount so the entry is not dropped from the ledger.
                AddLedgerEntry(entries, t.RevenueCategory, new LedgerEntry
                {
                    Date = rev.Date,
                    Description = rev.Description,
                    Reference = rev.Id,
                    Debit = 0,
                    Credit = ToDisplay(rev.EffectiveSubtotalUSD, rev.Date)
                });
            }
        }

        // Expense transactions (debits), all amounts in USD
        foreach (var exp in companyData.Expenses.Where(e => IsInDateRange(e.Date)))
        {
            var lineItemsTotal = exp.LineItems.Sum(li => li.Subtotal);
            if (exp.LineItems.Count > 0 && lineItemsTotal != 0)
            {
                var subtotalUSD = exp.EffectiveSubtotalUSD;

                foreach (var li in exp.LineItems)
                {
                    var catName = GetCategoryNameForProduct(li.ProductId);
                    var lineItemUSD = Math.Round(li.Subtotal / lineItemsTotal * subtotalUSD, 2);
                    AddLedgerEntry(entries, catName, new LedgerEntry
                    {
                        Date = exp.Date,
                        Description = li.Description.Length > 0 ? li.Description : exp.Description,
                        Reference = exp.Id,
                        Debit = ToDisplay(lineItemUSD, exp.Date),
                        Credit = 0
                    });
                }
            }
            else
            {
                AddLedgerEntry(entries, t.ExpensesCategory, new LedgerEntry
                {
                    Date = exp.Date,
                    Description = exp.Description,
                    Reference = exp.Id,
                    Debit = ToDisplay(exp.EffectiveSubtotalUSD, exp.Date),
                    Credit = 0
                });
            }
        }

        // Payments (credits to AR / debits to cash). Refunds are stored as negative payments; route
        // them to the Credit column as a positive value so the amount is visible (a negative Debit
        // renders blank), while the running balance (Debit - Credit) stays identical.
        foreach (var pmt in companyData.Payments.Where(p => IsInDateRange(p.Date)))
        {
            var customerName = companyData.GetCustomer(pmt.CustomerId)?.Name ?? "Unknown";
            var amount = ToDisplay(pmt.EffectiveAmountUSD, pmt.Date);
            AddLedgerEntry(entries, t.PaymentsReceivedCategory, new LedgerEntry
            {
                Date = pmt.Date,
                Description = $"Payment from {customerName}",
                Reference = pmt.Id,
                Debit = pmt.IsRefund ? 0 : amount,
                Credit = pmt.IsRefund ? -amount : 0
            });

            // A refund also comes off revenue on its own date, before tax (docs/Calculations.md §8).
            if (pmt.IsRefund)
            {
                AddLedgerEntry(entries, t.RevenueCategory, new LedgerEntry
                {
                    Date = pmt.Date,
                    Description = $"Refund to {customerName}",
                    Reference = pmt.Id,
                    Debit = ToDisplay(RefundAggregator.PreTaxPortionUSD(pmt, InvoicesById), pmt.Date),
                    Credit = 0
                });
            }
        }

        // Render grouped entries
        foreach (var group in entries.OrderBy(g => g.Key))
        {
            data.Rows.Add(new AccountingRow
            {
                Label = group.Key.ToUpperInvariant(),
                RowType = AccountingRowType.SectionHeader,
                Values = ["", "", "", "", ""]
            });

            var sortedEntries = group.Value.OrderBy(e => e.Date).ToList();
            var runningBalance = 0m;

            foreach (var entry in sortedEntries)
            {
                runningBalance += entry.Debit - entry.Credit;

                data.Rows.Add(new AccountingRow
                {
                    Label = entry.Date.ToString("MMM dd, yyyy"),
                    Values =
                    [
                        entry.Description,
                        entry.Reference,
                        entry.Debit > 0 ? FormatCurrency(entry.Debit) : "",
                        entry.Credit > 0 ? FormatCurrency(entry.Credit) : "",
                        FormatCurrencyWithSign(runningBalance)
                    ],
                    RowType = AccountingRowType.DataRow
                });
            }

            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = ["", "", "", "", ""] });
        }

        return data;
    }

    /// <summary>
    /// Helper to add a ledger entry to the grouped dictionary.
    /// </summary>
    private static void AddLedgerEntry(Dictionary<string, List<LedgerEntry>> entries, string category, LedgerEntry entry)
    {
        if (!entries.ContainsKey(category))
            entries[category] = [];
        entries[category].Add(entry);
    }

    /// <summary>
    /// Internal record for building general ledger data.
    /// </summary>
    private class LedgerEntry
    {
        public DateTime Date { get; init; }
        public string Description { get; init; } = "";
        public string Reference { get; init; } = "";
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
    }

    #endregion

    #region Sales by Product

    /// <summary>
    /// Generates Sales by Product data: units, gross revenue, and average sale
    /// price per product, ranked by revenue. Uses accrual basis (all invoiced
    /// revenue) to match the other formal reports. See docs/Calculations.md §13.
    /// </summary>
    private AccountingTableData GetProductSalesData()
    {
        var data = new AccountingTableData
        {
            Title = "Sales by Product",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Product", "Units", "Revenue", "Avg price"],
            ColumnWidthRatios = [0.42, 0.16, 0.22, 0.20]
        };

        if (companyData == null)
            return data;

        // Pass ToDisplay so each line item is converted at its transaction's own date
        // (Calculations.md §3a Phase 2); the returned RevenueUSD/AvgSalePriceUSD are then already
        // in DisplayCode (USD identity for the USD-company/fallback path).
        var products = ProductSalesService.GetProductSales(
            companyData,
            filters.StartDate ?? DateTime.MinValue,
            filters.EndDate ?? DateTime.MaxValue,
            cashBasis: false,
            toDisplay: ToDisplay);

        if (products.Count == 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "No product sales in this period",
                RowType = AccountingRowType.DataRow,
                Values = ["", "", ""]
            });
            return data;
        }

        foreach (var p in products)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = p.ProductName,
                Values =
                [
                    FormatUnits(p.UnitsSold),
                    FormatCurrency(p.RevenueUSD),
                    FormatCurrency(p.AvgSalePriceUSD)
                ],
                RowType = AccountingRowType.DataRow
            });
        }

        var totalUnits = products.Sum(p => p.UnitsSold);
        var totalRevenue = products.Sum(p => p.RevenueUSD);
        var overallAvg = totalUnits > 0 ? totalRevenue / totalUnits : 0;

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", ""] });
        data.Rows.Add(new AccountingRow
        {
            Label = "Total",
            Values =
            [
                FormatUnits(totalUnits),
                FormatCurrency(totalRevenue),
                FormatCurrency(overallAvg)
            ],
            RowType = AccountingRowType.GrandTotalRow
        });

        return data;
    }

    private static string FormatUnits(decimal units) => units.ToString("0.##");

    #endregion

    #region Accounts Receivable Aging

    /// <summary>
    /// Generates Accounts Receivable Aging data showing outstanding invoices grouped by customer and aging bucket.
    /// </summary>
    private AccountingTableData GetARAgingData()
    {
        var t = GetAccountingTerms();
        var data = new AccountingTableData
        {
            Title = t.ARAgingTitle,
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [t.CustomerColumn, "Current", "1-30 Days", "31-60 Days", "61-90 Days", "90+ Days", "Total"],
            ColumnWidthRatios = [0.25, 0.125, 0.125, 0.125, 0.125, 0.125, 0.125]
        };

        if (companyData == null)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "TOTAL",
                Values = [FormatCurrency(0), FormatCurrency(0), FormatCurrency(0), FormatCurrency(0), FormatCurrency(0), FormatCurrency(0)],
                RowType = AccountingRowType.TotalRow
            });
            return data;
        }

        // Age receivables "as of" the report's end date (matching the Balance Sheet), not today.
        var asOf = EndDateForValuation;

        // The same set and balances the Balance Sheet's Accounts Receivable uses.
        var byCustomer = GetReceivablesAsOfEndDate()
            .GroupBy(r => r.Invoice.CustomerId)
            .OrderBy(g => companyData.GetCustomer(g.Key)?.Name ?? "Unknown");

        var totalCurrent = 0m;
        var total1to30 = 0m;
        var total31to60 = 0m;
        var total61to90 = 0m;
        var total90Plus = 0m;
        var grandTotal = 0m;

        foreach (var group in byCustomer)
        {
            var customerName = companyData.GetCustomer(group.Key)?.Name ?? "Unknown";
            var current = 0m;
            var days1to30 = 0m;
            var days31to60 = 0m;
            var days61to90 = 0m;
            var days90Plus = 0m;

            foreach (var (invoice, balanceUSD) in group)
            {
                var daysPastDue = (asOf - invoice.DueDate.Date).Days;
                // Convert each open invoice's balance at its issue date (Calculations.md §3a Phase 2).
                var balance = ToDisplay(balanceUSD, invoice.IssueDate);

                if (daysPastDue <= 0)
                    current += balance;
                else if (daysPastDue <= 30)
                    days1to30 += balance;
                else if (daysPastDue <= 60)
                    days31to60 += balance;
                else if (daysPastDue <= 90)
                    days61to90 += balance;
                else
                    days90Plus += balance;
            }

            var customerTotal = current + days1to30 + days31to60 + days61to90 + days90Plus;

            data.Rows.Add(new AccountingRow
            {
                Label = customerName,
                Values =
                [
                    FormatCurrency(current),
                    FormatCurrency(days1to30),
                    FormatCurrency(days31to60),
                    FormatCurrency(days61to90),
                    FormatCurrency(days90Plus),
                    FormatCurrency(customerTotal)
                ],
                RowType = AccountingRowType.DataRow
            });

            totalCurrent += current;
            total1to30 += days1to30;
            total31to60 += days31to60;
            total61to90 += days61to90;
            total90Plus += days90Plus;
            grandTotal += customerTotal;
        }

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", "", "", ""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL",
            Values =
            [
                FormatCurrency(totalCurrent),
                FormatCurrency(total1to30),
                FormatCurrency(total31to60),
                FormatCurrency(total61to90),
                FormatCurrency(total90Plus),
                FormatCurrency(grandTotal)
            ],
            RowType = AccountingRowType.TotalRow
        });

        return data;
    }

    #endregion

    #region Country-Specific Accounting Terminology

    /// <summary>
    /// Holds all country-specific labels used across accounting reports.
    /// Defaults are US GAAP terminology.
    /// </summary>
    private class AccountingTerms
    {
        // Income Statement
        public string IncomeStatementTitle { get; set; } = "Income Statement";
        public string Revenue { get; set; } = "REVENUE";
        public string TotalRevenue { get; set; } = "Total Revenue";
        public string OperatingExpenses { get; set; } = "OPERATING EXPENSES";
        public string TotalOperatingExpenses { get; set; } = "Total Operating Expenses";
        public string NetIncome { get; set; } = "NET INCOME";

        // Balance Sheet
        public string BalanceSheetTitle { get; set; } = "Balance Sheet";
        public string AccountsReceivable { get; set; } = "Accounts Receivable";
        public string AccountsPayable { get; set; } = "Accounts Payable";

        public string ARAgingTitle { get; set; } = "Accounts Receivable Aging";
        public string CustomerColumn { get; set; } = "Customer";

        // General Ledger
        public string RevenueCategory { get; set; } = "Revenue";
        public string ExpensesCategory { get; set; } = "Expenses";
        public string PaymentsReceivedCategory { get; set; } = "Payments Received";

        // Balance Sheet
        public string TaxPayableLabel { get; set; } = "Sales Tax Payable";

        // Tax Summary
        public string TaxCollectedHeader { get; set; } = "TAX COLLECTED";
        public string TaxCollectedLineFormat { get; set; } = "Tax Collected at {0}%";
        public string TaxCollectedTotal { get; set; } = "Total Tax Collected";
        public string TaxPaidHeader { get; set; } = "TAX PAID";
        public string TaxPaidLineFormat { get; set; } = "Tax Paid at {0}%";
        public string TaxPaidTotal { get; set; } = "Total Tax Paid";
        public string NetTaxLabel { get; set; } = "NET TAX LIABILITY";
    }

    /// <summary>
    /// Determines the accounting tradition based on the company's country and
    /// returns all report labels using the appropriate terminology.
    /// Three traditions: US GAAP (Americas), UK (Commonwealth), IFRS (EU + rest of world).
    /// </summary>
    private AccountingTerms GetAccountingTerms()
    {
        var country = companyData?.Settings.Company.Country;
        var normalized = country?.Trim().ToUpperInvariant() ?? "";

        // Determine accounting tradition from country
        var tradition = normalized switch
        {
            "UNITED STATES" or "CANADA" or "PUERTO RICO" => AccountingTradition.US,

            "UNITED KINGDOM" or "IRELAND" or "AUSTRALIA" or "NEW ZEALAND"
            or "SOUTH AFRICA" or "INDIA" or "SINGAPORE" or "MALAYSIA"
            or "HONG KONG" or "KENYA" or "NIGERIA" or "GHANA" or "PAKISTAN"
            or "BANGLADESH" or "SRI LANKA" or "ZIMBABWE" or "BOTSWANA"
            or "JAMAICA" or "TRINIDAD AND TOBAGO" => AccountingTradition.UK,

            "FRANCE" or "GERMANY" or "ITALY" or "SPAIN" or "NETHERLANDS"
            or "BELGIUM" or "AUSTRIA" or "SWEDEN" or "NORWAY" or "DENMARK"
            or "FINLAND" or "PORTUGAL" or "GREECE" or "SWITZERLAND" or "POLAND"
            or "CZECH REPUBLIC" or "CZECHIA" or "HUNGARY" or "ROMANIA" or "BULGARIA" or "CROATIA"
            or "SLOVAKIA" or "SLOVENIA" or "LITHUANIA" or "LATVIA" or "ESTONIA"
            or "LUXEMBOURG" or "MALTA" or "CYPRUS" or "ICELAND"
            or "TURKEY" or "RUSSIA" or "UKRAINE" or "BRAZIL" or "ARGENTINA"
            or "CHILE" or "COLOMBIA" or "MEXICO" or "PERU"
            or "ISRAEL" or "UNITED ARAB EMIRATES" or "SAUDI ARABIA"
            or "JAPAN" or "SOUTH KOREA" or "CHINA" or "TAIWAN"
            or "THAILAND" or "VIETNAM" or "INDONESIA" or "PHILIPPINES" => AccountingTradition.IFRS,

            _ => AccountingTradition.US
        };

        // Determine tax system (orthogonal to accounting tradition)
        var taxSystem = normalized switch
        {
            "UNITED KINGDOM" or "FRANCE" or "GERMANY" or "ITALY" or "SPAIN" or "NETHERLANDS"
            or "BELGIUM" or "AUSTRIA" or "SWEDEN" or "NORWAY" or "DENMARK" or "FINLAND"
            or "IRELAND" or "PORTUGAL" or "GREECE" or "SWITZERLAND" or "POLAND"
            or "CZECH REPUBLIC" or "CZECHIA" or "HUNGARY" or "ROMANIA" or "BULGARIA" or "CROATIA"
            or "SLOVAKIA" or "SLOVENIA" or "LITHUANIA" or "LATVIA" or "ESTONIA"
            or "LUXEMBOURG" or "MALTA" or "CYPRUS" or "SOUTH AFRICA" or "KENYA"
            or "NIGERIA" or "GHANA" or "ZIMBABWE" or "BOTSWANA"
            or "BANGLADESH" or "SRI LANKA" or "JAMAICA" or "TRINIDAD AND TOBAGO"
            or "TURKEY" or "RUSSIA" or "UKRAINE" or "BRAZIL"
            or "ARGENTINA" or "CHILE" or "COLOMBIA" or "MEXICO" or "PERU"
            or "ISRAEL" or "UNITED ARAB EMIRATES" or "SAUDI ARABIA" or "THAILAND"
            or "VIETNAM" or "INDONESIA" or "PHILIPPINES" or "SOUTH KOREA"
            or "CHINA" or "TAIWAN" or "ICELAND" => TaxSystem.VAT,

            "CANADA" => TaxSystem.GstHst,
            "INDIA" or "SINGAPORE" or "MALAYSIA" or "AUSTRALIA" or "NEW ZEALAND"
            or "PAKISTAN" => TaxSystem.GST,
            "JAPAN" => TaxSystem.JCT,
            "UNITED STATES" or "PUERTO RICO" => TaxSystem.SalesTax,

            _ => TaxSystem.Tax
        };

        var terms = new AccountingTerms();

        // Apply accounting tradition overrides
        switch (tradition)
        {
            case AccountingTradition.UK:
                terms.IncomeStatementTitle = "Profit & Loss";
                terms.Revenue = "TURNOVER";
                terms.TotalRevenue = "Total Turnover";
                terms.OperatingExpenses = "OVERHEADS";
                terms.TotalOperatingExpenses = "Total Overheads";
                terms.NetIncome = "NET PROFIT";
                terms.AccountsReceivable = "Trade Debtors";
                terms.AccountsPayable = "Trade Creditors";
                terms.ARAgingTitle = "Trade Debtors Aging";
                terms.RevenueCategory = "Turnover";
                break;

            case AccountingTradition.IFRS:
                terms.NetIncome = "NET PROFIT";
                terms.AccountsReceivable = "Trade Receivables";
                terms.AccountsPayable = "Trade Payables";
                terms.ARAgingTitle = "Trade Receivables Aging";
                break;
        }

        // Apply tax system terminology
        switch (taxSystem)
        {
            case TaxSystem.VAT:
                terms.TaxPayableLabel = "VAT Payable";
                terms.TaxCollectedHeader = "VAT COLLECTED";
                terms.TaxCollectedLineFormat = "VAT Collected at {0}%";
                terms.TaxCollectedTotal = "Total VAT Collected";
                terms.TaxPaidHeader = "VAT PAID (INPUT VAT)";
                terms.TaxPaidLineFormat = "Input VAT at {0}%";
                terms.TaxPaidTotal = "Total Input VAT";
                terms.NetTaxLabel = "NET VAT PAYABLE";
                break;
            case TaxSystem.GstHst:
                terms.TaxPayableLabel = "GST/HST Payable";
                terms.TaxCollectedHeader = "GST/HST COLLECTED";
                terms.TaxCollectedLineFormat = "GST/HST Collected at {0}%";
                terms.TaxCollectedTotal = "Total GST/HST Collected";
                terms.TaxPaidHeader = "GST/HST PAID (INPUT TAX CREDITS)";
                terms.TaxPaidLineFormat = "ITC at {0}%";
                terms.TaxPaidTotal = "Total Input Tax Credits";
                terms.NetTaxLabel = "NET GST/HST PAYABLE";
                break;
            case TaxSystem.GST:
                terms.TaxPayableLabel = "GST Payable";
                terms.TaxCollectedHeader = "GST COLLECTED";
                terms.TaxCollectedLineFormat = "GST Collected at {0}%";
                terms.TaxCollectedTotal = "Total GST Collected";
                terms.TaxPaidHeader = "GST PAID (INPUT TAX CREDITS)";
                terms.TaxPaidLineFormat = "Input Tax Credit at {0}%";
                terms.TaxPaidTotal = "Total Input Tax Credits";
                terms.NetTaxLabel = "NET GST PAYABLE";
                break;
            case TaxSystem.JCT:
                terms.TaxPayableLabel = "Consumption Tax Payable";
                terms.TaxCollectedHeader = "CONSUMPTION TAX COLLECTED";
                terms.TaxCollectedLineFormat = "Consumption Tax at {0}%";
                terms.TaxCollectedTotal = "Total Consumption Tax Collected";
                terms.TaxPaidHeader = "CONSUMPTION TAX PAID";
                terms.TaxPaidLineFormat = "Consumption Tax Paid at {0}%";
                terms.TaxPaidTotal = "Total Consumption Tax Paid";
                terms.NetTaxLabel = "NET CONSUMPTION TAX LIABILITY";
                break;
            case TaxSystem.SalesTax:
                terms.TaxCollectedHeader = "SALES TAX COLLECTED";
                terms.TaxCollectedLineFormat = "Sales Tax Collected at {0}%";
                terms.TaxCollectedTotal = "Total Sales Tax Collected";
                terms.TaxPaidHeader = "SALES TAX PAID";
                terms.TaxPaidLineFormat = "Sales Tax Paid at {0}%";
                terms.TaxPaidTotal = "Total Sales Tax Paid";
                terms.NetTaxLabel = "NET SALES TAX LIABILITY";
                break;
        }

        return terms;
    }

    #endregion

    #region Tax Summary

    /// <summary>
    /// Generates Tax Summary data showing taxes collected, taxes paid, and net tax liability.
    /// </summary>
    private AccountingTableData GetTaxSummaryData()
    {
        var t = GetAccountingTerms();
        var data = new AccountingTableData
        {
            Title = "Tax Summary",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [],
            ColumnWidthRatios = [0.65, 0.35]
        };

        if (companyData == null)
        {
            AddEmptyTaxSummary(data, t);
            return data;
        }

        // Tax collected from revenue, grouped by tax rate
        // All amounts converted to USD for consistent cross-currency aggregation
        var filteredRevenues = companyData.Revenues
            .Where(r => IsInDateRange(r.Date))
            .ToList();

        // Round tax rates to 2 decimal places to consolidate near-identical rates
        var taxCollectedByRate = new Dictionary<decimal, decimal>();
        foreach (var rev in filteredRevenues)
        {
            var usdRatio = GetUSDRatio(rev);
            var anyLineItemTax = false;
            foreach (var li in rev.LineItems)
            {
                if (li.TaxRate > 0)
                {
                    anyLineItemTax = true;
                    var rate = Math.Round(li.TaxRate, 2);
                    taxCollectedByRate.TryAdd(rate, 0);
                    taxCollectedByRate[rate] +=
                        ToDisplay(Math.Round(li.TaxAmount * usdRatio, 2), rev.Date);
                }
            }

            // Fall back to the transaction-level tax when no line item carried a rate. Manually-entered
            // transactions always have a line item (with TaxRate 0) but record their tax at the
            // transaction level, so without this their collected tax would be omitted entirely.
            if (!anyLineItemTax && rev.TaxRate > 0)
            {
                // Transaction.TaxRate is stored as a percentage (e.g., 8 for 8%); convert to decimal
                // form (0.08) to match LineItem.TaxRate for consistent grouping.
                var rate = Math.Round(rev.TaxRate / 100m, 4);
                taxCollectedByRate.TryAdd(rate, 0);
                taxCollectedByRate[rate] += ToDisplay(rev.EffectiveTaxAmountUSD, rev.Date);
            }
        }

        // Tax paid on expenses, grouped by tax rate
        // All amounts converted to USD for consistent cross-currency aggregation
        var filteredExpenses = companyData.Expenses
            .Where(e => IsInDateRange(e.Date))
            .ToList();

        var taxPaidByRate = new Dictionary<decimal, decimal>();
        foreach (var exp in filteredExpenses)
        {
            var usdRatio = GetUSDRatio(exp);
            var anyLineItemTax = false;
            foreach (var li in exp.LineItems)
            {
                if (li.TaxRate > 0)
                {
                    anyLineItemTax = true;
                    var rate = Math.Round(li.TaxRate, 2);
                    taxPaidByRate.TryAdd(rate, 0);
                    taxPaidByRate[rate] +=
                        ToDisplay(Math.Round(li.TaxAmount * usdRatio, 2), exp.Date);
                }
            }

            // Fall back to the transaction-level tax when no line item carried a rate (see the
            // matching revenue loop above for why manually-entered transactions need this).
            if (!anyLineItemTax && exp.TaxRate > 0)
            {
                // Transaction.TaxRate is stored as a percentage (e.g., 8 for 8%); convert to decimal
                // form (0.08) to match LineItem.TaxRate for consistent grouping.
                var rate = Math.Round(exp.TaxRate / 100m, 4);
                taxPaidByRate.TryAdd(rate, 0);
                taxPaidByRate[rate] += ToDisplay(exp.EffectiveTaxAmountUSD, exp.Date);
            }
        }

        // Tax handed back on refunds in range is no longer owed (docs/Calculations.md §8).
        var taxRefunded = companyData.Payments
            .Where(p => p.IsRefund && IsInDateRange(p.Date))
            .Sum(p => ToDisplay(RefundAggregator.TaxPortionUSD(p, InvoicesById), p.Date));

        var totalTaxCollected = taxCollectedByRate.Values.Sum() - taxRefunded;
        var totalTaxPaid = taxPaidByRate.Values.Sum();
        var netTaxLiability = totalTaxCollected - totalTaxPaid;

        // Tax Collected section
        data.Rows.Add(new AccountingRow
        {
            Label = t.TaxCollectedHeader,
            RowType = AccountingRowType.SectionHeader,
            Values = ["Amount"]
        });

        foreach (var kvp in taxCollectedByRate.OrderBy(k => k.Key))
        {
            var ratePercent = kvp.Key * 100;
            data.Rows.Add(new AccountingRow
            {
                Label = string.Format(t.TaxCollectedLineFormat, ratePercent.ToString("0.##")),
                Values = [FormatCurrency(kvp.Value)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        if (taxRefunded != 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Less tax on refunds",
                Values = [FormatCurrencyWithSign(-taxRefunded)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = t.TaxCollectedTotal,
            Values = [FormatCurrencyWithSign(totalTaxCollected)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Tax Paid section
        data.Rows.Add(new AccountingRow
        {
            Label = t.TaxPaidHeader,
            RowType = AccountingRowType.SectionHeader,
            Values = [""]
        });

        foreach (var kvp in taxPaidByRate.OrderBy(k => k.Key))
        {
            var ratePercent = kvp.Key * 100;
            data.Rows.Add(new AccountingRow
            {
                Label = string.Format(t.TaxPaidLineFormat, ratePercent.ToString("0.##")),
                Values = [FormatCurrency(kvp.Value)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = t.TaxPaidTotal,
            Values = [FormatCurrency(totalTaxPaid)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Net Tax Liability
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = t.NetTaxLabel,
            Values = [FormatCurrencyWithSign(netTaxLiability)],
            RowType = AccountingRowType.GrandTotalRow
        });

        return data;
    }

    /// <summary>
    /// What is owed for the pay dates in range: everything withheld from employees, plus the
    /// employer's own contributions.
    ///
    /// Split by who collects it, because for a Quebec employer that is two agencies and two
    /// payments. Revenu Quebec collects Quebec income tax, QPP and QPIP; CRA collects federal
    /// income tax and EI. Totalling them together produced a single figure that was owed to
    /// nobody: too much to CRA by the whole Quebec side, and no mention at all of the payment
    /// Revenu Quebec was waiting for. The section only appears when there is a Quebec employee,
    /// so an employer outside Quebec sees exactly what they saw before.
    ///
    /// Everything except drafts. A draft has not happened yet. A voided run still counts,
    /// because its reversal counts too and the pair nets to zero; excluding the voided run
    /// while keeping its reversal would subtract the same payroll twice.
    ///
    /// No currency conversion here, unlike the other reports. Payroll is Canadian by
    /// definition and the deductions are always in Canadian dollars.
    /// </summary>
    private AccountingTableData GetPayrollRemittanceData()
    {
        var data = new AccountingTableData
        {
            Title = "Payroll Remittance",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = [],
            ColumnWidthRatios = [0.65, 0.35]
        };

        var runs = companyData?.PayRuns
            .Where(r => r.Status != Models.Payroll.PayRunStatus.Draft && IsInDateRange(r.PayDate))
            .ToList() ?? [];

        var lines = runs.SelectMany(r => r.Lines).ToList();

        // The province is stored on the line rather than read from the employee, so a run keeps
        // reporting to the right agency after someone transfers between provinces.
        var quebec = lines.Where(IsQuebecLine).ToList();
        var rest = lines.Where(l => !IsQuebecLine(l)).ToList();

        // CRA. Federal tax from everybody, provincial tax from everybody except Quebec, all of
        // EI, and CPP from outside Quebec only: a Quebec employee's pension money is QPP.
        decimal craTax = lines.Sum(l => l.FederalTax) + rest.Sum(l => l.ProvincialTax);
        decimal cppEmployee = rest.Sum(l => l.CppEmployee) + rest.Sum(l => l.Cpp2Employee);
        decimal cppEmployer = rest.Sum(l => l.CppEmployer) + rest.Sum(l => l.Cpp2Employer);
        decimal eiEmployee = lines.Sum(l => l.EiEmployee);
        decimal eiEmployer = lines.Sum(l => l.EiEmployer);
        decimal craTotal = craTax + cppEmployee + cppEmployer + eiEmployee + eiEmployer;

        // Revenu Quebec.
        decimal quebecTax = quebec.Sum(l => l.ProvincialTax);
        decimal qppEmployee = quebec.Sum(l => l.CppEmployee) + quebec.Sum(l => l.Cpp2Employee);
        decimal qppEmployer = quebec.Sum(l => l.CppEmployer) + quebec.Sum(l => l.Cpp2Employer);
        decimal qpipEmployee = lines.Sum(l => l.QpipEmployee);
        decimal qpipEmployer = lines.Sum(l => l.QpipEmployer);
        decimal quebecTotal = quebecTax + qppEmployee + qppEmployer + qpipEmployee + qpipEmployer;

        bool hasQuebec = quebec.Count > 0 || qpipEmployee != 0m || qpipEmployer != 0m;

        data.Rows.Add(new AccountingRow
        {
            Label = hasQuebec ? "Canada Revenue Agency - withheld from employees" : "Withheld from employees",
            RowType = AccountingRowType.SectionHeader,
            Values = ["Amount"]
        });
        data.Rows.Add(Row("Income tax", craTax));
        data.Rows.Add(Row("CPP", cppEmployee));
        data.Rows.Add(Row("EI", eiEmployee));
        data.Rows.Add(new AccountingRow
        {
            Label = "Total withheld",
            Values = [FormatCurrency(craTax + cppEmployee + eiEmployee)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = hasQuebec ? "Canada Revenue Agency - employer contributions" : "Employer contributions",
            RowType = AccountingRowType.SectionHeader,
            Values = [""]
        });
        data.Rows.Add(Row("CPP", cppEmployer));
        data.Rows.Add(Row("EI", eiEmployer));
        data.Rows.Add(new AccountingRow
        {
            Label = "Total employer",
            Values = [FormatCurrency(cppEmployer + eiEmployer)],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = hasQuebec ? "Total to remit to CRA" : "Total to remit",
            Values = [FormatCurrency(craTotal)],
            RowType = AccountingRowType.GrandTotalRow
        });

        if (hasQuebec)
        {
            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
            data.Rows.Add(new AccountingRow
            {
                Label = "Revenu Quebec - withheld from employees",
                RowType = AccountingRowType.SectionHeader,
                Values = [""]
            });
            data.Rows.Add(Row("Quebec income tax", quebecTax));
            data.Rows.Add(Row("QPP", qppEmployee));
            data.Rows.Add(Row("QPIP", qpipEmployee));
            data.Rows.Add(new AccountingRow
            {
                Label = "Total withheld",
                Values = [FormatCurrency(quebecTax + qppEmployee + qpipEmployee)],
                RowType = AccountingRowType.SubtotalRow
            });

            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
            data.Rows.Add(new AccountingRow
            {
                Label = "Revenu Quebec - employer contributions",
                RowType = AccountingRowType.SectionHeader,
                Values = [""]
            });
            data.Rows.Add(Row("QPP", qppEmployer));
            data.Rows.Add(Row("QPIP", qpipEmployer));
            data.Rows.Add(new AccountingRow
            {
                Label = "Total employer",
                Values = [FormatCurrency(qppEmployer + qpipEmployer)],
                RowType = AccountingRowType.SubtotalRow
            });

            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });
            data.Rows.Add(new AccountingRow
            {
                Label = "Total to remit to Revenu Quebec",
                Values = [FormatCurrency(quebecTotal)],
                RowType = AccountingRowType.GrandTotalRow
            });

            // Stated rather than left to be inferred from a nil line. The health services fund
            // is a real employer contribution that Revenu Quebec expects with this payment, and
            // this app does not calculate it, so a total that looks complete would be trusted.
            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
            data.Rows.Add(new AccountingRow
            {
                Label = "Excludes the contribution to the health services fund, which Argo Books "
                        + "does not calculate. Add it before remitting.",
                Values = [""],
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow
        {
            Label = $"Pay runs included: {runs.Count}",
            Values = [""],
            RowType = AccountingRowType.DataRow
        });

        return data;

        static bool IsQuebecLine(Models.Payroll.PayRunLine line) =>
            string.Equals(line.Province, "QC", StringComparison.OrdinalIgnoreCase);

        AccountingRow Row(string label, decimal value) => new()
        {
            Label = label,
            Values = [FormatCurrency(value)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        };
    }

    private void AddEmptyTaxSummary(AccountingTableData data, AccountingTerms t)
    {
        data.Rows.Add(new AccountingRow { Label = t.TaxCollectedHeader, RowType = AccountingRowType.SectionHeader, Values = ["Amount"] });
        data.Rows.Add(new AccountingRow { Label = t.TaxCollectedTotal, Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.TaxPaidHeader, RowType = AccountingRowType.SectionHeader, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.TaxPaidTotal, Values = [FormatCurrency(0)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });
        data.Rows.Add(new AccountingRow { Label = t.NetTaxLabel, Values = [FormatCurrency(0)], RowType = AccountingRowType.GrandTotalRow });
    }

    #endregion
}
