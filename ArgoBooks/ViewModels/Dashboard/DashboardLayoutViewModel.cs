using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels.Dashboard;

public partial class DashboardLayoutViewModel : ObservableObject
{
    private CompanyManager? _companyManager;
    private DashboardLayout? _savedLayout;

    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWidgets))]
    [NotifyPropertyChangedFor(nameof(HasNoWidgets))]
    private ObservableCollection<DashboardRowViewModel> _rows = [];

    public bool HasWidgets => Rows.Any(r => r.Widgets.Any(w => w.WidgetViewModel.IsWidgetVisible));
    public bool HasNoWidgets => !HasWidgets;
    public WidgetCatalogViewModel Catalog { get; } = new();

    private DashboardRowViewModel? _targetRowForAdd;

    public void Initialize(CompanyManager companyManager)
    {
        _companyManager = companyManager;
        Catalog.WidgetAddRequested -= OnWidgetAddRequested;
        Catalog.WidgetAddRequested += OnWidgetAddRequested;

        var settings = App.SettingsService?.GlobalSettings;
        var companyPath = companyManager.CurrentFilePath;

        // Sample company always gets the default layout
        DashboardLayout? layout = null;
        if (companyManager.IsSampleCompany)
        {
            layout = DashboardLayout.CreateDefault();
        }
        else
        {
            // Load per-company layout, fall back to legacy global layout, then default
            if (!string.IsNullOrEmpty(companyPath) && settings?.Ui.CompanyDashboardLayouts.TryGetValue(companyPath, out var companyLayout) == true)
                layout = companyLayout;
            layout ??= settings?.Ui.DashboardLayout ?? DashboardLayout.CreateDefault();
        }
        layout.MigrateIfNeeded();

        _savedLayout = layout.Clone();
        LoadFromLayout(layout);
    }

    private void LoadFromLayout(DashboardLayout layout)
    {
        UnwireWidgetEvents();
        foreach (var row in Rows)
            foreach (var w in row.Widgets)
                w.Cleanup();
        Rows.Clear();

        foreach (var row in layout.Rows)
        {
            var rowVm = new DashboardRowViewModel();
            foreach (var entry in row.Widgets)
            {
                if (!WidgetFactory.IsKnownType(entry.WidgetType))
                    continue;
                var host = WidgetFactory.CreateWidgetHost(entry);
                host.SetCompanyManager(_companyManager);
                WireUpWidgetEvents(host);
                rowVm.Widgets.Add(host);
            }
            Rows.Add(rowVm);
        }
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
    }

    private void WireUpWidgetEvents(WidgetHostViewModel host)
    {
        if (host.WidgetViewModel is SetupChecklistWidgetViewModel checklistVm)
            checklistVm.NavigationRequested += OnChecklistNavigationRequested;
    }

    private void UnwireWidgetEvents()
    {
        foreach (var row in Rows)
            foreach (var w in row.Widgets)
                UnwireWidgetEvent(w);
    }

    private void UnwireWidgetEvent(WidgetHostViewModel w)
    {
        if (w.WidgetViewModel is SetupChecklistWidgetViewModel checklistVm)
            checklistVm.NavigationRequested -= OnChecklistNavigationRequested;
    }

    private void OnChecklistNavigationRequested(object? sender, string pageName)
    {
        _ = App.NavigationService?.NavigateToAsync(pageName);
    }

    public void LoadAllWidgetData()
    {
        foreach (var row in Rows)
            foreach (var w in row.Widgets)
                w.LoadData();
    }

    [RelayCommand]
    private void EnterEditMode()
    {
        _savedLayout = GetCurrentLayout();
        IsEditMode = true;
        foreach (var row in Rows)
        {
            row.IsEditMode = true;
            foreach (var w in row.Widgets)
                w.IsEditMode = true;
        }
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditMode = false;
        foreach (var row in Rows)
        {
            row.IsEditMode = false;
            foreach (var w in row.Widgets)
                w.IsEditMode = false;
        }
        if (_savedLayout != null) LoadFromLayout(_savedLayout);
        LoadAllWidgetData();
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        IsEditMode = false;

        foreach (var row in Rows)
        {
            for (int j = row.Widgets.Count - 1; j >= 0; j--)
            {
                if (row.Widgets[j].WidgetViewModel is QuickActionsWidgetViewModel qa && qa.HasNoVisibleQuickActions)
                    row.Widgets.RemoveAt(j);
            }
        }

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Widgets.Count == 0)
                Rows.RemoveAt(i);
        }
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));

        foreach (var row in Rows)
        {
            row.IsEditMode = false;
            foreach (var w in row.Widgets)
                w.IsEditMode = false;
        }

        var layout = GetCurrentLayout();
        if (_savedLayout != null && DashboardCustomization.Describe(_savedLayout, layout) is { } change)
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.DashboardCustomized, change);
        _savedLayout = layout.Clone();

        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            var companyPath = _companyManager?.CurrentFilePath;
            if (!string.IsNullOrEmpty(companyPath))
                settings.Ui.CompanyDashboardLayouts[companyPath] = layout;
            else
                settings.Ui.DashboardLayout = layout;
            await App.SettingsService!.SaveGlobalSettingsAsync();
        }
    }

    [RelayCommand]
    private async Task ResetToDefault()
    {
        var wasEditMode = IsEditMode;
        var layout = DashboardLayout.CreateDefault();

        // Against the saved layout, not the half-edited one, and not when it was already the default.
        if (!DashboardCustomization.SameShape(_savedLayout ?? GetCurrentLayout(), layout))
            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.DashboardReset);
        LoadFromLayout(layout);
        LoadAllWidgetData();
        if (wasEditMode)
        {
            foreach (var row in Rows)
            {
                row.IsEditMode = true;
                foreach (var w in row.Widgets)
                    w.IsEditMode = true;
            }
        }
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));

        // Persist the reset so it survives close/reopen
        _savedLayout = layout.Clone();
        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            var companyPath = _companyManager?.CurrentFilePath;
            if (!string.IsNullOrEmpty(companyPath))
                settings.Ui.CompanyDashboardLayouts[companyPath] = layout;
            else
                settings.Ui.DashboardLayout = layout;
            await App.SettingsService!.SaveGlobalSettingsAsync();
        }
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new DashboardRowViewModel { IsEditMode = true };
        Rows.Add(row);
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
    }

    public void RemoveRow(DashboardRowViewModel row)
    {
        foreach (var w in row.Widgets)
        {
            UnwireWidgetEvent(w);
            w.Cleanup();
        }
        Rows.Remove(row);
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
    }

    public void OpenCatalogForRow(DashboardRowViewModel row)
    {
        _targetRowForAdd = row;
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets), 1.0 - row.TotalFraction);
        Catalog.IsOpen = true;
    }

    [RelayCommand]
    private void OpenCatalog()
    {
        var row = new DashboardRowViewModel { IsEditMode = true };
        Rows.Add(row);
        _targetRowForAdd = row;
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));
        Catalog.IsOpen = true;
    }

    private void OnWidgetAddRequested(object? sender, WidgetDefinition def)
    {
        var targetRow = _targetRowForAdd ?? Rows.LastOrDefault();
        if (targetRow == null)
        {
            targetRow = new DashboardRowViewModel { IsEditMode = true };
            Rows.Add(targetRow);
        }

        // Use default size, or shrink to the smallest available size that fits
        var size = def.DefaultSize;
        if (!targetRow.CanFit(size))
        {
            var fittingSize = def.AvailableSizes
                .OrderBy(s => s.ToFraction())
                .Cast<WidgetSize?>()
                .FirstOrDefault(s => targetRow.CanFit(s!.Value));
            if (fittingSize == null)
            {
                _targetRowForAdd = null;
                return;
            }
            size = fittingSize.Value;
        }

        var entry = new DashboardWidgetEntry(def.Type, size);
        if (def.ChartDataType.HasValue)
            entry.Config["ChartDataType"] = def.ChartDataType.Value.ToString();

        var host = WidgetFactory.CreateWidgetHost(entry);
        host.SetCompanyManager(_companyManager);
        WireUpWidgetEvents(host);
        host.IsEditMode = true;

        // Position after existing widgets so they don't stack at offset 0
        double usedFraction = targetRow.Widgets.Sum(w => w.Size.ToFraction());
        host.StartOffset = usedFraction;

        host.LoadData();
        targetRow.Widgets.Add(host);
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));
        _targetRowForAdd = null;
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
    }

    [RelayCommand]
    private void RemoveWidget(WidgetHostViewModel widget)
    {
        foreach (var row in Rows)
        {
            if (row.Widgets.Remove(widget))
            {
                UnwireWidgetEvent(widget);
                widget.Cleanup();
                break;
            }
        }
        Catalog.Refresh(Rows.SelectMany(r => r.Widgets));
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
    }

    public bool MoveWidgetToRow(DashboardRowViewModel sourceRow, int widgetIndex,
        DashboardRowViewModel targetRow, int insertIndex = -1)
    {
        if (widgetIndex < 0 || widgetIndex >= sourceRow.Widgets.Count) return false;
        if (sourceRow == targetRow) return false;

        var widget = sourceRow.Widgets[widgetIndex];
        if (!targetRow.CanFit(widget.Size)) return false;

        sourceRow.Widgets.RemoveAt(widgetIndex);
        if (insertIndex >= 0 && insertIndex < targetRow.Widgets.Count)
            targetRow.Widgets.Insert(insertIndex, widget);
        else
            targetRow.Widgets.Add(widget);

        if (sourceRow.Widgets.Count == 0)
            Rows.Remove(sourceRow);

        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(HasNoWidgets));
        return true;
    }

    public void MoveRow(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Rows.Count) return;
        if (toIndex < 0 || toIndex >= Rows.Count) return;
        if (fromIndex == toIndex) return;
        Rows.Move(fromIndex, toIndex);
    }

    private DashboardLayout GetCurrentLayout()
    {
        return new DashboardLayout
        {
            Rows = Rows.Select(r => r.ToRow()).ToList()
        };
    }

    public void Cleanup()
    {
        Catalog.WidgetAddRequested -= OnWidgetAddRequested;
        UnwireWidgetEvents();
        foreach (var row in Rows)
            foreach (var w in row.Widgets)
                w.Cleanup();
    }
}
