using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ArgoBooks.Controls;
using ArgoBooks.Controls.Dashboard;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using ArgoBooks.ViewModels.Dashboard;

namespace ArgoBooks.Views;

/// <summary>
/// Dashboard page providing an overview of key business metrics via customizable widgets.
/// </summary>
public partial class DashboardPage : UserControl
{
    private readonly PageChartInteractions _charts;
    private DashboardDragDropManager? _dragDropManager;
    private DashboardPageViewModel? _previousViewModel;
    private readonly List<(DashboardRowViewModel Row, NotifyCollectionChangedEventHandler Handler)> _rowSubscriptions = [];
    private readonly List<(WidgetViewModelBase Vm, PropertyChangedEventHandler Handler)> _widgetVisibilitySubscriptions = [];
    private WidgetHostViewModel? _settingsTarget;
    private DashboardRowViewModel? _settingsTargetRow;

    // Row drag state
    private bool _isRowDragging;
    private int _rowDragSourceIndex = -1;
    private int _rowDragPreviewIndex = -1;
    private Point _rowDragStartPoint;
    private Point _rowDragOffset;
    private Border? _rowDragGhost;
    private DashboardLayoutViewModel? _rowDragLayoutVm;

    /// <summary>
    /// Sets the clicked chart reference from an external source (e.g., ChartExpandOverlay).
    /// </summary>
    public void SetClickedChart(Control? chart, string name) => _charts.SetClickedChart(chart, name);

    public DashboardPage()
    {
        InitializeComponent();
        _charts = new PageChartInteractions(this);

        // Subscribe to ViewModel events when DataContext changes
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousViewModel != null)
        {
            _previousViewModel.SaveChartImageRequested -= _charts.OnSaveChartImageRequested;
            _previousViewModel.ExcelExportRequested -= _charts.OnExcelExportRequested;
            _previousViewModel.LayoutViewModel.Rows.CollectionChanged -= OnRowsCollectionChanged;
            _previousViewModel.LayoutViewModel.PropertyChanged -= OnLayoutPropertyChanged;
            _previousViewModel = null;
        }

