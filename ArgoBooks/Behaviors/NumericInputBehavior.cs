using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using System.Text.RegularExpressions;

namespace ArgoBooks.Behaviors;

/// <summary>
/// Attached behavior to restrict TextBox input to numeric values only.
/// Uses KeyDown event to prevent non-numeric characters from being entered.
/// </summary>
public static partial class NumericInputBehavior
{
    /// <summary>
    /// Allows only integer input (no decimals).
    /// </summary>
    public static readonly AttachedProperty<bool> IsIntegerOnlyProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsIntegerOnly", typeof(NumericInputBehavior));

    /// <summary>
    /// Allows decimal input (numbers with optional decimal point).
    /// </summary>
    public static readonly AttachedProperty<bool> IsDecimalOnlyProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsDecimalOnly", typeof(NumericInputBehavior));

    /// <summary>
    /// Lets a decimal box take a leading minus, for an amount whose sign means something.
    /// </summary>
    public static readonly AttachedProperty<bool> AllowNegativeProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("AllowNegative", typeof(NumericInputBehavior));

    static NumericInputBehavior()
    {
        IsIntegerOnlyProperty.Changed.AddClassHandler<TextBox>(OnIsIntegerOnlyChanged);
        IsDecimalOnlyProperty.Changed.AddClassHandler<TextBox>(OnIsDecimalOnlyChanged);
    }

    public static bool GetIsIntegerOnly(TextBox element) => element.GetValue(IsIntegerOnlyProperty);
    public static void SetIsIntegerOnly(TextBox element, bool value) => element.SetValue(IsIntegerOnlyProperty, value);

    public static bool GetIsDecimalOnly(TextBox element) => element.GetValue(IsDecimalOnlyProperty);
    public static void SetIsDecimalOnly(TextBox element, bool value) => element.SetValue(IsDecimalOnlyProperty, value);

    public static bool GetAllowNegative(TextBox element) => element.GetValue(AllowNegativeProperty);
    public static void SetAllowNegative(TextBox element, bool value) => element.SetValue(AllowNegativeProperty, value);

    private static void OnIsIntegerOnlyChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.AddHandler(InputElement.KeyDownEvent, OnIntegerKeyDown, RoutingStrategies.Tunnel);
            textBox.AddHandler(InputElement.TextInputEvent, OnIntegerTextInput, RoutingStrategies.Tunnel);
            textBox.PastingFromClipboard += OnIntegerPastingFromClipboard;
        }
        else
        {
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnIntegerKeyDown);
            textBox.RemoveHandler(InputElement.TextInputEvent, OnIntegerTextInput);
            textBox.PastingFromClipboard -= OnIntegerPastingFromClipboard;
        }
    }

    private static void OnIsDecimalOnlyChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.AddHandler(InputElement.KeyDownEvent, OnDecimalKeyDown, RoutingStrategies.Tunnel);
            textBox.AddHandler(InputElement.TextInputEvent, OnDecimalTextInput, RoutingStrategies.Tunnel);
            textBox.PastingFromClipboard += OnDecimalPastingFromClipboard;
        }
        else
        {
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnDecimalKeyDown);
            textBox.RemoveHandler(InputElement.TextInputEvent, OnDecimalTextInput);
            textBox.PastingFromClipboard -= OnDecimalPastingFromClipboard;
        }
    }

    private static void OnIntegerKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsAllowedControlKey(e))
            return;

        // Allow digits (both main keyboard and numpad)
        if (IsDigitKey(e.Key))
            return;

        // Block all other keys
        e.Handled = true;
    }

    private static void OnDecimalKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsAllowedControlKey(e))
            return;

        // Allow digits (both main keyboard and numpad)
        if (IsDigitKey(e.Key))
            return;

        // Allow decimal point (period and numpad decimal)
        if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
        {
            // Only allow if there's no decimal point already
            if (sender is TextBox textBox)
            {
                var currentText = textBox.Text ?? string.Empty;
                var selectionStart = textBox.SelectionStart;
                var selectionEnd = textBox.SelectionEnd;

                // Check if selection contains the decimal point
                var selectedText = selectionEnd > selectionStart
                    ? currentText.Substring(selectionStart, selectionEnd - selectionStart)
                    : string.Empty;

                // Allow if no existing decimal or the selection contains it
                if (!currentText.Contains('.') || selectedText.Contains('.'))
                    return;
            }
        }

        // A minus is taken only at the very start, and only where there is not one already.
        if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && sender is TextBox signedBox && GetAllowNegative(signedBox))
        {
            var text = signedBox.Text ?? string.Empty;
            var start = Math.Min(signedBox.SelectionStart, signedBox.SelectionEnd);
            var end = Math.Max(signedBox.SelectionStart, signedBox.SelectionEnd);
            if (start == 0 && (end > 0 || !text.StartsWith('-')))
                return;
        }

        // Block all other keys
        e.Handled = true;
    }

    private static bool IsDigitKey(Key key)
    {
        return key >= Key.D0 && key <= Key.D9 ||
               key >= Key.NumPad0 && key <= Key.NumPad9;
    }

    private static bool IsAllowedControlKey(KeyEventArgs e)
    {
        // Allow navigation and editing keys
        if (e.Key == Key.Back || e.Key == Key.Delete ||
            e.Key == Key.Left || e.Key == Key.Right ||
            e.Key == Key.Home || e.Key == Key.End ||
            e.Key == Key.Tab || e.Key == Key.Enter ||
            e.Key == Key.Up || e.Key == Key.Down)
            return true;

        // Allow Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z
        if (e.KeyModifiers.HasCommand())
        {
            if (e.Key == Key.A || e.Key == Key.C || e.Key == Key.V ||
                e.Key == Key.X || e.Key == Key.Z)
                return true;
        }

        return false;
    }

    // TextInput handlers as backup for paste operations
    private static void OnIntegerTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && !IntegerRegex().IsMatch(e.Text))
        {
            e.Handled = true;
        }
    }

    private static void OnDecimalTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox textBox || e.Text == null)
            return;

        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = textBox.SelectionStart;
        var selectionEnd = textBox.SelectionEnd;
        var selectionLength = selectionEnd - selectionStart;

        // Build the resulting text
        string newText;
        if (selectionLength > 0)
        {
            newText = currentText.Substring(0, selectionStart) + e.Text + currentText.Substring(selectionEnd);
        }
        else
        {
            var caretIndex = textBox.CaretIndex;
            newText = currentText.Substring(0, caretIndex) + e.Text + currentText.Substring(caretIndex);
        }

        // Check if the result would be a valid decimal
        if (!IsValidDecimalInput(newText, GetAllowNegative(textBox)))
        {
            e.Handled = true;
        }
    }

    private static bool IsValidDecimalInput(string text, bool allowNegative)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        // Allow digits and at most one decimal point
        return (allowNegative ? SignedDecimalInputRegex() : DecimalInputRegex()).IsMatch(text);
    }

    // Paste handlers that sanitize clipboard content
    private static async void OnIntegerPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not TextBox textBox)
                return;

            e.Handled = true;

            var clipboard = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clipboard == null)
                return;

            var clipboardText = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(clipboardText))
                return;

            // Filter to only digits
            var filteredText = new string(clipboardText.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(filteredText))
                return;

            // Insert the filtered text at the caret position
            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionEnd = textBox.SelectionEnd;

            string newText;
            if (selectionEnd > selectionStart)
            {
                newText = currentText.Substring(0, selectionStart) + filteredText + currentText.Substring(selectionEnd);
            }
            else
            {
                var caretIndex = textBox.CaretIndex;
                newText = currentText.Substring(0, caretIndex) + filteredText + currentText.Substring(caretIndex);
            }

            textBox.Text = newText;
            textBox.CaretIndex = selectionStart + filteredText.Length;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "OnIntegerPastingFromClipboard");
        }
    }

    private static async void OnDecimalPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not TextBox textBox)
                return;

            e.Handled = true;

            var clipboard = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clipboard == null)
                return;

            var clipboardText = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(clipboardText))
                return;

            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionEnd = textBox.SelectionEnd;

            // Check if current text (excluding selection) already has a decimal point
            var textWithoutSelection = selectionEnd > selectionStart
                ? currentText.Substring(0, selectionStart) + currentText.Substring(selectionEnd)
                : currentText;
            var hasExistingDecimal = textWithoutSelection.Contains('.');

            // Filter to only digits and at most one decimal point
            var filteredChars = new List<char>();
            var hasDecimalInPaste = false;
            foreach (var c in clipboardText)
            {
                if (char.IsDigit(c))
                {
                    filteredChars.Add(c);
                }
                else if (c == '.' && !hasDecimalInPaste && !hasExistingDecimal)
                {
                    filteredChars.Add(c);
                    hasDecimalInPaste = true;
                }
            }

            var filteredText = new string(filteredChars.ToArray());
            if (string.IsNullOrEmpty(filteredText))
                return;

            // A pasted negative keeps its sign when it lands at the start of a box that takes one.
            var pasteAt = selectionEnd > selectionStart ? selectionStart : textBox.CaretIndex;
            if (GetAllowNegative(textBox) && pasteAt == 0 && clipboardText.TrimStart().StartsWith('-')
                && !textWithoutSelection.Contains('-'))
            {
                filteredText = "-" + filteredText;
            }

            // Insert the filtered text at the caret position
            string newText;
            if (selectionEnd > selectionStart)
            {
                newText = currentText.Substring(0, selectionStart) + filteredText + currentText.Substring(selectionEnd);
            }
            else
            {
                var caretIndex = textBox.CaretIndex;
                newText = currentText.Substring(0, caretIndex) + filteredText + currentText.Substring(caretIndex);
            }

            textBox.Text = newText;
            textBox.CaretIndex = selectionStart + filteredText.Length;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "OnDecimalPastingFromClipboard");
        }
    }

    [GeneratedRegex(@"^[0-9]*$")]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[0-9]*\.?[0-9]*$")]
    private static partial Regex DecimalInputRegex();

    [GeneratedRegex(@"^-?[0-9]*\.?[0-9]*$")]
    private static partial Regex SignedDecimalInputRegex();
}
