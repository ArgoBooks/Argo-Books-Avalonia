using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArgoBooks.Data;
using ArgoBooks.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ArgoBooks.Controls;

/// <summary>
/// The state, province or county of an address. Countries in <see cref="Regions"/> pick from a list;
/// any other country takes free text.
/// </summary>
public partial class RegionInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static readonly StyledProperty<string?> CountryProperty =
        AvaloniaProperty.Register<RegionInput, string?>(nameof(Country));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<RegionInput, string?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public string? Country
    {
        get => GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The field's label for a country, such as Province for Canada or County for Ireland.</summary>
    public static readonly IValueConverter LabelConverter =
        new FuncValueConverter<string?, string>(country => Regions.LabelFor(country).Translate());

    public ObservableCollection<RegionOption> Options { get; } = [];

    public bool HasList { get; private set; }

    public string ListPlaceholder { get; private set; } = string.Empty;

    private RegionOption? _selectedOption;
    private bool _syncing;

    public RegionOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (ReferenceEquals(_selectedOption, value)) return;
            _selectedOption = value;
            RaisePropertyChanged();
            if (!_syncing && value != null)
                Value = value.Code;
        }
    }

    public RegionInput()
    {
        InitializeComponent();
        RebuildOptions();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CountryProperty)
        {
            var previous = Regions.For(change.GetOldValue<string?>());
            var next = Regions.For(change.GetNewValue<string?>());

            // A province picked for the old country means nothing once the country changes.
            if (previous != next && Regions.Find(previous, Value) != null && Regions.Find(next, Value) == null)
                Value = string.Empty;

            RebuildOptions();
        }
        else if (change.Property == ValueProperty)
        {
            SyncSelection();
        }
    }

    private void RebuildOptions()
    {
        var list = Regions.For(Country);

        _syncing = true;
        try
        {
            SelectedOption = null;
            Options.Clear();
            if (list != null)
            {
                foreach (var region in list)
                    Options.Add(new RegionOption(region.Code, region.Name, region.Name.Translate()));
            }
        }
        finally
        {
            _syncing = false;
        }

        HasList = list != null;
        ListPlaceholder = Regions.PlaceholderFor(Country).Translate();
        RaisePropertyChanged(nameof(HasList));
        RaisePropertyChanged(nameof(ListPlaceholder));

        SyncSelection();
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            for (var i = Options.Count - 1; i >= 0; i--)
            {
                if (Options[i].IsKept)
                    Options.RemoveAt(i);
            }

            if (!HasList || string.IsNullOrWhiteSpace(Value))
            {
                SelectedOption = null;
                return;
            }

            var match = Options.FirstOrDefault(o => o.Matches(Value));
            if (match == null)
            {
                // A value typed before the list existed stays visible until something is picked.
                var kept = Value.Trim();
                match = new RegionOption(kept, kept, kept, isKept: true);
                Options.Insert(0, match);
            }
            else if (!string.Equals(Value, match.Code, StringComparison.Ordinal))
            {
                // A full name typed before the list existed becomes its code. Setting Value runs this
                // again, which then finds the code directly.
                Value = match.Code;
                return;
            }

            SelectedOption = match;
        }
        finally
        {
            _syncing = false;
        }
    }
}

/// <summary>One entry in a <see cref="RegionInput"/> list.</summary>
public sealed class RegionOption(string code, string englishName, string name, bool isKept = false)
{
    public string Code { get; } = code;

    /// <summary>The name shown, translated.</summary>
    public string Name { get; } = name;

    /// <summary>True for a saved value that is not on the list, shown so it is not lost.</summary>
    public bool IsKept { get; } = isKept;

    public bool Matches(string value)
    {
        var trimmed = value.Trim();
        return string.Equals(trimmed, Code, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, englishName, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Name;
}
