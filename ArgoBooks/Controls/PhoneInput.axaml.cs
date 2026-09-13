using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using ArgoBooks.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// A phone number input control with country code selector and auto-formatting.
/// </summary>
public partial class PhoneInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private TextBox? _phoneNumberBox;
    private TextBox? _countrySearchBox;
    private ListBox? _countryListBox;
    private bool _isUpdatingText;
    private bool _isFormattingPhone;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Styled Properties

    public static readonly StyledProperty<CountryDialCode?> SelectedCountryProperty =
        AvaloniaProperty.Register<PhoneInput, CountryDialCode?>(nameof(SelectedCountry), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PhoneNumberProperty =
        AvaloniaProperty.Register<PhoneInput, string>(nameof(PhoneNumber), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> FullPhoneNumberProperty =
        AvaloniaProperty.Register<PhoneInput, string>(nameof(FullPhoneNumber), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    #endregion

    #region Properties

    public CountryDialCode? SelectedCountry
    {
        get => GetValue(SelectedCountryProperty);
        set => SetValue(SelectedCountryProperty, value);
    }

    /// <summary>
    /// Gets or sets the phone number (without country code).
    /// </summary>
    public string PhoneNumber
    {
        get => GetValue(PhoneNumberProperty);
        set => SetValue(PhoneNumberProperty, value);
    }

    /// <summary>
    /// Gets or sets the full phone number including country code.
    /// </summary>
    public string FullPhoneNumber
    {
        get => GetValue(FullPhoneNumberProperty);
        set => SetValue(FullPhoneNumberProperty, value);
    }

    /// <summary>
    /// Gets whether the phone number is complete (has the expected number of digits).
    /// Returns true if the phone is empty (optional field) or has the correct length.
    /// </summary>
    public bool IsPhoneComplete
    {
        get
        {
            var digits = ExtractDigits(PhoneNumber);
            // Empty phone is valid (optional field)
            if (string.IsNullOrEmpty(digits))
                return true;

            var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
            var expectedDigits = country?.PhoneFormat.Count(c => c == 'X') ?? 10;
            return digits.Length == expectedDigits;
        }
    }

    /// <summary>
    /// Gets whether there is a partial (incomplete) phone number entered.
    /// </summary>
    public bool HasIncompletePhone
    {
        get
        {
            var digits = ExtractDigits(PhoneNumber);
            if (string.IsNullOrEmpty(digits))
                return false;

            var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
            var expectedDigits = country?.PhoneFormat.Count(c => c == 'X') ?? 10;
            return digits.Length > 0 && digits.Length < expectedDigits;
        }
    }

    private string _countrySearchText = string.Empty;
    public string CountrySearchText
    {
        get => _countrySearchText;
        set
        {
            if (_countrySearchText != value)
            {
                _countrySearchText = value;
                RaisePropertyChanged();
                if (!_isUpdatingText)
                {
                    UpdateFilteredCountries();
                    if (!string.IsNullOrEmpty(value))
                    {
                        IsCountryDropdownOpen = true;
                    }
                }
            }
        }
    }

    private string _formattedPhoneNumber = string.Empty;
    /// <summary>
    /// Gets or sets the formatted phone number display.
    /// </summary>
    public string FormattedPhoneNumber
    {
        get => _formattedPhoneNumber;
        set
        {
            if (_formattedPhoneNumber != value && !_isFormattingPhone)
            {
                _isFormattingPhone = true;
                var rawDigits = ExtractDigits(value);
                var formatted = FormatPhoneNumber(rawDigits);
                _formattedPhoneNumber = formatted;
                PhoneNumber = rawDigits;
                UpdateFullPhoneNumber();
                RaisePropertyChanged();
                _isFormattingPhone = false;
            }
        }
    }

    public bool IsCountryDropdownOpen
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();

                if (value)
                {
                    // Refresh the list when opening
                    SelectedIndex = 0;
                    UpdateFilteredCountries();
                }
            }
        }
    }

    /// <summary>
    /// Gets whether there are filtered countries.
    /// </summary>
    public bool HasFilteredCountries
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the currently highlighted index in the dropdown.
    /// </summary>
    public int SelectedIndex
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();
                ScrollToSelectedItem();
            }
        }
    } = -1;

    /// <summary>
    /// Gets the filtered dial codes based on search.
    /// </summary>
    public ObservableCollection<CountryDialCode> FilteredDialCodes { get; } = [];

    #endregion

    #region Commands

    public ICommand ToggleCountryDropdownCommand { get; }
    public ICommand SelectCountryCommand { get; }

    #endregion

    #region Static Data

    /// <summary>
    /// Checks if a full phone number string ("{dialCode} {digits}") is complete
    /// based on its country's expected format.
    /// Returns true if empty (optional field) or has the correct number of digits.
    /// </summary>
    public static bool IsFullPhoneComplete(string fullPhone)
    {
        if (string.IsNullOrWhiteSpace(fullPhone))
            return true;

        var parts = fullPhone.Split(' ', 2);
        if (parts.Length < 2)
            return true;

        var dialCode = parts[0];
        var numberPart = parts[1];
        var digits = new string(numberPart.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits))
            return true;

        var country = AllDialCodes
            .OrderByDescending(c => c.DialCode.Length)
            .FirstOrDefault(c => dialCode.Equals(c.DialCode, StringComparison.OrdinalIgnoreCase));

        if (country == null)
            return true;

        var expectedDigits = country.PhoneFormat.Count(c => c == 'X');
        return digits.Length == expectedDigits;
    }

    /// <summary>
    /// Complete list of country dial codes with phone format patterns.
    /// Generated from the shared Countries data, with priority countries listed first.
    /// </summary>
    public static readonly List<CountryDialCode> AllDialCodes = CreateAllDialCodes();

    private static List<CountryDialCode> CreateAllDialCodes()
    {
        var priorityCount = Countries.Priority.Count;
        var result = Countries.AllWithPriorityFirst
            .Select((c, index) => new CountryDialCode(c.Code, c.Name, c.DialCode, c.PhoneFormat, c.FlagFileName)
            {
                ShowSeparatorAfter = index == priorityCount - 1 // Last priority country
            })
            .ToList();
        return result;
    }

    #endregion

    public PhoneInput()
    {
        ToggleCountryDropdownCommand = new RelayCommand(ToggleCountryDropdown);
        SelectCountryCommand = new RelayCommand<CountryDialCode>(SelectCountry);

        InitializeComponent();

        SelectedCountry = AllDialCodes.FirstOrDefault(c => c.Code == "US");
        UpdateCountrySearchText();
        UpdateFilteredCountries();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedCountryProperty)
        {
            UpdateCountrySearchText();
            ReformatPhoneNumberForNewCountry();
            UpdatePhoneNumberPlaceholder();
            UpdateFullPhoneNumber();
            RaisePropertyChanged(nameof(IsPhoneComplete));
            RaisePropertyChanged(nameof(HasIncompletePhone));
        }
        else if (change.Property == PhoneNumberProperty && !_isUpdatingText)
        {
            _isUpdatingText = true;
            // A bound value can arrive as typed, brackets and dashes included. Only the digits
            // count toward the format's length; counted as they stood, they cut the number short.
            _formattedPhoneNumber = FormatPhoneNumber(ExtractDigits(PhoneNumber));
            if (_phoneNumberBox != null)
                _phoneNumberBox.Text = _formattedPhoneNumber;
            RaisePropertyChanged(nameof(FormattedPhoneNumber));
            UpdateFullPhoneNumber();
            _isUpdatingText = false;
        }
        else if (change.Property == FullPhoneNumberProperty && !_isUpdatingText)
        {
            ParseFullPhoneNumber(change.NewValue as string ?? string.Empty);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _phoneNumberBox = this.FindControl<TextBox>("PhoneNumberBox");
        _countrySearchBox = this.FindControl<TextBox>("CountrySearchBox");

        if (_phoneNumberBox != null)
        {
            _phoneNumberBox.TextChanged += OnPhoneNumberTextChanged;
            UpdatePhoneNumberPlaceholder();

            // Sync any phone number that was set before the control loaded
            if (!string.IsNullOrEmpty(_formattedPhoneNumber))
                _phoneNumberBox.Text = _formattedPhoneNumber;
        }

        if (_countrySearchBox != null)
        {
            _countrySearchBox.GotFocus += OnCountrySearchBoxGotFocus;
            _countrySearchBox.KeyDown += OnCountrySearchBoxKeyDown;
            // Tunneling so we re-open the dropdown even when the textbox already has focus.
            // Without this, clicking-elsewhere light-dismisses the popup but the textbox keeps
            // focus, and the next click on the textbox doesn't fire GotFocus.
            _countrySearchBox.AddHandler(PointerPressedEvent, OnCountrySearchBoxPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        _countryListBox = this.FindControl<ListBox>("CountryListBox");
        if (_countryListBox != null)
        {
            _countryListBox.PointerWheelChanged += OnCountryListBoxPointerWheelChanged;
            _countryListBox.PointerReleased += OnCountryListBoxPointerReleased;
        }
    }

    private void OnCountryListBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
    }

    private void OnCountryListBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_countryListBox?.SelectedItem is CountryDialCode country)
        {
            SelectCountry(country);
        }
    }

    private void ScrollToSelectedItem()
    {
        if (_countryListBox == null || SelectedIndex < 0 || SelectedIndex >= FilteredDialCodes.Count)
            return;

        _countryListBox.ScrollIntoView(FilteredDialCodes[SelectedIndex]);
    }

    private void ReformatPhoneNumberForNewCountry()
    {
        if (_phoneNumberBox == null || _isFormattingPhone)
            return;

        _isFormattingPhone = true;

        // Get current digits and reformat with new country's format
        var currentText = _phoneNumberBox.Text ?? string.Empty;
        var rawDigits = ExtractDigits(currentText);

        if (!string.IsNullOrEmpty(rawDigits))
        {
            var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
            var maxDigits = country?.PhoneFormat.Count(c => c == 'X') ?? 10;

            // Limit digits to new format's max
            if (rawDigits.Length > maxDigits)
            {
                rawDigits = rawDigits[..maxDigits];
            }

            var formatted = FormatPhoneNumber(rawDigits);
            _formattedPhoneNumber = formatted;
            _phoneNumberBox.Text = formatted;
            PhoneNumber = rawDigits;
            RaisePropertyChanged(nameof(FormattedPhoneNumber));
        }

        _isFormattingPhone = false;
    }

    private void UpdatePhoneNumberPlaceholder()
    {
        if (_phoneNumberBox == null)
            return;

        var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
        if (country != null)
        {
            // Generate example placeholder by replacing X with sample digits
            var placeholder = country.PhoneFormat;
            var digitIndex = 1;
            var result = new StringBuilder();

            foreach (var ch in placeholder)
            {
                if (ch == 'X')
                {
                    result.Append((digitIndex % 10).ToString());
                    digitIndex++;
                }
                else
                {
                    result.Append(ch);
                }
            }

            _phoneNumberBox.PlaceholderText = result.ToString();
        }
    }

    private void OnPhoneNumberTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormattingPhone || _phoneNumberBox == null)
            return;

        _isFormattingPhone = true;

        var currentText = _phoneNumberBox.Text ?? string.Empty;
        var caretIndex = _phoneNumberBox.CaretIndex;

        // Extract digits and limit to max allowed by country format
        var rawDigits = ExtractDigits(currentText);
        var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
        var maxDigits = country?.PhoneFormat.Count(c => c == 'X') ?? 10;
        if (rawDigits.Length > maxDigits)
        {
            rawDigits = rawDigits[..maxDigits];
        }
        var formatted = FormatPhoneNumber(rawDigits);

        // Only update if different
        if (formatted != currentText)
        {
            // Calculate new caret position
            var digitsBeforeCaret = ExtractDigits(currentText[..Math.Min(caretIndex, currentText.Length)]).Length;

            _formattedPhoneNumber = formatted;
            _phoneNumberBox.Text = formatted;

            // Find position in formatted string that corresponds to same number of digits
            var newCaretPos = 0;
            var digitCount = 0;
            for (int i = 0; i < formatted.Length && digitCount < digitsBeforeCaret; i++)
            {
                if (char.IsDigit(formatted[i]))
                    digitCount++;
                newCaretPos = i + 1;
            }

            _phoneNumberBox.CaretIndex = Math.Min(newCaretPos, formatted.Length);
        }

        PhoneNumber = rawDigits;
        UpdateFullPhoneNumber();
        RaisePropertyChanged(nameof(FormattedPhoneNumber));
        RaisePropertyChanged(nameof(IsPhoneComplete));
        RaisePropertyChanged(nameof(HasIncompletePhone));

        _isFormattingPhone = false;
    }

    private void OnCountrySearchBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        IsCountryDropdownOpen = true;
        _countrySearchBox?.SelectAll();
    }

    private void OnCountrySearchBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsCountryDropdownOpen)
        {
            IsCountryDropdownOpen = true;
        }
    }

    private void OnCountrySearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!IsCountryDropdownOpen)
                {
                    IsCountryDropdownOpen = true;
                    SelectedIndex = 0;
                }
                else if (SelectedIndex < FilteredDialCodes.Count - 1)
                {
                    SelectedIndex++;
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (IsCountryDropdownOpen && SelectedIndex > 0)
                {
                    SelectedIndex--;
                }
                e.Handled = true;
                break;
            case Key.Escape:
                IsCountryDropdownOpen = false;
                UpdateCountrySearchText();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                if (IsCountryDropdownOpen && SelectedIndex >= 0 && SelectedIndex < FilteredDialCodes.Count)
                {
                    SelectCountry(FilteredDialCodes[SelectedIndex]);
                }
                else if (FilteredDialCodes.Count > 0)
                {
                    SelectCountry(FilteredDialCodes[0]);
                }
                if (e.Key == Key.Enter)
                    e.Handled = true;
                break;
        }
    }

    private void ToggleCountryDropdown()
    {
        IsCountryDropdownOpen = !IsCountryDropdownOpen;
        if (IsCountryDropdownOpen)
        {
            _countrySearchBox?.Focus();
            _countrySearchBox?.SelectAll();
        }
    }

    private void SelectCountry(CountryDialCode? country)
    {
        if (country == null)
            return;

        SelectedCountry = country;
        UpdateCountrySearchText();
        IsCountryDropdownOpen = false;
        _phoneNumberBox?.Focus();
    }

    private void UpdateCountrySearchText()
    {
        _isUpdatingText = true;
        _countrySearchText = SelectedCountry?.DialCode ?? "+1";
        RaisePropertyChanged(nameof(CountrySearchText));
        _isUpdatingText = false;
    }

    private void UpdateFilteredCountries()
    {
        FilteredDialCodes.Clear();

        var searchText = _countrySearchText.Trim().ToLowerInvariant();

        bool showingFullList = string.IsNullOrEmpty(searchText) || searchText.StartsWith('+');

        IEnumerable<CountryDialCode> filtered;

        if (showingFullList)
        {
            // Show all when empty or when showing dial code
            filtered = AllDialCodes;
        }
        else
        {
            // Search by dial code, country name, or country code
            filtered = AllDialCodes.Where(c =>
                c.DialCode.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Code.Equals(searchText, StringComparison.OrdinalIgnoreCase));
        }

        // Get the last priority country code (Canada = "CA")
        var lastPriorityCode = Countries.Priority.LastOrDefault()?.Code;

        foreach (var item in filtered)
        {
            // Only show separator when displaying the full list, after the last priority country
            item.ShowSeparatorAfter = showingFullList && item.Code == lastPriorityCode;
            FilteredDialCodes.Add(item);
        }

        HasFilteredCountries = FilteredDialCodes.Count > 0;
    }

    private void UpdateFullPhoneNumber()
    {
        if (_isUpdatingText)
            return;

        var dialCode = SelectedCountry?.DialCode ?? "+1";
        var digits = ExtractDigits(PhoneNumber);
        FullPhoneNumber = string.IsNullOrEmpty(digits) ? string.Empty : $"{dialCode} {digits}";
    }

    private void ParseFullPhoneNumber(string fullPhone)
    {
        if (string.IsNullOrWhiteSpace(fullPhone))
        {
            SelectedCountry = AllDialCodes.FirstOrDefault(c => c.Code == "US");
            PhoneNumber = string.Empty;
            return;
        }

        _isUpdatingText = true;

        // Try to match against known dial codes (longest match first)
        var sortedDialCodes = AllDialCodes.OrderByDescending(d => d.DialCode.Length).ToList();

        foreach (var dialCode in sortedDialCodes)
        {
            if (fullPhone.StartsWith(dialCode.DialCode))
            {
                SelectedCountry = dialCode;
                var remaining = fullPhone[dialCode.DialCode.Length..].Trim();
                PhoneNumber = ExtractDigits(remaining);
                _formattedPhoneNumber = FormatPhoneNumber(PhoneNumber);
                RaisePropertyChanged(nameof(FormattedPhoneNumber));
                UpdateCountrySearchText();
                _isUpdatingText = false;
                return;
            }
        }

        // No matching dial code found, use default
        SelectedCountry = AllDialCodes.FirstOrDefault(c => c.Code == "US");
        PhoneNumber = ExtractDigits(fullPhone);
        _formattedPhoneNumber = FormatPhoneNumber(PhoneNumber);
        RaisePropertyChanged(nameof(FormattedPhoneNumber));
        UpdateCountrySearchText();
        _isUpdatingText = false;
    }

    /// <summary>
    /// Formats a phone number using the selected country's format pattern.
    /// </summary>
    private string FormatPhoneNumber(string digits)
    {
        if (string.IsNullOrEmpty(digits))
            return string.Empty;

        var country = SelectedCountry ?? AllDialCodes.FirstOrDefault(c => c.Code == "US");
        if (country == null)
            return digits;

        var maxDigits = country.PhoneFormat.Count(c => c == 'X');
        var limitedDigits = digits.Length > maxDigits ? digits[..maxDigits] : digits;

        return country.FormatPhoneNumber(limitedDigits);
    }

    /// <summary>
    /// Extracts only digits from a string.
    /// </summary>
    private static string ExtractDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return new string(input.Where(char.IsDigit).ToArray());
    }
}

