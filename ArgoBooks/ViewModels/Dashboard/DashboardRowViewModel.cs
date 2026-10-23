using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public partial class DashboardRowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _isEmpty;

    public ObservableCollection<WidgetHostViewModel> Widgets { get; } = [];

    public double TotalFraction => Widgets.Sum(w => w.Size.ToFraction());

    public bool CanFit(WidgetSize size) => TotalFraction + size.ToFraction() <= 1.001;

    /// <summary>The leftmost quarter of the row where a widget this wide covers no other, or the end of the widgets when no gap is wide enough.</summary>
    public double FreeOffset(double fraction)
    {
        for (var start = 0.0; start <= 1 - fraction + 0.001; start += 0.25)
        {
            if (!Widgets.Any(w => start < w.StartOffset + w.Size.ToFraction() - 0.001 && start + fraction > w.StartOffset + 0.001))
                return start;
        }
        return TotalFraction;
    }

    /// <summary>Where a widget starting at <paramref name="offset"/> goes in the list, which is the order a full row lays its widgets out in.</summary>
    public int IndexForOffset(double offset) => Widgets.Count(w => w.StartOffset < offset - 0.001);

    public DashboardRowViewModel()
    {
        Widgets.CollectionChanged += (_, _) => UpdateIsEmpty();
        UpdateIsEmpty();
    }

    private void UpdateIsEmpty() => IsEmpty = Widgets.Count == 0;

    public DashboardRow ToRow() => new()
    {
        Widgets = Widgets.Select(w => w.ToEntry()).ToList()
    };
}
