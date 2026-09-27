using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Duplicate opens the add form filled from an existing expense, revenue or invoice. The copy is a
/// new record dated today, with nothing carried over that belongs to the original alone, and saving
/// it leaves the original as it was.
/// </summary>
public class DuplicateRecordTests : ModalViewModelTestBase
{
    [Fact]
    public async Task DuplicatingAnExpense_FillsTheAddFormAndSavesASecondExpense()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Suppliers.Add(new Supplier { Id = "SUP-1", Name = "Staples" });
        Company.Categories.Add(new Category { Id = "CAT-E", Name = "Office", Type = CategoryType.Expense });
        Company.Products.Add(new Product { Id = "P1", Name = "Paper", CategoryId = "CAT-E", CostPrice = 30m });
        Company.Products.Add(new Product { Id = "P2", Name = "Toner", CategoryId = "CAT-E", CostPrice = 20m });
        Company.Receipts.Add(new Receipt
        {
            Id = "RCP-1", TransactionId = "PUR-2025-00001", FileName = "receipt.png",
            OriginalFilePath = Path.Combine(Path.GetTempPath(), "receipt.png")
        });
        var original = new Expense
        {
            Id = "PUR-2025-00001",
            Date = new DateTime(2025, 1, 10),
            SupplierId = "SUP-1",
            OriginalCurrency = "USD",
            LineItems =
            [
                new LineItem { ProductId = "P1", Description = "Paper", Quantity = 2, UnitPrice = 30m },
                new LineItem { ProductId = "P2", Description = "Toner", Quantity = 1, UnitPrice = 20m }
            ],
            Amount = 80m,
            TaxAmount = 8m,
            ShippingCost = 5m,
            Discount = 3m,
            Fee = 2m,
            Total = 92m,
            TotalUSD = 92m,
            PaymentMethod = PaymentMethod.BankTransfer,
            Notes = "Monthly supplies",
            ReceiptId = "RCP-1"
        };
        Company.Expenses.Add(original);
        var before = Snapshot(original);

        var vm = new ExpenseModalsViewModel();
        vm.OpenDuplicateModal(new ExpenseDisplayItem { Id = original.Id });

        Assert.True(vm.IsAddEditModalOpen);
        Assert.False(vm.IsEditMode);
        Assert.Equal(DateTime.Today, vm.ModalDate!.Value.Date);
        Assert.Equal("SUP-1", vm.SelectedSupplier?.Id);
        Assert.Equal([("P1", 2m, 30m), ("P2", 1m, 20m)],
            vm.LineItems.Select(li => (li.SelectedProduct?.Id, li.Quantity ?? 0, li.UnitPrice ?? 0)));
        Assert.Equal((8m, 5m, 3m, 2m), (vm.ModalTaxAmount, vm.ModalShipping, vm.ModalDiscount, vm.ModalFee));
        Assert.Equal("Monthly supplies", vm.ModalNotes);
        Assert.False(vm.HasReceipt);
        Assert.Equal(92m, vm.Total);

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(2, Company.Expenses.Count);
        Assert.Equal(before, Snapshot(original));
        var copy = Company.Expenses.Single(e => e != original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(DateTime.Today, copy.Date.Date);
        Assert.Equal(("SUP-1", 92m, PaymentMethod.BankTransfer, "Monthly supplies", "USD"),
            (copy.SupplierId, copy.Total, copy.PaymentMethod, copy.Notes, copy.OriginalCurrency));
        Assert.Equal([("P1", 2m, 30m), ("P2", 1m, 20m)],
            copy.LineItems.Select(li => (li.ProductId, li.Quantity, li.UnitPrice)));
        Assert.Null(copy.ReceiptId);
        Assert.Single(Company.Receipts);

        Undo();
        Assert.Same(original, Assert.Single(Company.Expenses));
    }

