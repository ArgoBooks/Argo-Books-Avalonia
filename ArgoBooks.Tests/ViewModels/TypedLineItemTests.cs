using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The item and category boxes on an expense line. On save the text decides: a name matching a
/// product uses it, one matching nothing is created, and a different category on a line with an
/// existing product moves that product, taking every transaction that uses it along. A mistake
/// here rewrites records nobody touched, which is why these go through the real save.
/// </summary>
public class TypedLineItemTests : ModalViewModelTestBase
{
    private ExpenseModalsViewModel NewVm()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Categories.Add(new Category { Id = "CAT-RENT", Name = "Rent", Type = CategoryType.Expense });
        Company.Categories.Add(new Category { Id = "CAT-SUP", Name = "Supplies", Type = CategoryType.Expense });
        Company.Categories.Add(new Category { Id = "CAT-SVC", Name = "Services", Type = CategoryType.Revenue });
        Company.Products.Add(new Product { Id = "P-WIDGET", Name = "Widget", CategoryId = "CAT-RENT", Type = CategoryType.Expense, CostPrice = 100m });
        Company.Products.Add(new Product { Id = "P-GADGET", Name = "Gadget", CategoryId = "CAT-SUP", Type = CategoryType.Expense, CostPrice = 40m });
        Company.Products.Add(new Product { Id = "P-CONSULT", Name = "Consulting", CategoryId = "CAT-SVC", Type = CategoryType.Revenue, UnitPrice = 90m });

