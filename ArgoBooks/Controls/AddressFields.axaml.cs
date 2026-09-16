using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace ArgoBooks.Controls;

/// <summary>
/// The street, country, region, city and postal code fields, laid out the same way in every form that takes an address.
/// </summary>
public partial class AddressFields : UserControl
{
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(Heading));

    public static readonly StyledProperty<string?> StreetProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(Street), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> CityProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(City), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> RegionProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(Region), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PostalCodeProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(PostalCode), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> CountryProperty =
        AvaloniaProperty.Register<AddressFields, string?>(nameof(Country), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Section heading shown above the fields; hidden when empty.</summary>
    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string? Street
    {
        get => GetValue(StreetProperty);
        set => SetValue(StreetProperty, value);
    }

    public string? City
    {
        get => GetValue(CityProperty);
        set => SetValue(CityProperty, value);
    }

    /// <summary>State, province or county, depending on the country.</summary>
    public string? Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public string? PostalCode
    {
        get => GetValue(PostalCodeProperty);
        set => SetValue(PostalCodeProperty, value);
    }

    public string? Country
    {
        get => GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }

    public AddressFields()
    {
        InitializeComponent();
    }
}
