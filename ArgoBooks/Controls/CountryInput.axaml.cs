using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArgoBooks.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// A searchable country input control with flag display.
/// </summary>
public partial class CountryInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private TextBox? _countrySearchBox;
    private bool _isUpdatingText;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Styled Properties

    public static readonly StyledProperty<string> SelectedCountryNameProperty =
        AvaloniaProperty.Register<CountryInput, string>(nameof(SelectedCountryName), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public string SelectedCountryName
    {
        get => GetValue(SelectedCountryNameProperty);
        set => SetValue(SelectedCountryNameProperty, value);
    }

    #endregion

    #region Properties

    public CountryDialCode? SelectedCountry
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SelectedCountryFlag));
            }
        }
    }

    /// <summary>
    /// Gets the selected country's flag image.
    /// </summary>
    public IImage? SelectedCountryFlag => SelectedCountry?.FlagImage;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                RaisePropertyChanged();

                if (!_isUpdatingText)
                {
                    // Keep the selection in sync with the typed text: an exact
                    // name match selects that country; anything else (partial
                    // text after backspacing, cleared box) drops the selection
                    // so the flag disappears and the bound name is emptied,
                    // letting validation (e.g. a wizard's Next button) react.
                    var match = string.IsNullOrWhiteSpace(value)
                        ? null
                        : PhoneInput.AllDialCodes.FirstOrDefault(c =>
                            c.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (!ReferenceEquals(match, SelectedCountry))
                    {
                        _isUpdatingText = true;
                        SelectedCountry = match;
                        SelectedCountryName = match?.Name ?? string.Empty;
                        _isUpdatingText = false;
                    }

                    UpdateFilteredCountries();
                    if (!string.IsNullOrEmpty(value) && value != SelectedCountry?.Name)
                    {
                        IsDropdownOpen = true;
                    }
                }
            }
        }
    }

    public bool IsDropdownOpen
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
            }
        }
    } = -1;

    /// <summary>
    /// Gets the filtered countries based on search.
    /// </summary>
    public ObservableCollection<CountryDialCode> FilteredCountries { get; } = [];

    #endregion

    #region Commands

    public ICommand ToggleDropdownCommand { get; }
    public ICommand SelectCountryCommand { get; }

    #endregion

    public CountryInput()
    {
        ToggleDropdownCommand = new RelayCommand(ToggleDropdown);
        SelectCountryCommand = new RelayCommand<CountryDialCode>(SelectCountry);

        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedCountryNameProperty && !_isUpdatingText)
        {
            var name = change.NewValue as string ?? string.Empty;
            // First try exact match
            var country = PhoneInput.AllDialCodes.FirstOrDefault(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            // If no match, try normalizing the country name (handles USA, UK, etc.)
            if (country == null && !string.IsNullOrWhiteSpace(name))
            {
                var normalizedName = Countries.NormalizeCountry(name);
                if (normalizedName != null)
                {
                    country = PhoneInput.AllDialCodes.FirstOrDefault(c =>
                        c.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
                }
            }
            SelectedCountry = country;
            UpdateSearchText();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _countrySearchBox = this.FindControl<TextBox>("CountrySearchBox");

        if (_countrySearchBox != null)
        {
            _countrySearchBox.GotFocus += OnCountrySearchBoxGotFocus;
            _countrySearchBox.KeyDown += OnCountrySearchBoxKeyDown;
            // Tunneling so we re-open the dropdown even when the textbox already has focus.
            // Without this, clicking-elsewhere light-dismisses the popup but the textbox keeps
            // focus, and the next click on the textbox doesn't fire GotFocus.
            _countrySearchBox.AddHandler(PointerPressedEvent, OnCountrySearchBoxPointerPressed, RoutingStrategies.Tunnel);
        }

    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_countrySearchBox != null)
        {
            _countrySearchBox.GotFocus -= OnCountrySearchBoxGotFocus;
            _countrySearchBox.KeyDown -= OnCountrySearchBoxKeyDown;
            _countrySearchBox.RemoveHandler(PointerPressedEvent, OnCountrySearchBoxPointerPressed);
        }

    }

    private void OnCountrySearchBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        IsDropdownOpen = true;
        _countrySearchBox?.SelectAll();
    }

    private void OnCountrySearchBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDropdownOpen)
        {
            IsDropdownOpen = true;
        }
    }

    private void OnCountrySearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!IsDropdownOpen)
                {
                    IsDropdownOpen = true;
                    SelectedIndex = 0;
                }
                else if (SelectedIndex < FilteredCountries.Count - 1)
                {
                    SelectedIndex++;
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (IsDropdownOpen && SelectedIndex > 0)
                {
                    SelectedIndex--;
                }
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropdownOpen = false;
                UpdateSearchText();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                if (IsDropdownOpen && SelectedIndex >= 0 && SelectedIndex < FilteredCountries.Count)
                {
                    SelectCountry(FilteredCountries[SelectedIndex]);
                }
                else if (FilteredCountries.Count > 0)
                {
                    SelectCountry(FilteredCountries[0]);
                }
                if (e.Key == Key.Enter)
                    e.Handled = true;
                break;
        }
    }

    private void ToggleDropdown()
    {
        IsDropdownOpen = !IsDropdownOpen;
        if (IsDropdownOpen)
        {
            _countrySearchBox?.Focus();
            _countrySearchBox?.SelectAll();
        }
    }

    private void SelectCountry(CountryDialCode? country)
    {
        if (country == null)
            return;

        _isUpdatingText = true;
        SelectedCountry = country;
        SelectedCountryName = country.Name;
        UpdateSearchText();
        IsDropdownOpen = false;
        _isUpdatingText = false;
    }

    private void UpdateSearchText()
    {
        _isUpdatingText = true;
        SearchText = SelectedCountry?.Name ?? string.Empty;
        _isUpdatingText = false;
    }

    private void UpdateFilteredCountries()
    {
        FilteredCountries.Clear();

        var searchText = _searchText.Trim().ToLowerInvariant();

        bool showingFullList = string.IsNullOrEmpty(searchText) || searchText == SelectedCountry?.Name.ToLowerInvariant();

        IEnumerable<CountryDialCode> filtered;

        if (showingFullList)
        {
            filtered = PhoneInput.AllDialCodes;
        }
        else
        {
            filtered = PhoneInput.SearchCountries(searchText, matchDialCode: false);
        }

        // Get the last priority country code (Canada = "CA")
        var lastPriorityCode = Countries.Priority.LastOrDefault()?.Code;

        foreach (var item in filtered)
        {
            // Only show separator when displaying the full list, after the last priority country
            item.ShowSeparatorAfter = showingFullList && item.Code == lastPriorityCode;
            FilteredCountries.Add(item);
        }

        HasFilteredCountries = FilteredCountries.Count > 0;
    }
}
