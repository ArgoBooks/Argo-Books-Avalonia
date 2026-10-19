using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Panels;

/// <summary>
/// Dropdown panel for file operations including open, save, and recent files.
/// </summary>
public partial class FileMenuPanel : UserControl
{
    private int _focusedIndex = -1;
    private readonly int _menuItemCount = 10; // Total menu items (0-9)
    private IInputElement? _opener;
    private bool _isInSubmenu;
    private bool _isOverSubmenu;
    private bool _isOverRecentButton;
    private FileMenuPanelViewModel? _previousVm;
    private PropertyChangedEventHandler? _propertyChangedHandler;

    public FileMenuPanel()
    {
        InitializeComponent();

        // Animate and focus the menu when it opens
        DataContextChanged += (_, _) =>
        {
            // Unsubscribe from previous ViewModel to prevent leak
            if (_previousVm != null && _propertyChangedHandler != null)
            {
                _previousVm.PropertyChanged -= _propertyChangedHandler;
            }

            if (DataContext is FileMenuPanelViewModel vm)
            {
                _previousVm = vm;
                _propertyChangedHandler = (_, e) =>
                {
                    if (e.PropertyName == nameof(FileMenuPanelViewModel.IsOpen))
                    {
                        if (vm.IsOpen)
                        {
                            _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                            // Animate in
                            Dispatcher.UIThread.Post(() =>
                            {
                                FileMenuBorder.Opacity = 1;
                                FileMenuBorder.RenderTransform = new TranslateTransform(0, 0);
                                _focusedIndex = -1;
                                _isInSubmenu = false;
                                FileMenuBorder.Focus();
                            }, DispatcherPriority.Render);
                        }
                        else
                        {
                            // Reset for next open
                            Dispatcher.UIThread.Post(() =>
                            {
                                FileMenuBorder.Opacity = 0;
                                FileMenuBorder.RenderTransform = new TranslateTransform(0, -8);
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

    /// <summary>
    /// Closes the panel when clicking on the backdrop.
    /// </summary>
    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FileMenuPanelViewModel vm)
        {
            vm.IsRecentSubmenuOpen = false;
            vm.CloseCommand.Execute(null);
        }
    }

    /// <summary>
    /// Shows the recent submenu on hover.
    /// </summary>
    private void OpenRecent_PointerEntered(object? sender, PointerEventArgs e)
    {
        _isOverRecentButton = true;
        if (DataContext is FileMenuPanelViewModel vm)
        {
            vm.IsRecentSubmenuOpen = true;
        }
    }

    /// <summary>
    /// Hides the recent submenu when leaving with delay.
    /// </summary>
    private void OpenRecent_PointerExited(object? sender, PointerEventArgs e)
    {
        _isOverRecentButton = false;
        DelayedCloseSubmenu();
    }

    /// <summary>
    /// Keeps the recent submenu open when hovering over it.
    /// </summary>
    private void RecentSubmenu_PointerEntered(object? sender, PointerEventArgs e)
    {
        _isOverSubmenu = true;
    }

    /// <summary>
    /// Hides the recent submenu when leaving.
    /// </summary>
    private void RecentSubmenu_PointerExited(object? sender, PointerEventArgs e)
    {
        _isOverSubmenu = false;
        DelayedCloseSubmenu();
    }

    private async void DelayedCloseSubmenu()
    {
        try
        {
            await Task.Delay(100);
            if (!_isOverSubmenu && !_isOverRecentButton)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext is FileMenuPanelViewModel vm)
                    {
                        vm.IsRecentSubmenuOpen = false;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "DelayedCloseSubmenu");
        }
    }

    /// <summary>
    /// Handles keyboard navigation in the file menu.
    /// </summary>
    private void FileMenu_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileMenuPanelViewModel vm) return;

        SyncWithFocus();

        switch (e.Key)
        {
            case Key.Up:
                if (_isInSubmenu)
                    NavigateSubmenuUp();
                else
                    NavigateUp();
                e.Handled = true;
                break;
            case Key.Down:
                if (_isInSubmenu)
                    NavigateSubmenuDown();
                else
                    NavigateDown();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (_isInSubmenu)
                    ActivateCurrentSubmenuItem();
                else
                    Helpers.MenuItemNavigation.Activate(this, _focusedIndex);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isInSubmenu)
                {
                    ExitSubmenu();
                }
                else
                {
                    vm.IsRecentSubmenuOpen = false;
                    vm.CloseCommand.Execute(null);
                }
                e.Handled = true;
                break;
            case Key.Right:
                if (_focusedIndex == 2 && !_isInSubmenu) // Open Recent
                {
                    EnterSubmenu();
                }
                e.Handled = true;
                break;
            case Key.Left:
                if (_isInSubmenu)
                {
                    ExitSubmenu();
                }
                e.Handled = true;
                break;
            case Key.Tab:
                ModalHelper.LeavePanelOnTab(e, _opener, () =>
                {
                    vm.IsRecentSubmenuOpen = false;
                    vm.CloseCommand.Execute(null);
                });
                break;
        }
    }

    /// <summary>Picks up the item that has focus, since Tab and the mouse move it without the arrow keys knowing.</summary>
    private void SyncWithFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        _isInSubmenu = focused is Avalonia.Visual visual && RecentSubmenu.IsVisualAncestorOf(visual);
        if (focused is Button { Name: { } name } && name.StartsWith("MenuItem") && int.TryParse(name["MenuItem".Length..], out var index))
            _focusedIndex = index;
    }

    private void EnterSubmenu()
    {
        if (DataContext is not FileMenuPanelViewModel vm)
            return;

        vm.IsRecentSubmenuOpen = true;
        // Posted, because the list has only just been shown and its buttons cannot take focus before they are laid out.
        Dispatcher.UIThread.Post(() =>
        {
            if (ModalHelper.VisibleButtons(RecentSubmenu).FirstOrDefault() is { } first && first.Focus(NavigationMethod.Directional))
                _isInSubmenu = true;
        }, DispatcherPriority.Loaded);
    }

    private void ExitSubmenu()
    {
        _isInSubmenu = false;
        _focusedIndex = 2; // Open Recent
        Helpers.MenuItemNavigation.Focus(this, _focusedIndex);
    }

    private void NavigateSubmenuUp() => ModalHelper.StepThroughButtons(RecentSubmenu, -1);

    private void NavigateSubmenuDown() => ModalHelper.StepThroughButtons(RecentSubmenu, 1);

    private void ActivateCurrentSubmenuItem()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button { Command: { } command } button
            && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    private void NavigateUp()
    {
        if (_focusedIndex <= 0)
            _focusedIndex = _menuItemCount - 1;
        else
            _focusedIndex--;
        Helpers.MenuItemNavigation.Focus(this, _focusedIndex);
        CheckOpenRecentSubmenu();
    }

    private void NavigateDown()
    {
        if (_focusedIndex >= _menuItemCount - 1)
            _focusedIndex = 0;
        else
            _focusedIndex++;
        Helpers.MenuItemNavigation.Focus(this, _focusedIndex);
        CheckOpenRecentSubmenu();
    }

    private void CheckOpenRecentSubmenu()
    {
        if (DataContext is FileMenuPanelViewModel vm)
        {
            // Auto-open submenu when navigating to Open Recent (index 2)
            vm.IsRecentSubmenuOpen = _focusedIndex == 2;
        }
    }
}
