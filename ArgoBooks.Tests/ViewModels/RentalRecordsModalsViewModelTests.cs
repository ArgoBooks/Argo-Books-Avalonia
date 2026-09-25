using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real RentalRecordsModalsViewModel return/undo/redo flow. Guards the fix where the redo
/// lambda re-read the live return-modal fields (which reset when the modal reopens) instead of the
/// values the user actually confirmed, so redo could flip "paid" back off and change the total.
/// </summary>
public class RentalRecordsModalsViewModelTests : ModalViewModelTestBase
{
    private RentalRecord SeedActiveRental()
    {
        var record = new RentalRecord
        {
            Id = "RNT-1",
            CustomerId = "CUST-1",
            Status = RentalStatus.Active,
            StartDate = DateTime.Today.AddDays(-5),
            SecurityDeposit = 20m,
            Quantity = 1,
            RateType = RateType.Daily,
            RateAmount = 10m,
            // RentalItemId "X" does not resolve to inventory, so the return skips stock adjustments.
            LineItems = new List<RentalLineItem>
            {
                new() { RentalItemId = "X", Quantity = 1, RateType = RateType.Daily, RateAmount = 10m }
            }
        };
        Company.Rentals.Add(record);
        return record;
    }

    private static RentalRecordDisplayItem Row(bool active = true) =>
        new() { Id = "RNT-1", IsActive = active, ItemName = "Widget", CustomerName = "Bob" };

    [Fact]
    public void ReturnModal_ChangingReturnDate_RecomputesTheChargedCost()
    {
        var record = SeedActiveRental(); // started 5 days ago, 1 unit at 10/day
        var vm = new RentalRecordsModalsViewModel();
        vm.OpenReturnModal(Row());
        Assert.Equal(50m, vm.ReturnTotalCost);

        vm.ReturnDate = new DateTimeOffset(record.StartDate.AddDays(10));
        Assert.Equal(100m, vm.ReturnTotalCost);

        vm.ConfirmReturn();
        Assert.Equal(100m, record.TotalCost);
    }

    [Fact]
    public void ReturnRental_ExtraCharges_AreAddedToTheTotal()
    {
        var record = SeedActiveRental();
        var vm = new RentalRecordsModalsViewModel();

        vm.OpenReturnModal(Row());
        vm.ReturnExtraCharges = "25";
        vm.ReturnExtraChargesNote = "Late";
        vm.ConfirmReturn();

        Assert.Equal((75m, 25m, "Late"), (record.TotalCost, record.ExtraCharges, record.ExtraChargesNote));
    }

