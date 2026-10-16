using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Panels;

/// <summary>
/// Dropdown panel providing help resources and support links.
/// </summary>
public partial class HelpPanel : UserControl
{
    private int _focusedIndex = -1;
    private readonly int _menuItemCount = 6; // Total menu items (0-5)
    private IInputElement? _opener;
    private HelpPanelViewModel? _previousVm;
    private PropertyChangedEventHandler? _propertyChangedHandler;

    public HelpPanel()
    {
        InitializeComponent();

        // Animate and focus the panel when it opens
        DataContextChanged += (_, _) =>
        {
            // Unsubscribe from previous ViewModel to prevent leak
            if (_previousVm != null && _propertyChangedHandler != null)
            {
                _previousVm.PropertyChanged -= _propertyChangedHandler;
            }

            if (DataContext is HelpPanelViewModel vm)
            {
                _previousVm = vm;
                _propertyChangedHandler = (_, e) =>
                {
                    if (e.PropertyName == nameof(HelpPanelViewModel.IsOpen))
                    {
                        if (vm.IsOpen)
                        {
                            _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                            Dispatcher.UIThread.Post(() =>
                            {
                                HelpPanelBorder.Opacity = 1;
                                HelpPanelBorder.RenderTransform = new TranslateTransform(0, 0);
                                _focusedIndex = -1;
                                HelpPanelBorder.Focus();
                            }, DispatcherPriority.Render);
                        }
                        else
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                HelpPanelBorder.Opacity = 0;
                                HelpPanelBorder.RenderTransform = new TranslateTransform(0, -8);
                            }, DispatcherPriority.Background);
                        }
                    }
                };
                vm.PropertyChanged += _propertyChangedHandler;
            }
            else
            {
                _previousVm = null;
                _propertyChangedHandler = null;
            }
        };
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is HelpPanelViewModel vm)
        {
            vm.CloseCommand.Execute(null);
        }
    }

    private void HelpPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HelpPanelViewModel vm) return;

        switch (e.Key)
        {
            case Key.Up:
                NavigateUp();
                e.Handled = true;
                break;
            case Key.Down:
                NavigateDown();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                Helpers.MenuItemNavigation.Activate(this, _focusedIndex);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Tab:
                ModalHelper.LeavePanelOnTab(e, _opener, () => vm.CloseCommand.Execute(null));
                break;
        }
    }

    private void NavigateUp()
    {
        if (_focusedIndex <= 0)
            _focusedIndex = _menuItemCount - 1;
        else
            _focusedIndex--;
        Helpers.MenuItemNavigation.Focus(this, _focusedIndex);
    }

    private void NavigateDown()
    {
        if (_focusedIndex >= _menuItemCount - 1)
            _focusedIndex = 0;
        else
            _focusedIndex++;
        Helpers.MenuItemNavigation.Focus(this, _focusedIndex);
    }
}