        var vm = new ExpenseModalsViewModel();
        vm.OpenAddModal();
        return vm;
    }

    /// <summary>Picking from the list, which also fills the category box from the product.</summary>
    private static void Pick(ExpenseModalsViewModel vm, ExpenseLineItem line, string productId)
    {
        line.SelectedProduct = vm.ProductOptions.First(p => p.Id == productId);
        line.ItemText = line.SelectedProduct.Name;
        line.CategoryText = line.SelectedCategory?.Name;
    }

    private static ExpenseLineItem AddLine(ExpenseModalsViewModel vm)
    {
        vm.AddLineItemCommand.Execute(null);
        return vm.LineItems[^1];
    }

    private Product ProductNamed(string name) => Company.Products.Single(p => p.Name == name);

    #region Which product, and which category

    [Fact]
    public async Task TypingAnExistingProductOverAPickedOne_LeavesItInItsOwnCategory()
    {
        var vm = NewVm();
        var line = vm.LineItems[0];
        Pick(vm, line, "P-WIDGET");

        // Typing over a pick changes only the text, so the category box still shows Rent.
        line.ItemText = "Gadget";
        line.UnitPrice = 40m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal("CAT-SUP", ProductNamed("Gadget").CategoryId);
        Assert.Equal("CAT-RENT", ProductNamed("Widget").CategoryId);
        Assert.Equal("P-GADGET", Assert.Single(Company.Expenses).LineItems[0].ProductId);
    }

    [Fact]
    public async Task ChangingTheCategoryOnOneOfTwoLines_MovesTheProduct_AndUndoPutsItBack()
    {
        var vm = NewVm();
        var first = vm.LineItems[0];
        Pick(vm, first, "P-WIDGET");
        var second = AddLine(vm);
        Pick(vm, second, "P-WIDGET");
        first.CategoryText = "Supplies";

        await vm.SaveTransactionCommand.ExecuteAsync(null);
        Assert.Equal("CAT-SUP", ProductNamed("Widget").CategoryId);

        Undo(); // the expense
        Undo(); // the product change saved with it
        Assert.Empty(Company.Expenses);
        Assert.Equal("CAT-RENT", ProductNamed("Widget").CategoryId);

        Redo();
        Redo();
        Assert.Single(Company.Expenses);
        Assert.Equal("CAT-SUP", ProductNamed("Widget").CategoryId);
    }

    [Fact]
    public async Task TwoLinesAskingForDifferentCategories_StopTheSave()
    {
        var vm = NewVm();
        var first = vm.LineItems[0];
        Pick(vm, first, "P-WIDGET");
        var second = AddLine(vm);
        Pick(vm, second, "P-WIDGET");
        first.CategoryText = "Supplies";
        second.CategoryText = "Utilities";

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Empty(Company.Expenses);
        Assert.True(vm.HasValidationMessage);
        Assert.Equal("CAT-RENT", ProductNamed("Widget").CategoryId);
        Assert.DoesNotContain(Company.Categories, c => c.Name == "Utilities");
    }

    [Fact]
    public async Task TheSameNewItemInTwoCategories_StopsTheSave()
    {
        var vm = NewVm();
        var first = vm.LineItems[0];
        first.ItemText = "Paper";
        first.CategoryText = "Rent";
        first.UnitPrice = 5m;
        var second = AddLine(vm);
        second.ItemText = "paper";
        second.CategoryText = "Supplies";
        second.UnitPrice = 5m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Empty(Company.Expenses);
        Assert.True(vm.HasValidationMessage);
        Assert.DoesNotContain(Company.Products, p => p.Name.Equals("Paper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheSameNewItemOnTwoLines_BecomesOneProduct()
    {
        var vm = NewVm();
        var first = vm.LineItems[0];
        first.ItemText = "Paper";
        first.CategoryText = "Supplies";
        first.UnitPrice = 5m;
        var second = AddLine(vm);
        second.ItemText = "paper";
        second.CategoryText = "Supplies";
        second.UnitPrice = 5m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var paper = Assert.Single(Company.Products, p => p.Name.Equals("Paper", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CAT-SUP", paper.CategoryId);
        Assert.All(Assert.Single(Company.Expenses).LineItems, li => Assert.Equal(paper.Id, li.ProductId));
    }

    /// <summary>
    /// Product names are unique across both sides, which the product form enforces.
    /// </summary>
    [Fact]
    public async Task ANameAlreadyUsedByARevenueProduct_IsNotCreatedTwice()
    {
        var vm = NewVm();
        var line = vm.LineItems[0];
        line.ItemText = "consulting";
        line.CategoryText = "Rent";
        line.UnitPrice = 50m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Empty(Company.Expenses);
        Assert.True(vm.HasValidationMessage);
        Assert.Single(Company.Products, p => p.Name.Equals("Consulting", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A phone capture matches a line to a product by name on either side, so an expense line can
    /// hold a revenue product the expense form does not list. Loaded as plain text, its own name
    /// then read as a new item clashing with that product, and the expense could not be saved.
    /// </summary>
    [Fact]
    public async Task EditingAnEntryWhoseProductTheFormDoesNotList_KeepsThatProduct()
    {
        var vm = NewVm();
        Company.Expenses.Add(new Expense
        {
            Id = "PUR-1",
            Date = new DateTime(2026, 3, 1),
            OriginalCurrency = "USD",
            Amount = 90m,
            Total = 90m,
            LineItems = [new LineItem { ProductId = "P-CONSULT", Description = "Consulting", Quantity = 1, UnitPrice = 90m }]
        });

        vm.OpenEditModal(new ExpenseDisplayItem { Id = "PUR-1" });
        vm.ModalNotes = "edited";
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.False(vm.HasValidationMessage);
        Assert.Equal("edited", expense.Notes);
        Assert.Equal("P-CONSULT", expense.LineItems[0].ProductId);
        Assert.Single(Company.Products, p => p.Name == "Consulting");
    }

    #endregion

    #region What is saved

    /// <summary>
    /// A typed name only becomes the product at save, after the currency conversion has run. If
    /// that filled in the product's price, the entry kept a total the conversion never saw, and a
    /// non-USD company counted it as nothing.
    /// </summary>
    [Fact]
    public async Task ATypedExistingProductWithNoPrice_SavesThePriceOnTheLine()
    {
        var vm = NewVm();
        vm.LineItems[0].ItemText = "Gadget";

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.Equal(expense.Total, expense.TotalUSD);
        Assert.Equal(0m, expense.LineItems[0].UnitPrice);
        Assert.Equal("P-GADGET", expense.LineItems[0].ProductId);
    }

    #endregion

    #region Unsaved changes

    [Fact]
    public async Task TypingANewItemName_WhenEditing_CountsAsAChange()
    {
        var vm = NewVm();
        Pick(vm, vm.LineItems[0], "P-WIDGET");
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.OpenEditModal(new ExpenseDisplayItem { Id = Company.Expenses.Single().Id });
        Assert.False(vm.HasEditModalChanges);

        vm.LineItems[0].ItemText = "Something new";

        Assert.True(vm.HasEditModalChanges);
    }

    [Fact]
    public async Task TypingANewCategoryName_WhenEditing_CountsAsAChange()
    {
        var vm = NewVm();
        Pick(vm, vm.LineItems[0], "P-WIDGET");
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.OpenEditModal(new ExpenseDisplayItem { Id = Company.Expenses.Single().Id });
        Assert.False(vm.HasEditModalChanges);

        vm.LineItems[0].CategoryText = "Office";

        Assert.True(vm.HasEditModalChanges);
    }

    [Fact]
    public void TypingOnlyACategory_CountsAsEnteredData()
    {
        var vm = NewVm();

        vm.LineItems[0].CategoryText = "Rent";

        Assert.True(vm.HasEnteredData);
    }

    #endregion
}