/// <summary>
/// Represents a country with its dial code.
/// </summary>
public class CountryDialCode(string code, string name, string dialCode, string phoneFormat, string? flagFileName = null)
{
    /// <summary>
    /// ISO country code (e.g., US, GB).
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// Country name for display.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Phone dial code (e.g., +1, +44).
    /// </summary>
    public string DialCode { get; } = dialCode;

    /// <summary>
    /// Phone number format pattern using X for digits (e.g., "(XXX) XXX-XXXX").
    /// </summary>
    public string PhoneFormat { get; } = phoneFormat;

    /// <summary>
    /// Flag file name (matches the PNG file in Assets/CountryFlags).
    /// </summary>
    public string FlagFileName { get; } = flagFileName ?? name;

    /// <summary>
    /// Gets whether this is a priority/common country.
    /// </summary>
    public bool IsPriority => Countries.IsPriority(Code);

    /// <summary>
    /// Gets whether this country should show a separator after it (last priority country).
    /// </summary>
    public bool ShowSeparatorAfter { get; set; }

    /// <summary>
    /// Path to the flag image asset.
    /// </summary>
    public string FlagPath => $"avares://ArgoBooks/Assets/CountryFlags/{FlagFileName}.png";

    /// <summary>
    /// Gets the flag image loaded from resources.
    /// </summary>
    public IImage? FlagImage
    {
        get
        {
            if (field == null)
            {
                try
                {
                    var uri = new Uri(FlagPath);
                    var assets = Avalonia.Platform.AssetLoader.Open(uri);
                    field = new Bitmap(assets);
                }
                catch
                {
                    // Flag not found, return null
                }
            }
            return field;
        }
    }

    /// <summary>
    /// Display format for the dropdown.
    /// </summary>
    public string DisplayName => $"{DialCode} {Name}";

    /// <summary>
    /// Formats a phone number according to this country's format pattern.
    /// </summary>
    public string FormatPhoneNumber(string digits)
    {
        if (string.IsNullOrEmpty(digits))
            return string.Empty;

        var sb = new StringBuilder();
        var digitIndex = 0;
        var maxDigits = PhoneFormat.Count(c => c == 'X');

        foreach (var ch in PhoneFormat)
        {
            if (digitIndex >= digits.Length)
                break;

            if (ch == 'X')
            {
                sb.Append(digits[digitIndex]);
                digitIndex++;
            }
            else
            {
                sb.Append(ch);
            }
        }

        // If there are remaining digits beyond the format, append them
        if (digitIndex < digits.Length && digitIndex >= maxDigits)
        {
            // Remaining digits go beyond the format pattern
            for (int i = digitIndex; i < digits.Length; i++)
            {
                sb.Append(digits[i]);
            }
        }

        return sb.ToString();
    }

    public override string ToString() => DisplayName;
}
