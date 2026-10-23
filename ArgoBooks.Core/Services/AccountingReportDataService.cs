using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
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
    /// The single currency the whole report is rendered in (docs/Calculations.md Rule 4).
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

    private PurchaseTaxRule? _purchaseTaxRule;

    private HashSet<string>? _payrollExpenseIds;

    /// <summary>Every expense a pay run recorded, which the Income Statement counts from the pay runs instead.</summary>
    private HashSet<string> PayrollExpenseIds => _payrollExpenseIds ??= PayrollBooks.ExpenseIds(companyData!);

    private PurchaseTaxRule Rule => _purchaseTaxRule ??= PurchaseTax.RuleFor(companyData);

    private Dictionary<string, decimal>? _capitalizedShares;

    /// <summary>The share of each expense that bought a fixed asset, which counts as depreciation over the years rather than as an expense now.</summary>
    private Dictionary<string, decimal> CapitalizedShares =>
        _capitalizedShares ??= AssetDepreciation.CapitalizedShares(companyData!.Expenses, companyData.Assets, Rule);

    /// <summary>An amount on a fixed asset in <see cref="DisplayCode"/>, at its purchase date unless another is given, or null when that rate is missing.</summary>
    private decimal? AssetToDisplay(FixedAsset asset, decimal amount, DateTime? date = null) =>
        amount == 0 ? 0m : DisplayCurrency.FromNative(amount, asset.Currency, DisplayCode, (date ?? asset.PurchaseDate).Date);

    /// <summary>The fixed assets' depreciation and their gains or losses on leaving the books in the report's range.</summary>
    private (decimal Depreciation, decimal GainOnDisposal, bool Pending) AssetFiguresInRange()
    {
        decimal depreciation = 0m, gain = 0m;
        var pending = false;
        foreach (var asset in companyData!.Assets)
        {
            if (AssetToDisplay(asset, AssetDepreciation.Between(asset, RangeStart, RangeEnd)) is { } booked)
                depreciation += booked;
            else
                pending = true;

            if (asset.DisposedDate is not { } gone || !IsInDateRange(gone))
                continue;
            var proceeds = AssetToDisplay(asset, asset.Proceeds, gone);
            var bookValue = AssetToDisplay(asset, AssetDepreciation.BookValueAtDisposal(asset));
            if (proceeds is { } p && bookValue is { } b)
                gain += p - b;
            else
                pending = true;
        }
        return (depreciation, gain, pending);
    }

    /// <summary>What fixed assets sold for on days from <paramref name="from"/> to <paramref name="to"/>, and whether any rate was missing.</summary>
    private (decimal Proceeds, bool Pending) AssetSaleProceeds(DateTime from, DateTime to)
    {
        decimal total = 0m;
        var pending = false;
        foreach (var asset in companyData!.Assets.Where(a => a.DisposedDate is { } gone && gone >= from && gone <= to))
        {
            if (AssetToDisplay(asset, asset.Proceeds, asset.DisposedDate) is { } proceeds)
                total += proceeds;
            else
                pending = true;
        }
        return (total, pending);
    }

    private string ResolveDisplayCode() => companyData == null
        ? "USD"
        : DisplayCurrency.Resolve(GetCurrencyCode(), DisplayCurrency.ReportDates(companyData, filters.EndDate));

    /// <summary>
    /// A USD amount in <see cref="DisplayCode"/> at its transaction's date (docs/Calculations.md Rule 4), so a
    /// total equals the sum of its rows converted at each row's own date.
    /// </summary>
    private decimal ToDisplay(decimal amountUSD, DateTime date) => DisplayCurrency.FromUSD(amountUSD, DisplayCode, date);

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

    private const string PendingText = "Pending";

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
        // Compared by day, because a report range selects whole days while a transaction carries a time of day and a custom end date is midnight.
        if (filters.StartDate.HasValue && date.Date < filters.StartDate.Value.Date)
            return false;
        if (filters.EndDate.HasValue && date.Date > filters.EndDate.Value.Date)
            return false;
        return true;
    }

    /// <summary>
    /// The same whole days as <see cref="IsInDateRange"/>, as the [start, end] the aggregators take.
    /// An unset end is open, as an unset start is.
    /// </summary>
    private DateTime RangeStart => filters.StartDate?.Date ?? DateTime.MinValue;

    private DateTime RangeEnd => filters.EndDate is { } end && end.Date < DateTime.MaxValue.Date
        ? end.Date.AddDays(1).AddTicks(-1)
        : DateTime.MaxValue;

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
            var paidByEnd = payments.Where(p => IsOnOrBeforeEndDate(p.Date)).ToList();
            decimal balanceUSD;
            if (payments.Count == 0)
            {
                balanceUSD = invoice.Status == InvoiceStatus.Paid ? 0m : invoice.EffectiveBalanceUSD;
            }
            else if (invoice.Total > 0 && paidByEnd.All(p =>
                         string.Equals(p.OriginalCurrency, invoice.OriginalCurrency, StringComparison.OrdinalIgnoreCase)))
            {
                // The share still owed in the invoice's own currency, valued at the invoice's rate.
                var owed = Math.Round(invoice.Total - paidByEnd.Sum(p => p.Amount), 2, MidpointRounding.AwayFromZero);
                balanceUSD = owed <= 0 ? 0m : invoice.EffectiveTotalUSD * Math.Min(1m, owed / invoice.Total);
            }
            else
            {
                balanceUSD = Math.Max(0m, invoice.EffectiveTotalUSD - paidByEnd.Sum(p => p.EffectiveAmountUSD));
            }

            if (Math.Round(balanceUSD, 2, MidpointRounding.AwayFromZero) > 0)
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
            AccountingReportType.CustomerStatement => GetCustomerStatementData(),
            AccountingReportType.SalesByCustomer => GetSalesByCustomerData(),
            AccountingReportType.ExpensesBySupplier => GetExpensesBySupplierData(),
            AccountingReportType.PayrollSummary => GetPayrollSummaryData(),
            AccountingReportType.Rentals => GetRentalsData(),
            AccountingReportType.AssetRegister => GetAssetRegisterData(),
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
    /// Adds a transaction's tax, by rate, to <paramref name="byRate"/> for the Tax Summary. The amount
    /// is the transaction's own recorded tax (<c>EffectiveTaxAmountUSD</c>), the figure the Balance
    /// Sheet and the tax charts use, shared across its lines by each line's own tax
    /// (<see cref="LineAllocation.AllocateTax"/>). A transaction whose lines carry no tax goes under
    /// its own rate.
    /// </summary>
    private void AddTaxByRate(Dictionary<decimal, decimal> byRate, Transaction txn)
    {
        var taxDisplay = ToDisplay(txn.EffectiveTaxAmountUSD, txn.Date);
        if (taxDisplay == 0)
            return;

        // LineItem.TaxRate is a fraction (0.08); Transaction.TaxRate is a percentage (8).
        if (!LineAllocation.HasLineTax(txn.LineItems))
        {
            var rate = Math.Round(txn.TaxRate / 100m, 4);
            byRate[rate] = byRate.GetValueOrDefault(rate) + taxDisplay;
            return;
        }

        foreach (var (line, share) in LineAllocation.AllocateTax(txn.LineItems, taxDisplay).Shares)
        {
            if (share == 0) continue;
            var rate = Math.Round(line.TaxRate, 2);
            byRate[rate] = byRate.GetValueOrDefault(rate) + share;
        }
    }

    /// <summary>
    /// Groups transaction pre-tax totals by category, derived from line items' product IDs.
    /// Uses Subtotal (pre-tax) because sales tax is a liability, not revenue/expense.
    /// Each transaction's USD amounts are converted to DisplayCode at the transaction's OWN date
    /// (docs/Calculations.md Rule 4) before being summed, so the result is already in
    /// DisplayCode.
    /// </summary>
    private Dictionary<string, decimal> GroupTransactionsByCategory(
        IEnumerable<Transaction> transactions)
    {
        var result = new Dictionary<string, decimal>();

        foreach (var txn in transactions)
        {
            var allocation = LineAllocation.Allocate(txn, LineAllocationBasis.PreTax);
            if (!allocation.IsSplit)
            {
                // No lines, or lines that add up to 0 (a 100% discount): post the transaction's own
                // pre-tax amount so it isn't dropped.
                result["Uncategorized"] = result.GetValueOrDefault("Uncategorized") + ToDisplay(allocation.UnallocatedUSD, txn.Date);
                continue;
            }

            foreach (var (lineItem, lineUSD) in allocation.Shares)
            {
                var categoryName = GetCategoryNameForProduct(lineItem.ProductId);
                result[categoryName] = result.GetValueOrDefault(categoryName) + ToDisplay(lineUSD, txn.Date);
            }
        }

        return result;
    }

    /// <summary>
    /// What each expense cost the business (<see cref="PurchaseTax.CostUSD"/>) by category, leaving out the share that bought fixed assets (§16) and tracked stock bought: it is
    /// stock until it sells, when it shows as cost of goods sold (docs/Calculations.md §14). The rest of
    /// such an expense, shipping and fees included, is spread over its other lines, or goes under
    /// Uncategorized when they can't take it, as for any transaction (§13).
    /// </summary>
    private Dictionary<string, decimal> GroupExpensesByCategory(IEnumerable<Expense> expenses)
    {
        var result = new Dictionary<string, decimal>();

        void Add(string category, decimal amount) => result[category] = result.GetValueOrDefault(category) + amount;

        foreach (var expense in expenses)
        {
            var capitalized = CapitalizedShares.GetValueOrDefault(expense.Id);
            if (capitalized >= 1)
                continue;

            var costUSD = PurchaseTax.CostUSD(expense, Rule);
            var afterStock = costUSD - CostOfGoodsAggregator.StockPurchaseUSD(expense) - costUSD * capitalized;
            // What is left after the stock never crosses zero: a supplier credit is below zero throughout.
            var remainingUSD = !expense.LineItems.Any(li => li.IsStockPurchase) ? costUSD * (1m - capitalized)
                : costUSD < 0 ? Math.Min(0m, afterStock) : Math.Max(0m, afterStock);
            var otherLines = expense.LineItems.Where(li => !li.IsStockPurchase).ToList();
            var allocation = LineAllocation.Allocate(otherLines, remainingUSD);
            if (allocation.IsSplit)
            {
                foreach (var (line, lineUSD) in allocation.Shares)
                    Add(GetCategoryNameForProduct(line.ProductId), ToDisplay(lineUSD, expense.Date));
            }
            else if (remainingUSD != 0)
            {
                Add("Uncategorized", ToDisplay(remainingUSD, expense.Date));
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

        var income = IncomeInRange();
        var revenueByCategory = income.RevenueByCategory;
        var expenseByCategory = income.ExpenseByCategory;
        var refunds = income.Refunds;
        var depreciation = income.Depreciation;
        var gainOnDisposal = income.GainOnDisposal;
        var totalRevenue = income.TotalRevenue;
        var totalExpenses = income.TotalExpenses;
        var costOfGoods = income.CostOfGoods;
        var netIncome = income.NetIncome;
        var assetsPending = income.AssetsPending || income.PayrollPending;
        var costPending = income.CostPending;

        string AfterCost(decimal amount) => costPending ? PendingText : FormatCurrencyWithSign(amount);
        string AssetBased(decimal amount) => assetsPending ? PendingText : FormatCurrencyWithSign(amount);

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

        if (costOfGoods != 0 || costPending)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Cost of Goods Sold",
                Values = [AfterCost(-costOfGoods)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Gross Profit",
                Values = [AfterCost(totalRevenue - costOfGoods)],
                RowType = AccountingRowType.SubtotalRow
            });
        }

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

        if (income.Wages != 0 || income.EmployerContributions != 0 || income.PayrollPending)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Wages and Salaries",
                Values = [income.PayrollPending ? PendingText : FormatCurrency(income.Wages)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
            data.Rows.Add(new AccountingRow
            {
                Label = "Employer Payroll Contributions",
                Values = [income.PayrollPending ? PendingText : FormatCurrency(income.EmployerContributions)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        if (depreciation != 0 || income.AssetsPending)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Depreciation",
                Values = [AssetBased(depreciation)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = t.TotalOperatingExpenses,
            Values = [AssetBased(totalExpenses)],
            RowType = AccountingRowType.SubtotalRow
        });

        if (gainOnDisposal != 0)
        {
            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
            data.Rows.Add(new AccountingRow
            {
                Label = gainOnDisposal > 0 ? "Gain on Sale of Assets" : "Loss on Sale of Assets",
                Values = [AssetBased(gainOnDisposal)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // Net Income
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = t.NetIncome,
            Values = [assetsPending ? PendingText : AfterCost(netIncome)],
            RowType = AccountingRowType.GrandTotalRow
        });

        if (companyData.Assets.Count > 0)
            data.Footnote = "Purchases recorded as fixed assets are not counted as expenses here. Their cost is counted as depreciation over the years they are used.";

        return data;
    }

    /// <summary>Everything the Income Statement adds up for the report's range, in <see cref="DisplayCode"/>.</summary>
    private sealed record IncomeFigures(
        Dictionary<string, decimal> RevenueByCategory, decimal Refunds, decimal CostOfGoods,
        Dictionary<string, decimal> ExpenseByCategory, decimal Wages, decimal EmployerContributions,
        decimal Depreciation, decimal GainOnDisposal, bool AssetsPending, bool PayrollPending, bool CostPending)
    {
        public decimal TotalRevenue => RevenueByCategory.Values.Sum() - Refunds;
        public decimal TotalExpenses => ExpenseByCategory.Values.Sum() + Wages + EmployerContributions + Depreciation;
        public decimal NetIncome => TotalRevenue - CostOfGoods - TotalExpenses + GainOnDisposal;
    }

    private IncomeFigures IncomeInRange()
    {
        var revenues = companyData!.Revenues.Where(r => IsInDateRange(r.Date)).ToList();

        // Payroll counts from its pay runs below, so the expenses that paid it are left out here.
        var expenses = companyData.Expenses
            .Where(e => IsInDateRange(e.Date) && !PayrollExpenseIds.Contains(e.Id))
            .ToList();

        // Refunds come off revenue on their own date, before tax (docs/Calculations.md §8).
        var refunds = RefundAggregator.GetRefundedPreTaxInDateRangeDisplay(
            companyData.Payments, InvoicesById, RangeStart, RangeEnd, ToDisplay);

        // A pay run costs its gross pay and the employer's contributions on its pay date, in the company's currency.
        decimal wages = 0m, employer = 0m;
        var payrollPending = false;
        foreach (var run in PayrollBooks.RecordedRuns(companyData).Where(r => IsInDateRange(r.PayDate)))
        {
            var gross = DisplayCurrency.FromNative(run.TotalGross, GetCurrencyCode(), DisplayCode, run.PayDate.Date);
            var share = DisplayCurrency.FromNative(run.TotalEmployerShare, GetCurrencyCode(), DisplayCode, run.PayDate.Date);
            if (gross is { } g && share is { } e)
            {
                wages += g;
                employer += e;
            }
            else
            {
                payrollPending = true;
            }
        }

        var (depreciation, gainOnDisposal, assetsPending) = AssetFiguresInRange();

        return new IncomeFigures(
            GroupTransactionsByCategory(revenues), refunds,
            revenues.Sum(r => ToDisplay(CostOfGoodsAggregator.CostOfGoodsSoldUSD(r), r.Date)),
            GroupExpensesByCategory(expenses), wages, employer, depreciation, gainOnDisposal,
            assetsPending, payrollPending,
            // A sale waiting for its stock's cost counts none yet, so the figures that subtract cost of goods sold aren't known (Calculations.md §14).
            CostOfGoodsAggregator.IsCostOfGoodsPending(revenues));
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
            ColumnWidthRatios = [0.65, 0.35]
        };

        if (companyData == null)
        {
            AddEmptyBalanceSheet(data, t);
            return data;
        }

        // An opening balance dated after the report has not happened yet as far as the report is concerned.
        var opening = companyData.Settings.OpeningBalance is { } ob && IsOnOrBeforeEndDate(ob.Date) ? ob : null;
        decimal? openingConverted = opening == null
            ? 0m
            : DisplayCurrency.FromNative(opening.Amount, opening.Currency, DisplayCode, opening.Date.Date);
        var cashPending = openingConverted == null;
        var openingCash = openingConverted ?? 0m;

        // The opening balance already includes everything before its date, so only what moved on or after it is added.
        var cashFrom = opening?.Date.Date ?? DateTime.MinValue;

        data.Footnote = (opening == null
                ? "Cash is estimated from recorded transactions, starting from zero. Enter your opening bank balance in Edit Company to start from it instead. "
                : $"Cash is the opening bank balance on {DateFormats.Format(opening.Date, companyData.Settings.Localization.DateFormat)} plus the money recorded in and out since. ")
            + "Inventory valued at each item's current unit cost (cost "
            + "history is not tracked) applied to stock levels "
            + "reconstructed as of the report date. Retained earnings are the profit to date; owner's equity "
            + "is the rest, such as what the business owned before these books.";

        // Cash = Revenue (Paid, no invoice) + Payments - Expenses, all filtered by date. Uses post-tax (total) amounts because cash includes tax collected/paid.
        var cashFromRevenue = companyData.Revenues
            .Where(r => RevenueAggregator.IsCollected(r)
                        && string.IsNullOrEmpty(r.InvoiceId)
                        && r.Date >= cashFrom
                        && IsOnOrBeforeEndDate(r.Date))
            .Sum(r => ToDisplay(r.EffectiveTotalUSD, r.Date));

        // Everything except revenue-linked, which is already counted by its own Revenue row.
        var cashFromPayments = companyData.Payments
            .Where(p => string.IsNullOrEmpty(p.RevenueId) && p.Date >= cashFrom && IsOnOrBeforeEndDate(p.Date))
            .Sum(p => ToDisplay(p.EffectiveAmountUSD, p.Date));

        var cashPaidForExpenses = ExpenseAggregator.SumExpensesDisplay(companyData.Expenses, cashFrom, RangeEnd, ToDisplay);

        var (cashFromAssetSales, salesPending) = AssetSaleProceeds(cashFrom, RangeEnd);
        cashPending |= salesPending;

        var cash = openingCash + cashFromRevenue + cashFromPayments - cashPaidForExpenses + cashFromAssetSales;

        // Accounts Receivable = what was still owed at the end date, each balance converted at the
        // invoice's issue date.
        var accountsReceivable = GetReceivablesAsOfEndDate()
            .Sum(r => ToDisplay(r.BalanceUSD, r.Invoice.IssueDate));

        // Inventory valued at current unit cost, using stock levels reconstructed as of the report end date. See docs/Calculations.md §10.
        var inventoryValue = ToDisplay(
            InventoryValuationService.TotalValueAsOf(companyData, EndDateForValuation),
            EndDateForValuation);

        var totalCurrentAssets = cash + accountsReceivable + inventoryValue;

        // Fixed assets stay at what they cost, converted at their purchase date, less the depreciation booked so far.
        var heldAssets = companyData.Assets.Where(a => AssetDepreciation.IsHeldOn(a, EndDateForValuation)).ToList();
        decimal fixedAssetCost = 0m, accumulatedDepreciation = 0m;
        var assetsPending = false;
        foreach (var asset in heldAssets)
        {
            var cost = AssetToDisplay(asset, asset.Cost);
            var accumulated = AssetToDisplay(asset, AssetDepreciation.AccumulatedAsOf(asset, EndDateForValuation));
            if (cost is { } costShown && accumulated is { } accumulatedShown)
            {
                fixedAssetCost += costShown;
                accumulatedDepreciation += accumulatedShown;
            }
            else
            {
                assetsPending = true;
            }
        }
        var netFixedAssets = fixedAssetCost - accumulatedDepreciation;

        var totalAssets = totalCurrentAssets + netFixedAssets;

        // Accounts Payable = purchase orders placed but not received or cancelled, each converted at
        // the order date. A draft has not been sent to a supplier, so nothing is owed on it yet.
        var accountsPayable = companyData.PurchaseOrders
            .Where(po => po.Status != PurchaseOrderStatus.Received
                         && po.Status != PurchaseOrderStatus.Cancelled
                         && po.Status != PurchaseOrderStatus.Draft
                         && IsOnOrBeforeEndDate(po.OrderDate))
            .Sum(po => ToDisplay(po.EffectiveTotalUSD, po.OrderDate));

        // Sales Tax Payable = tax collected on all revenue, less tax handed back on refunds (docs/Calculations.md §8), minus the tax on expenses the company claims back.
        var taxCollected = companyData.Revenues
            .Where(r => IsOnOrBeforeEndDate(r.Date))
            .Sum(r => ToDisplay(r.EffectiveTotalUSD - r.EffectiveSubtotalUSD, r.Date));
        var taxRefunded = RefundAggregator.GetRefundedTaxInDateRangeDisplay(
            companyData.Payments, InvoicesById, DateTime.MinValue, RangeEnd, ToDisplay);
        var taxPaidOnExpenses = companyData.Expenses
            .Where(e => IsOnOrBeforeEndDate(e.Date))
            .Sum(e => ToDisplay(PurchaseTax.ClaimableUSD(e, Rule), e.Date));
        var salesTaxPayable = taxCollected - taxRefunded - taxPaidOnExpenses;

        // A security deposit is not earned (docs/Calculations.md §4), but the invoice total carrying it is cash or receivables from the day it is issued.
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

        // What pay runs withheld and the employer added, until an expense records paying it to the agency.
        decimal payrollOwing = 0m;
        var payrollPending = false;
        foreach (var (amount, payDate) in PayrollBooks.OwingAsOf(companyData, EndDateForValuation))
        {
            if (DisplayCurrency.FromNative(amount, GetCurrencyCode(), DisplayCode, payDate.Date) is { } owing)
                payrollOwing += owing;
            else
                payrollPending = true;
        }

        var totalLiabilities = accountsPayable + salesTaxPayable + securityDeposits + payrollOwing;

        // Retained earnings are the profit to date, as the Income Statement counts it from the first record to the end date.
        var earnings = new AccountingReportDataService(companyData, new ReportFilters { EndDate = filters.EndDate }).IncomeInRange();
        var retainedEarnings = earnings.NetIncome;
        var earningsPending = earnings.AssetsPending || earnings.PayrollPending || earnings.CostPending;

        // Owner's equity is what is left: assets owned before these books, and money the owner put in or took out.
        var totalEquity = totalAssets - totalLiabilities;
        var ownersEquity = totalEquity - openingCash - retainedEarnings;

        // Without the opening balance's rate the cash, and every total built on it, is unknown rather than zero.
        string CashBased(decimal amount) => cashPending ? PendingText : FormatCurrencyWithSign(amount);
        string AssetBased(decimal amount) => assetsPending ? PendingText : FormatCurrencyWithSign(amount);
        string TotalBased(decimal amount) => cashPending || assetsPending || payrollPending ? PendingText : FormatCurrencyWithSign(amount);
        string EarningsBased(decimal amount) => earningsPending ? PendingText : FormatCurrencyWithSign(amount);

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
            Values = [CashBased(cash)],
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
            Values = [CashBased(totalCurrentAssets)],
            IndentLevel = 0,
            RowType = AccountingRowType.SubtotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        if (heldAssets.Count > 0)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Fixed Assets",
                RowType = AccountingRowType.SectionHeader,
                IndentLevel = 0,
                Values = [""]
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Property and Equipment",
                Values = [AssetBased(fixedAssetCost)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Less Accumulated Depreciation",
                Values = [AssetBased(-accumulatedDepreciation)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Total Fixed Assets",
                Values = [AssetBased(netFixedAssets)],
                IndentLevel = 0,
                RowType = AccountingRowType.SubtotalRow
            });

            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

            data.Footnote += " Fixed assets are shown at cost less the depreciation booked so far.";
        }

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL ASSETS",
            Values = [TotalBased(totalAssets)],
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

        if (payrollOwing != 0 || payrollPending)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Payroll Remittances Owing",
                Values = [payrollPending ? PendingText : FormatCurrencyWithSign(payrollOwing)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL LIABILITIES",
            Values = [payrollPending ? PendingText : FormatCurrencyWithSign(totalLiabilities)],
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

        if (opening != null)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "Opening Balance",
                Values = [CashBased(openingCash)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });
        }

        data.Rows.Add(new AccountingRow
        {
            Label = "Owner's Equity",
            Values = [earningsPending ? PendingText : TotalBased(ownersEquity)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "Retained Earnings",
            Values = [EarningsBased(retainedEarnings)],
            IndentLevel = 1,
            RowType = AccountingRowType.DataRow
        });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL EQUITY",
            Values = [TotalBased(totalEquity)],
            RowType = AccountingRowType.TotalRow
        });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });

        // TOTAL LIABILITIES & EQUITY
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "TOTAL LIABILITIES & EQUITY",
            Values = [TotalBased(totalLiabilities + totalEquity)],
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

        // Operating Activities Exclude invoice-linked revenue to avoid double counting with Payments.
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

        // The share of an expense that bought a fixed asset is an investment rather than a running cost.
        var cashPaidForAssets = companyData.Expenses
            .Where(e => IsInDateRange(e.Date) && CapitalizedShares.ContainsKey(e.Id))
            .Sum(e => ToDisplay(PurchaseTax.CostUSD(e, Rule) * CapitalizedShares[e.Id], e.Date));
        var cashPaidForExpenses = ExpenseAggregator.SumExpensesDisplay(companyData.Expenses, RangeStart, RangeEnd, ToDisplay)
                                  - cashPaidForAssets;
        var (cashFromAssetSales, salesPending) = AssetSaleProceeds(RangeStart, RangeEnd);

        var totalOperating = cashFromSales + cashFromInvoicePayments - cashPaidForExpenses;
        var totalInvesting = cashFromAssetSales - cashPaidForAssets;

        var netChange = totalOperating + totalInvesting;
        string SalesBased(decimal amount) => salesPending ? PendingText : FormatCurrencyWithSign(amount);

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

        if (cashPaidForAssets != 0 || cashFromAssetSales != 0 || salesPending)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = "INVESTING ACTIVITIES",
                RowType = AccountingRowType.SectionHeader,
                Values = [""]
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Purchase of Fixed Assets",
                Values = [FormatCurrencyWithSign(-cashPaidForAssets)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Sale of Fixed Assets",
                Values = [SalesBased(cashFromAssetSales)],
                IndentLevel = 1,
                RowType = AccountingRowType.DataRow
            });

            data.Rows.Add(new AccountingRow
            {
                Label = "Net Cash from Investing Activities",
                Values = [SalesBased(totalInvesting)],
                RowType = AccountingRowType.SubtotalRow
            });

            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = [""] });
        }

        // Net change
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = [""] });

        data.Rows.Add(new AccountingRow
        {
            Label = "NET CHANGE IN CASH",
            Values = [SalesBased(netChange)],
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
            var allocation = LineAllocation.Allocate(rev, LineAllocationBasis.PreTax);
            // No lines, or lines that add up to 0 (a 100% discount) post the transaction's own
            // amount, so the entry is not dropped from the ledger.
            if (allocation.IsSplit)
            {
                foreach (var (li, lineUSD) in allocation.Shares)
                {
                    AddLedgerEntry(entries, GetCategoryNameForProduct(li.ProductId), new LedgerEntry
                    {
                        Date = rev.Date,
                        Description = li.Description.Length > 0 ? li.Description : rev.Description,
                        Reference = rev.Id,
                        Debit = 0,
                        Credit = ToDisplay(lineUSD, rev.Date)
                    });
                }
            }
            else
            {
                AddLedgerEntry(entries, t.RevenueCategory, new LedgerEntry
                {
                    Date = rev.Date,
                    Description = rev.Description,
                    Reference = rev.Id,
                    Debit = 0,
                    Credit = ToDisplay(allocation.UnallocatedUSD, rev.Date)
                });
            }
        }

        // Expense transactions (debits), all amounts in USD
        foreach (var exp in companyData.Expenses.Where(e => IsInDateRange(e.Date)))
        {
            var payroll = PayrollExpenseIds.Contains(exp.Id);
            var allocation = LineAllocation.Allocate(exp.LineItems, PurchaseTax.CostUSD(exp, Rule));
            if (allocation.IsSplit)
            {
                foreach (var (li, lineUSD) in allocation.Shares)
                {
                    AddLedgerEntry(entries, payroll ? "Payroll" : GetCategoryNameForProduct(li.ProductId), new LedgerEntry
                    {
                        Date = exp.Date,
                        Description = li.Description.Length > 0 ? li.Description : exp.Description,
                        Reference = exp.Id,
                        Debit = ToDisplay(lineUSD, exp.Date),
                        Credit = 0
                    });
                }
            }
            else
            {
                AddLedgerEntry(entries, payroll ? "Payroll" : t.ExpensesCategory, new LedgerEntry
                {
                    Date = exp.Date,
                    Description = exp.Description,
                    Reference = exp.Id,
                    Debit = ToDisplay(allocation.UnallocatedUSD, exp.Date),
                    Credit = 0
                });
            }
        }

        // Payments (credits to AR / debits to cash).
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
                    Label = entry.Date.ToString(Data.DateFormats.Short),
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

        // ToDisplay converts each line item at its transaction's own date (Calculations.md Rule 4), so the returned USD figures are already in DisplayCode.
        var products = ProductSalesService.GetProductSales(
            companyData,
            RangeStart,
            RangeEnd,
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

        data.Footnote = "Revenue includes the tax charged on each sale.";
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
                // Convert each open invoice's balance at its issue date (Calculations.md Rule 4).
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

    #region Customer Statement

    /// <summary>One of the customer's invoices, with its total and each payment on it in the display currency.</summary>
    private sealed record StatementInvoice(Invoice Invoice, decimal Total, List<StatementPayment> Payments)
    {
        public decimal PaidOnOrBefore(DateTime date) => Payments.Where(p => p.Date.Date <= date.Date).Sum(p => p.Amount);

        public decimal PaidBefore(DateTime date) => Payments.Where(p => p.Date.Date < date.Date).Sum(p => p.Amount);
    }

    /// <summary>A payment on a statement invoice. DateKnown is false for a paid amount imported without its payments.</summary>
    private sealed record StatementPayment(DateTime Date, decimal Amount, bool DateKnown);

    /// <summary>A statement for one customer: what they owe today, or what was invoiced and paid between the dates.</summary>
    private AccountingTableData GetCustomerStatementData()
    {
        var customer = companyData != null && !string.IsNullOrEmpty(filters.CustomerId)
            ? companyData.GetCustomer(filters.CustomerId)
            : null;

        if (customer == null)
        {
            return new AccountingTableData
            {
                Title = "Customer Statement",
                Subtitle = "Choose a customer in Report Settings",
                ColumnHeaders = ["Invoice", "Balance Due"],
                ColumnWidthRatios = [0.7, 0.3],
                Rows = [new AccountingRow { Label = "Amount due", Values = [FormatCurrency(0)], RowType = AccountingRowType.TotalRow }]
            };
        }

        var title = string.IsNullOrWhiteSpace(customer.CompanyName) || customer.CompanyName == customer.Name
            ? $"Statement for {customer.Name}"
            : $"Statement for {customer.Name}, {customer.CompanyName}";
        var invoices = GetStatementInvoices(customer.Id);

        return filters.StatementShowsActivity
            ? GetStatementActivityData(title, invoices)
            : GetStatementOpenInvoicesData(title, invoices);
    }

    /// <summary>The customer's issued invoices, with payments converted at the invoice's date so a paid invoice comes to exactly zero.</summary>
    private List<StatementInvoice> GetStatementInvoices(string customerId)
    {
        var paymentsByInvoice = companyData!.Payments
            .Where(p => !p.IsRefund && p.Amount > 0 && !string.IsNullOrEmpty(p.InvoiceId))
            .ToLookup(p => p.InvoiceId);

        var result = new List<StatementInvoice>();
        foreach (var invoice in companyData.Invoices.Where(i => i.CustomerId == customerId
                                                                && i.Status != InvoiceStatus.Cancelled
                                                                && i.Status != InvoiceStatus.Draft))
        {
            var total = Math.Round(ToDisplay(invoice.EffectiveTotalUSD, invoice.IssueDate), 2, MidpointRounding.AwayFromZero);
            var payments = new List<StatementPayment>();
            var remaining = total;

            var recorded = paymentsByInvoice[invoice.Id].OrderBy(p => p.Date).ToList();
            if (recorded.Count == 0)
            {
                var paid = invoice.Status == InvoiceStatus.Paid
                    ? total
                    : total - Math.Round(ToDisplay(invoice.EffectiveBalanceUSD, invoice.IssueDate), 2, MidpointRounding.AwayFromZero);
                if (paid > 0)
                    payments.Add(new StatementPayment(invoice.IssueDate, Math.Min(paid, total), false));
            }

            foreach (var payment in recorded)
            {
                if (remaining <= 0)
                    break;

                var amount = invoice.Total > 0 && string.Equals(payment.OriginalCurrency, invoice.OriginalCurrency, StringComparison.OrdinalIgnoreCase)
                    ? total * payment.Amount / invoice.Total
                    : ToDisplay(payment.EffectiveAmountUSD, invoice.IssueDate);
                amount = Math.Min(Math.Round(amount, 2, MidpointRounding.AwayFromZero), remaining);
                remaining -= amount;

                // A payment taken before the invoice was issued is shown on the issue date, so it never lands outside the invoice's own period.
                var date = payment.Date.Date < invoice.IssueDate.Date ? invoice.IssueDate : payment.Date;
                payments.Add(new StatementPayment(date, amount, true));
            }

            result.Add(new StatementInvoice(invoice, total, payments));
        }
        return result;
    }

    private string FormatStatementDate(DateTime date) =>
        DateFormats.Format(date, companyData?.Settings.Localization.DateFormat);

    /// <summary>The invoices the customer still owes today, oldest due first.</summary>
    private AccountingTableData GetStatementOpenInvoicesData(string title, List<StatementInvoice> invoices)
    {
        var today = DateTime.Today;
        var data = new AccountingTableData
        {
            Title = title,
            Subtitle = $"Unpaid invoices as of {FormatStatementDate(today)} · {GetCurrencySubtitle()}",
            ColumnHeaders = ["Invoice", "Issued", "Due", "Amount", "Paid", "Balance Due"],
            ColumnWidthRatios = [0.2, 0.16, 0.16, 0.16, 0.16, 0.16]
        };

        var open = invoices
            .Where(i => i.Invoice.IssueDate.Date <= today)
            .Select(i => (Item: i, Paid: i.PaidOnOrBefore(today)))
            .Where(x => x.Item.Total - x.Paid > 0)
            .OrderBy(x => x.Item.Invoice.DueDate)
            .ThenBy(x => x.Item.Invoice.IssueDate)
            .ToList();

        var amountDue = 0m;
        var overdue = 0m;
        foreach (var (item, paid) in open)
        {
            var balance = item.Total - paid;
            var isOverdue = item.Invoice.DueDate.Date < today;
            amountDue += balance;
            if (isOverdue)
                overdue += balance;

            data.Rows.Add(new AccountingRow
            {
                Label = isOverdue ? $"{item.Invoice.InvoiceNumber}, overdue" : item.Invoice.InvoiceNumber,
                Values =
                [
                    FormatStatementDate(item.Invoice.IssueDate),
                    FormatStatementDate(item.Invoice.DueDate),
                    FormatCurrency(item.Total),
                    paid > 0 ? FormatCurrency(paid) : "",
                    FormatCurrency(balance)
                ]
            });
        }

        if (open.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "No unpaid invoices", Values = ["", "", "", "", ""] });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", "", ""] });
        if (overdue > 0 && overdue < amountDue)
            data.Rows.Add(new AccountingRow { Label = "Overdue", Values = ["", "", "", "", FormatCurrency(overdue)], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { Label = "Amount due", Values = ["", "", "", "", FormatCurrency(amountDue)], RowType = AccountingRowType.TotalRow });

        if (open.Any(x => x.Paid > 0))
            data.Footnote = "Paid is what has already been received on each invoice.";
        return data;
    }

    /// <summary>What was invoiced and paid between the dates, ending with what has been paid since so an old statement does not read as still owed.</summary>
    private AccountingTableData GetStatementActivityData(string title, List<StatementInvoice> invoices)
    {
        var today = DateTime.Today;
        var start = (filters.StartDate ?? invoices.Select(i => i.Invoice.IssueDate).DefaultIfEmpty(today).Min()).Date;
        var end = (filters.EndDate ?? today).Date;
        if (end > today)
            end = today;
        if (start > end)
            start = end;

        var data = new AccountingTableData
        {
            Title = title,
            Subtitle = $"Account activity from {FormatStatementDate(start)} to {FormatStatementDate(end)} · {GetCurrencySubtitle()}",
            ColumnHeaders = ["Details", "Date", "Charges", "Payments", "Balance"],
            ColumnWidthRatios = [0.36, 0.16, 0.16, 0.16, 0.16]
        };

        var forward = invoices
            .Where(i => i.Invoice.IssueDate.Date < start)
            .Sum(i => i.Total - i.PaidBefore(start));

        var entries = new List<(DateTime Date, int Order, string Details, decimal Charge, decimal Payment)>();
        foreach (var item in invoices)
        {
            var number = item.Invoice.InvoiceNumber;
            if (item.Invoice.IssueDate.Date >= start && item.Invoice.IssueDate.Date <= end)
            {
                var paidInFullToday = item.PaidOnOrBefore(today) >= item.Total;
                entries.Add((item.Invoice.IssueDate, 0, paidInFullToday ? $"Invoice {number} (now paid)" : $"Invoice {number}", item.Total, 0m));
            }

            foreach (var payment in item.Payments.Where(p => p.Date.Date >= start && p.Date.Date <= end))
                entries.Add((payment.Date, 1, payment.DateKnown ? $"Payment for {number}" : $"Payment for {number} (date not recorded)", 0m, payment.Amount));
        }

        data.Rows.Add(new AccountingRow
        {
            Label = $"Balance before {FormatStatementDate(start)}",
            Values = ["", "", "", FormatCurrency(forward)]
        });

        var balance = forward;
        var charges = 0m;
        var paymentsInPeriod = 0m;
        foreach (var entry in entries.OrderBy(e => e.Date.Date).ThenBy(e => e.Order))
        {
            balance += entry.Charge - entry.Payment;
            charges += entry.Charge;
            paymentsInPeriod += entry.Payment;
            data.Rows.Add(new AccountingRow
            {
                Label = entry.Details,
                Values =
                [
                    FormatStatementDate(entry.Date),
                    entry.Charge > 0 ? FormatCurrency(entry.Charge) : "",
                    entry.Payment > 0 ? FormatCurrency(entry.Payment) : "",
                    FormatCurrency(balance)
                ]
            });
        }

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", ""] });
        data.Rows.Add(new AccountingRow { Label = "Invoiced in this period", Values = ["", FormatCurrency(charges), "", ""], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { Label = "Paid in this period", Values = ["", "", FormatCurrency(paymentsInPeriod), ""], RowType = AccountingRowType.SubtotalRow });
        data.Rows.Add(new AccountingRow { Label = $"Balance due on {FormatStatementDate(end)}", Values = ["", "", "", FormatCurrency(balance)], RowType = AccountingRowType.TotalRow });

        var paidSince = invoices
            .Where(i => i.Invoice.IssueDate.Date <= end)
            .Sum(i => i.PaidOnOrBefore(today) - i.PaidOnOrBefore(end));
        if (paidSince > 0)
        {
            data.Rows.Add(new AccountingRow { Label = $"Paid since {FormatStatementDate(end)}", Values = ["", "", FormatCurrency(paidSince), ""] });
            data.Rows.Add(new AccountingRow
            {
                Label = $"Still owed from this statement on {FormatStatementDate(today)}",
                Values = ["", "", "", FormatCurrency(balance - paidSince)],
                RowType = AccountingRowType.GrandTotalRow
            });
        }

        data.Footnote = $"This statement covers {FormatStatementDate(start)} to {FormatStatementDate(end)} only. Invoices from other dates are not listed.";
        return data;
    }

    #endregion

    #region Sales by Customer and Expenses by Supplier

    /// <summary>Each customer's sales before tax in the period, less refunds, adding up to the Income Statement's revenue.</summary>
    private AccountingTableData GetSalesByCustomerData()
    {
        var data = new AccountingTableData
        {
            Title = "Sales by Customer",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Customer", "Sales", "Refunds", "Net Sales", "Share"],
            ColumnWidthRatios = [0.36, 0.16, 0.16, 0.16, 0.16]
        };

        var sales = new Dictionary<string, decimal>();
        var refunds = new Dictionary<string, decimal>();
        if (companyData != null)
        {
            foreach (var revenue in companyData.Revenues.Where(r => IsInDateRange(r.Date)))
            {
                var key = revenue.CustomerId ?? "";
                sales[key] = sales.GetValueOrDefault(key) + ToDisplay(revenue.EffectiveSubtotalUSD, revenue.Date);
            }

            foreach (var refund in companyData.Payments.Where(p => p.IsRefund && p.Date >= RangeStart && p.Date <= RangeEnd))
            {
                var key = !string.IsNullOrEmpty(refund.CustomerId)
                    ? refund.CustomerId
                    : InvoicesById.TryGetValue(refund.InvoiceId ?? "", out var invoice) ? invoice.CustomerId : "";
                refunds[key] = refunds.GetValueOrDefault(key) + ToDisplay(RefundAggregator.PreTaxPortionUSD(refund, InvoicesById), refund.Date);
            }
        }

        // Sales without a customer on file go last, so their row reads as the rest rather than as a customer called that.
        var rows = sales.Keys.Union(refunds.Keys)
            .Select(id => (Name: CustomerName(id, "(Sales with no customer)"), Known: IsKnownCustomer(id),
                Sales: sales.GetValueOrDefault(id), Refunds: refunds.GetValueOrDefault(id)))
            .Select(c => (c.Name, c.Known, c.Sales, c.Refunds, Net: c.Sales - c.Refunds))
            .OrderBy(c => c.Known ? 0 : 1)
            .ThenByDescending(c => c.Net)
            .ThenBy(c => c.Name)
            .ToList();

        var totalNet = rows.Sum(c => c.Net);
        foreach (var c in rows)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = c.Name,
                Values =
                [
                    FormatCurrency(c.Sales),
                    c.Refunds != 0 ? FormatCurrencyWithSign(-c.Refunds) : "",
                    FormatCurrencyWithSign(c.Net),
                    totalNet > 0 ? $"{c.Net / totalNet * 100:0.0}%" : ""
                ]
            });
        }

        if (rows.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "No sales in this period", Values = ["", "", "", ""] });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", ""] });
        data.Rows.Add(new AccountingRow
        {
            Label = "Total",
            Values =
            [
                FormatCurrency(rows.Sum(c => c.Sales)),
                FormatCurrencyWithSign(-rows.Sum(c => c.Refunds)),
                FormatCurrencyWithSign(totalNet),
                totalNet > 0 ? "100.0%" : ""
            ],
            RowType = AccountingRowType.TotalRow
        });

        data.Footnote = "Sales are before tax, the same figures the Income Statement's revenue adds up.";
        return data;
    }

    // The brackets mark these as descriptions, so nobody takes them for a customer's name.
    private string CustomerName(string id, string noneLabel) =>
        string.IsNullOrEmpty(id) ? noneLabel : companyData?.GetCustomer(id)?.Name ?? "(Customer no longer on file)";

    private bool IsKnownCustomer(string id) => !string.IsNullOrEmpty(id) && companyData?.GetCustomer(id) != null;

    /// <summary>What was spent with each supplier in the period, before and after tax, stock bought for resale included.</summary>
    private AccountingTableData GetExpensesBySupplierData()
    {
        var data = new AccountingTableData
        {
            Title = "Expenses by Supplier",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Supplier", "Expenses", "Before Tax", "Tax", "Total"],
            ColumnWidthRatios = [0.36, 0.13, 0.17, 0.17, 0.17]
        };

        // Wages and remittances are paid to employees and the tax agencies, not to a supplier, so they get a row of their own rather than counting as missing one.
        const string payrollKey = "\0payroll";
        var wageIds = companyData == null ? [] : PayrollExpenseIds;

        var rows = (companyData?.Expenses ?? [])
            .Where(e => IsInDateRange(e.Date))
            .GroupBy(e => wageIds.Contains(e.Id) ? payrollKey : e.SupplierId ?? "")
            .Select(g => (
                Name: g.Key == payrollKey
                    ? "(Payroll)"
                    : string.IsNullOrEmpty(g.Key)
                        ? "(Expenses with no supplier)"
                        : companyData!.GetSupplier(g.Key)?.Name ?? "(Supplier no longer on file)",
                Known: g.Key != payrollKey && !string.IsNullOrEmpty(g.Key) && companyData!.GetSupplier(g.Key) != null,
                Count: g.Count(),
                BeforeTax: g.Sum(e => ToDisplay(e.EffectiveSubtotalUSD, e.Date)),
                Total: g.Sum(e => ToDisplay(e.EffectiveTotalUSD, e.Date))))
            .OrderBy(s => s.Known ? 0 : 1)
            .ThenByDescending(s => s.Total)
            .ThenBy(s => s.Name)
            .ToList();

        foreach (var s in rows)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = s.Name,
                Values = [s.Count.ToString(), FormatCurrency(s.BeforeTax), FormatCurrency(s.Total - s.BeforeTax), FormatCurrency(s.Total)]
            });
        }

        if (rows.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "No expenses in this period", Values = ["", "", "", ""] });

        var beforeTax = rows.Sum(s => s.BeforeTax);
        var total = rows.Sum(s => s.Total);
        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", ""] });
        data.Rows.Add(new AccountingRow
        {
            Label = "Total",
            Values = [rows.Sum(s => s.Count).ToString(), FormatCurrency(beforeTax), FormatCurrency(total - beforeTax), FormatCurrency(total)],
            RowType = AccountingRowType.TotalRow
        });

        data.Footnote = "Includes stock bought for resale, which the Income Statement counts as cost of goods sold once it sells.";
        return data;
    }

    #endregion

    #region Payroll Summary

    /// <summary>What each employee was paid and had withheld, from the same pay runs as the Payroll Remittance, so a voided run and its reversal cancel out.</summary>
    private AccountingTableData GetPayrollSummaryData()
    {
        var data = new AccountingTableData
        {
            Title = "Payroll Summary",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Employee", "Gross Pay", "CPP/QPP", "EI/QPIP", "Income Tax", "Net Pay"],
            ColumnWidthRatios = [0.25, 0.15, 0.15, 0.15, 0.15, 0.15]
        };

        var runs = (companyData?.PayRuns ?? [])
            .Where(r => r.Status != Models.Payroll.PayRunStatus.Draft && IsInDateRange(r.PayDate))
            .OrderBy(r => r.PayDate)
            .ToList();

        var employees = runs
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.EmployeeId)
            .Select(g => (
                Name: companyData!.Employees.FirstOrDefault(e => e.Id == g.Key)?.Name ?? g.Last().EmployeeName,
                Gross: g.Sum(l => l.GrossPay),
                Pension: g.Sum(l => l.CppEmployee + l.Cpp2Employee),
                Insurance: g.Sum(l => l.EiEmployee + l.QpipEmployee),
                Tax: g.Sum(l => l.FederalTax + l.ProvincialTax),
                Net: g.Sum(l => l.NetPay),
                PensionEmployer: g.Sum(l => l.CppEmployer + l.Cpp2Employer),
                InsuranceEmployer: g.Sum(l => l.EiEmployer + l.QpipEmployer)))
            .OrderBy(e => e.Name)
            .ToList();

        foreach (var e in employees)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = e.Name,
                Values = [FormatCurrency(e.Gross), FormatCurrency(e.Pension), FormatCurrency(e.Insurance), FormatCurrency(e.Tax), FormatCurrency(e.Net)]
            });
        }

        if (employees.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "No payroll in this period", Values = ["", "", "", "", ""] });

        var gross = employees.Sum(e => e.Gross);
        var pensionEmployer = employees.Sum(e => e.PensionEmployer);
        var insuranceEmployer = employees.Sum(e => e.InsuranceEmployer);

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", "", ""] });
        data.Rows.Add(new AccountingRow
        {
            Label = "Total",
            Values =
            [
                FormatCurrency(gross),
                FormatCurrency(employees.Sum(e => e.Pension)),
                FormatCurrency(employees.Sum(e => e.Insurance)),
                FormatCurrency(employees.Sum(e => e.Tax)),
                FormatCurrency(employees.Sum(e => e.Net))
            ],
            RowType = AccountingRowType.TotalRow
        });
        data.Rows.Add(new AccountingRow
        {
            Label = "Employer contributions",
            Values = ["", FormatCurrency(pensionEmployer), FormatCurrency(insuranceEmployer), "", ""]
        });
        data.Rows.Add(new AccountingRow
        {
            Label = "Total payroll cost",
            Values = [FormatCurrency(gross + pensionEmployer + insuranceEmployer), "", "", "", ""],
            RowType = AccountingRowType.SubtotalRow
        });

        data.Footnote = runs.Count == 1 ? "From 1 pay run." : $"From {runs.Count} pay runs.";
        return data;
    }

    #endregion

    #region Rentals

    /// <summary>What is rented out today and what came back in the period, in the company's currency because rentals are recorded in it.</summary>
    private AccountingTableData GetRentalsData()
    {
        var currency = GetCurrencyCode();
        string Money(decimal amount) => CurrencyInfo.FormatAmount(amount, currency);

        var data = new AccountingTableData
        {
            Title = "Rentals",
            Subtitle = $"Amounts in {currency}",
            ColumnHeaders = ["Rental", "Customer", "Due / Returned", "Earned", "Deposit Held"],
            ColumnWidthRatios = [0.3, 0.22, 0.16, 0.16, 0.16]
        };

        var today = DateTime.Today;
        var rentals = companyData?.Rentals ?? [];

        string Items(Models.Rentals.RentalRecord rental) => string.Join(", ", rental.EffectiveLineItems()
            .Select(li => li.Quantity > 1
                ? $"{RentalBookings.ItemName(companyData, li.RentalItemId)} x{li.Quantity}"
                : RentalBookings.ItemName(companyData, li.RentalItemId)));

        decimal Kept(Models.Rentals.RentalRecord rental) =>
            Math.Max(0m, rental.SecurityDeposit - (rental.DepositRefunded ?? rental.SecurityDeposit));

        decimal Held(Models.Rentals.RentalRecord rental) => rental.Status switch
        {
            RentalStatus.Active or RentalStatus.Overdue => rental.SecurityDeposit,
            RentalStatus.Returned when RentalBookings.DepositRefundPending(companyData, rental) => rental.DepositRefunded ?? 0m,
            _ => 0m
        };

        var outNow = rentals
            .Where(r => r.Status is RentalStatus.Active or RentalStatus.Overdue)
            .OrderBy(r => r.DueDate)
            .ToList();

        data.Rows.Add(new AccountingRow { Label = $"RENTED OUT ON {FormatStatementDate(today).ToUpperInvariant()}", RowType = AccountingRowType.SectionHeader, Values = ["", "", "", ""] });
        foreach (var rental in outNow)
        {
            var due = FormatStatementDate(rental.DueDate);
            data.Rows.Add(new AccountingRow
            {
                Label = Items(rental),
                IndentLevel = 1,
                Values = [CustomerName(rental.CustomerId, "(No customer)"), rental.DueDate.Date < today ? $"{due}, overdue" : due, "", Money(Held(rental))]
            });
        }
        if (outNow.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "Nothing is rented out", IndentLevel = 1, Values = ["", "", "", ""] });

        var returned = rentals
            .Where(r => r.Status == RentalStatus.Returned && r.ReturnDate is { } back && IsInDateRange(back))
            .OrderBy(r => r.ReturnDate)
            .ToList();

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = ["", "", "", ""] });
        data.Rows.Add(new AccountingRow { Label = "RETURNED IN THIS PERIOD", RowType = AccountingRowType.SectionHeader, Values = ["", "", "", ""] });
        foreach (var rental in returned)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = Items(rental),
                IndentLevel = 1,
                Values = [CustomerName(rental.CustomerId, "(No customer)"), FormatStatementDate(rental.ReturnDate!.Value), Money((rental.TotalCost ?? 0m) + Kept(rental)), Money(Held(rental))]
            });
        }
        if (returned.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "Nothing came back in this period", IndentLevel = 1, Values = ["", "", "", ""] });

        data.Rows.Add(new AccountingRow { RowType = AccountingRowType.SeparatorLine, Values = ["", "", "", ""] });
        data.Rows.Add(new AccountingRow
        {
            Label = "Earned from rentals returned in this period",
            Values = ["", "", Money(returned.Sum(r => (r.TotalCost ?? 0m) + Kept(r))), ""],
            RowType = AccountingRowType.SubtotalRow
        });
        data.Rows.Add(new AccountingRow
        {
            Label = "Deposits held today",
            Values = ["", "", "", Money(rentals.Sum(Held))],
            RowType = AccountingRowType.TotalRow
        });

        data.Footnote = "Earned is the rental charges plus any deposit kept. Reserved and cancelled rentals are not listed.";
        return data;
    }

    #endregion

    #region Asset Register

    /// <summary>Every fixed asset held at the end date, with its depreciation in the period and so far, then those that left the books in the period.</summary>
    private AccountingTableData GetAssetRegisterData()
    {
        var data = new AccountingTableData
        {
            Title = "Asset Register",
            Subtitle = GetCurrencySubtitle(),
            ColumnHeaders = ["Asset", "Type", "Purchased", "Cost", "This Period", "Accumulated", "Book Value"],
            ColumnWidthRatios = [0.19, 0.21, 0.11, 0.1225, 0.1225, 0.1225, 0.1225]
        };

        var end = EndDateForValuation;
        var assets = companyData?.Assets ?? [];
        List<string> Blank() => ["", "", "", "", "", ""];

        string Shown(FixedAsset asset, decimal amount, DateTime? date = null) =>
            AssetToDisplay(asset, amount, date) is { } shown ? FormatCurrencyWithSign(shown) : PendingText;

        decimal? Sum(IEnumerable<FixedAsset> list, Func<FixedAsset, decimal> amount)
        {
            decimal total = 0m;
            foreach (var asset in list)
            {
                if (AssetToDisplay(asset, amount(asset)) is not { } shown)
                    return null;
                total += shown;
            }
            return total;
        }

        string Total(decimal? amount) => amount is { } value ? FormatCurrencyWithSign(value) : PendingText;

        static string TypeLabel(FixedAsset asset) =>
            string.IsNullOrEmpty(asset.CcaClass) ? asset.Type.ToDisplayName() : $"{asset.Type.ToDisplayName()}, Class {asset.CcaClass}";

        var held = assets
            .Where(a => AssetDepreciation.IsHeldOn(a, end))
            .OrderBy(a => a.PurchaseDate)
            .ThenBy(a => a.Name)
            .ToList();

        data.Rows.Add(new AccountingRow { Label = $"HELD ON {FormatStatementDate(end).ToUpperInvariant()}", RowType = AccountingRowType.SectionHeader, Values = Blank() });
        foreach (var asset in held)
        {
            data.Rows.Add(new AccountingRow
            {
                Label = asset.Name,
                IndentLevel = 1,
                Values =
                [
                    TypeLabel(asset),
                    FormatStatementDate(asset.PurchaseDate),
                    Shown(asset, asset.Cost),
                    Shown(asset, AssetDepreciation.Between(asset, RangeStart, RangeEnd)),
                    Shown(asset, AssetDepreciation.AccumulatedAsOf(asset, end)),
                    Shown(asset, AssetDepreciation.BookValueAsOf(asset, end))
                ]
            });
        }
        if (held.Count == 0)
            data.Rows.Add(new AccountingRow { Label = "No fixed assets on this date", IndentLevel = 1, Values = Blank() });

        data.Rows.Add(new AccountingRow
        {
            Label = "Total held",
            Values =
            [
                "", "",
                Total(Sum(held, a => a.Cost)),
                Total(Sum(held, a => AssetDepreciation.Between(a, RangeStart, RangeEnd))),
                Total(Sum(held, a => AssetDepreciation.AccumulatedAsOf(a, end))),
                Total(Sum(held, a => AssetDepreciation.BookValueAsOf(a, end)))
            ],
            RowType = AccountingRowType.TotalRow
        });

        var gone = assets
            .Where(a => a.DisposedDate is { } date && IsInDateRange(date))
            .OrderBy(a => a.DisposedDate)
            .ToList();

        if (gone.Count > 0)
        {
            data.Rows.Add(new AccountingRow { RowType = AccountingRowType.BlankRow, Values = Blank() });
            data.Rows.Add(new AccountingRow
            {
                Label = "SOLD OR DISPOSED IN THIS PERIOD",
                RowType = AccountingRowType.SectionHeader,
                Values = ["", "Left the books", "Cost", "This Period", "Received", "Gain or Loss"]
            });
            foreach (var asset in gone)
            {
                var received = AssetToDisplay(asset, asset.Proceeds, asset.DisposedDate);
                var bookValue = AssetToDisplay(asset, AssetDepreciation.BookValueAtDisposal(asset));
                data.Rows.Add(new AccountingRow
                {
                    Label = asset.Name,
                    IndentLevel = 1,
                    Values =
                    [
                        TypeLabel(asset),
                        FormatStatementDate(asset.DisposedDate!.Value),
                        Shown(asset, asset.Cost),
                        Shown(asset, AssetDepreciation.Between(asset, RangeStart, RangeEnd)),
                        received is { } r ? FormatCurrency(r) : PendingText,
                        received is { } got && bookValue is { } worth ? FormatCurrencyWithSign(got - worth) : PendingText
                    ]
                });
            }
        }

        data.Footnote = "Cost leaves out any tax on the purchase that is claimed back. Land is not depreciated. Depreciation is booked at the end of each month, and not in the month an asset is sold or disposed of.";
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

        var taxSystem = TaxSystems.For(country);

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

        var taxCollectedByRate = new Dictionary<decimal, decimal>();
        foreach (var rev in filteredRevenues)
            AddTaxByRate(taxCollectedByRate, rev);

        // Tax paid on expenses, grouped by tax rate
        var filteredExpenses = companyData.Expenses
            .Where(e => IsInDateRange(e.Date))
            .ToList();

        var taxPaidByRate = new Dictionary<decimal, decimal>();
        foreach (var exp in filteredExpenses)
        {
            var claimableUSD = PurchaseTax.ClaimableUSD(exp, Rule);
            if (claimableUSD == exp.EffectiveTaxAmountUSD)
                AddTaxByRate(taxPaidByRate, exp);
            else if (claimableUSD != 0 && Rule.ClaimableRate is { } rate)
                taxPaidByRate[rate] = taxPaidByRate.GetValueOrDefault(rate) + ToDisplay(claimableUSD, exp.Date);
        }

        // Tax handed back on refunds in range is no longer owed (docs/Calculations.md §8).
        var taxRefunded = RefundAggregator.GetRefundedTaxInDateRangeDisplay(
            companyData.Payments, InvoicesById, RangeStart, RangeEnd, ToDisplay);

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

        // Where no tax on purchases is claimed back, as with US sales tax, there is nothing to set against the tax collected.
        if (Rule.ClaimableRate != 0)
        {
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
        }

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

            // Stated rather than left to be inferred from a nil line.
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
            Payroll.CraFormat.IsQuebec(line.Province);

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
