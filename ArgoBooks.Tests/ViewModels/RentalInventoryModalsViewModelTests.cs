using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A rental's return, edit and delete find the stock through the rental item's link at that moment,
/// so the link must not move to another stock item while units are out.
/// </summary>
public class RentalInventoryModalsViewModelTests : ModalViewModelTestBase
{
    private RentalItem SeedItemWithRental(RentalStatus status)
    {
        Company.Products.Add(new Product { Id = "P1", Name = "Ladder" });
        Company.Products.Add(new Product { Id = "P2", Name = "Drill" });
        Company.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 7 });
        Company.Inventory.Add(new InventoryItem { Id = "INV-2", ProductId = "P2", InStock = 4 });
        var item = new RentalItem { Id = "RI-1", InventoryItemId = "INV-1", DailyRate = 10m };
        Company.RentalInventory.Add(item);
        Company.Rentals.Add(new RentalRecord
        {
            Id = "RNT-1", CustomerId = "CUST-1", RentalItemId = "RI-1", Quantity = 3, Status = status
        });
        return item;
    }

    private static void Relink(RentalInventoryModalsViewModel vm, string inventoryItemId)
    {
        vm.OpenEditModal(new RentalItemDisplayItem { Id = "RI-1" });
        vm.ModalInventoryItem = vm.AvailableInventoryItems.Single(i => i.Id == inventoryItemId);
        vm.SaveEditedItem();
    }

    [Theory]
    [InlineData(RentalStatus.Active)]
    [InlineData(RentalStatus.Overdue)]
    public void Relink_WhileUnitsAreOut_IsRefused(RentalStatus status)
    {
        var item = SeedItemWithRental(status);
        var vm = new RentalInventoryModalsViewModel();

        Relink(vm, "INV-2");

        Assert.Equal("INV-1", item.InventoryItemId);
        Assert.NotNull(vm.ModalInventoryItemError);
    }

    [Fact]
    public void Relink_OnceEveryRentalIsBack_IsAllowed()
    {
        var item = SeedItemWithRental(RentalStatus.Returned);
        var vm = new RentalInventoryModalsViewModel();

        Relink(vm, "INV-2");

        Assert.Equal("INV-2", item.InventoryItemId);
    }

    // Rent Out saved one deposit however many units went out.
    [Fact]
    public void RentOut_ThreeUnits_TakesADepositAndStockForEach()
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Bob" });
        Company.Products.Add(new Product { Id = "P1", Name = "Ladder" });
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 5 };
        Company.Inventory.Add(stock);
        Company.RentalInventory.Add(new RentalItem { Id = "RI-1", InventoryItemId = "INV-1", DailyRate = 10m, SecurityDeposit = 20m });
        var vm = new RentalInventoryModalsViewModel();

        vm.OpenRentOutModal(new RentalItemDisplayItem { Id = "RI-1", Name = "Ladder" });
        vm.RentOutCustomer = vm.AvailableCustomers.Single();
        vm.RentOutQuantity = "3";
        vm.ConfirmRentOut();

        var rental = Assert.Single(Company.Rentals);
        Assert.Equal((60m, 20m, 3), (rental.SecurityDeposit, rental.LineItems.Single().SecurityDeposit, rental.Quantity));
        Assert.Equal(2m, stock.InStock);
    }
}
