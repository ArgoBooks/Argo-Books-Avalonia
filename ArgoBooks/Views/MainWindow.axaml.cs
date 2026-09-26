using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArgoBooks.Controls;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using System.ComponentModel;
using Avalonia.VisualTree;

namespace ArgoBooks.Views;

/// <summary>
/// The main application window with custom chrome and state persistence.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Count input for the session's active time. Tunnelling, so a click or key press
        // still registers when a child control handles it and the event never bubbles
        // back up here. Only presses and key downs: pointer movement would fire
        // constantly and would count a cursor drifting over the window as work.
        AddHandler(InputElement.KeyDownEvent, OnAnyInput, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, OnAnyInput, RoutingStrategies.Tunnel);

        // Subscribe to DataContext changes to ensure content is set
        DataContextChanged += OnDataContextChanged;

        // Set up window drag behavior for custom title bar
        var dragRegion = this.FindControl<Border>("DragRegion");
        var titleBar = this.FindControl<Border>("TitleBar");

        if (titleBar != null)
        {
            titleBar.PointerPressed += OnTitleBarPointerPressed;
        }

        if (dragRegion != null)
        {
            dragRegion.PointerPressed += OnTitleBarPointerPressed;
        }


        // Subscribe to window events for state persistence
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        PositionChanged += OnPositionChanged;

        // Update maximize/restore icon whenever the window state changes (e.g., drag-to-restore)
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                UpdateMaximizeIcon();
        };

        ConfigureMacOsChrome();
    }

    /// <summary>
    /// Swaps the custom Windows-style chrome for the native macOS title bar.
    ///
    /// The caption buttons this window draws are Windows through and through: right
    /// aligned, 46x30, a rectangle for maximise and the Windows accent red on close
    /// hover. On macOS the same three actions belong in traffic lights at the top left,
    /// so rather than restyling the custom buttons per platform this hands the job back
    /// to AppKit, which also restores double-click-to-zoom and the green fullscreen
    /// button for free.
    ///
    /// Hiding the custom title bar costs nothing: Title is already bound to WindowTitle
    /// ("Company - Argo Books"), which is exactly what the native bar renders.
    /// </summary>
    private void ConfigureMacOsChrome()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        ExtendClientAreaToDecorationsHint = false;
        WindowDecorations = WindowDecorations.Full;

        if (this.FindControl<Border>("TitleBar") is { } titleBar)
            titleBar.IsVisible = false;

        if (this.FindControl<StackPanel>("WindowControls") is { } windowControls)
            windowControls.IsVisible = false;
    }


    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// What to return to when leaving fullscreen. Maximized rather than Normal by
    /// default: dropping out of fullscreen into a small floating window reads as the
    /// app having resized itself.
    /// </summary>
    private WindowState _preFullScreenState = WindowState.Maximized;

    /// <summary>
    /// Runs ahead of every key press and click purely to timestamp it. Deliberately does
    /// nothing else and never marks the event handled, so it cannot alter what the input
    /// actually does.
    /// </summary>
    private static void OnAnyInput(object? sender, RoutedEventArgs e)
    {
        App.TelemetryManager?.MarkActivity();
    }

    /// <summary>
    /// F11 toggles fullscreen; Escape leaves it, but only when nothing else wanted the
    /// key first.
    ///
    /// The window draws its own chrome, so fullscreen is the only way to get the app
    /// edge to edge with nothing above it.
    ///
    /// Escape is shared with every modal and with the expanded-chart overlay, which both
    /// close on it. Two guards keep this from stealing it: the event is ignored once
    /// something has marked it handled, and any open <see cref="ModalOverlay"/> blocks it
    /// outright. The second guard matters because a modal only sees the key while it
    /// holds focus, so focus sitting elsewhere would otherwise let Escape drop the
    /// window out of fullscreen with a dialog still on screen.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape
                 && !e.Handled
                 && WindowState == WindowState.FullScreen
                 && !IsAnyModalOpen())
        {
            ToggleFullScreen();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private bool IsAnyModalOpen() =>
        this.GetVisualDescendants().OfType<ModalOverlay>().Any(m => m.IsOpen);

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _preFullScreenState;
        }
        else
        {
            // Minimized is never worth returning to, so it falls back to Maximized.
            _preFullScreenState = WindowState == WindowState.Minimized
                ? WindowState.Maximized
                : WindowState;
            WindowState = WindowState.FullScreen;
        }

        UpdateMaximizeIcon();
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        var maximizeRect = this.FindControl<Border>("MaximizeRect");
        var restoreIcon = this.FindControl<Canvas>("RestoreIcon");
        if (maximizeRect == null || restoreIcon == null) return;

        // FullScreen counts as filling the screen, so the chrome shows "restore" there
        // too. Clicking it then lands on Maximized, which is a sensible way out for
        // anyone who got into fullscreen and does not know the shortcut.
        var fillsScreen = WindowState is WindowState.Maximized or WindowState.FullScreen;
        maximizeRect.IsVisible = !fillsScreen;
        restoreIcon.IsVisible = fillsScreen;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Double-click to maximize/restore
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                // Single click to start drag
                BeginMoveDrag(e);
            }
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // Restore window position if saved
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.LoadWindowState();

            // Apply saved position if valid
            if (viewModel is { WindowLeft: >= 0, WindowTop: >= 0 })
            {
                var screens = Screens;
                var savedBounds = new PixelRect(
                    (int)viewModel.WindowLeft,
                    (int)viewModel.WindowTop,
                    (int)viewModel.WindowWidth,
                    (int)viewModel.WindowHeight);

                // Check if saved position is visible on any screen
                bool isVisible = false;
                foreach (var screen in screens.All)
                {
                    if (screen.WorkingArea.Intersects(savedBounds))
                    {
                        isVisible = true;
                        break;
                    }
                }

                if (isVisible)
                {
                    Position = new PixelPoint((int)viewModel.WindowLeft, (int)viewModel.WindowTop);
                }
            }
        }
    }

    private bool _isClosingConfirmed;

    // Set once shutdown starts. The telemetry upload can take seconds on a slow connection,
    // and a second click on X meanwhile would end the session and upload everything again.
    private bool _isEndingSession;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            // If we've already confirmed closing and done telemetry upload, just save window state
            if (_isClosingConfirmed)
            {
                SaveWindowState();
                return;
            }

            if (_isEndingSession)
            {
                e.Cancel = true;
                return;
            }

            // Check for unsaved changes in the reports page first
            if (App.HasReportsPageUnsavedChanges)
            {
                e.Cancel = true;

                var shouldContinue = await App.ConfirmReportsUnsavedChangesAsync();
                if (!shouldContinue)
                {
                    return; // User cancelled, don't close
                }

                // User confirmed, check for other unsaved changes before closing
                var hasAppUnsavedChanges = App.UndoRedoManager.IsAtSavedState == false;
                if (!hasAppUnsavedChanges)
                {
                    // No other unsaved changes, do telemetry upload and close
                    await EndTelemetryAndCloseAsync();
                    return;
                }
                // Fall through to handle app-level unsaved changes
            }

            // Check for unsaved changes - use UndoRedoManager's saved state tracking
            // which correctly handles undo back to original state
            var hasUnsavedChanges = !App.UndoRedoManager.IsAtSavedState;
            if (hasUnsavedChanges)
            {
                // Cancel the close event to show dialog
                e.Cancel = true;

                if (DataContext is MainWindowViewModel { UnsavedChangesDialogViewModel: not null } viewModel)
                {
                    var result = await viewModel.UnsavedChangesDialogViewModel.ShowAsync(
                        "Unsaved Changes".Translate(),
                        "You have unsaved changes. Would you like to save them before closing?".Translate());

                    switch (result)
                    {
                        case UnsavedChangesResult.Save:
                            // Save and close
                            if (App.CompanyManager != null)
                            {
                                // Sample company cannot be saved directly - redirect to Save As
                                if (App.CompanyManager.IsSampleCompany)
                                {
                                    var saved = await App.SaveCompanyAsFromWindowAsync();
                                    if (!saved) return; // User cancelled Save As, don't close
                                }
                                else
                                {
                                    try
                                    {
                                        var saved = await App.SaveCompanyWithSecurityGuidanceAsync();
                                        if (!saved) return; // User cancelled the blocked-save dialog, don't close
                                    }
                                    catch (Exception ex)
                                    {
                                        // Staying open keeps the changes; closing would discard them.
                                        App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "Save before closing failed");
                                        await App.ShowWarningDialogAsync(
                                            "Could Not Save".Translate(),
                                            "Your changes could not be saved, so the company is still open with them. {0}".TranslateFormat(ex.Message));
                                        return;
                                    }
                                }
                            }
                            await EndTelemetryAndCloseAsync();
                            break;

                        case UnsavedChangesResult.DontSave:
                            // Close without saving
                            await EndTelemetryAndCloseAsync();
                            break;

                        case UnsavedChangesResult.Cancel:
                        case UnsavedChangesResult.None:
                            // Don't close
                            break;
                    }
                }
            }
            else
            {
                // No unsaved changes, do telemetry upload and close
                e.Cancel = true;
                await EndTelemetryAndCloseAsync();
            }
        }
        catch (Exception ex)
        {
            // Cancel close to prevent data loss if an error occurred during the closing logic
            e.Cancel = true;
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "OnWindowClosing");
        }
    }

    /// <summary>
    /// Ends the telemetry session, waits for upload to complete, then closes the window.
    /// </summary>
    private async Task EndTelemetryAndCloseAsync()
    {
        _isEndingSession = true;
        SaveWindowState();
        if (App.TelemetryManager != null)
        {
            await App.TelemetryManager.EndSessionAsync();
        }
        if (Core.Services.PendingConversionService.Instance is { } conversions)
        {
            await conversions.FlushAsync();
        }
        _isClosingConfirmed = true;
        Close();
    }

    private void SaveWindowState()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Only save position if not maximized
            if (WindowState == WindowState.Normal)
            {
                viewModel.WindowLeft = Position.X;
                viewModel.WindowTop = Position.Y;
            }

            viewModel.SaveWindowState();
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Update position in view model when window moves (if not maximized)
        if (DataContext is MainWindowViewModel viewModel && WindowState == WindowState.Normal)
        {
            viewModel.WindowLeft = e.Point.X;
            viewModel.WindowTop = e.Point.Y;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Manually set the content when DataContext changes to work around binding timing issues
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Subscribe to property changes to update content when CurrentView changes
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Set initial content
            UpdateMainContent(viewModel.CurrentView);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentView) && DataContext is MainWindowViewModel viewModel)
        {
            UpdateMainContent(viewModel.CurrentView);
        }
    }

    private void UpdateMainContent(object? content)
    {
        var contentControl = this.FindControl<ContentControl>("MainContent");
        if (contentControl != null && content != null)
        {
            contentControl.Content = content;
        }
    }
}
