using System.ComponentModel;
using ArgoBooks.Core.Models.Dashboard;

namespace ArgoBooks.ViewModels.Dashboard;

public class SetupChecklistWidgetViewModel : WidgetViewModelBase
{
    public override WidgetType WidgetType => WidgetType.SetupChecklist;

    public SetupChecklistViewModel Checklist { get; } = new();

    /// <summary>
    /// Forwarded navigation event from the checklist.
    /// </summary>
    public event EventHandler<string>? NavigationRequested;

    public override void Initialize(Dictionary<string, string> config)
    {
        Checklist.NavigationRequested += OnChecklistNavigationRequested;
        Checklist.PropertyChanged += OnChecklistPropertyChanged;
    }

    public override void LoadData()
    {
        Checklist.Refresh();
        IsWidgetVisible = Checklist.IsVisible;
    }

    public override void Cleanup()
    {
        Checklist.NavigationRequested -= OnChecklistNavigationRequested;
        Checklist.PropertyChanged -= OnChecklistPropertyChanged;
    }

    private void OnChecklistPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupChecklistViewModel.IsVisible))
            IsWidgetVisible = Checklist.IsVisible;
    }

    private void OnChecklistNavigationRequested(object? sender, string target)
    {
        NavigationRequested?.Invoke(this, target);
    }
}
