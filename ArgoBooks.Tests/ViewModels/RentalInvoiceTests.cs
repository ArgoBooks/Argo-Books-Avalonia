using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// An invoice made from a rental charged every unit at the first item's rate.
/// </summary>
public class RentalInvoiceTests : ModalViewModelTestBase
{
    [Fact]
    public void InvoiceFromRental_ChargesEachItemAtItsOwnRate_PlusExtraCharges()
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Bob" });
        foreach (var (n, name) in new[] { (1, "Ladder"), (2, "Generator") })
        {
            Company.Products.Add(new Product { Id = $"P{n}", Name = name });
            Company.Inventory.Add(new InventoryItem { Id = $"INV-{n}", ProductId = $"P{n}", InStock = 5 });
            Company.RentalInventory.Add(new RentalItem { Id = $"RI-{n}", InventoryItemId = $"INV-{n}" });
        }
        Company.Rentals.Add(new RentalRecord
        {
            Id = "RNT-1", CustomerId = "CUST-1", Status = RentalStatus.Returned,
            StartDate = DateTime.Today.AddDays(-3), DueDate = DateTime.Today, ReturnDate = DateTime.Today,
            SecurityDeposit = 40m, ExtraCharges = 15m, ExtraChargesNote = "Late", TotalCost = 225m,
            LineItems =
            [
                new RentalLineItem { RentalItemId = "RI-1", Quantity = 2, RateType = RateType.Daily, RateAmount = 10m },
                new RentalLineItem { RentalItemId = "RI-2", Quantity = 1, RateType = RateType.Daily, RateAmount = 50m }
            ]
        });

        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateFromRental("RNT-1");

        Assert.Equal(new decimal?[] { 2m, 1m, 1m }, vm.LineItems.Select(li => li.Quantity));
        Assert.Equal(new[] { 60m, 150m, 15m }, vm.LineItems.Select(li => (li.Quantity ?? 0m) * (li.UnitPrice ?? 0m)));
        Assert.All(vm.LineItems, li => Assert.Equal("RNT-1", li.RentalRecordId));
        Assert.Equal(40m, vm.SecurityDeposit);
    }
}
