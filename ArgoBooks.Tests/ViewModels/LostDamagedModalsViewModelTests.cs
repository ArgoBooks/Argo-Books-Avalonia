using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Removing a record from the Lost/Damaged page is its own undo step, as removing a return from the
/// Returns page is, so Ctrl+Z brings it back instead of undoing something earlier.
/// </summary>
public class LostDamagedModalsViewModelTests : ModalViewModelTestBase
{
    [Fact]
    public void RemoveRecord_CanBeUndoneAndRedone()
    {
        var record = new LostDamaged { Id = "LOST-001", ProductId = "P1", Quantity = 2 };
        Company.LostDamaged.Add(record);
        var vm = new LostDamagedModalsViewModel();

        vm.OpenUndoItemModal(record, "LOST-001");
        vm.ConfirmUndoItemCommand.Execute(null);
        Assert.Empty(Company.LostDamaged);

        Undo();
        Assert.Same(record, Assert.Single(Company.LostDamaged));

        Redo();
        Assert.Empty(Company.LostDamaged);
    }
}
