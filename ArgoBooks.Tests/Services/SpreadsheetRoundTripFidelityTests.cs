using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// What survives an export followed by an import.
///
/// The spreadsheet export is a report and the backup is the restore path, but people do export
/// and re-import, and until now that quietly changed the books: every invoice came back with a
/// hash in front of its id and no lines on it, every multi-unit expense came back as one unit,
/// and every foreign-currency transaction came back as company currency.
///
/// These go through the real Excel writer and the real Excel reader rather than testing either
/// half in isolation, because each of those bugs lived in the seam between them and the two
/// halves were individually self-consistent.
/// </summary>
public class SpreadsheetRoundTripFidelityTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    private static readonly string[] AllSheets =
    [
        "Customers", "Suppliers", "Products", "Invoices", "Invoice Line Items",
        "Payments", "Expenses", "Revenue", "Purchase Orders", "Purchase Order Line Items",
        "Employees", "Pay Runs",
    ];

    private static CompanyData Source()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";

        data.Customers.Add(new Customer { Id = "CUS-003", Name = "Jane Doe", Email = "jane@x.com" });
        data.Suppliers.Add(new Supplier { Id = "SUP-014", Name = "Acme Hardware" });
        data.Products.Add(new Product { Id = "PRD-001", Name = "Widget" });

        data.Invoices.Add(new Invoice
        {
            Id = "INV-2026-00001",
            InvoiceNumber = "#INV-2026-00001",
            CustomerId = "CUS-003",
            IssueDate = new DateTime(2026, 5, 4),
            DueDate = new DateTime(2026, 6, 4),
            Subtotal = 300m,
            TaxAmount = 15m,
            Total = 315m,
            Status = Core.Enums.InvoiceStatus.Sent,
            LineItems =
            {
                new LineItem { ProductId = "PRD-001", Description = "Widget", Quantity = 2m, UnitPrice = 100m, TaxRate = 0.05m },
                new LineItem { Description = "Delivery", Quantity = 1m, UnitPrice = 100m, TaxRate = 0.05m, Discount = 0m },
            },
        });

        data.Payments.Add(new Payment
        {
            Id = "PAY-001",
            InvoiceId = "INV-2026-00001",
            CustomerId = "CUS-003",
            Date = new DateTime(2026, 5, 11),
            Amount = 100m,
        });

        data.Expenses.Add(new Expense
        {
            Id = "PUR-001",
            Date = new DateTime(2026, 5, 4),
            SupplierId = "SUP-014",
            Description = "Screws",
            Quantity = 3m,
            UnitPrice = 12m,
            Amount = 36m,
            TaxAmount = 1.80m,
            ShippingCost = 5m,
            Total = 42.80m,
            ReferenceNumber = "RCPT-88",
        });

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = new DateTime(2026, 5, 6),
            CustomerId = "CUS-003",
            Description = "Consulting",
            Quantity = 4m,
            UnitPrice = 50m,
            Amount = 200m,
            Total = 200m,
        });

        return data;
    }

    private Task<CompanyData> RoundTripAsync(CompanyData source) => RoundTripAsync(source, AllSheets);

    private async Task<CompanyData> RoundTripAsync(CompanyData source, string[] sheets)
    {
        await new SpreadsheetExportService().ExportToExcelAsync(_path, source, [.. sheets], null, null);

        var target = new CompanyData();
        target.Settings.Localization.Currency = "USD";
        await new SpreadsheetImportService().ImportFromExcelAsync(_path, target);

        return target;
    }

    #region Invoices

    [Fact]
    public async Task AnInvoiceKeepsItsIdInsteadOfGainingAHash()
    {
        CompanyData target = await RoundTripAsync(Source());

        Invoice invoice = Assert.Single(target.Invoices);

        Assert.Equal("INV-2026-00001", invoice.Id);
        Assert.Equal("#INV-2026-00001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task APaymentStillPointsAtItsInvoice()
    {
        CompanyData target = await RoundTripAsync(Source());

        Payment payment = Assert.Single(target.Payments);

        Assert.Contains(target.Invoices, i => i.Id == payment.InvoiceId);
    }

    [Fact]
    public async Task AnInvoiceKeepsItsLines()
    {
        CompanyData target = await RoundTripAsync(Source());

        Invoice invoice = Assert.Single(target.Invoices);

        Assert.Equal(2, invoice.LineItems.Count);

        LineItem widget = invoice.LineItems[0];
        Assert.Equal("PRD-001", widget.ProductId);
        Assert.Equal("Widget", widget.Description);
        Assert.Equal(2m, widget.Quantity);
        Assert.Equal(100m, widget.UnitPrice);
        Assert.Equal(0.05m, widget.TaxRate);
    }

    [Fact]
    public async Task ImportingTheSameFileTwice_DoesNotDoubleTheLines()
    {
        // Re-running an import after fixing something in the sheet is normal. Appending rather
        // than replacing would silently double every invoice.
        CompanyData source = Source();
        await new SpreadsheetExportService().ExportToExcelAsync(_path, source, [.. AllSheets], null, null);

        var target = new CompanyData();
        target.Settings.Localization.Currency = "USD";

        var importer = new SpreadsheetImportService();
        await importer.ImportFromExcelAsync(_path, target);
        await importer.ImportFromExcelAsync(_path, target);

        Assert.Equal(2, Assert.Single(target.Invoices).LineItems.Count);
    }

    [Fact]
    public async Task ASheetThatOnlyHasAnInvoiceNumber_StillImports()
    {
        // Spreadsheets from other systems have no ID column. The number has to keep working as the identifier.
        var source = new CompanyData();
        source.Settings.Localization.Currency = "USD";
        source.Customers.Add(new Customer { Id = "CUS-003", Name = "Jane Doe" });
        source.Invoices.Add(new Invoice
        {
            Id = "INV-9",
            InvoiceNumber = "INV-9",
            CustomerId = "CUS-003",
            IssueDate = new DateTime(2026, 5, 4),
            Total = 10m,
        });

        CompanyData target = await RoundTripAsync(source);

        Invoice invoice = Assert.Single(target.Invoices);
        Assert.Equal("INV-9", invoice.Id);
        Assert.Equal("INV-9", invoice.InvoiceNumber);
    }

    #endregion

    #region Revenue from invoices

    private static CompanyData PaidInvoiceSource(bool withLinkedRevenue)
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Customers.Add(new Customer { Id = "CUS-003", Name = "Jane Doe" });
        data.Invoices.Add(new Invoice
        {
            Id = "INV-2025-00001",
            InvoiceNumber = "#INV-2025-00001",
            CustomerId = "CUS-003",
            IssueDate = new DateTime(2025, 5, 4),
            Subtotal = 300m,
            TaxAmount = 15m,
            Total = 315m,
            AmountPaid = 315m,
            Balance = 0m,
            Status = Core.Enums.InvoiceStatus.Paid,
        });

        if (withLinkedRevenue)
        {
            data.Revenues.Add(new Revenue
            {
                Id = "REV-2025-00001",
                Date = new DateTime(2025, 5, 4),
                CustomerId = "CUS-003",
                Description = "Widget",
                InvoiceId = "INV-2025-00001",
                Quantity = 1m,
                UnitPrice = 300m,
                TaxAmount = 15m,
                Total = 315m,
            });
        }

        data.Revenues.Add(new Revenue
        {
            Id = "REV-2025-00002",
            Date = new DateTime(2025, 5, 6),
            CustomerId = "CUS-003",
            Description = "Consulting",
            Quantity = 1m,
            UnitPrice = 200m,
            Total = 200m,
        });

        return data;
    }

    // The export modal writes Revenue before Invoices; the other order is how AllSheets lists them.
    public static TheoryData<string[]> SheetOrders => new()
    {
        new[] { "Customers", "Revenue", "Invoices", "Payments" },
        new[] { "Customers", "Invoices", "Payments", "Revenue" },
    };

    [Theory]
    [MemberData(nameof(SheetOrders))]
    public async Task APaidInvoicesRevenue_ComesBackLinkedAndIsNotCountedTwice(string[] sheets)
    {
        CompanyData target = await RoundTripAsync(PaidInvoiceSource(withLinkedRevenue: true), sheets);

        Assert.Equal(["REV-2025-00001", "REV-2025-00002"], target.Revenues.Select(r => r.Id).Order().ToArray());
        Revenue linked = Assert.Single(target.Revenues, r => r.InvoiceId == "INV-2025-00001");
        Assert.Equal("REV-2025-00001", linked.Id);
        Assert.Equal(515m, target.Revenues.Sum(r => r.Total));
    }

    [Fact]
    public async Task AKeptDeposit_StaysAKeptDeposit()
    {
        CompanyData source = PaidInvoiceSource(withLinkedRevenue: true);
        source.Revenues.Add(new Revenue
        {
            Id = "REV-2025-00003",
            Date = new DateTime(2025, 6, 1),
            CustomerId = "CUS-003",
            Description = "Kept security deposit",
            InvoiceId = "INV-2025-00001",
            IsKeptDeposit = true,
            Quantity = 1m,
            UnitPrice = 50m,
            Total = 50m,
        });

        CompanyData target = await RoundTripAsync(source);

        Revenue deposit = target.Revenues.Single(r => r.Id == "REV-2025-00003");
        Assert.True(deposit.IsKeptDeposit);
        Assert.Equal("INV-2025-00001", deposit.InvoiceId);
        Assert.Single(target.Revenues, r => r.InvoiceId == "INV-2025-00001" && !r.IsKeptDeposit);
    }

    [Fact]
    public async Task ARevenueCreatedForAPaidInvoice_NeverTakesAnImportedRevenuesId()
    {
        // The id counter is only brought up to date after every sheet is in.
        CompanyData source = PaidInvoiceSource(withLinkedRevenue: false);
        string clash = "REV-2025-00001";
        source.Revenues.Single().Id = clash;

        CompanyData target = await RoundTripAsync(source);

        Assert.Equal(2, target.Revenues.Count);
        Assert.Equal(2, target.Revenues.Select(r => r.Id).Distinct().Count());
        Revenue sale = target.Revenues.Single(r => r.Id == clash);
        Assert.Null(sale.InvoiceId);
        Assert.Equal(200m, sale.Total);
        Assert.Equal(315m, Assert.Single(target.Revenues, r => r.InvoiceId == "INV-2025-00001").Total);
    }

    public static TheoryData<string[]> SheetOrdersWithLines => new()
    {
        new[] { "Customers", "Products", "Revenue", "Invoices", "Invoice Line Items" },
        new[] { "Customers", "Products", "Invoices", "Invoice Line Items", "Revenue" },
    };

    [Theory]
    [MemberData(nameof(SheetOrdersWithLines))]
    public async Task RevenueFromAMultiLineInvoice_KeepsTheInvoicesLinesInsteadOfMakingAProduct(string[] sheets)
    {
        // Its description only summarises the lines, "Widget (+2 more)", and the importer took
        // that for a product name and created a product and a category called that.
        CompanyData source = PaidInvoiceSource(withLinkedRevenue: true);
        source.Products.Add(new Product { Id = "PRD-001", Name = "Widget" });
        source.Products.Add(new Product { Id = "PRD-002", Name = "Gadget" });
        source.Products.Add(new Product { Id = "PRD-003", Name = "Gizmo" });
        List<LineItem> lines =
        [
            new() { ProductId = "PRD-001", Description = "Widget", Quantity = 1m, UnitPrice = 100m },
            new() { ProductId = "PRD-002", Description = "Gadget", Quantity = 1m, UnitPrice = 100m },
            new() { ProductId = "PRD-003", Description = "Gizmo", Quantity = 1m, UnitPrice = 100m },
        ];
        source.Invoices.Single().LineItems = lines;
        Revenue fromInvoice = source.Revenues.Single(r => r.Id == "REV-2025-00001");
        fromInvoice.Description = "Widget (+2 more)";
        fromInvoice.LineItems = [.. lines];

        CompanyData target = await RoundTripAsync(source, sheets);

        Assert.DoesNotContain(target.Products, p => p.Name == "Widget (+2 more)");
        Assert.DoesNotContain(target.Categories, c => c.Name == "Widget (+2 more)");
        Revenue revenue = target.Revenues.Single(r => r.Id == "REV-2025-00001");
        Assert.Equal(["PRD-001", "PRD-002", "PRD-003"], revenue.LineItems.Select(li => li.ProductId ?? "").ToArray());
    }

    [Fact]
    public async Task ARevenueNamingAnInvoiceThatIsNotThere_ImportsAsAPlainSale()
    {
        var source = new CompanyData();
        source.Settings.Localization.Currency = "USD";
        source.Revenues.Add(new Revenue
        {
            Id = "REV-2025-00009",
            Date = new DateTime(2025, 5, 6),
            Description = "Widget",
            InvoiceId = "1001",
            Quantity = 1m,
            UnitPrice = 40m,
            Total = 40m,
        });

        CompanyData target = await RoundTripAsync(source, ["Revenue"]);

        Revenue revenue = Assert.Single(target.Revenues);
        Assert.Null(revenue.InvoiceId);
        Product widget = Assert.Single(target.Products, p => p.Name == "Widget");
        Assert.Equal(widget.Id, Assert.Single(revenue.LineItems).ProductId);
    }

    [Fact]
    public async Task ThePaidInvoicesRevenueMadeOnImport_TakesTheInvoicesLines()
    {
        // With no Revenue sheet the importer makes the paid invoice's revenue itself.
        CompanyData source = PaidInvoiceSource(withLinkedRevenue: false);
        source.Revenues.Clear();
        source.Products.Add(new Product { Id = "PRD-001", Name = "Widget" });
        source.Products.Add(new Product { Id = "PRD-002", Name = "Gadget" });
        source.Invoices.Single().LineItems =
        [
            new() { ProductId = "PRD-001", Description = "Widget", Quantity = 2m, UnitPrice = 100m, TaxRate = 0.05m },
            new() { ProductId = "PRD-002", Description = "Gadget", Quantity = 1m, UnitPrice = 100m, TaxRate = 0.05m },
        ];

        CompanyData target = await RoundTripAsync(source, ["Customers", "Products", "Invoices", "Invoice Line Items"]);

        Revenue revenue = Assert.Single(target.Revenues);
        Assert.Equal("INV-2025-00001", revenue.InvoiceId);
        Assert.Equal(["PRD-001", "PRD-002"], revenue.LineItems.Select(li => li.ProductId ?? "").ToArray());
        Assert.Equal([2m, 1m], revenue.LineItems.Select(li => li.Quantity).ToArray());

        List<ArgoBooks.Core.Models.Reports.ProductSalesData> sales = ProductSalesService.GetProductSales(
            target, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), cashBasis: true);
        Assert.Equal(315m, sales.Sum(s => s.RevenueUSD));
        Assert.Equal(3m, sales.Sum(s => s.UnitsSold));
    }

    #endregion

    #region Quantities

    [Fact]
    public async Task AMultiUnitExpense_ComesBackWithItsQuantity()
    {
        // The Unit Price column was being fed Amount, and there was no Quantity column at all,
        // so three units at $12 came back as one unit at $36.
        CompanyData target = await RoundTripAsync(Source());

        Expense expense = Assert.Single(target.Expenses);

        Assert.Equal(3m, expense.Quantity);
        Assert.Equal(12m, expense.UnitPrice);
        Assert.Equal(36m, expense.Amount);
    }

    [Fact]
    public async Task AMultiUnitRevenueRow_ComesBackWithItsQuantity()
    {
        CompanyData target = await RoundTripAsync(Source());

        Revenue revenue = target.Revenues.Single(r => r.Id == "REV-001");

        Assert.Equal(4m, revenue.Quantity);
        Assert.Equal(50m, revenue.UnitPrice);
    }

    [Fact]
    public async Task AnExpenseKeepsItsShippingAndReference()
    {
        CompanyData target = await RoundTripAsync(Source());

        Expense expense = Assert.Single(target.Expenses);

        Assert.Equal(5m, expense.ShippingCost);
        Assert.Equal("RCPT-88", expense.ReferenceNumber);
    }

    #endregion

    #region Currency

    [Theory]
    [InlineData("Invoices")]
    [InlineData("Expenses")]
    [InlineData("Revenue")]
    [InlineData("Payments")]
    [InlineData("Purchase Orders")]
    public async Task EverySheetThatCanCarryACurrency_PrintsIt(string sheet)
    {
        // The importer has accepted a Currency column on all five of these for a long time; the
        // export simply never wrote one. A sheet showing 1,200 with no indication it is euros is
        // a wrong report before it is a lossy one.
        CompanyData source = Source();
        source.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = "PO-001",
            SupplierId = "SUP-014",
            OrderDate = new DateTime(2026, 5, 2),
            Total = 240m,
        });

        foreach (Expense e in source.Expenses) e.OriginalCurrency = "EUR";
        foreach (Revenue r in source.Revenues) r.OriginalCurrency = "EUR";
        foreach (Invoice i in source.Invoices) i.OriginalCurrency = "EUR";
        foreach (Payment p in source.Payments) p.OriginalCurrency = "EUR";
        foreach (PurchaseOrder p in source.PurchaseOrders) p.OriginalCurrency = "EUR";

        string csv = Path.ChangeExtension(_path, ".csv");

        try
        {
            await new SpreadsheetExportService().ExportToCsvAsync(csv, source, [sheet], null, null);
            List<List<string>> rows = CsvReader.ReadAllRows(csv, out List<string> headers);

            int index = headers.IndexOf("Currency");
            Assert.True(index >= 0, $"no Currency column on {sheet}: {string.Join(", ", headers)}");
            Assert.All(rows, r => Assert.Equal("EUR", r[index]));
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public async Task ATransactionInTheCompanysOwnCurrency_KeepsItThroughARoundTrip()
    {
        CompanyData source = Source();
        source.Settings.Localization.Currency = "CAD";
        foreach (Expense e in source.Expenses) e.OriginalCurrency = "CAD";

        await new SpreadsheetExportService().ExportToExcelAsync(_path, source, [.. AllSheets], null, null);

        var target = new CompanyData();
        target.Settings.Localization.Currency = "CAD";
        await new SpreadsheetImportService().ImportFromExcelAsync(_path, target);

        Assert.Equal("CAD", Assert.Single(target.Expenses).OriginalCurrency);
    }

    #endregion

    #region Payroll

    private static Employee Dana() => new()
    {
        Id = "EMP-001",
        Name = "Dana Smith",
        EmployeeNumber = "42",
        Sin = "046454286",
        Province = "AB",
        PayType = PayType.Salary,
        PayRate = 62400m,
        PayFrequency = PayFrequency.Biweekly,
        StandardHoursPerWeek = 37.5m,
        FederalClaimAmount = 16500m,
        ProvincialClaimAmount = 22769m,
        IsCppExempt = true,
        IsEiExempt = true,
        DentalBenefit = DentalBenefitCode.PayeeAndSpouse,
        StartDate = new DateTime(2024, 1, 8),
        EndDate = new DateTime(2026, 7, 10),
        Address = new Address { Street = "42 Employee Road", City = "Calgary", State = "AB", ZipCode = "T2P1A1", Country = "CAN" },
        Notes = "on the payroll",
    };

    [Fact]
    public async Task AnEmployee_SurvivesARoundTripWithEverythingPayrollNeeds()
    {
        CompanyData source = Source();
        source.Employees.Add(Dana());

        CompanyData target = await RoundTripAsync(source);
        Employee employee = Assert.Single(target.Employees);

        Assert.Equal("EMP-001", employee.Id);
        Assert.Equal("Dana Smith", employee.Name);
        Assert.Equal("42", employee.EmployeeNumber);
        Assert.Equal("046454286", employee.Sin);
        Assert.Equal("AB", employee.Province);
        Assert.Equal(PayType.Salary, employee.PayType);
        Assert.Equal(62400m, employee.PayRate);
        Assert.Equal(PayFrequency.Biweekly, employee.PayFrequency);
        Assert.Equal(37.5m, employee.StandardHoursPerWeek);
        Assert.Equal(16500m, employee.FederalClaimAmount);
        Assert.Equal(22769m, employee.ProvincialClaimAmount);
        Assert.True(employee.IsCppExempt);
        Assert.True(employee.IsEiExempt);
        Assert.Equal(DentalBenefitCode.PayeeAndSpouse, employee.DentalBenefit);
        Assert.Equal(new DateTime(2024, 1, 8), employee.StartDate);
        Assert.Equal(new DateTime(2026, 7, 10), employee.EndDate);
        Assert.Equal("42 Employee Road", employee.Address.Street);
        Assert.Equal("on the payroll", employee.Notes);
    }

    // A TD1 claiming nothing is its own flag, because a zero amount has always meant no TD1 was
    // filed. Lose the flag and the employee is given the basic personal amount again.
    [Fact]
    public async Task AnEmployeeWhoseTd1sClaimNothing_StillClaimsNothingAfterARoundTrip()
    {
        CompanyData source = Source();
        Employee dana = Dana();
        dana.FederalClaimAmount = 0m;
        dana.ProvincialClaimAmount = 0m;
        dana.FederalClaimIsZero = true;
        dana.ProvincialClaimIsZero = true;
        source.Employees.Add(dana);

        CompanyData target = await RoundTripAsync(source);
        Employee employee = Assert.Single(target.Employees);

        Assert.True(employee.FederalClaimIsZero);
        Assert.True(employee.ProvincialClaimIsZero);
    }

    [Fact]
    public async Task AnEmployeeWithNoContractHours_ComesBackWithNoneRatherThanZero()
    {
        // Zero hours on a record of employment reads as "worked none" and costs the employee
        // their claim, so blank has to stay blank across the trip.
        CompanyData source = Source();
        Employee dana = Dana();
        dana.StandardHoursPerWeek = null;
        source.Employees.Add(dana);

        CompanyData target = await RoundTripAsync(source);

        Assert.Null(Assert.Single(target.Employees).StandardHoursPerWeek);
    }

    [Fact]
    public async Task AnArchivedEmployee_StaysArchived()
    {
        // They are archived rather than deleted precisely so a T4 can still be produced for
        // them in February. Coming back active would put a leaver into the next pay run.
        CompanyData source = Source();
        Employee dana = Dana();
        dana.IsArchived = true;
        source.Employees.Add(dana);

        CompanyData target = await RoundTripAsync(source);

        Assert.True(Assert.Single(target.Employees).IsArchived);
    }

    [Fact]
    public async Task ASocialInsuranceNumberWrittenWithSpaces_IsStoredAsDigits()
    {
        CompanyData source = Source();
        source.Employees.Add(Dana());

        await new SpreadsheetExportService().ExportToExcelAsync(_path, source, [.. AllSheets], null, null);

        var target = new CompanyData();
        target.Settings.Localization.Currency = "USD";
        target.Employees.Add(new Employee { Id = "EMP-001", Name = "Dana Smith", Sin = "046 454 286" });
        await new SpreadsheetImportService().ImportFromExcelAsync(_path, target);

        Assert.Equal("046454286", Assert.Single(target.Employees).Sin);
    }

    [Fact]
    public async Task PayRunsAreExportedButNotImported()
    {
        // Deliberate. An approved run's figures are frozen so a stub reprinted next year still
        // matches the one the employee was handed; reading them back from a sheet anybody could
        // have edited would defeat that.
        CompanyData source = Source();
        source.Employees.Add(Dana());
        source.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = new DateTime(2026, 7, 3),
            PeriodStart = new DateTime(2026, 6, 20),
            PeriodEnd = new DateTime(2026, 7, 3),
            RateEditionId = "2026-07",
            Status = PayRunStatus.Approved,
            Lines =
            {
                new PayRunLine
                {
                    EmployeeId = "EMP-001",
                    EmployeeName = "Dana Smith",
                    Province = "AB",
                    PayPeriodsPerYear = 26,
                    BasePay = 2400m,
                    GrossPay = 2400m,
                    CppEmployee = 134.79m,
                    EiEmployee = 39.12m,
                    FederalTax = 250m,
                    ProvincialTax = 120m,
                    NetPay = 1856.09m,
                },
            },
        });

        CompanyData target = await RoundTripAsync(source);

        Assert.Empty(target.PayRuns);
    }

    [Fact]
    public async Task ThePayRunSheetCarriesTheRegisterAndTheEditionThatProducedIt()
    {
        // Two runs in the same year can use different CRA tables, so a register that does not
        // say which one produced a figure cannot be checked against anything.
        CompanyData source = Source();
        source.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = new DateTime(2026, 7, 3),
            RateEditionId = "2026-07",
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", EmployeeName = "Dana Smith", GrossPay = 2400m, NetPay = 1856.09m } },
        });

        string csv = Path.ChangeExtension(_path, ".csv");

        try
        {
            await new SpreadsheetExportService().ExportToCsvAsync(csv, source, ["Pay Runs"], null, null);
            List<List<string>> rows = CsvReader.ReadAllRows(csv, out List<string> headers);
            List<string> row = Assert.Single(rows);

            Assert.Equal("PR-0001", row[headers.IndexOf("Run ID")]);
            Assert.Equal("Dana Smith", row[headers.IndexOf("Employee Name")]);
            Assert.Equal("2026-07", row[headers.IndexOf("Rate Edition")]);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    #endregion

    #region Purchase orders

    [Fact]
    public async Task APurchaseOrderKeepsItsLines()
    {
        CompanyData source = Source();
        source.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = "PO-001",
            SupplierId = "SUP-014",
            OrderDate = new DateTime(2026, 5, 2),
            Total = 240m,
            LineItems =
            {
                new PurchaseOrderLineItem { ProductId = "PRD-001", Quantity = 20, UnitCost = 12m },
            },
        });

        CompanyData target = await RoundTripAsync(source);

        PurchaseOrder order = Assert.Single(target.PurchaseOrders);
        PurchaseOrderLineItem line = Assert.Single(order.LineItems);

        Assert.Equal("PRD-001", line.ProductId);
        Assert.Equal(20, line.Quantity);
    }

    #endregion
}
