using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Importing over existing records (with "Skip existing records" off) changes only what the sheet
/// carries. A column the sheet does not have leaves the stored value alone; before, it was read
/// as blank and overwrote it, so a sheet of ids and notes wiped addresses, zeroed totals, reset
/// payment methods to Cash and marked unpaid revenue as paid.
/// </summary>
public class SpreadsheetImportUpdateTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private async Task ImportSheetAsync(CompanyData data, string sheet, string[] headers, params object[][] rows)
    {
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet(sheet);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            wb.SaveAs(_path);
        }

        await new SpreadsheetImportService().ImportFromExcelAsync(_path, data, new ImportOptions { SkipExistingRecords = false });
    }

    private static Address Calgary() => new() { Street = "1 Main St", City = "Calgary", State = "AB", ZipCode = "T2P1A1", Country = "Canada" };

    [Fact]
    public async Task UpdatingACustomersNotes_LeavesTheRestOfTheCustomer()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer
        {
            Id = "CUS-001", Name = "Jane Doe", CompanyName = "Doe Co", Email = "jane@x.com", Phone = "555-0100",
            Address = Calgary(), Notes = "old", Status = EntityStatus.Inactive, TotalPurchases = 99m
        });

        await ImportSheetAsync(data, "Customers", ["ID", "Notes"], ["CUS-001", "new"]);

        var c = Assert.Single(data.Customers);
        Assert.Equal("new", c.Notes);
        Assert.Equal("Jane Doe", c.Name);
        Assert.Equal("Doe Co", c.CompanyName);
        Assert.Equal("jane@x.com", c.Email);
        Assert.Equal("555-0100", c.Phone);
        Assert.Equal("Calgary", c.Address.City);
        Assert.Equal(EntityStatus.Inactive, c.Status);
        Assert.Equal(99m, c.TotalPurchases);
    }

    [Fact]
    public async Task UpdatingASuppliersNotes_LeavesTheRestOfTheSupplier()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier
        {
            Id = "SUP-001", Name = "Acme", Email = "a@acme.com", Phone = "555-0199", Website = "acme.com", Address = Calgary(), Notes = "old"
        });

        await ImportSheetAsync(data, "Suppliers", ["ID", "Notes"], ["SUP-001", "new"]);

        var s = Assert.Single(data.Suppliers);
        Assert.Equal("new", s.Notes);
        Assert.Equal("Acme", s.Name);
        Assert.Equal("a@acme.com", s.Email);
        Assert.Equal("555-0199", s.Phone);
        Assert.Equal("acme.com", s.Website);
        Assert.Equal("Calgary", s.Address.City);
    }

    [Fact]
    public async Task UpdatingAProductsDescription_LeavesItsCategorySupplierAndStockLevels()
    {
        var data = new CompanyData();
        data.Categories.Add(new Category { Id = "CAT-EXP-001", Name = "Supplies", Type = CategoryType.Expense });
        data.Suppliers.Add(new Supplier { Id = "SUP-001", Name = "Acme" });
        data.Products.Add(new Product
        {
            Id = "PRD-001", Name = "Paper", Type = CategoryType.Expense, ItemType = "Service", Sku = "P-1",
            Description = "old", CategoryId = "CAT-EXP-001", SupplierId = "SUP-001",
            ReorderPoint = 5, OverstockThreshold = 50, TrackInventory = true
        });

        await ImportSheetAsync(data, "Products", ["ID", "Description"], ["PRD-001", "new"]);

        var p = Assert.Single(data.Products);
        Assert.Equal("new", p.Description);
        Assert.Equal("Paper", p.Name);
        Assert.Equal(CategoryType.Expense, p.Type);
        Assert.Equal("Service", p.ItemType);
        Assert.Equal("P-1", p.Sku);
        Assert.Equal("CAT-EXP-001", p.CategoryId);
        Assert.Equal("SUP-001", p.SupplierId);
        Assert.Equal(5, p.ReorderPoint);
        Assert.Equal(50, p.OverstockThreshold);
    }

    [Fact]
    public async Task UpdatingALocationsPhone_LeavesTheRestOfTheLocation()
    {
        var data = new CompanyData();
        data.Locations.Add(new Location
        {
            Id = "LOC-001", Name = "Warehouse", ContactPerson = "Bob", Phone = "555-0111", Address = Calgary(),
            Capacity = 100, CurrentUtilization = 40
        });

        await ImportSheetAsync(data, "Locations", ["ID", "Phone"], ["LOC-001", "555-0222"]);

        var l = Assert.Single(data.Locations);
        Assert.Equal("555-0222", l.Phone);
        Assert.Equal("Warehouse", l.Name);
        Assert.Equal("Bob", l.ContactPerson);
        Assert.Equal("Calgary", l.Address.City);
        Assert.Equal(100, l.Capacity);
        Assert.Equal(40, l.CurrentUtilization);
    }

    private static List<LineItem> ThreeLines() =>
    [
        new() { ProductId = "PRD-001", Description = "Paper", Quantity = 1m, UnitPrice = 30m },
        new() { ProductId = "PRD-002", Description = "Pens", Quantity = 1m, UnitPrice = 30m },
        new() { ProductId = "PRD-003", Description = "Ink", Quantity = 1m, UnitPrice = 30m },
    ];

    [Fact]
    public async Task UpdatingAnExpensesReference_LeavesItsMoneyDateMethodAndLines()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense
        {
            Id = "PUR-001", Date = new DateTime(2026, 2, 1), SupplierId = "SUP-001", Description = "Paper (+2 more)",
            Quantity = 3m, UnitPrice = 30m, Amount = 90m, TaxAmount = 9m, Total = 99m, TotalUSD = 99m,
            PaymentMethod = PaymentMethod.CreditCard, ReferenceNumber = "R1", OriginalCurrency = "USD",
            LineItems = ThreeLines()
        });

        await ImportSheetAsync(data, "Expenses", ["ID", "Reference"], ["PUR-001", "R2"]);

        var e = Assert.Single(data.Expenses);
        Assert.Equal("R2", e.ReferenceNumber);
        Assert.Equal(new DateTime(2026, 2, 1), e.Date);
        Assert.Equal("SUP-001", e.SupplierId);
        Assert.Equal(3m, e.Quantity);
        Assert.Equal(99m, e.Total);
        Assert.Equal(99m, e.TotalUSD);
        Assert.Equal(9m, e.TaxAmount);
        Assert.Equal(PaymentMethod.CreditCard, e.PaymentMethod);
        Assert.Equal(3, e.LineItems.Count);
    }

    [Fact]
    public async Task UpdatingARevenuesReference_LeavesItUnpaidWithItsMoneyAndLines()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-001", Date = new DateTime(2026, 2, 1), CustomerId = "CUS-001", Description = "Paper (+2 more)",
            Quantity = 3m, UnitPrice = 30m, Amount = 90m, Total = 90m, TotalUSD = 90m,
            PaymentStatus = RevenuePaymentStatus.Unpaid, ReferenceNumber = "R1", OriginalCurrency = "USD",
            LineItems = ThreeLines()
        });

        await ImportSheetAsync(data, "Revenue", ["ID", "Reference"], ["REV-001", "R2"]);

        var r = Assert.Single(data.Revenues);
        Assert.Equal("R2", r.ReferenceNumber);
        Assert.Equal(RevenuePaymentStatus.Unpaid, r.PaymentStatus);
        Assert.Equal(new DateTime(2026, 2, 1), r.Date);
        Assert.Equal("CUS-001", r.CustomerId);
        Assert.Equal(90m, r.Total);
        Assert.Equal(90m, r.TotalUSD);
        Assert.Equal(3, r.LineItems.Count);
        Assert.DoesNotContain(data.Products, p => p.Name == "Paper (+2 more)");
    }

    [Fact]
    public async Task UpdatingAForeignExpensesTotal_KeepsItsCurrency()
    {
        // No currency on the sheet says nothing about the currency, so a euro expense stays in euros
        // rather than becoming that many dollars.
        var data = new CompanyData();
        data.Expenses.Add(new Expense
        {
            Id = "PUR-001", Date = new DateTime(2999, 1, 1), Description = "Hotel", Quantity = 1m, UnitPrice = 100m,
            Amount = 100m, Total = 100m, OriginalCurrency = "EUR", IsPendingConversion = true
        });

        await ImportSheetAsync(data, "Expenses", ["ID", "Total"], ["PUR-001", 120]);

        var e = Assert.Single(data.Expenses);
        Assert.Equal(120m, e.Total);
        Assert.Equal("EUR", e.OriginalCurrency);
        Assert.True(e.IsPendingConversion);
        Assert.Contains(data.PendingConversions, p => p.TransactionId == "PUR-001" && p.OriginalCurrency == "EUR" && p.Total == 120m);
    }

    [Fact]
    public async Task UpdatingAnInvoicesStatus_LeavesItsCustomerDatesAndMoney()
    {
        // Marking a batch paid from an ID and Status sheet.
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-001", InvoiceNumber = "#1001", CustomerId = "CUS-001",
            IssueDate = new DateTime(2026, 2, 1), DueDate = new DateTime(2026, 3, 3),
            Subtotal = 100m, TaxAmount = 10m, Total = 110m, AmountPaid = 50m, Balance = 60m,
            TotalUSD = 110m, BalanceUSD = 60m, OriginalCurrency = "USD", Status = InvoiceStatus.Sent
        });

        await ImportSheetAsync(data, "Invoices", ["ID", "Status"], ["INV-001", "Paid"]);

        var i = Assert.Single(data.Invoices);
        Assert.Equal(InvoiceStatus.Paid, i.Status);
        Assert.Equal("#1001", i.InvoiceNumber);
        Assert.Equal("CUS-001", i.CustomerId);
        Assert.Equal(new DateTime(2026, 2, 1), i.IssueDate);
        Assert.Equal(new DateTime(2026, 3, 3), i.DueDate);
        Assert.Equal(100m, i.Subtotal);
        Assert.Equal(10m, i.TaxAmount);
        Assert.Equal(110m, i.Total);
        Assert.Equal(50m, i.AmountPaid);
        Assert.Equal(60m, i.Balance);
        Assert.Equal(110m, i.TotalUSD);
        Assert.Equal(60m, i.BalanceUSD);
    }

    [Fact]
    public async Task UpdatingAForeignInvoicesTotal_KeepsWhatWasPaidAndItsCurrency()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-001", InvoiceNumber = "INV-001", CustomerId = "CUS-001", IssueDate = new DateTime(2999, 1, 1),
            Total = 100m, AmountPaid = 40m, Balance = 60m, OriginalCurrency = "EUR", IsPendingConversion = true
        });
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "INV-001", TransactionType = "Invoice", OriginalCurrency = "EUR",
            TransactionDate = new DateTime(2999, 1, 1), Total = 100m, Balance = 60m
        });

        await ImportSheetAsync(data, "Invoices", ["ID", "Total"], ["INV-001", 150]);

        var i = Assert.Single(data.Invoices);
        Assert.Equal(150m, i.Total);
        Assert.Equal(40m, i.AmountPaid);
        Assert.Equal(110m, i.Balance);
        Assert.Equal("EUR", i.OriginalCurrency);
        var queued = Assert.Single(data.PendingConversions, p => p.TransactionId == "INV-001");
        Assert.Equal(150m, queued.Total);
        Assert.Equal(110m, queued.Balance);
    }

    [Fact]
    public async Task UpdatingAPaymentsNotes_LeavesItsInvoiceAmountMethodAndCurrency()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice { Id = "INV-001", InvoiceNumber = "INV-001" });
        data.Payments.Add(new Payment
        {
            Id = "PAY-001", InvoiceId = "INV-001", CustomerId = "CUS-001", Date = new DateTime(2026, 2, 1),
            Amount = 100m, AmountUSD = 108m, OriginalCurrency = "EUR", PaymentMethod = PaymentMethod.CreditCard,
            ReferenceNumber = "CHQ-9", Notes = "old"
        });

        await ImportSheetAsync(data, "Payments", ["ID", "Notes"], ["PAY-001", "new"]);

        var p = Assert.Single(data.Payments);
        Assert.Equal("new", p.Notes);
        Assert.Equal("INV-001", p.InvoiceId);
        Assert.Equal("CUS-001", p.CustomerId);
        Assert.Equal(new DateTime(2026, 2, 1), p.Date);
        Assert.Equal(100m, p.Amount);
        Assert.Equal(PaymentMethod.CreditCard, p.PaymentMethod);
        Assert.Equal("CHQ-9", p.ReferenceNumber);
        Assert.Equal("EUR", p.OriginalCurrency);
        Assert.Equal(108m, p.AmountUSD);
    }

    [Fact]
    public async Task UpdatingAPurchaseOrdersStatus_LeavesItsSupplierDatesAndTotal()
    {
        var data = new CompanyData();
        data.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = "PO-001", SupplierId = "SUP-001", OrderDate = new DateTime(2026, 2, 1),
            ExpectedDeliveryDate = new DateTime(2026, 2, 15), Total = 500m, TotalUSD = 370m,
            OriginalCurrency = "CAD", Status = PurchaseOrderStatus.Sent
        });

        await ImportSheetAsync(data, "Purchase Orders", ["ID", "Status"], ["PO-001", "Received"]);

        var po = Assert.Single(data.PurchaseOrders);
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);
        Assert.Equal("SUP-001", po.SupplierId);
        Assert.Equal(new DateTime(2026, 2, 1), po.OrderDate);
        Assert.Equal(new DateTime(2026, 2, 15), po.ExpectedDeliveryDate);
        Assert.Equal(500m, po.Total);
        Assert.Equal(370m, po.TotalUSD);
        Assert.Equal("CAD", po.OriginalCurrency);
    }

    [Fact]
    public async Task UpdatingARecurringInvoicesStatus_LeavesItsCustomerAmountAndSchedule()
    {
        var data = new CompanyData();
        data.RecurringInvoices.Add(new RecurringInvoice
        {
            Id = "REC-INV-001", CustomerId = "CUS-001", Amount = 250m, Description = "Retainer",
            Frequency = Frequency.Quarterly, NextInvoiceDate = new DateTime(2026, 4, 1), Status = RecurringInvoiceStatus.Active
        });

        await ImportSheetAsync(data, "Recurring Invoices", ["ID", "Status"], ["REC-INV-001", "Paused"]);

        var r = Assert.Single(data.RecurringInvoices);
        Assert.Equal(RecurringInvoiceStatus.Paused, r.Status);
        Assert.Equal("CUS-001", r.CustomerId);
        Assert.Equal(250m, r.Amount);
        Assert.Equal("Retainer", r.Description);
        Assert.Equal(Frequency.Quarterly, r.Frequency);
        Assert.Equal(new DateTime(2026, 4, 1), r.NextInvoiceDate);
    }

    [Fact]
    public async Task UpdatingAnInventoryItemsStock_LeavesItsProductLocationAndCost()
    {
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem
        {
            Id = "INV-ITM-001", ProductId = "PRD-001", LocationId = "LOC-001", InStock = 10, Reserved = 2,
            ReorderPoint = 5, UnitCost = 4.5m, LastUpdated = new DateTime(2026, 1, 5)
        });

        await ImportSheetAsync(data, "Inventory", ["ID", "In Stock"], ["INV-ITM-001", 12]);

        var item = Assert.Single(data.Inventory);
        Assert.Equal(12, item.InStock);
        Assert.Equal("PRD-001", item.ProductId);
        Assert.Equal("LOC-001", item.LocationId);
        Assert.Equal(2, item.Reserved);
        Assert.Equal(5, item.ReorderPoint);
        Assert.Equal(4.5m, item.UnitCost);
    }

    [Fact]
    public async Task UpdatingAStockAdjustmentsReason_LeavesItsQuantitiesAndTimestamp()
    {
        var data = new CompanyData();
        data.StockAdjustments.Add(new StockAdjustment
        {
            Id = "ADJ-001", InventoryItemId = "INV-ITM-001", AdjustmentType = AdjustmentType.Remove, Quantity = 3,
            PreviousStock = 10, NewStock = 7, Reason = "old", ReferenceNumber = "R1", UserId = "U1",
            Timestamp = new DateTime(2026, 1, 5)
        });

        await ImportSheetAsync(data, "Stock Adjustments", ["ID", "Reason"], ["ADJ-001", "Breakage"]);

        var a = Assert.Single(data.StockAdjustments);
        Assert.Equal("Breakage", a.Reason);
        Assert.Equal("INV-ITM-001", a.InventoryItemId);
        Assert.Equal(AdjustmentType.Remove, a.AdjustmentType);
        Assert.Equal(3, a.Quantity);
        Assert.Equal(10, a.PreviousStock);
        Assert.Equal(7, a.NewStock);
        Assert.Equal("R1", a.ReferenceNumber);
        Assert.Equal("U1", a.UserId);
        Assert.Equal(new DateTime(2026, 1, 5), a.Timestamp);
    }

    [Fact]
    public async Task UpdatingACategorysDescription_LeavesItsNameTypeParentAndIcon()
    {
        var data = new CompanyData();
        data.Categories.Add(new Category
        {
            Id = "CAT-002", Name = "Fuel", Type = CategoryType.Expense, ParentId = "CAT-001", Description = "old", Icon = "⛽"
        });

        await ImportSheetAsync(data, "Categories", ["ID", "Description"], ["CAT-002", "new"]);

        var c = Assert.Single(data.Categories);
        Assert.Equal("new", c.Description);
        Assert.Equal("Fuel", c.Name);
        Assert.Equal(CategoryType.Expense, c.Type);
        Assert.Equal("CAT-001", c.ParentId);
        Assert.Equal("⛽", c.Icon);
    }

    [Fact]
    public async Task UpdatingAReturnsStatus_LeavesItsPartiesDateAndRefund()
    {
        var data = new CompanyData();
        data.Returns.Add(new Return
        {
            Id = "RET-001", OriginalTransactionId = "PUR-001", ReturnType = "Supplier", SupplierId = "SUP-001",
            ReturnDate = new DateTime(2026, 2, 1), RefundAmount = 80m, RestockingFee = 5m, Status = ReturnStatus.Pending,
            Notes = "dented", ProcessedBy = "Bob",
            Items = [new ReturnItem { ProductId = "PRD-001", Quantity = 2, Reason = "Damaged" }]
        });

        await ImportSheetAsync(data, "Returns", ["ID", "Status"], ["RET-001", "Completed"]);

        var r = Assert.Single(data.Returns);
        Assert.Equal(ReturnStatus.Completed, r.Status);
        Assert.Equal("PUR-001", r.OriginalTransactionId);
        Assert.Equal("Supplier", r.ReturnType);
        Assert.Equal("SUP-001", r.SupplierId);
        Assert.Equal(new DateTime(2026, 2, 1), r.ReturnDate);
        Assert.Equal(80m, r.RefundAmount);
        Assert.Equal(5m, r.RestockingFee);
        Assert.Equal("dented", r.Notes);
        Assert.Equal("Bob", r.ProcessedBy);
        Assert.Equal(2, Assert.Single(r.Items).Quantity);
    }

    [Fact]
    public async Task UpdatingALossesNotes_LeavesItsProductQuantityReasonAndValue()
    {
        var data = new CompanyData();
        data.LostDamaged.Add(new LostDamaged
        {
            Id = "LOST-001", ProductId = "PRD-001", InventoryItemId = "INV-ITM-001", Quantity = 4,
            Reason = LostDamagedReason.Stolen, DateDiscovered = new DateTime(2026, 2, 1), ValueLost = 120m,
            Notes = "old", InsuranceClaim = true
        });

        await ImportSheetAsync(data, "Lost Damaged", ["ID", "Notes"], ["LOST-001", "new"]);

        var l = Assert.Single(data.LostDamaged);
        Assert.Equal("new", l.Notes);
        Assert.Equal("PRD-001", l.ProductId);
        Assert.Equal("INV-ITM-001", l.InventoryItemId);
        Assert.Equal(4, l.Quantity);
        Assert.Equal(LostDamagedReason.Stolen, l.Reason);
        Assert.Equal(new DateTime(2026, 2, 1), l.DateDiscovered);
        Assert.Equal(120m, l.ValueLost);
        Assert.True(l.InsuranceClaim);
    }

    [Fact]
    public async Task UpdatingARentalItemsStatus_LeavesItsItemAndRates()
    {
        var data = new CompanyData();
        data.RentalInventory.Add(new RentalItem
        {
            Id = "RNT-ITM-001", InventoryItemId = "INV-ITM-001", DailyRate = 20m, WeeklyRate = 100m,
            MonthlyRate = 300m, SecurityDeposit = 50m, Status = EntityStatus.Active
        });

        await ImportSheetAsync(data, "Rental Inventory", ["ID", "Status"], ["RNT-ITM-001", "Inactive"]);

        var r = Assert.Single(data.RentalInventory);
        Assert.Equal(EntityStatus.Inactive, r.Status);
        Assert.Equal("INV-ITM-001", r.InventoryItemId);
        Assert.Equal(20m, r.DailyRate);
        Assert.Equal(100m, r.WeeklyRate);
        Assert.Equal(300m, r.MonthlyRate);
        Assert.Equal(50m, r.SecurityDeposit);
    }

    private static RentalRecord TwoLineRental() => new()
    {
        Id = "RNT-001", CustomerId = "CUS-001", StartDate = new DateTime(2026, 2, 1), DueDate = new DateTime(2026, 2, 8),
        ReturnDate = new DateTime(2026, 2, 7), TotalCost = 240m, Status = RentalStatus.Active, Paid = true,
        RentalItemId = "RNT-ITM-001", Quantity = 3, RateType = RateType.Weekly, RateAmount = 100m, SecurityDeposit = 90m,
        LineItems =
        [
            new() { RentalItemId = "RNT-ITM-001", Quantity = 1, RateType = RateType.Weekly, RateAmount = 100m, SecurityDeposit = 50m },
            new() { RentalItemId = "RNT-ITM-002", Quantity = 2, RateType = RateType.Daily, RateAmount = 20m, SecurityDeposit = 20m },
        ]
    };

    [Fact]
    public async Task UpdatingARentalsStatus_LeavesItsCustomerDatesMoneyAndLines()
    {
        var data = new CompanyData();
        data.Rentals.Add(TwoLineRental());

        await ImportSheetAsync(data, "Rental Records", ["ID", "Status"], ["RNT-001", "Returned"]);

        var r = Assert.Single(data.Rentals);
        Assert.Equal(RentalStatus.Returned, r.Status);
        Assert.Equal("CUS-001", r.CustomerId);
        Assert.Equal(new DateTime(2026, 2, 1), r.StartDate);
        Assert.Equal(new DateTime(2026, 2, 8), r.DueDate);
        Assert.Equal(new DateTime(2026, 2, 7), r.ReturnDate);
        Assert.Equal(240m, r.TotalCost);
        Assert.True(r.Paid);
        Assert.Equal(2, r.LineItems.Count);
        Assert.Equal("RNT-ITM-002", r.LineItems[1].RentalItemId);
        Assert.Equal(3, r.Quantity);
        Assert.Equal(90m, r.SecurityDeposit);
    }

    [Fact]
    public async Task UpdatingARentalsRates_KeepsTheRestOfEachLine()
    {
        var data = new CompanyData();
        data.Rentals.Add(TwoLineRental());

        await ImportSheetAsync(data, "Rental Records", ["ID", "Rate Amount"], ["RNT-001", 110], ["RNT-001", 25]);

        var r = Assert.Single(data.Rentals);
        Assert.Equal(2, r.LineItems.Count);
        Assert.Equal(("RNT-ITM-001", 1, RateType.Weekly, 110m, 50m),
            (r.LineItems[0].RentalItemId, r.LineItems[0].Quantity, r.LineItems[0].RateType, r.LineItems[0].RateAmount, r.LineItems[0].SecurityDeposit));
        Assert.Equal(("RNT-ITM-002", 2, RateType.Daily, 25m, 20m),
            (r.LineItems[1].RentalItemId, r.LineItems[1].Quantity, r.LineItems[1].RateType, r.LineItems[1].RateAmount, r.LineItems[1].SecurityDeposit));
        Assert.Equal(110m, r.RateAmount);
        Assert.Equal(90m, r.SecurityDeposit);
    }

    [Fact]
    public async Task UpdatingAnEmployeesNotes_LeavesTheirNameAndArchivedState()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "EMP-001", Name = "Sam Lee", IsArchived = true, Notes = "old" });

        await ImportSheetAsync(data, "Employees", ["ID", "Notes"], ["EMP-001", "new"]);

        var e = Assert.Single(data.Employees);
        Assert.Equal("new", e.Notes);
        Assert.Equal("Sam Lee", e.Name);
        Assert.True(e.IsArchived);
    }
}
