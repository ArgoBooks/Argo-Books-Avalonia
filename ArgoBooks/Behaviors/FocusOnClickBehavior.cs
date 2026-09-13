using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArgoBooks.Behaviors;

/// <summary>
/// Attached behavior that focuses the first TextBox child when the parent element is clicked.
/// Usage: behaviors:FocusOnClickBehavior.FocusTextBoxOnClick="True"
/// </summary>
public static class FocusOnClickBehavior
{
    public static readonly AttachedProperty<bool> FocusTextBoxOnClickProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "FocusTextBoxOnClick",
            typeof(FocusOnClickBehavior));

    static FocusOnClickBehavior()
    {
        FocusTextBoxOnClickProperty.Changed.AddClassHandler<Control>(OnFocusTextBoxOnClickChanged);
    }

    public static void SetFocusTextBoxOnClick(Control element, bool value)
    {
        element.SetValue(FocusTextBoxOnClickProperty, value);
    }

    private static void OnFocusTextBoxOnClickChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            element.PointerPressed += OnPointerPressed;
            element.Cursor = new Cursor(StandardCursorType.Ibeam);
        }
        else
        {
            element.PointerPressed -= OnPointerPressed;
            element.Cursor = Cursor.Default;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            var textBox = FindTextBox(control);
            textBox?.Focus();
        }
    }

    private static TextBox? FindTextBox(Control control)
    {
        foreach (var child in control.GetVisualDescendants())
        {
            if (child is TextBox textBox)
            {
                return textBox;
            }
        }
        return null;
    }
}
