using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArgoBooks.Core.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace ArgoBooks.Controls;

/// <summary>
/// A searchable country picker with flags, built on <see cref="SearchableDropdown"/>, with the priority
/// countries above the rest.
/// </summary>
public partial class CountryInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static readonly IReadOnlyList<CountryDialCode> Priority = PhoneInput.PriorityDialCodes;

    private static readonly IReadOnlyList<CountryDialCode> Others = PhoneInput.OtherDialCodes;

    public static readonly StyledProperty<string> SelectedCountryNameProperty =
        AvaloniaProperty.Register<CountryInput, string>(nameof(SelectedCountryName), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public string SelectedCountryName
    {
        get => GetValue(SelectedCountryNameProperty);
        set => SetValue(SelectedCountryNameProperty, value);
    }

    public IReadOnlyList<CountryDialCode> PriorityCountries => Priority;

    public IReadOnlyList<CountryDialCode> OtherCountries => Others;

    private bool _syncing;

    public CountryDialCode? SelectedCountry
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            RaisePropertyChanged();
            if (_syncing) return;

            _syncing = true;
            try
            {
                SelectedCountryName = value?.Name ?? string.Empty;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    /// <summary>
    /// The text in the box. Typing a country's full name picks it and anything else clears the pick, so a
    /// form's validation (such as the company wizard's Next button) treats a half-typed country as none.
    /// </summary>
    public string? SearchText
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RaisePropertyChanged();
            if (!_syncing)
                SelectedCountry = Find(value);
        }
    }

    public CountryInput()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SelectedCountryNameProperty || _syncing)
            return;

        var name = change.GetNewValue<string>();
        _syncing = true;
        try
        {
            // A saved name like "USA" or "UK" still finds its country.
            SelectedCountry = Find(name) ?? Find(Countries.NormalizeCountry(name));
        }
        finally
        {
            _syncing = false;
        }
    }

    private static CountryDialCode? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        return Priority.Concat(Others).FirstOrDefault(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
