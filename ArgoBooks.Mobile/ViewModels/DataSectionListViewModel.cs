using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>The list of rows for one Data hub section (e.g. all Expenses), opened from <see cref="DataHubViewModel"/>.</summary>
public partial class DataSectionListViewModel : ViewModelBase
{
    public string Title { get; }

    public ObservableCollection<RowItemViewModel> Rows { get; }

    public bool IsEmpty => Rows.Count == 0;

    public DataSectionListViewModel(string title, IReadOnlyList<RowDto> rows, Action<RowDto> onOpenRow)
    {
        Title = title;
        Rows = new ObservableCollection<RowItemViewModel>(rows.Select(r => new RowItemViewModel(r, onOpenRow)));
    }
}
