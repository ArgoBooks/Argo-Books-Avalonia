using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Receipts page.
/// </summary>
public partial class ReceiptsPage : UserControl
{

    public ReceiptsPage()
    {
        InitializeComponent();

        // Subscribe to drag-drop events
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Subscribe to data context changes to wire up ViewModel events
        DataContextChanged += OnDataContextChanged;
    }

    private ReceiptsPageViewModel? _previousViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousViewModel != null)
        {
            _previousViewModel.ScanFileRequested -= OnScanFileRequested;
            _previousViewModel = null;
        }

        if (DataContext is ReceiptsPageViewModel viewModel)
        {
            viewModel.ScanFileRequested += OnScanFileRequested;
            _previousViewModel = viewModel;
        }
    }

    private void OnScanFileRequested(object? sender, EventArgs e)
    {
        try
        {
            var viewModel = DataContext as ReceiptsPageViewModel;
            if (viewModel == null) return;

            // Open the bulk drop zone modal instead of going straight to file picker
            App.ReceiptsModalsViewModel?.OpenBulkDropZone();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnScanFileRequested");
        }
    }

    private void OnReceiptCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ReceiptDisplayItem receipt })
        {
            if (DataContext is ReceiptsPageViewModel viewModel)
            {
                var props = e.GetCurrentPoint(null).Properties;
                if (!props.IsLeftButtonPressed) return;

                var keyMods = e.KeyModifiers;
                if (keyMods.HasCommand() || keyMods.HasFlag(KeyModifiers.Shift))
                {
                    // Ctrl/Shift+click: toggle selection
                    viewModel.ToggleReceiptSelectionCommand.Execute(receipt);
                    e.Handled = true;
                }
                else if (viewModel.IsSelectionMode)
                {
                    // Already in selection mode: toggle selection
                    viewModel.ToggleReceiptSelectionCommand.Execute(receipt);
                    e.Handled = true;
                }
                else
                {
                    viewModel.OpenPreviewCommand.Execute(receipt);
                }

                e.Handled = true;
            }
        }
    }

    private void OnReceiptRowPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore if clicking on checkbox or action buttons
        if (e.Source is CheckBox or Button) return;

        if (sender is Border { DataContext: ReceiptDisplayItem receipt })
        {
            if (DataContext is ReceiptsPageViewModel viewModel)
            {
                var keyMods = e.KeyModifiers;
                if (keyMods.HasCommand() || keyMods.HasFlag(KeyModifiers.Shift))
                {
                    viewModel.ToggleReceiptSelectionCommand.Execute(receipt);
                }
                else if (viewModel.IsSelectionMode)
                {
                    viewModel.ToggleReceiptSelectionCommand.Execute(receipt);
                }

                e.Handled = true;
            }
        }
    }

    private void OnTableBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        // If the click was on a receipt card or its children, the card handler will handle it
        // This only fires for clicks on the empty background area
        if (DataContext is ReceiptsPageViewModel viewModel && viewModel.IsSelectionMode)
        {
            viewModel.ExitSelectionModeCommand.Execute(null);
        }
    }

    private void OnTableSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // ArgoTable has already passed the width on; only grid view's override is left to do.
        if (DataContext is ReceiptsPageViewModel viewModel && e.WidthChanged && viewModel.IsGridView)
            viewModel.ColumnWidths.NeedsHorizontalScroll = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not ReceiptsPageViewModel viewModel) return;

        // Check if the data contains files
        var files = e.DataTransfer.TryGetFiles();
        if (files != null)
        {
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (FilePickerTypes.IsSupportedReceiptFile(path))
                {
                    e.DragEffects = DragDropEffects.Copy;
                    viewModel.IsDragOver = true;
                    return;
                }
            }
        }

        e.DragEffects = DragDropEffects.None;
        viewModel.IsDragOver = false;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is ReceiptsPageViewModel viewModel)
        {
            viewModel.IsDragOver = false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not ReceiptsPageViewModel viewModel) return;

            viewModel.IsDragOver = false;

            var files = e.DataTransfer.TryGetFiles();
            if (files != null)
            {
                var filePaths = files
                    .Select(f => f.TryGetLocalPath())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                if (filePaths.Count > 0)
                {
                    await viewModel.HandleFilesDroppedAsync(filePaths!);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnDrop");
        }
    }
}
