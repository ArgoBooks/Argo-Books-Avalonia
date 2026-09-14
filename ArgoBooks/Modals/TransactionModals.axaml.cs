using Avalonia.Controls;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal dialogs for creating, editing and filtering expense and revenue records.
/// </summary>
public partial class TransactionModals : UserControl
{
    public TransactionModals()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ITransactionModalsViewModel? _previousViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousViewModel != null)
        {
            _previousViewModel.ScrollToLineItemsRequested -= OnScrollToLineItemsRequested;
            _previousViewModel = null;
        }

        if (DataContext is ITransactionModalsViewModel vm)
        {
            vm.ScrollToLineItemsRequested += OnScrollToLineItemsRequested;
            _previousViewModel = vm;
        }
    }

    private void OnScrollToLineItemsRequested(object? sender, EventArgs e)
    {
        LineItemsSection?.BringIntoView();
    }
}
