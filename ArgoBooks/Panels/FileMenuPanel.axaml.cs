using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Panels;

/// <summary>
/// Dropdown panel for file operations including open, save, and recent files.
/// </summary>
public partial class FileMenuPanel : UserControl
{
    private int _focusedIndex = -1;
    private readonly int _menuItemCount = 10; // Total menu items (0-9)
    private readonly int _submenuItemCount = 4; // Submenu items (0-3)
    private int _submenuFocusedIndex = -1;
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
                            // Animate in
                            Dispatcher.UIThread.Post(() =>
                            {
                                FileMenuBorder.Opacity = 1;
                                FileMenuBorder.RenderTransform = new TranslateTransform(0, 0);
                                _focusedIndex = -1;
                                _submenuFocusedIndex = -1;
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
                    ActivateCurrentItem();
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
        }
    }

    private void EnterSubmenu()
    {
        if (DataContext is FileMenuPanelViewModel vm)
        {
            vm.IsRecentSubmenuOpen = true;
            _isInSubmenu = true;
            _submenuFocusedIndex = 0;
            FocusSubmenuItem(0);
        }
    }

    private void ExitSubmenu()
    {
        _isInSubmenu = false;
        _submenuFocusedIndex = -1;
        FocusMenuItem(_focusedIndex);
    }

    private void NavigateSubmenuUp()
    {
        if (_submenuFocusedIndex <= 0)
            _submenuFocusedIndex = _submenuItemCount - 1;
        else
            _submenuFocusedIndex--;
        FocusSubmenuItem(_submenuFocusedIndex);
    }

    private void NavigateSubmenuDown()
    {
        if (_submenuFocusedIndex >= _submenuItemCount - 1)
            _submenuFocusedIndex = 0;
        else
            _submenuFocusedIndex++;
        FocusSubmenuItem(_submenuFocusedIndex);
    }

    private void FocusSubmenuItem(int index)
    {
        var menuItem = this.FindControl<Button>($"SubmenuItem{index}");
        menuItem?.Focus();
    }

    private void ActivateCurrentSubmenuItem()
    {
        var menuItem = this.FindControl<Button>($"SubmenuItem{_submenuFocusedIndex}");
        if (menuItem?.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
        {
            menuItem.Command.Execute(menuItem.CommandParameter);
        }
    }

    private void NavigateUp()
    {
        if (_focusedIndex <= 0)
            _focusedIndex = _menuItemCount - 1;
        else
            _focusedIndex--;
        FocusMenuItem(_focusedIndex);
        CheckOpenRecentSubmenu();
    }

    private void NavigateDown()
    {
        if (_focusedIndex >= _menuItemCount - 1)
            _focusedIndex = 0;
        else
            _focusedIndex++;
        FocusMenuItem(_focusedIndex);
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

    private void FocusMenuItem(int index)
    {
        var menuItem = this.FindControl<Button>($"MenuItem{index}");
        menuItem?.Focus();
    }

    private void ActivateCurrentItem()
    {
        var menuItem = this.FindControl<Button>($"MenuItem{_focusedIndex}");
        if (menuItem?.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
        {
            menuItem.Command.Execute(menuItem.CommandParameter);
        }
    }
}