        if (DataContext is DashboardPageViewModel viewModel)
        {
            _previousViewModel = viewModel;
            viewModel.SaveChartImageRequested += _charts.OnSaveChartImageRequested;
            viewModel.ExcelExportRequested += _charts.OnExcelExportRequested;
            viewModel.LayoutViewModel.Rows.CollectionChanged += OnRowsCollectionChanged;
            viewModel.LayoutViewModel.PropertyChanged += OnLayoutPropertyChanged;
            RebuildRows(viewModel.LayoutViewModel);
            UpdateEmptyMessageVisibility(viewModel.LayoutViewModel);
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardLayoutViewModel.HasWidgets)
            or nameof(DashboardLayoutViewModel.IsEditMode))
        {
            if (sender is DashboardLayoutViewModel layoutVm)
                UpdateEmptyMessageVisibility(layoutVm);
        }
    }

    private void UpdateEmptyMessageVisibility(DashboardLayoutViewModel layoutVm)
    {
        var isEmpty = !layoutVm.HasWidgets;
        EmptyDashboardMessage.IsVisible = isEmpty && !layoutVm.IsEditMode;
        EmptyDashboardEditMessage.IsVisible = isEmpty && layoutVm.IsEditMode;
    }

    #region Row Panel Management

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not DashboardPageViewModel viewModel) return;

        if (e.Action == NotifyCollectionChangedAction.Move
            && e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0
            && e.OldStartingIndex < RowsContainer.Children.Count)
        {
            // Reorder visual children without rebuilding, avoids chart reload
            var child = RowsContainer.Children[e.OldStartingIndex];
            RowsContainer.Children.RemoveAt(e.OldStartingIndex);
            RowsContainer.Children.Insert(e.NewStartingIndex, child);
            SetupDragDrop(viewModel.LayoutViewModel);
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Remove
            && e.OldStartingIndex >= 0
            && e.OldStartingIndex < RowsContainer.Children.Count)
        {
            // Remove the visual child without rebuilding, avoids chart reload
            RowsContainer.Children.RemoveAt(e.OldStartingIndex);
            SetupDragDrop(viewModel.LayoutViewModel);
            return;
        }

        RebuildRows(viewModel.LayoutViewModel);
    }

    private void RebuildRows(DashboardLayoutViewModel layoutVm)
    {
        // Unsubscribe from old widget collection handlers (prevents subscription leak)
        foreach (var (row, handler) in _rowSubscriptions)
            row.Widgets.CollectionChanged -= handler;
        _rowSubscriptions.Clear();

        // Unsubscribe from old widget visibility handlers
        foreach (var (vm, handler) in _widgetVisibilitySubscriptions)
            vm.PropertyChanged -= handler;
        _widgetVisibilitySubscriptions.Clear();

        // Unsubscribe from old widget VMs
        foreach (var child in RowsContainer.Children)
        {
            if (child is DashboardRowHost rowHost)
            {
                foreach (var widgetChild in rowHost.Panel.Children)
                {
                    if (widgetChild is WidgetHost host && host.DataContext is WidgetHostViewModel oldVm)
                        oldVm.PropertyChanged -= OnWidgetHostPropertyChanged;
                }
            }
        }

        RowsContainer.Children.Clear();
        _dragDropManager?.Detach();
        _dragDropManager = null;

        for (int rowIdx = 0; rowIdx < layoutVm.Rows.Count; rowIdx++)
        {
            var rowVm = layoutVm.Rows[rowIdx];
            var rowHost = new DashboardRowHost { DataContext = rowVm };

            // Wire row-level buttons
            var capturedRowVm = rowVm; // capture for lambda
            rowHost.AddButton.Click += (_, _) => layoutVm.OpenCatalogForRow(capturedRowVm);
            rowHost.DeleteButton.Click += (_, _) => layoutVm.RemoveRow(capturedRowVm);

            // Populate widget panel
            foreach (var hostVm in rowVm.Widgets)
            {
                var widgetHost = CreateWidgetHost(hostVm, layoutVm);
                rowHost.Panel.Children.Add(widgetHost);
            }

            // Hide the row if all its widgets are invisible (e.g., completed setup checklist)
            UpdateRowVisibility(rowHost, rowVm);
            var capturedHost = rowHost;
            var capturedVm = rowVm;
            foreach (var hostVm in rowVm.Widgets)
            {
                PropertyChangedEventHandler visibilityHandler = (_, args) =>
                {
                    if (args.PropertyName == nameof(WidgetViewModelBase.IsWidgetVisible))
                    {
                        UpdateRowVisibility(capturedHost, capturedVm);
                        UpdateEmptyMessageVisibility(layoutVm);
                    }
                };
                hostVm.WidgetViewModel.PropertyChanged += visibilityHandler;
                _widgetVisibilitySubscriptions.Add((hostVm.WidgetViewModel, visibilityHandler));
            }

            // Listen for widget collection changes in this row
            var capturedRowHost = rowHost;
            NotifyCollectionChangedEventHandler widgetHandler = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Move
                    && args.OldStartingIndex >= 0 && args.NewStartingIndex >= 0)
                {
                    // Reorder visual children without rebuilding, avoids chart reload
                    var moveChild = capturedRowHost.Panel.Children[args.OldStartingIndex];
                    capturedRowHost.Panel.Children.RemoveAt(args.OldStartingIndex);
                    capturedRowHost.Panel.Children.Insert(args.NewStartingIndex, moveChild);
                }
                else if (args.Action == NotifyCollectionChangedAction.Remove
                    && args.OldStartingIndex >= 0
                    && args.OldStartingIndex < capturedRowHost.Panel.Children.Count)
                {
                    // Remove the visual child without rebuilding, avoids chart reload
                    var panel = capturedRowHost.Panel;
                    var removeChild = panel.Children[args.OldStartingIndex];
                    if (removeChild is WidgetHost host && host.DataContext is WidgetHostViewModel oldVm)
                        oldVm.PropertyChanged -= OnWidgetHostPropertyChanged;
                    panel.Children.RemoveAt(args.OldStartingIndex);
                    panel.InvalidateArrange();
                }
                else if (args.Action == NotifyCollectionChangedAction.Add
                    && args.NewStartingIndex >= 0 && args.NewItems?.Count == 1)
                {
                    // Add the visual child without rebuilding, avoids chart reload
                    if (args.NewItems[0] is WidgetHostViewModel newVm)
                    {
                        var newHost = CreateWidgetHost(newVm, layoutVm);
                        capturedRowHost.Panel.Children.Insert(args.NewStartingIndex, newHost);
                        // Attach drag handle to existing manager
                        if (_dragDropManager != null)
                        {
                            var dragHandle = newHost.FindControl<Border>("DragHandle");
                            if (dragHandle != null)
                                _dragDropManager.AttachDragHandle(dragHandle);
                        }
                    }
                }
                else
                {
                    RebuildRows(layoutVm);
                }
            };
            rowVm.Widgets.CollectionChanged += widgetHandler;
            _rowSubscriptions.Add((rowVm, widgetHandler));

            RowsContainer.Children.Add(rowHost);
        }

        SetupDragDrop(layoutVm);
    }

    private WidgetHost CreateWidgetHost(WidgetHostViewModel hostVm, DashboardLayoutViewModel layoutVm)
    {
        var widgetHost = new WidgetHost { DataContext = hostVm, Margin = new Thickness(6, 0) };
        widgetHost.SetWidgetContent(hostVm);
        DashboardRowPanel.SetWidgetFraction(widgetHost, hostVm.Size.ToFraction());
        if (hostVm.StartOffset > 0.001)
            DashboardRowPanel.SetStartOffset(widgetHost, hostVm.StartOffset);
        hostVm.PropertyChanged += (sender, args) =>
        {
            OnWidgetHostPropertyChanged(sender, args);
            if (args.PropertyName == nameof(WidgetHostViewModel.IsConfigOpen) && hostVm.IsConfigOpen)
            {
                // Find the settings button to position the popup relative to it
                var settingsBtn = widgetHost.FindControl<Button>("SettingsButton");
                ShowSettingsPopup(hostVm, widgetHost, settingsBtn, layoutVm);
            }
        };

        var removeButton = widgetHost.FindControl<Button>("RemoveButton");
        if (removeButton != null)
        {
            removeButton.Command = layoutVm.RemoveWidgetCommand;
            removeButton.CommandParameter = hostVm;
        }

        return widgetHost;
    }

    private void OnWidgetHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetHostViewModel.Size) && sender is WidgetHostViewModel hostVm)
        {
            foreach (var child in RowsContainer.Children)
            {
                if (child is not DashboardRowHost rowHost) continue;
                foreach (var widgetChild in rowHost.Panel.Children)
                {
                    if (widgetChild is WidgetHost wh && wh.DataContext == hostVm)
                    {
                        // Preserve position: compute current offset from widget's visual position
                        var panelWidth = rowHost.Panel.Bounds.Width;
                        if (panelWidth > 0)
                        {
                            var currentLeft = wh.Bounds.Left;
                            var offset = Math.Round(currentLeft / panelWidth * 4) / 4;
                            var newFraction = hostVm.Size.ToFraction();
                            // Clamp so widget doesn't overflow the row
                            offset = Math.Min(offset, 1.0 - newFraction);
                            offset = Math.Max(0, offset);
                            hostVm.StartOffset = offset;
                            DashboardRowPanel.SetStartOffset(wh, offset);
                        }

                        DashboardRowPanel.SetWidgetFraction(wh, hostVm.Size.ToFraction());
                        rowHost.Panel.InvalidateMeasure();
                        rowHost.Panel.InvalidateArrange();
                        return;
                    }
                }
            }
        }
    }

    private void SetupDragDrop(DashboardLayoutViewModel layoutVm)
    {
        var rowPanels = new List<DashboardRowPanel>();
        foreach (var child in RowsContainer.Children)
        {
            if (child is DashboardRowHost rowHost)
                rowPanels.Add(rowHost.Panel);
        }

        if (rowPanels.Count == 0) return;

        _dragDropManager = new DashboardDragDropManager(
            rowPanels,
            RowsContainer,
            MainScrollViewer,
            layoutVm);

        for (int idx = 0; idx < RowsContainer.Children.Count; idx++)
        {
            if (RowsContainer.Children[idx] is not DashboardRowHost rowHost) continue;

            // Wire widget drag handles
            for (int i = 0; i < rowHost.Panel.Children.Count; i++)
            {
                if (rowHost.Panel.Children[i] is WidgetHost widgetHost)
                {
                    var dragHandle = widgetHost.FindControl<Border>("DragHandle");
                    if (dragHandle != null)
                        _dragDropManager.AttachDragHandle(dragHandle);
                }
            }

            // Wire row drag handle, find index dynamically at press time
            var capturedRowHost = rowHost;
            rowHost.DragHandle.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
                var currentIndex = RowsContainer.Children.IndexOf(capturedRowHost);
                if (currentIndex < 0) return;
                _rowDragSourceIndex = currentIndex;
                _rowDragPreviewIndex = currentIndex;
                _rowDragStartPoint = e.GetPosition(RowsContainer);
                _rowDragLayoutVm = layoutVm;
                e.Handled = true;
            };
        }

        // Row drag pointer handlers, remove first to prevent stacking
        MainScrollViewer.RemoveHandler(PointerMovedEvent, OnRowPointerMoved);
        MainScrollViewer.RemoveHandler(PointerReleasedEvent, OnRowPointerReleased);
        MainScrollViewer.AddHandler(PointerMovedEvent, OnRowPointerMoved, handledEventsToo: true);
        MainScrollViewer.AddHandler(PointerReleasedEvent, OnRowPointerReleased, handledEventsToo: true);
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_rowDragSourceIndex < 0) return;
        if (_rowDragSourceIndex >= RowsContainer.Children.Count)
        {
            _rowDragSourceIndex = -1;
            _isRowDragging = false;
            return;
        }
        var position = e.GetPosition(RowsContainer);

        if (!_isRowDragging)
        {
            var delta = position - _rowDragStartPoint;
            if (Math.Abs(delta.Y) < 5) return;
            _isRowDragging = true;

            var sourceRow = RowsContainer.Children[_rowDragSourceIndex];
            sourceRow.Opacity = 0;

            // Calculate offset from pointer to row top-left
            _rowDragOffset = new Point(0, _rowDragStartPoint.Y - sourceRow.Bounds.Top);

            // Create ghost
            _rowDragGhost = new Border
            {
                Width = sourceRow.Bounds.Width,
                Height = sourceRow.Bounds.Height,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(30, 59, 130, 246)),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(59, 130, 246)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                IsHitTestVisible = false,
                Opacity = 0.7,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            if (RowsContainer.Parent is Panel parent)
                parent.Children.Add(_rowDragGhost);
        }

        // Position ghost
        if (_rowDragGhost != null)
        {
            var ghostY = position.Y - _rowDragOffset.Y;
            _rowDragGhost.Margin = new Thickness(0, ghostY, 0, 0);
        }

        // Determine target row from ghost center crossing row midpoints
        int targetIndex = _rowDragSourceIndex;
        if (_rowDragGhost != null)
        {
            var ghostCenterY = position.Y - _rowDragOffset.Y + _rowDragGhost.Height / 2;

            for (int i = 0; i < RowsContainer.Children.Count; i++)
            {
                if (i == _rowDragSourceIndex) continue;
                if (RowsContainer.Children[i] is not DashboardRowHost rowHost) continue;
                var midY = rowHost.Bounds.Top + rowHost.Bounds.Height / 2;

                if (i > _rowDragSourceIndex && ghostCenterY > midY)
                    targetIndex = Math.Max(targetIndex, i);
                else if (i < _rowDragSourceIndex && ghostCenterY < midY)
                    targetIndex = Math.Min(targetIndex, i);
            }
        }

        if (targetIndex != _rowDragPreviewIndex)
        {
            _rowDragPreviewIndex = targetIndex;
            if (_rowDragSourceIndex >= RowsContainer.Children.Count) return;

            // Apply transforms to show preview
            double sourceHeight = RowsContainer.Children[_rowDragSourceIndex].Bounds.Height
                + 12; // spacing
            for (int i = 0; i < RowsContainer.Children.Count; i++)
            {
                if (i == _rowDragSourceIndex) continue;
                double dy = 0;
                if (targetIndex > _rowDragSourceIndex && i > _rowDragSourceIndex && i <= targetIndex)
                    dy = -sourceHeight;
                else if (targetIndex < _rowDragSourceIndex && i >= targetIndex && i < _rowDragSourceIndex)
                    dy = sourceHeight;

                RowsContainer.Children[i].RenderTransform = Math.Abs(dy) > 0.5
                    ? new Avalonia.Media.TranslateTransform(0, dy)
                    : null;
            }
        }

        e.Handled = true;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rowDragSourceIndex < 0) return;

        int sourceIndex = _rowDragSourceIndex;
        int targetIndex = _rowDragPreviewIndex;
        var layoutVm = _rowDragLayoutVm;

        // Reset visual state
        for (int i = 0; i < RowsContainer.Children.Count; i++)
            RowsContainer.Children[i].RenderTransform = null;
        if (sourceIndex < RowsContainer.Children.Count)
            RowsContainer.Children[sourceIndex].Opacity = 1.0;

        // Remove ghost
        if (RowsContainer.Parent is Panel ghostParent && _rowDragGhost != null)
            ghostParent.Children.Remove(_rowDragGhost);
        _rowDragGhost = null;

        _isRowDragging = false;
        _rowDragSourceIndex = -1;
        _rowDragPreviewIndex = -1;
        _rowDragLayoutVm = null;

        // Perform the actual move
        if (layoutVm != null && targetIndex >= 0 && targetIndex != sourceIndex)
            layoutVm.MoveRow(sourceIndex, targetIndex);
    }

    private static void UpdateRowVisibility(DashboardRowHost rowHost, DashboardRowViewModel rowVm)
    {
        rowHost.IsVisible = rowVm.Widgets.Count == 0
            || rowVm.Widgets.Any(w => w.WidgetViewModel.IsWidgetVisible);
    }

    #endregion

    #region Widget Settings Popup

    private void ShowSettingsPopup(WidgetHostViewModel hostVm, WidgetHost widgetHost, Button? settingsBtn, DashboardLayoutViewModel layoutVm)
    {
        _settingsTarget = hostVm;

        // Find which row this widget belongs to
        _settingsTargetRow = null;
        foreach (var row in layoutVm.Rows)
        {
            if (row.Widgets.Contains(hostVm))
            {
                _settingsTargetRow = row;
                break;
            }
        }

        // Build popup content
        SettingsContent.Children.Clear();

        // Header
        var headerText = new TextBlock
        {
            Text = "Settings",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 13
        };
        headerText.SetValue(TextBlock.ForegroundProperty, Application.Current?.FindResource("TextPrimaryBrush") as Avalonia.Media.IBrush ?? Avalonia.Media.Brushes.White);
        SettingsContent.Children.Add(headerText);
        SettingsContent.Children.Add(new Separator { Height = 1, Margin = new Thickness(0, 0, 0, 4) });

        // Widget-specific config content
        var configView = WidgetSettingsFactory.CreateConfigView(hostVm.WidgetViewModel);
        if (configView != null)
        {
            configView.DataContext = hostVm.WidgetViewModel;
            SettingsContent.Children.Add(configView);
            SettingsContent.Children.Add(new Separator { Height = 1, Margin = new Thickness(0, 4, 0, 0) });
        }

        // Size section
        var sizeLabel = new TextBlock { Text = "Size", FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Medium, Margin = new Thickness(0, 0, 0, 4) };
        SettingsContent.Children.Add(sizeLabel);

        var sizePanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        BuildSizeButtons(sizePanel, hostVm, layoutVm);
        SettingsContent.Children.Add(sizePanel);

        // Position popup below the settings button, right-aligned with it
        // Use right-alignment: popup's right edge = button's right edge
        var anchor = (Visual)(settingsBtn ?? (Control)widgetHost);
        double x = 8, y = 8;

        // Translate the button's top-left and bottom-right to page coordinates
        var topLeft = anchor.TranslatePoint(new Point(0, 0), this);
        var bottomRight = anchor.TranslatePoint(new Point(anchor.Bounds.Width, anchor.Bounds.Height), this);

        System.Diagnostics.Debug.WriteLine($"[SettingsPopup] anchor.Bounds={anchor.Bounds}");
        System.Diagnostics.Debug.WriteLine($"[SettingsPopup] topLeft={topLeft}, bottomRight={bottomRight}");
        System.Diagnostics.Debug.WriteLine($"[SettingsPopup] page.Bounds={Bounds}");

        if (bottomRight.HasValue)
        {
            var buttonRight = bottomRight.Value.X;
            y = bottomRight.Value.Y + 4;

            var popupWidth = 280.0;
            x = buttonRight - popupWidth;

            System.Diagnostics.Debug.WriteLine($"[SettingsPopup] buttonRight={buttonRight}, x={x}, y={y}");
        }

        // Clamp to stay within page bounds
        x = Math.Max(8, x);
        y = Math.Max(8, y);

        SettingsPopup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        SettingsPopup.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        SettingsPopup.Margin = new Thickness(x, y, 0, 0);

        System.Diagnostics.Debug.WriteLine($"[SettingsPopup] final margin=({x}, {y})");

        SettingsBackdrop.IsVisible = true;
        SettingsPopup.IsVisible = true;

        // Wire backdrop click
        SettingsBackdrop.PointerPressed -= OnSettingsBackdropPressed;
        SettingsBackdrop.PointerPressed += OnSettingsBackdropPressed;
    }

    private void OnSettingsBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseSettingsPopup();
        e.Handled = true;
    }

    private void CloseSettingsPopup()
    {
        SettingsBackdrop.IsVisible = false;
        SettingsPopup.IsVisible = false;
        if (_settingsTarget != null)
        {
            _settingsTarget.IsConfigOpen = false;
            _settingsTarget = null;
        }
        _settingsTargetRow = null;
        SettingsContent.Children.Clear();
    }

    private void BuildSizeButtons(StackPanel panel, WidgetHostViewModel hostVm, DashboardLayoutViewModel layoutVm)
    {
        // Calculate how much room other widgets in the row use
        double otherFraction = 0;
        if (_settingsTargetRow != null)
        {
            foreach (var w in _settingsTargetRow.Widgets)
            {
                if (w != hostVm)
                    otherFraction += w.Size.ToFraction();
            }
        }

        foreach (var size in hostVm.AvailableSizes)
        {
            var label = size switch
            {
                WidgetSize.Tiny => "25%",
                WidgetSize.Small => "33%",
                WidgetSize.Medium => "50%",
                WidgetSize.MedLarge => "75%",
                WidgetSize.Large => "100%",
                _ => size.ToString()
            };

            bool fits = otherFraction + size.ToFraction() <= 1.001;
            bool isSelected = hostVm.Size == size;

            var btn = new Button
            {
                Content = label,
                MinWidth = 44,
                MinHeight = 30,
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(6),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.Medium,
                Tag = size,
                IsEnabled = fits || isSelected,
                Classes = { isSelected ? "size-btn-selected" : "size-btn" }
            };

            var capturedSize = size;
            btn.Click += (_, _) =>
            {
                if (!fits && !isSelected) return;
                hostVm.Size = capturedSize;
                // Rebuild size buttons to update selection and fit states
                panel.Children.Clear();
                BuildSizeButtons(panel, hostVm, layoutVm);
            };

            panel.Children.Add(btn);
        }
    }

    #endregion
}
