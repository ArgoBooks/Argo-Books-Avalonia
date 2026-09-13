using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Views;

/// <summary>
/// The main application shell containing sidebar and content area.
/// </summary>
public partial class AppShell : UserControl
{
    private const double CompactPageThreshold = 1200;
    private const double MinimalPageThreshold = 900;

    private HeaderViewModel? _previousHeaderVm;

    public AppShell()
    {
        InitializeComponent();

        // Use tunnel strategy to catch all pointer presses on sidebar/header,
        // even when child controls handle the event. This ensures page-level
        // context menus (column visibility, chart) close on any sidebar/header click.
        AppSidebar.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, RoutingStrategies.Tunnel);
        AppHeader.AddHandler(PointerPressedEvent, OnHeaderPointerPressed, RoutingStrategies.Tunnel);

        // Responsive page content margin
        AppContent.SizeChanged += OnContentSizeChanged;

        // Animate toast slide in/out from right
        DataContextChanged += (_, _) =>
        {
            if (_previousHeaderVm != null)
                _previousHeaderVm.PropertyChanged -= OnHeaderViewModelPropertyChanged;

            if (DataContext is AppShellViewModel vm)
            {
                _previousHeaderVm = vm.HeaderViewModel;
                vm.HeaderViewModel.PropertyChanged += OnHeaderViewModelPropertyChanged;
            }
            else
            {
                _previousHeaderVm = null;
            }
        };
    }

    private void OnHeaderViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HeaderViewModel.ShowNotificationToast))
            return;

        var vm = (HeaderViewModel)sender!;
        if (vm.ShowNotificationToast)
        {
            Dispatcher.UIThread.Post(() =>
            {
                NotificationToastBorder.Opacity = 1;
                NotificationToastBorder.RenderTransform = new TranslateTransform(0, 0);
            }, DispatcherPriority.Render);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                NotificationToastBorder.Opacity = 0;
                NotificationToastBorder.RenderTransform = new TranslateTransform(360, 0);
            }, DispatcherPriority.Background);
        }
    }

    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width < MinimalPageThreshold)
            PageContentControl.Margin = new Thickness(0);
        else if (width < CompactPageThreshold)
            PageContentControl.Margin = new Thickness(12);
        else
            PageContentControl.Margin = new Thickness(30);
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Focus the shell to receive keyboard events
        Focus();

        // Subscribe to file scan request from quick action (guard against duplicate subscriptions on re-load)
        if (DataContext is AppShellViewModel vm)
        {
            vm.OpenFileScanRequested -= OnOpenFileScanRequested;
            vm.OpenFileScanRequested += OnOpenFileScanRequested;
        }
    }

    /// <summary>
    /// Closes page-level context menus when the sidebar is clicked.
    /// </summary>
    private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as AppShellViewModel)?.ClosePageContextMenus();
    }

    /// <summary>
    /// Closes page-level context menus when the header is clicked.
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as AppShellViewModel)?.ClosePageContextMenus();
    }

    /// <summary>
    /// Handles the request to open a file for scanning.
    /// </summary>
    private async void OnOpenFileScanRequested(object? sender, EventArgs e)
    {
        try
        {
            if (App.ReceiptsModalsViewModel == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Receipt to Scan",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerTypes.AllSupportedTypes, FilePickerTypes.ImageFileType, FilePickerTypes.PdfFileType]
            });

            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && App.ReceiptsModalsViewModel != null)
                {
                    await App.ReceiptsModalsViewModel.OpenScanModalAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnOpenFileScanRequested");
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // A page that already acted on the key keeps it. The reports designer binds its own
        // Ctrl+S to saving a template, and on that page saving the template is what the key
        // should do.
        if (e.Handled)
            return;

        if (DataContext is not AppShellViewModel vm)
            return;

        switch (e.Key)
        {
            // Quick actions panel.
            case Key.K when e.KeyModifiers.HasCommand():
                vm.OpenQuickActionsCommand.Execute(null);
                e.Handled = true;
                break;

            // Save, routed through the same command as the header's save button so it
            // answers the same way: "Saved" when something changed, "No changes found"
            // when nothing did. Saving is never silent, which is the point: a shortcut
            // that does nothing visible reads as a shortcut that did not work.
            //
            // Shift is excluded so Ctrl+Shift+S stays available for Save As.
            case Key.S when e.KeyModifiers.HasCommand() && !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                vm.HeaderViewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;

            // Save As, the other shortcut the File menu advertises. Routed through the menu's own command so it shares the sample
            // company redirect and the save-location dialog.
            case Key.S when e.KeyModifiers.HasCommand() && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                vm.FileMenuPanelViewModel.SaveAsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
