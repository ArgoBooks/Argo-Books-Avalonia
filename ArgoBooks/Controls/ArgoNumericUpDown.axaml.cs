using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// Custom NumericUpDown control with vertically stacked up/down spinner buttons.
/// </summary>
public partial class ArgoNumericUpDown : UserControl
{
    private TextBox? _inputTextBox;

    #region Styled Properties

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ArgoNumericUpDown, double>(nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay,
            coerce: CoerceValue);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ArgoNumericUpDown, double>(nameof(Minimum), double.MinValue);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ArgoNumericUpDown, double>(nameof(Maximum), double.MaxValue);

    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<ArgoNumericUpDown, double>(nameof(Increment), 1);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the current value.
    /// </summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum allowed value.
    /// </summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum allowed value.
    /// </summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the increment/decrement step.
    /// </summary>
    public double Increment
    {
        get => GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    /// <summary>
    /// Command to increment the value.
    /// </summary>
    public ICommand IncrementCommand { get; }

    /// <summary>
    /// Command to decrement the value.
    /// </summary>
    public ICommand DecrementCommand { get; }

    #endregion

    public ArgoNumericUpDown()
    {
        IncrementCommand = new RelayCommand(IncrementValue);
        DecrementCommand = new RelayCommand(DecrementValue);
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (_inputTextBox != null)
        {
            _inputTextBox.LostFocus += OnTextBoxLostFocus;
            _inputTextBox.KeyDown += OnTextBoxKeyDown;
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_inputTextBox != null)
        {
            _inputTextBox.LostFocus -= OnTextBoxLostFocus;
            _inputTextBox.KeyDown -= OnTextBoxKeyDown;
        }
    }

    private void OnTextBoxLostFocus(object? sender, FocusChangedEventArgs e)
    {
        ParseAndSetValue();
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ParseAndSetValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            IncrementValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            DecrementValue();
            e.Handled = true;
        }
    }

    private void ParseAndSetValue()
    {
        if (_inputTextBox == null) return;

        // Skipped when the text matches the formatted value, because the integer format would otherwise round-trip 253.7 to 254 and write it back.
        if (_inputTextBox.Text == Value.ToString("0"))
            return;

        if (double.TryParse(_inputTextBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var newValue))
        {
            Value = Math.Clamp(newValue, Minimum, Maximum);
        }

        // Update the text box to show the clamped/valid value
        _inputTextBox.Text = Value.ToString("0");
    }

    private void IncrementValue()
    {
        Value = Math.Min(Value + Increment, Maximum);
    }

    private void DecrementValue()
    {
        Value = Math.Max(Value - Increment, Minimum);
    }

    private static double CoerceValue(AvaloniaObject obj, double value)
    {
        if (obj is ArgoNumericUpDown control)
        {
            return Math.Clamp(value, control.Minimum, control.Maximum);
        }
        return value;
    }
}
