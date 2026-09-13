namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One selectable row in the review screen's product picker sheet (see
/// <see cref="ReviewViewModel"/>). Carries its own <see cref="SelectCommand"/> bound to a callback
/// supplied by the picker, the same pattern <see cref="CompanyRowViewModel"/> uses for its list
/// rows, so the ItemsControl's DataTemplate doesn't need to reach back up to an ancestor's command.
/// </summary>
public class ProductChoiceViewModel : ViewModelBase
{
    public string Name { get; }

    public IRelayCommand SelectCommand { get; }

    public ProductChoiceViewModel(string name, Action onSelect)
    {
        Name = name;
        SelectCommand = new RelayCommand(onSelect ?? throw new ArgumentNullException(nameof(onSelect)));
    }
}
