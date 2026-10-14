using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Panels;

/// <summary>
/// Dropdown panel for switching between recent company files.
/// </summary>
public partial class CompanySwitcherPanel : UserControl
{
    private CompanySwitcherPanelViewModel? _previousVm;
    private PropertyChangedEventHandler? _propertyChangedHandler;
    private IInputElement? _opener;

    public CompanySwitcherPanel()
    {
        InitializeComponent();

        // Animate and focus the panel when it opens
        DataContextChanged += (_, _) =>
        {
            // Unsubscribe from previous ViewModel
            if (_previousVm != null && _propertyChangedHandler != null)
            {
                _previousVm.PropertyChanged -= _propertyChangedHandler;
            }

            if (DataContext is CompanySwitcherPanelViewModel vm)
            {
                _previousVm = vm;
                _propertyChangedHandler = (_, e) =>
                {
                    if (e.PropertyName == nameof(CompanySwitcherPanelViewModel.IsOpen))
                    {
                        if (vm.IsOpen)
                        {
                            _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                            // Animate in
                            Dispatcher.UIThread.Post(() =>
                            {
                                SwitcherBorder.Opacity = 1;
                                SwitcherBorder.RenderTransform = new TranslateTransform(0, 0);
                                SwitcherBorder.Focus();
                            }, DispatcherPriority.Render);
                        }
                        else
                        {
                            // Reset for next open
                            Dispatcher.UIThread.Post(() =>
                            {
                                SwitcherBorder.Opacity = 0;
                                SwitcherBorder.RenderTransform = new TranslateTransform(0, -8);
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
        if (DataContext is CompanySwitcherPanelViewModel vm)
        {
            vm.CloseCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles keyboard navigation in the panel.
    /// </summary>
    private void Panel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CompanySwitcherPanelViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Tab:
                ModalHelper.LeavePanelOnTab(e, _opener, () => vm.CloseCommand.Execute(null));
                break;
        }
    }
}
