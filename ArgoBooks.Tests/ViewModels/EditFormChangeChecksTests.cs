using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A form asks whether to save on the way out only when it notices something changed, and it
/// notices by comparing the fields it listed against how they were when the form opened. A field
/// the form saves but left off that list is thrown away on close with nothing said, so each test
/// here changes one field and nothing else.
/// </summary>
public class EditFormChangeChecksTests : ModalViewModelTestBase
{
    [Fact]
    public void ChangingOnlyAPaymentsDateCountsAsAChange()
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Ada Lovelace" });
        Company.Invoices.Add(new Invoice { Id = "INV-1", CustomerId = "CUS-1", Total = 100m });
        Company.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", CustomerId = "CUS-1",
            Amount = 100m, Date = new DateTime(2026, 5, 1),
        });

        var vm = new PaymentModalsViewModel();
        vm.OpenEditModal(new PaymentDisplayItem { Id = "PAY-1" });
        Assert.False(vm.HasEditModalChanges);

        vm.ModalDate = new DateTimeOffset(new DateTime(2026, 5, 2));

        Assert.True(vm.HasEditModalChanges);
    }

    [Fact]
    public void ChangingOnlyACustomersStatusCountsAsAChange()
    {
        Company.Customers.Add(new Customer
        {
            Id = "CUS-1", Name = "Ada Lovelace", Status = EntityStatus.Active,
        });

        var vm = new CustomerModalsViewModel();
        vm.OpenEditModal(new CustomerDisplayItem { Id = "CUS-1" });
        Assert.False(vm.HasEditModalChanges);

        vm.ModalStatus = "Inactive";

        Assert.True(vm.HasEditModalChanges);
    }

    /// <summary>
    /// The one that costs something. A line's location decides which location the stock comes out
    /// of, so a change dropped here leaves one location short and the other over, and nothing on
    /// screen ever said the change had not been kept.
    /// </summary>
    [Fact]
    public async Task ChangingOnlyALinesLocationCountsAsAChange()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Categories.Add(new Category { Id = "CAT-SUP", Name = "Supplies", Type = CategoryType.Expense });
        Company.Products.Add(new Product
        {
            Id = "P-WIDGET", Name = "Widget", CategoryId = "CAT-SUP",
            Type = CategoryType.Expense, CostPrice = 10m, TrackInventory = true,
        });
        Company.Locations.Add(new Location { Id = "LOC-SHOP", Name = "Shop" });
        Company.Locations.Add(new Location { Id = "LOC-WARE", Name = "Warehouse" });
        Company.Inventory.Add(new InventoryItem { Id = "INV-A", ProductId = "P-WIDGET", LocationId = "LOC-SHOP", InStock = 5 });
        Company.Inventory.Add(new InventoryItem { Id = "INV-B", ProductId = "P-WIDGET", LocationId = "LOC-WARE", InStock = 5 });

        var vm = new ExpenseModalsViewModel();
        vm.OpenAddModal();
        var line = vm.LineItems[0];
        line.SelectedProduct = vm.ProductOptions.Single(p => p.Id == "P-WIDGET");
        line.ItemText = line.SelectedProduct.Name;
        line.Quantity = 1;
        line.UnitPrice = 10m;
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.OpenEditModal(new ExpenseDisplayItem { Id = Company.Expenses.Single().Id });
        var editing = vm.LineItems[0];
        Assert.True(editing.LocationOptions.Count > 1, "the line needs somewhere else to move to");
        Assert.False(vm.HasEditModalChanges);

        editing.SelectedLocation = editing.LocationOptions.Single(o => o.Id != editing.SelectedLocation?.Id);

        Assert.True(vm.HasEditModalChanges);
    }
}
