using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A quantity typed onto the paper is rounded as it is applied, not only as it is printed. If the
/// model kept more places than the invoice shows, the line amount would not be the product of the
/// two figures the customer can see.
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
    [InlineData("17500.5555", 17500.556)]
    [InlineData("17500.5554", 17500.555)]
    [InlineData("0.125", 0.125)]
    [InlineData("17500.50", 17500.50)]
    [InlineData("2", 2)]
    public void TypingAQuantity_StoresItToThreeDecimalPlaces(string typed, decimal stored)
    {
        var vm = EditorWithOneLine();

        vm.ApplyPaperEdit("quantity", 0, typed);

        Assert.Equal(stored, vm.LineItems[0].Quantity);
    }

    /// <summary>Half of a thousandth rounds away from zero, the way an invoice is expected to.</summary>
    [Fact]
    public void TypingAHalfThousandth_RoundsUp()
    {
        var vm = EditorWithOneLine();

        vm.ApplyPaperEdit("quantity", 0, "1.0005");

        Assert.Equal(1.001m, vm.LineItems[0].Quantity);
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