    private void SeedDepositInvoice(RentalRecord record)
    {
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUST-1", OriginalCurrency = "USD",
            IssueDate = DateTime.Today.AddDays(-5), Subtotal = 50m, SecurityDeposit = 20m, Total = 70m, TotalUSD = 70m,
            AmountPaid = 70m, Status = InvoiceStatus.Paid
        });
        record.InvoiceIds.Add("INV-1");
    }

    private static void Return(RentalRecordsModalsViewModel vm, string refund)
    {
        vm.OpenReturnModal(Row());
        vm.ReturnDepositRefund = refund;
        vm.ConfirmReturn();
    }

    // A deposit the business keeps is earned, so it becomes revenue on the day the rental comes back.
    [Fact]
    public void ReturnRental_KeepingTheDeposit_MakesItRevenue()
    {
        SeedDepositInvoice(SeedActiveRental());
        var vm = new RentalRecordsModalsViewModel();

        Return(vm, refund: "0");

        var kept = Assert.Single(Company.Revenues);
        Assert.True(kept.IsKeptDeposit);
        Assert.Equal((20m, RevenuePaymentStatus.Paid, DateTime.Today, "INV-1"),
            (kept.Total, kept.PaymentStatus, kept.Date.Date, kept.InvoiceId));
    }

    [Fact]
    public void ReturnRental_RefundingPartOfTheDeposit_KeepsTheRest()
    {
        var record = SeedActiveRental();
        SeedDepositInvoice(record);

        Return(new RentalRecordsModalsViewModel(), refund: "15");

        Assert.Equal(15m, record.DepositRefunded);
        Assert.Equal(5m, Assert.Single(Company.Revenues).Total);
    }

    [Fact]
    public void ReturnRental_GivingTheDepositBack_AddsNoRevenue()
    {
        SeedDepositInvoice(SeedActiveRental());

        Return(new RentalRecordsModalsViewModel(), refund: "20");

        Assert.Empty(Company.Revenues);
    }

    [Fact]
    public void ReturnRental_RefundingMoreThanTheDeposit_IsRefused()
    {
        var record = SeedActiveRental();
        var vm = new RentalRecordsModalsViewModel();

        Return(vm, refund: "25");

        Assert.Equal(RentalStatus.Active, record.Status);
        Assert.NotNull(vm.ReturnDepositRefundError);
    }

    // The deposit came in with the invoice, so it is priced at the invoice's rate. When that rate
    // isn't in yet, the kept deposit waits for the same date rather than taking the return day's.
    [Fact]
    public void ReturnRental_KeepingADepositOnAnInvoiceWaitingForItsRate_WaitsForTheInvoicesDate()
    {
        var record = SeedActiveRental();
        var issued = DateTime.Today.AddMonths(-3);
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUST-1", OriginalCurrency = "EUR",
            IssueDate = issued, Subtotal = 50m, SecurityDeposit = 20m, Total = 70m,
            AmountPaid = 70m, Status = InvoiceStatus.Paid, IsPendingConversion = true
        });
        record.InvoiceIds.Add("INV-1");

        Return(new RentalRecordsModalsViewModel(), refund: "0");

        var kept = Assert.Single(Company.Revenues);
        Assert.True(kept.IsPendingConversion);
        var queued = Assert.Single(Company.PendingConversions);
        Assert.Equal((kept.Id, issued), (queued.TransactionId, queued.TransactionDate));
    }

    // A rental paid without an invoice never reached revenue or profit.
    [Fact]
    public void ReturnRental_PaidWithoutAnInvoice_RecordsTheRevenue()
    {
        var record = SeedActiveRental();
        var vm = new RentalRecordsModalsViewModel();

        vm.OpenReturnModal(Row());
        vm.ReturnMarkAsPaid = true;
        vm.ConfirmReturn();

        var revenue = Assert.Single(Company.Revenues);
        Assert.Equal((50m, "RNT-1", revenue.Id), (revenue.Total, revenue.ReferenceNumber, record.RevenueId));
    }

    [Fact]
    public void MarkAsPaid_AfterTheReturn_RecordsTheRevenueAndUndoRemovesIt()
    {
        var record = SeedActiveRental();
        var vm = new RentalRecordsModalsViewModel();
        Return(vm, refund: "20");

        vm.MarkAsPaid(Row(active: false));

        Assert.True(record.Paid);
        Assert.Equal(50m, Assert.Single(Company.Revenues).Total);

        Undo();
        Assert.False(record.Paid);
        Assert.Empty(Company.Revenues);
    }

    [Fact]
    public void MarkAsUnpaid_RemovesTheRevenue_AndUndoPutsItBack()
    {
        var record = SeedActiveRental();
        var vm = new RentalRecordsModalsViewModel();
        vm.OpenReturnModal(Row());
        vm.ReturnMarkAsPaid = true;
        vm.ConfirmReturn();
        var revenue = Assert.Single(Company.Revenues);

        vm.MarkAsUnpaid(Row(active: false));

        Assert.False(record.Paid);
        Assert.Null(record.RevenueId);
        Assert.Empty(Company.Revenues);

        Undo();
        Assert.True(record.Paid);
        Assert.Equal(revenue.Id, record.RevenueId);
        Assert.Same(revenue, Assert.Single(Company.Revenues));
    }

    private InventoryItem SeedRentableStock(int inStock)
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Bob" });
        Company.Products.Add(new Product { Id = "P1", Name = "Ladder" });
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = inStock };
        Company.Inventory.Add(stock);
        Company.RentalInventory.Add(new RentalItem { Id = "RI-1", InventoryItemId = "INV-1", DailyRate = 10m });
        return stock;
    }

    private static RentalRecordsModalsViewModel NewRental(string quantity, int startIn, int dueIn)
    {
        var vm = new RentalRecordsModalsViewModel();
        vm.OpenAddModal();
        vm.ModalCustomer = vm.AvailableCustomers.Single();
        var line = vm.RentalLineItems.Single();
        line.SelectedItem = vm.AvailableItems.Single();
        line.Quantity = quantity;
        vm.ModalStartDate = new DateTimeOffset(DateTime.Today.AddDays(startIn));
        vm.ModalDueDate = new DateTimeOffset(DateTime.Today.AddDays(dueIn));
        return vm;
    }

    private static RentalRecordsModalsViewModel NewRentalWithTwoLinesOfSameItem(string firstQty, string secondQty)
    {
        var vm = new RentalRecordsModalsViewModel();
        vm.OpenAddModal();
        vm.ModalCustomer = vm.AvailableCustomers.Single();
        var first = vm.RentalLineItems.Single();
        first.SelectedItem = vm.AvailableItems.Single();
        first.Quantity = firstQty;
        vm.AddRentalLineItem();
        var second = vm.RentalLineItems.Last();
        second.SelectedItem = vm.AvailableItems.Single();
        second.Quantity = secondQty;
        return vm;
    }

    [Fact]
    public void NewRental_SameItemOnTwoLines_CannotRentMoreThanInStock()
    {
        var stock = SeedRentableStock(5);
        var vm = NewRentalWithTwoLinesOfSameItem("3", "3");

        vm.SaveNewRecord();

        Assert.Empty(Company.Rentals);
        Assert.Equal(5, stock.InStock);
        Assert.Contains(vm.RentalLineItems, li => li.QuantityError != null);
    }

    [Fact]
    public void NewRental_SameItemOnTwoLines_WithinStock_Saves()
    {
        var stock = SeedRentableStock(5);
        var vm = NewRentalWithTwoLinesOfSameItem("2", "3");

        vm.SaveNewRecord();

        Assert.Single(Company.Rentals);
        Assert.Equal(0, stock.InStock);
    }

    [Fact]
    public void NewRental_StartingLater_IsReservedAndLeavesTheStock()
    {
        var stock = SeedRentableStock(5);

        NewRental("2", startIn: 3, dueIn: 5).SaveNewRecord();

        Assert.Equal(RentalStatus.Reserved, Assert.Single(Company.Rentals).Status);
        Assert.Equal(5, stock.InStock);
    }

    // The same unit can't be promised twice for days that overlap, but it can for other days.
    [Fact]
    public void NewRental_OverlappingAReservation_IsRefused_ButLaterDatesSave()
    {
        SeedRentableStock(1);
        Company.Rentals.Add(new RentalRecord
        {
            Id = "RNT-1", CustomerId = "CUST-1", Status = RentalStatus.Reserved,
            StartDate = DateTime.Today.AddDays(3), DueDate = DateTime.Today.AddDays(5),
            LineItems = [new RentalLineItem { RentalItemId = "RI-1", Quantity = 1, RateType = RateType.Daily, RateAmount = 10m }]
        });

        var overlapping = NewRental("1", startIn: 4, dueIn: 6);
        overlapping.SaveNewRecord();
        Assert.Single(Company.Rentals);
        Assert.NotNull(overlapping.RentalLineItems.Single().QuantityError);

        NewRental("1", startIn: 6, dueIn: 8).SaveNewRecord();
        Assert.Equal(2, Company.Rentals.Count);
    }

    [Fact]
    public void CheckOut_TakesTheReservationsStock_AndUndoPutsItBack()
    {
        var stock = SeedRentableStock(5);
        var record = new RentalRecord
        {
            Id = "RNT-1", CustomerId = "CUST-1", Status = RentalStatus.Reserved,
            StartDate = DateTime.Today.AddDays(2), DueDate = DateTime.Today.AddDays(4),
            LineItems = [new RentalLineItem { RentalItemId = "RI-1", Quantity = 2, RateType = RateType.Daily, RateAmount = 10m }]
        };
        Company.Rentals.Add(record);
        var vm = new RentalRecordsModalsViewModel();

        vm.CheckOut(new RentalRecordDisplayItem { Id = "RNT-1", IsReserved = true });

        Assert.Equal((RentalStatus.Active, DateTime.Today, 3m), (record.Status, record.StartDate, stock.InStock));

        Undo();
        Assert.Equal((RentalStatus.Reserved, DateTime.Today.AddDays(2), 5m), (record.Status, record.StartDate, stock.InStock));
    }

    // "Rent Out" saves only the top-level item and quantity, with no line items, so an edit has to
    // read the units already out from those or it takes them off the stock a second time.
    [Fact]
    public void EditRentOutRental_ChangingOnlyTheDueDate_LeavesStockAlone()
    {
        var stock = SeedRentableStock(7); // 10 owned, 3 out on this rental
        var record = new RentalRecord
        {
            Id = "RNT-1", CustomerId = "CUST-1", RentalItemId = "RI-1", Quantity = 3,
            RateType = RateType.Daily, RateAmount = 10m, Status = RentalStatus.Active,
            StartDate = DateTime.Today, DueDate = DateTime.Today.AddDays(1)
        };
        Company.Rentals.Add(record);
        var vm = new RentalRecordsModalsViewModel();

        vm.OpenEditModal(new RentalRecordDisplayItem { Id = "RNT-1", IsActive = true });
        vm.ModalDueDate = new DateTimeOffset(DateTime.Today.AddDays(5));
        vm.SaveEditedRecord();

        Assert.Equal(DateTime.Today.AddDays(5), record.DueDate);
        Assert.Equal(7, stock.InStock);
        Assert.DoesNotContain(Company.StockAdjustments, a => a.Reason == "Rental edited");
    }
}
