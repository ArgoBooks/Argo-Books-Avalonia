using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class CategoryModalsViewModelTests : ModalViewModelTestBase
{
    private static readonly PropertyInfo ConfirmationDialogProperty =
        typeof(App).GetProperty(nameof(App.ConfirmationDialog))!;

    private Category AddCategory(string id, string? parentId = null)
    {
        var category = new Category { Id = id, Name = id, Type = CategoryType.Expense, ParentId = parentId };
        Company.Categories.Add(category);
        return category;
    }

    /// <summary>Starts a delete and answers the dialog's first question with its primary button.</summary>
    private static void DeleteAnsweringPrimary(Category category)
    {
        var dialog = new ConfirmationDialogViewModel();
        var priorDialog = ConfirmationDialogProperty.GetValue(null);
        var priorContext = SynchronizationContext.Current;
        // With no context, the delete's await resumes inline as soon as the dialog is answered.
        SynchronizationContext.SetSynchronizationContext(null);
        ConfirmationDialogProperty.SetValue(null, dialog);
        try
        {
            new CategoryModalsViewModel().OpenDeleteConfirm(new CategoryDisplayItem { Id = category.Id, Name = category.Name });
            if (dialog.IsOpen)
                dialog.PrimaryActionCommand.Execute(null);
        }
        finally
        {
            ConfirmationDialogProperty.SetValue(null, priorDialog);
            SynchronizationContext.SetSynchronizationContext(priorContext);
        }
    }

    [Fact]
    public void DeleteAll_SubcategoryUsedByAProduct_IsRefused()
    {
        var parent = AddCategory("CAT-PUR-001");
        var child = AddCategory("CAT-PUR-002", parent.Id);
        Company.Products.Add(new Product { Id = "PRD-001", Name = "Widget", CategoryId = child.Id });

        DeleteAnsweringPrimary(parent);

        Assert.Equal([parent, child], Company.Categories);
    }

    // The Categories page draws two levels, so a category with subcategories moved under another
    // would put them on a third level nobody can see, edit or delete.
    [Fact]
    public void Move_CategoryWithSubcategoriesUnderAnother_IsRefused()
    {
        var parent = AddCategory("CAT-PUR-001");
        AddCategory("CAT-PUR-002", parent.Id);
        var target = AddCategory("CAT-PUR-003");
        var vm = new CategoryModalsViewModel();

        vm.OpenMoveModal(new CategoryDisplayItem { Id = parent.Id, Name = parent.Name }, isExpensesTab: true);
        vm.MoveTargetCategory = vm.MoveTargetCategories.Single(c => c.Id == target.Id);
        vm.ConfirmMove();

        Assert.Null(parent.ParentId);
        Assert.NotNull(vm.MoveError);
    }
}
