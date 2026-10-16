using Avalonia.Controls;

namespace ArgoBooks.Helpers;

/// <summary>Keyboard navigation for the panels whose items are buttons named MenuItem1, MenuItem2 and so on.</summary>
internal static class MenuItemNavigation
{
    public static void Focus(Control panel, int index) =>
        panel.FindControl<Button>($"MenuItem{index}")?.Focus();

    /// <summary>Runs the item's command, as a click would.</summary>
    public static void Activate(Control panel, int index)
    {
        var menuItem = panel.FindControl<Button>($"MenuItem{index}");
        if (menuItem?.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
        {
            menuItem.Command.Execute(menuItem.CommandParameter);
        }
    }
}
