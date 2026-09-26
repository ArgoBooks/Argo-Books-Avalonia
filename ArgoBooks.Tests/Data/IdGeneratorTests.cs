using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using Xunit;

namespace ArgoBooks.Tests.Data;

/// <summary>
/// Tests for the IdGenerator class.
/// </summary>
public class IdGeneratorTests
{
    private CompanyData CreateCompanyData()
    {
        return new CompanyData();
    }

    #region Category ID Generation Tests

    [Theory]
    [InlineData(CategoryType.Revenue, "CAT-REV-001")]
    [InlineData(CategoryType.Expense, "CAT-EXP-001")]
    [InlineData(CategoryType.Rental, "CAT-RNT-001")]
    public void NextCategoryId_GeneratesCorrectPrefixForType(CategoryType type, string expectedId)
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var id = generator.NextCategoryId(type);

        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void NextCategoryId_SharesCounterAcrossTypes()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var salesId = generator.NextCategoryId(CategoryType.Revenue);
        var purchaseId = generator.NextCategoryId(CategoryType.Expense);
        var rentalId = generator.NextCategoryId(CategoryType.Rental);

        Assert.Equal("CAT-REV-001", salesId);
        Assert.Equal("CAT-EXP-002", purchaseId);
        Assert.Equal("CAT-RNT-003", rentalId);
    }

    [Fact]
    public void NextCategoryId_SkipsAnIdACategoryAlreadyHas()
    {
        var companyData = CreateCompanyData();
        companyData.Categories.Add(new Category { Id = "CAT-EXP-001", Name = "Typed by hand", Type = CategoryType.Expense });

        var id = new IdGenerator(companyData).NextCategoryId(CategoryType.Expense);

        Assert.Equal("CAT-EXP-002", id);
    }

    #endregion

    #region Revenue and Expense ID Generation Tests

    [Fact]
    public void NextRevenueId_TakesTheYearFromTheRevenuesDate()
    {
        var id = new IdGenerator(CreateCompanyData()).NextRevenueId(new DateTime(2019, 12, 31));

        Assert.Equal("REV-2019-00001", id);
    }

    [Fact]
    public void NextExpenseId_TakesTheYearFromTheExpensesDate()
    {
        var id = new IdGenerator(CreateCompanyData()).NextExpenseId(new DateTime(2021, 1, 1));

        Assert.Equal("PUR-2021-00001", id);
    }

    [Fact]
    public void NextRevenueAndExpenseId_SkipIdsAlreadyTaken()
    {
        var companyData = CreateCompanyData();
        companyData.Revenues.Add(new Revenue { Id = "REV-2024-00001" });
        companyData.Expenses.Add(new Expense { Id = "PUR-2024-00001" });
        var generator = new IdGenerator(companyData);

        Assert.Equal("REV-2024-00002", generator.NextRevenueId(new DateTime(2024, 6, 1)));
        Assert.Equal("PUR-2024-00002", generator.NextExpenseId(new DateTime(2024, 6, 1)));
        Assert.Equal(2, companyData.IdCounters.Revenue);
        Assert.Equal(2, companyData.IdCounters.Expense);
    }

    #endregion

    #region Invoice ID Generation Tests

    [Fact]
    public void NextInvoiceId_IncludesYear()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var id = generator.NextInvoiceId();

        Assert.Contains(DateTime.UtcNow.Year.ToString(), id);
        Assert.Matches(@"^INV-\d{4}-\d{5}$", id);
    }

    [Fact]
    public void NextInvoiceNumber_ReturnsDisplayFormat()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        // First generate an invoice ID to increment counter
        generator.NextInvoiceId();

        var number = generator.NextInvoiceNumber();

        Assert.StartsWith("#INV-", number);
        Assert.Contains(DateTime.UtcNow.Year.ToString(), number);
    }

    #endregion

    #region Counter Persistence Tests

    [Fact]
    public void Counters_ArePersistedInCompanyData()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        generator.NextInvoiceId();
        generator.NextInvoiceId();

        Assert.Equal(2, companyData.IdCounters.Invoice);
    }

    [Fact]
    public void NewGenerator_UsesExistingCounters()
    {
        var companyData = CreateCompanyData();
        companyData.IdCounters.Invoice = 50;

        var generator = new IdGenerator(companyData);
        var id = generator.NextInvoiceId();

        Assert.Contains("00051", id);
    }

    [Fact]
    public void DifferentEntityTypes_HaveIndependentCounters()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        generator.NextInvoiceId();
        generator.NextInvoiceId();
        generator.NextInvoiceId();

        var categoryId = generator.NextCategoryId(CategoryType.Revenue);

        Assert.Equal("CAT-REV-001", categoryId);
        Assert.Equal(3, companyData.IdCounters.Invoice);
        Assert.Equal(1, companyData.IdCounters.Category);
    }

    #endregion

    #region One format per record

    private static readonly int Year = DateTime.UtcNow.Year;

    public static TheoryData<string, Func<IdGenerator, string>, string> FirstIds => new()
    {
        { "Customer", g => g.NextCustomerId(), "CUS-001" },
        { "Supplier", g => g.NextSupplierId(), "SUP-001" },
        { "Product", g => g.NextProductId(), "PRD-001" },
        { "Placeholder product", g => g.NextPlaceholderProductId(), "PRD-IMP-001" },
        { "Location", g => g.NextLocationId(), "LOC-001" },
        { "Employee", g => g.NextEmployeeId(), "EMP-001" },
        { "Pay run", g => g.NextPayRunId(), "PR-0001" },
        { "Invoice template", g => g.NextInvoiceTemplateId(), "template-1" },
        { "Invoice", g => g.NextInvoiceId(), $"INV-{Year}-00001" },
        { "Quote", g => g.NextQuoteId(), $"QUO-{Year}-00001" },
        { "Payment", g => g.NextPaymentId(), "PAY-001" },
        { "Receipt", g => g.NextReceiptId(), $"RCP-{Year}-00001" },
        { "Recurring invoice", g => g.NextRecurringInvoiceId(), "REC-INV-00001" },
        { "Recurring transaction", g => g.NextRecurringTransactionId(), "REC-TXN-00001" },
        { "Inventory item", g => g.NextInventoryItemId(), "INV-ITM-00001" },
        { "Stock adjustment", g => g.NextStockAdjustmentId(), "ADJ-00001" },
        { "Stock transfer", g => g.NextStockTransferId(), "TRF-00001" },
        { "Purchase order", g => g.NextPurchaseOrderId(), "PO-00001" },
        { "Rental item", g => g.NextRentalItemId(), "RNT-ITM-001" },
        { "Rental", g => g.NextRentalId(), "RNT-001" },
        { "Return", g => g.NextReturnId(), "RET-001" },
        { "Lost or damaged", g => g.NextLostDamagedId(), "LOST-001" },
    };

    [Theory]
    [MemberData(nameof(FirstIds))]
    public void EachRecordType_GetsItsOneFormat(string record, Func<IdGenerator, string> next, string expected)
    {
        var id = next(new IdGenerator(CreateCompanyData()));

        Assert.True(id == expected, $"{record}: expected {expected}, got {id}");
    }

    [Fact]
    public void DisplayNumbers_MatchTheIdJustGenerated()
    {
        var companyData = CreateCompanyData();
        companyData.IdCounters.Invoice = 6;
        companyData.IdCounters.Quote = 2;
        companyData.IdCounters.PurchaseOrder = 11;
        var generator = new IdGenerator(companyData);

        Assert.Equal($"INV-{Year}-00007", generator.NextInvoiceId());
        Assert.Equal($"#INV-{Year}-00007", generator.NextInvoiceNumber());
        Assert.Equal($"QUO-{Year}-00003", generator.NextQuoteId());
        Assert.Equal($"#QUO-{Year}-00003", generator.NextQuoteNumber());
        Assert.Equal("PO-00012", generator.NextPurchaseOrderId());
        Assert.Equal($"#PO-{Year}-012", generator.NextPurchaseOrderNumber());
    }

    #endregion

    #region Skipping IDs already taken

    public static TheoryData<string, Action<CompanyData>, Func<IdGenerator, string>, string, Func<IdCounters, int>> TakenFirstIds => new()
    {
        { "Customer", d => d.Customers.Add(new Customer { Id = "CUS-001" }), g => g.NextCustomerId(), "CUS-002", c => c.Customer },
        { "Supplier", d => d.Suppliers.Add(new Supplier { Id = "SUP-001" }), g => g.NextSupplierId(), "SUP-002", c => c.Supplier },
        { "Product", d => d.Products.Add(new Product { Id = "PRD-001" }), g => g.NextProductId(), "PRD-002", c => c.Product },
        { "Placeholder product", d => d.Products.Add(new Product { Id = "PRD-IMP-001" }), g => g.NextPlaceholderProductId(), "PRD-IMP-002", c => c.Product },
        { "Location", d => d.Locations.Add(new Location { Id = "LOC-001" }), g => g.NextLocationId(), "LOC-002", c => c.Location },
        { "Invoice template", d => d.InvoiceTemplates.Add(new InvoiceTemplate { Id = "template-1" }), g => g.NextInvoiceTemplateId(), "template-2", c => c.InvoiceTemplate },
        { "Invoice", d => d.Invoices.Add(new Invoice { Id = $"INV-{Year}-00001" }), g => g.NextInvoiceId(), $"INV-{Year}-00002", c => c.Invoice },
        { "Quote", d => d.Quotes.Add(new Quote { Id = $"QUO-{Year}-00001" }), g => g.NextQuoteId(), $"QUO-{Year}-00002", c => c.Quote },
        { "Payment", d => d.Payments.Add(new Payment { Id = "PAY-001" }), g => g.NextPaymentId(), "PAY-002", c => c.Payment },
        { "Receipt", d => d.Receipts.Add(new Receipt { Id = $"RCP-{Year}-00001" }), g => g.NextReceiptId(), $"RCP-{Year}-00002", c => c.Receipt },
        { "Recurring invoice", d => d.RecurringInvoices.Add(new RecurringInvoice { Id = "REC-INV-00001" }), g => g.NextRecurringInvoiceId(), "REC-INV-00002", c => c.RecurringInvoice },
        { "Recurring transaction", d => d.RecurringTransactions.Add(new RecurringTransaction { Id = "REC-TXN-00001" }), g => g.NextRecurringTransactionId(), "REC-TXN-00002", c => c.RecurringTransaction },
        { "Inventory item", d => d.Inventory.Add(new InventoryItem { Id = "INV-ITM-00001" }), g => g.NextInventoryItemId(), "INV-ITM-00002", c => c.InventoryItem },
        { "Stock adjustment", d => d.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-00001" }), g => g.NextStockAdjustmentId(), "ADJ-00002", c => c.StockAdjustment },
        { "Stock transfer", d => d.StockTransfers.Add(new StockTransfer { Id = "TRF-00001" }), g => g.NextStockTransferId(), "TRF-00002", c => c.StockTransfer },
        { "Purchase order", d => d.PurchaseOrders.Add(new PurchaseOrder { Id = "PO-00001" }), g => g.NextPurchaseOrderId(), "PO-00002", c => c.PurchaseOrder },
        { "Rental item", d => d.RentalInventory.Add(new RentalItem { Id = "RNT-ITM-001" }), g => g.NextRentalItemId(), "RNT-ITM-002", c => c.RentalItem },
        { "Rental", d => d.Rentals.Add(new RentalRecord { Id = "RNT-001" }), g => g.NextRentalId(), "RNT-002", c => c.Rental },
        { "Return", d => d.Returns.Add(new Return { Id = "RET-001" }), g => g.NextReturnId(), "RET-002", c => c.Return },
        { "Lost or damaged", d => d.LostDamaged.Add(new LostDamaged { Id = "LOST-001" }), g => g.NextLostDamagedId(), "LOST-002", c => c.LostDamaged },
    };

    [Theory]
    [MemberData(nameof(TakenFirstIds))]
    public void AnIdARecordAlreadyHas_IsSkipped_AndTheCounterLandsOnTheIdGiven(
        string record, Action<CompanyData> addRecord, Func<IdGenerator, string> next, string expected, Func<IdCounters, int> counter)
    {
        // Typed by hand or brought in by an import, neither of which moves the counter.
        var companyData = CreateCompanyData();
        addRecord(companyData);

        var id = next(new IdGenerator(companyData));

        Assert.True(id == expected, $"{record}: expected {expected}, got {id}");
        Assert.Equal(2, counter(companyData.IdCounters));
    }

    [Fact]
    public void EmployeeAndPayRunIds_AreNumberedPastTheHighestOnFile()
    {
        var companyData = CreateCompanyData();
        companyData.Employees.Add(new Employee { Id = "EMP-007" });
        companyData.Employees.Add(new Employee { Id = "EMP-002" });
        companyData.PayRuns.Add(new PayRun { Id = "PR-0041" });
        var generator = new IdGenerator(companyData);

        Assert.Equal("EMP-008", generator.NextEmployeeId());
        Assert.Equal("PR-0042", generator.NextPayRunId());
    }

    [Fact]
    public void PeekNextInvoice_SkipsATakenIdWithoutMovingTheCounter()
    {
        var companyData = CreateCompanyData();
        companyData.IdCounters.Invoice = 3;
        companyData.Invoices.Add(new Invoice { Id = $"INV-{Year}-00004" });
        var generator = new IdGenerator(companyData);

        var peeked = generator.PeekNextInvoice();

        Assert.Equal(($"INV-{Year}-00005", $"#INV-{Year}-00005"), peeked);
        Assert.Equal(3, companyData.IdCounters.Invoice);
        Assert.Equal(peeked.Id, generator.NextInvoiceId());
        Assert.Equal(peeked.Number, generator.NextInvoiceNumber());
    }

    [Fact]
    public void PeekNextQuoteNumber_SkipsATakenIdWithoutMovingTheCounter()
    {
        var companyData = CreateCompanyData();
        companyData.Quotes.Add(new Quote { Id = $"QUO-{Year}-00001" });
        var generator = new IdGenerator(companyData);

        Assert.Equal($"#QUO-{Year}-00002", generator.PeekNextQuoteNumber());
        Assert.Equal(0, companyData.IdCounters.Quote);
    }

    #endregion

    #region Import batches

    [Fact]
    public void ATakenSet_KeepsABatchDistinctBeforeAnyOfItIsAdded()
    {
        // An import mints an ID for every blank row before those rows reach the company, and a
        // row further down the sheet can already carry the ID the counter would reach.
        var companyData = CreateCompanyData();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PAY-002" };
        var generator = new IdGenerator(companyData);

        var minted = Enumerable.Range(0, 4).Select(_ => generator.NextPaymentId(taken)).ToList();

        Assert.Equal(["PAY-001", "PAY-003", "PAY-004", "PAY-005"], minted);
        Assert.All(minted, id => Assert.Contains(id, taken));
        Assert.Equal(5, companyData.IdCounters.Payment);
        Assert.Empty(companyData.Payments);
    }

    [Fact]
    public void ATakenSet_IsCheckedAlongsideTheCompanysOwnRecords()
    {
        var companyData = CreateCompanyData();
        companyData.Customers.Add(new Customer { Id = "CUS-001" });
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cus-002" };

        var id = new IdGenerator(companyData).NextCustomerId(taken);

        Assert.Equal("CUS-003", id);
        Assert.Contains("CUS-003", taken);
    }

    [Fact]
    public void ATakenSet_ReachesEveryRecordTypeAnImportCanMint()
    {
        var date = new DateTime(2025, 3, 1);
        var cases = new (Func<IdGenerator, ISet<string>, string> Next, string Taken, string Expected)[]
        {
            ((g, t) => g.NextCategoryId(CategoryType.Expense, t), "CAT-EXP-001", "CAT-EXP-002"),
            ((g, t) => g.NextCustomerId(t), "CUS-001", "CUS-002"),
            ((g, t) => g.NextSupplierId(t), "SUP-001", "SUP-002"),
            ((g, t) => g.NextProductId(t), "PRD-001", "PRD-002"),
            ((g, t) => g.NextPlaceholderProductId(t), "PRD-IMP-001", "PRD-IMP-002"),
            ((g, t) => g.NextLocationId(t), "LOC-001", "LOC-002"),
            ((g, t) => g.NextEmployeeId(t), "EMP-001", "EMP-002"),
            ((g, t) => g.NextRevenueId(date, t), "REV-2025-00001", "REV-2025-00002"),
            ((g, t) => g.NextExpenseId(date, t), "PUR-2025-00001", "PUR-2025-00002"),
            ((g, t) => g.NextInvoiceId(t), $"INV-{Year}-00001", $"INV-{Year}-00002"),
            ((g, t) => g.NextPaymentId(t), "PAY-001", "PAY-002"),
            ((g, t) => g.NextRecurringInvoiceId(t), "REC-INV-00001", "REC-INV-00002"),
            ((g, t) => g.NextInventoryItemId(t), "INV-ITM-00001", "INV-ITM-00002"),
            ((g, t) => g.NextStockAdjustmentId(t), "ADJ-00001", "ADJ-00002"),
            ((g, t) => g.NextPurchaseOrderId(t), "PO-00001", "PO-00002"),
            ((g, t) => g.NextRentalItemId(t), "RNT-ITM-001", "RNT-ITM-002"),
            ((g, t) => g.NextReturnId(t), "RET-001", "RET-002"),
            ((g, t) => g.NextLostDamagedId(t), "LOST-001", "LOST-002"),
        };

        foreach (var (next, takenId, expected) in cases)
        {
            var taken = new HashSet<string> { takenId };
            Assert.Equal(expected, next(new IdGenerator(CreateCompanyData()), taken));
        }
    }

    #endregion

    #region Counters only move forward

    [Fact]
    public void ACounterAheadOfEveryRecord_IsNotPulledBack()
    {
        // Ahead because records were deleted; reusing their numbers would hand out a deleted ID.
        var companyData = CreateCompanyData();
        companyData.IdCounters.Return = 40;
        companyData.Returns.Add(new Return { Id = "RET-003" });

        var id = new IdGenerator(companyData).NextReturnId();

        Assert.Equal("RET-041", id);
        Assert.Equal(41, companyData.IdCounters.Return);
    }

    [Fact]
    public void OldFormatIdsOnFile_NeverCollideWithNewOnes()
    {
        // Payments synced from the portal used to be PAY-2026-00005; one made now is PAY-006.
        // Both count on the one payment counter.
        var companyData = CreateCompanyData();
        companyData.Payments.Add(new Payment { Id = "PAY-2026-00005" });
        companyData.IdCounters.Payment = 5;
        var generator = new IdGenerator(companyData);

        Assert.Equal("PAY-006", generator.NextPaymentId());
        Assert.Equal("PAY-007", generator.NextPaymentId());
        Assert.Equal(7, companyData.IdCounters.Payment);
    }

    #endregion
}
