using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Thin wrapper around a <see cref="RowDto"/> for binding in a list (Data hub section list,
/// Dashboard "who owes you"/recent activity, Analytics tab rows), with an Open command that
/// navigates to the read-only item detail screen.
/// </summary>
public partial class RowItemViewModel : ObservableObject
{
    private readonly Action<RowDto> _onOpen;

    public RowDto Row { get; }

    public string Title => Row.Title;
    public string Subtitle => Row.Subtitle;
    public string Amount => Row.Amount;

    public RowItemViewModel(RowDto row, Action<RowDto> onOpen)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
    }

    [RelayCommand]
    private void Open() => _onOpen(Row);
}