    [Fact]
    public async Task DuplicatingARevenueFromAnInvoice_SavesAPaidRevenueWithNoInvoiceAndMovesStockAgain()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Products.Add(new Product { Id = "P1", Name = "Widget", UnitPrice = 50m, TrackInventory = true });
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 8 };
        Company.Inventory.Add(stock);
        var original = new Revenue
        {
            Id = "REV-2025-00001",
            Date = new DateTime(2025, 6, 1),
            CustomerId = "CUS-1",
            OriginalCurrency = "USD",
            LineItems = [new LineItem { ProductId = "P1", Description = "Widget", Quantity = 2, UnitPrice = 50m }],
            Amount = 100m,
            Total = 100m,
            TotalUSD = 100m,
            PaymentMethod = PaymentMethod.CreditCard,
            PaymentStatus = RevenuePaymentStatus.Unpaid,
            InvoiceId = "INV-2025-00001",
            ReferenceNumber = "#INV-2025-00001",
            Notes = "Auto-created from invoice #INV-2025-00001"
        };
        Company.Revenues.Add(original);
        var before = Snapshot(original);

        var vm = new RevenueModalsViewModel();
        vm.OpenDuplicateModal(new RevenueDisplayItem { Id = original.Id });

        Assert.True(vm.IsAddEditModalOpen);
        Assert.False(vm.IsEditMode);
        Assert.True(vm.ModalPaid);
        Assert.Equal(DateTime.Today, vm.ModalDate!.Value.Date);
        Assert.Equal("CUS-1", vm.SelectedCustomer?.Id);

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(before, Snapshot(original));
        Assert.Equal(("INV-2025-00001", RevenuePaymentStatus.Unpaid), (original.InvoiceId, original.PaymentStatus));
        var copy = Company.Revenues.Single(r => r != original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(DateTime.Today, copy.Date.Date);
        Assert.Null(copy.InvoiceId);
        Assert.Equal(string.Empty, copy.ReferenceNumber);
        Assert.Equal(RevenuePaymentStatus.Paid, copy.PaymentStatus);
        Assert.Equal(("CUS-1", 100m, PaymentMethod.CreditCard), (copy.CustomerId, copy.Total, copy.PaymentMethod));
        Assert.Equal(6, stock.InStock);

        Undo();
        Assert.Same(original, Assert.Single(Company.Revenues));
        Assert.Equal(8, stock.InStock);
    }

    [Fact]
    public async Task DuplicatingAPaidInvoice_OpensANewDraftDatedTodayWithTheSameTerms()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Products.Add(new Product { Id = "P1", Name = "Consulting", UnitPrice = 100m, Type = CategoryType.Revenue });
        Company.Revenues.Add(new Revenue { Id = "REV-2025-00001", CustomerId = "CUS-1", InvoiceId = "INV-2025-00007" });
        Company.Payments.Add(new Payment { Id = "PAY-1", InvoiceId = "INV-2025-00007", CustomerId = "CUS-1", Amount = 147m });
        var original = new Invoice
        {
            Id = "INV-2025-00007",
            InvoiceNumber = "#INV-2025-00007",
            CustomerId = "CUS-1",
            IssueDate = new DateTime(2025, 3, 1),
            DueDate = new DateTime(2025, 3, 31),
            Status = InvoiceStatus.Paid,
            LineItems =
            [
                new LineItem { ProductId = "P1", RevenueRecordId = "REV-2025-00001", Description = "Consulting", Quantity = 1, UnitPrice = 100m }
            ],
            TaxRate = 10m,
            ShippingAmount = 7m,
            DiscountAmount = 5m,
            CustomFeeLabel = "Rush",
            CustomFeeAmount = 3m,
            SecurityDeposit = 40m,
            Notes = "Thanks for your business",
            OriginalCurrency = "USD",
            RecurringInvoiceId = "REC-1",
            AmountPaid = 147m,
            History = [new InvoiceHistoryEntry { Action = "Published to Portal" }]
        };
        Company.Invoices.Add(original);

        var vm = new InvoiceModalsViewModel();
        vm.DuplicateInvoice(new InvoiceDisplayItem { Id = original.Id });

        Assert.True(vm.IsCreateEditModalOpen);
        Assert.False(vm.IsEditMode);
        Assert.Equal("Draft", vm.ModalStatus);
        Assert.Equal(DateTime.Today, vm.ModalIssueDate!.Value.Date);
        Assert.Equal(DateTime.Today.AddDays(30), vm.ModalDueDate!.Value.Date);
        Assert.Equal("CUS-1", vm.SelectedCustomer?.Id);
        Assert.Equal((10m, 7m, 5m, "Rush", 3m, 40m), (vm.TaxRate, vm.ShippingAmount, vm.DiscountAmount,
            vm.CustomFeeLabel, vm.CustomFeeAmount, vm.SecurityDeposit));
        Assert.Equal("Thanks for your business", vm.ModalNotes);
        var line = Assert.Single(vm.LineItems);
        Assert.Equal(("P1", 1m, 100m), (line.SelectedProduct?.Id, line.Quantity ?? 0, line.UnitPrice ?? 0));
        Assert.Null(line.RevenueRecordId);

        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        var copy = Company.Invoices.Single(i => i != original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotEqual(original.InvoiceNumber, copy.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Draft, copy.Status);
        Assert.Equal(0m, copy.AmountPaid);
        Assert.Empty(copy.History);
        Assert.True(string.IsNullOrEmpty(copy.RecurringInvoiceId));
        Assert.Null(Assert.Single(copy.LineItems).RevenueRecordId);
        Assert.Equal(40m, copy.SecurityDeposit);
        Assert.Empty(Company.RecurringInvoices);

        Assert.Equal((InvoiceStatus.Paid, 147m, "REC-1"), (original.Status, original.AmountPaid, original.RecurringInvoiceId));
        Assert.Equal("REV-2025-00001", original.LineItems.Single().RevenueRecordId);
        Assert.Equal("INV-2025-00007", Company.Revenues.Single().InvoiceId);
    }

    [Fact]
    public async Task DuplicatingARentalInvoice_DropsTheRentalAndItsDeposit()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Products.Add(new Product { Id = "P1", Name = "Ladder", UnitPrice = 20m, Type = CategoryType.Revenue });
        var rental = new RentalRecord { Id = "RNT-1", CustomerId = "CUS-1", SecurityDeposit = 50m, InvoiceIds = ["INV-2025-00003"] };
        Company.Rentals.Add(rental);
        var original = new Invoice
        {
            Id = "INV-2025-00003",
            InvoiceNumber = "#INV-2025-00003",
            CustomerId = "CUS-1",
            IssueDate = new DateTime(2025, 5, 1),
            DueDate = new DateTime(2025, 5, 15),
            Status = InvoiceStatus.Sent,
            LineItems = [new LineItem { ProductId = "P1", RentalRecordId = "RNT-1", Description = "Ladder", Quantity = 3, UnitPrice = 20m }],
            SecurityDeposit = 50m,
            OriginalCurrency = "USD"
        };
        Company.Invoices.Add(original);

        var vm = new InvoiceModalsViewModel();
        vm.DuplicateInvoice(new InvoiceDisplayItem { Id = original.Id });

        Assert.Equal(0m, vm.SecurityDeposit);
        Assert.Null(Assert.Single(vm.LineItems).RentalRecordId);
        Assert.Equal(DateTime.Today.AddDays(14), vm.ModalDueDate!.Value.Date);

        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        var copy = Company.Invoices.Single(i => i != original);
        Assert.Equal(0m, copy.SecurityDeposit);
        Assert.Equal(60m, copy.Total);
        Assert.Null(copy.LineItems.Single().RentalRecordId);
        Assert.Equal(["INV-2025-00003"], rental.InvoiceIds);
        Assert.Equal(50m, original.SecurityDeposit);
    }

    private static string Snapshot(Transaction t) =>
        $"{t.Id} {t.Date:yyyy-MM-dd} {t.Total} {t.PaymentMethod} {t.ReceiptId} {t.Notes} " +
        string.Join(",", t.LineItems.Select(li => $"{li.ProductId}:{li.Quantity}x{li.UnitPrice}"));
}
