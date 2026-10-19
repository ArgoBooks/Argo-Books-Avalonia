using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ArgoBooks.Converters;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Controls;

/// <summary>
/// Button group with undo/redo buttons and dropdown history panels.
/// </summary>
public partial class UndoRedoButtonGroup : UserControl
{
    private Popup? _undoPopup;
    private Popup? _redoPopup;
    private ItemsControl? _undoHistoryList;
    private ItemsControl? _redoHistoryList;
    private TextBlock? _undoCountLabel;
    private TextBlock? _redoCountLabel;
    private int _currentUndoHoverIndex = -1;
    private int _currentRedoHoverIndex = -1;

    private static IBrush HighlightBrush => ConverterUtils.AccentTint(40);

    public UndoRedoButtonGroup()
    {
        InitializeComponent();
        // Tunnelled, because the dropdown button that keeps focus would otherwise take Enter and reopen its list.
        AddHandler(KeyDownEvent, OnHistoryKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Lets the arrow keys select actions in an open history list the way hovering does, and Enter apply them like a click.</summary>
    private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not IUndoRedoButtonGroupViewModel vm)
            return;

        if (_undoPopup?.IsOpen == true)
        {
            e.Handled = HandleHistoryKey(e.Key, _undoPopup, _undoHistoryList, ref _currentUndoHoverIndex, vm.UndoToCommand);
            UpdateUndoHighlighting();
            UpdateUndoCountLabel();
        }
        else if (_redoPopup?.IsOpen == true)
        {
            e.Handled = HandleHistoryKey(e.Key, _redoPopup, _redoHistoryList, ref _currentRedoHoverIndex, vm.RedoToCommand);
            UpdateRedoHighlighting();
            UpdateRedoCountLabel();
        }
    }

    private static bool HandleHistoryKey(Key key, Popup popup, ItemsControl? list, ref int index, ICommand command)
    {
        switch (key)
        {
            case Key.Down:
                index = Math.Min(index + 1, (list?.ItemCount ?? 0) - 1);
                break;
            case Key.Up:
                index = Math.Max(index - 1, 0);
                break;
            case Key.Enter:
                if (index >= 0)
                    command.Execute(index);
                popup.Close();
                index = -1;
                return true;
            case Key.Escape:
                popup.Close();
                index = -1;
                return true;
            default:
                return false;
        }

        if (index >= 0)
            list?.ContainerFromIndex(index)?.BringIntoView();
        return true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _undoPopup = this.FindControl<Popup>("UndoPopup");
        _redoPopup = this.FindControl<Popup>("RedoPopup");
        _undoHistoryList = this.FindControl<ItemsControl>("UndoHistoryList");
        _redoHistoryList = this.FindControl<ItemsControl>("RedoHistoryList");
        _undoCountLabel = this.FindControl<TextBlock>("UndoCountLabel");
        _redoCountLabel = this.FindControl<TextBlock>("RedoCountLabel");
    }

    private void UndoDropdownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_undoPopup != null && DataContext is IUndoRedoButtonGroupViewModel vm)
        {
            vm.RefreshHistory();
            _redoPopup?.Close();
            _currentUndoHoverIndex = -1;
            UpdateUndoCountLabel();
            _undoPopup.IsOpen = !_undoPopup.IsOpen;
        }
    }

    private void RedoDropdownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_redoPopup != null && DataContext is IUndoRedoButtonGroupViewModel vm)
        {
            vm.RefreshHistory();
            _undoPopup?.Close();
            _currentRedoHoverIndex = -1;
            UpdateRedoCountLabel();
            _redoPopup.IsOpen = !_redoPopup.IsOpen;
        }
    }

    private void UndoItem_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { Tag: int index })
        {
            _currentUndoHoverIndex = index;
            UpdateUndoHighlighting();
            UpdateUndoCountLabel();
        }
    }

    private void UndoItem_PointerExited(object? sender, PointerEventArgs e)
    {
        // Don't clear immediately - only clear when leaving the whole list
    }

    private void UndoItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: int index } && DataContext is IUndoRedoButtonGroupViewModel vm)
        {
            vm.UndoToCommand.Execute(index);
            _undoPopup?.Close();
            _currentUndoHoverIndex = -1;
        }
    }

    private void RedoItem_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { Tag: int index })
        {
            _currentRedoHoverIndex = index;
            UpdateRedoHighlighting();
            UpdateRedoCountLabel();
        }
    }

    private void RedoItem_PointerExited(object? sender, PointerEventArgs e)
    {
        // Don't clear immediately - only clear when leaving the whole list
    }

    private void RedoItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: int index } && DataContext is IUndoRedoButtonGroupViewModel vm)
        {
            vm.RedoToCommand.Execute(index);
            _redoPopup?.Close();
            _currentRedoHoverIndex = -1;
        }
    }

    private void UpdateUndoHighlighting()
    {
        if (_undoHistoryList == null) return;

        for (int i = 0; i < _undoHistoryList.Items.Count; i++)
        {
            var container = _undoHistoryList.ContainerFromIndex(i);
            if (container is ContentPresenter { Child: Border border })
            {
                // Highlight items from 0 to hoverIndex (inclusive)
                border.Background = i <= _currentUndoHoverIndex ? HighlightBrush : Brushes.Transparent;
            }
        }
    }

    private void UpdateRedoHighlighting()
    {
        if (_redoHistoryList == null) return;

        for (int i = 0; i < _redoHistoryList.Items.Count; i++)
        {
            var container = _redoHistoryList.ContainerFromIndex(i);
            if (container is ContentPresenter { Child: Border border })
            {
                // Highlight items from 0 to hoverIndex (inclusive)
                border.Background = i <= _currentRedoHoverIndex ? HighlightBrush : Brushes.Transparent;
            }
        }
    }

    private void UpdateUndoCountLabel()
    {
        if (_undoCountLabel == null) return;

        if (_currentUndoHoverIndex < 0)
        {
            _undoCountLabel.Text = "Hover to select actions".Translate();
        }
        else
        {
            int count = _currentUndoHoverIndex + 1;
            _undoCountLabel.Text = count == 1
                ? "Undo 1 Action".Translate()
                : "Undo {0} Actions".TranslateFormat(count);
        }
    }

    private void UpdateRedoCountLabel()
    {
        if (_redoCountLabel == null) return;

        if (_currentRedoHoverIndex < 0)
        {
            _redoCountLabel.Text = "Hover to select actions".Translate();
        }
        else
        {
            int count = _currentRedoHoverIndex + 1;
            _redoCountLabel.Text = count == 1
                ? "Redo 1 Action".Translate()
                : "Redo {0} Actions".TranslateFormat(count);
        }
    }

}
