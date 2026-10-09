using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A quantity typed onto the paper is rounded as it is applied, not only as it is printed. If the
/// model kept 17500.555 while the invoice showed 17500.56, the line amount would not be the product
/// of the two figures the customer can see.
/// </summary>
public class InvoicePaperQuantityTests : ModalViewModelTestBase
{
    private static InvoiceModalsViewModel EditorWithOneLine()
    {
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        Assert.NotEmpty(vm.LineItems);
        return vm;
    }

    [Theory]
    [InlineData("17500.555", 17500.56)]
    [InlineData("17500.554", 17500.55)]
    [InlineData("17500.50", 17500.50)]
    [InlineData("2", 2)]
    public void TypingAQuantity_StoresItToTwoDecimalPlaces(string typed, decimal stored)
    {
        var vm = EditorWithOneLine();

        vm.ApplyPaperEdit("quantity", 0, typed);

        Assert.Equal(stored, vm.LineItems[0].Quantity);
    }

    /// <summary>Half of a hundredth rounds away from zero, the way an invoice is expected to.</summary>
    [Fact]
    public void TypingAHalfHundredth_RoundsUp()
    {
        var vm = EditorWithOneLine();

        vm.ApplyPaperEdit("quantity", 0, "1.005");

        Assert.Equal(1.01m, vm.LineItems[0].Quantity);
    }

    [Fact]
    public void TypingSomethingThatIsNotANumber_LeavesTheQuantityAlone()
    {
        var vm = EditorWithOneLine();
        vm.ApplyPaperEdit("quantity", 0, "3");

        vm.ApplyPaperEdit("quantity", 0, "abc");

        Assert.Equal(3m, vm.LineItems[0].Quantity);
    }
}
