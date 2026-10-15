using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArgoBooks.Utilities;

/// <summary>
/// Helper class for common modal operations.
/// </summary>
public static class ModalHelper
{
    /// <summary>
    /// Returns focus to the AppShell so keyboard shortcuts (like Ctrl+K) work again.
    /// Call this when a modal closes.
    /// </summary>
    /// <param name="control">Any control in the visual tree (typically the modal itself).</param>
    public static void ReturnFocusToAppShell(Control control)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var topLevel = TopLevel.GetTopLevel(control);

            // Focus that has already moved to something still on screen, as Tab out of a panel does, stays where it went.
            if (topLevel?.FocusManager?.GetFocusedElement() is Visual { IsEffectivelyVisible: true } focused
                && focused is not TopLevel && focused != control && !control.IsVisualAncestorOf(focused))
                return;

            if (topLevel != null)
            {
                var appShell = topLevel.GetVisualDescendants()
                    .OfType<UserControl>()
                    .FirstOrDefault(x => x.GetType().Name == "AppShell");
                appShell?.Focus();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>The buttons a user can reach in a panel, in the order they appear.</summary>
    public static List<Button> VisibleButtons(Visual container) =>
        container.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
            .ToList();

    /// <summary>Moves focus to the next or previous button in a panel, wrapping round, as the arrow keys do in a menu.</summary>
    public static void StepThroughButtons(Visual container, int step)
    {
        var buttons = VisibleButtons(container);
        if (buttons.Count == 0)
            return;

        var current = TopLevel.GetTopLevel(container)?.FocusManager?.GetFocusedElement() is Button focused ? buttons.IndexOf(focused) : -1;
        var next = current < 0
            ? (step > 0 ? 0 : buttons.Count - 1)
            : (current + step + buttons.Count) % buttons.Count;
        buttons[next].Focus(NavigationMethod.Directional);
    }

    /// <summary>Closes a header panel on Tab and moves focus on from the button that opened it, the way Tab leaves a drop-down menu.</summary>
    public static void LeavePanelOnTab(KeyEventArgs e, IInputElement? opener, Action close)
    {
        var direction = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? NavigationDirection.Previous : NavigationDirection.Next;
        close();
        e.Handled = true;

        Dispatcher.UIThread.Post(() =>
        {
            if (opener is InputElement { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } element && element.Focus())
                TopLevel.GetTopLevel(element)?.FocusManager?.TryMoveFocus(direction);
        });
    }
}
