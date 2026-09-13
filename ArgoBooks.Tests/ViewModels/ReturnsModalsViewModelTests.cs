using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Creating a return only adds a Return record (line quantities, stock and the transaction are left
/// alone), so undoing one from the Returns page must only remove that record.
/// </summary>
public class ReturnsModalsViewModelTests : ModalViewModelTestBase
{
    [Fact]
    public void UndoReturn_RemovesOnlyTheReturnRecord_AndCanBeUndone()
    {
        var revenue = new Revenue
        {
            Id = "REV-1",
            LineItems = [new LineItem { ProductId = "P1", Quantity = 5 }]
        };
        Company.Revenues.Add(revenue);
        var returnRecord = new Return
        {
            Id = "RET-001",
            OriginalTransactionId = "REV-1",
            ReturnType = "Customer",
            Items = [new ReturnItem { ProductId = "P1", Quantity = 5 }]
        };
        Company.Returns.Add(returnRecord);

        var vm = new ReturnsModalsViewModel();
        vm.OpenUndoReturnModal(returnRecord, "RET-001");
        vm.ConfirmUndoReturnCommand.Execute(null);

        Assert.Empty(Company.Returns);
        Assert.Equal(5m, revenue.LineItems.Single().Quantity);

        Undo();
        Assert.Same(returnRecord, Assert.Single(Company.Returns));

        Redo();
        Assert.Empty(Company.Returns);
        Assert.Equal(5m, revenue.LineItems.Single().Quantity);
    }
}
