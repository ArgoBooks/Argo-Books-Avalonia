using System.ComponentModel;
using ArgoBooks.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal dialog for editing company information and logo.
/// </summary>
public partial class EditCompanyModal : UserControl
{
    private EditCompanyModalViewModel? _viewModel;

    public EditCompanyModal()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as EditCompanyModalViewModel;
        if (_viewModel != null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditCompanyModalViewModel.IsEditingOpeningBalance) || _viewModel?.IsEditingOpeningBalance != true)
            return;

        // The boxes open at the bottom of the modal, so they are scrolled to once laid out rather than left below the fold.
        Dispatcher.UIThread.Post(() =>
        {
            BodyScrollViewer.ScrollToEnd();
            OpeningBalanceBox.Focus();
            OpeningBalanceBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
