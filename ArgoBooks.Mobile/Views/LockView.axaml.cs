using Avalonia.Controls;
using Avalonia.Interactivity;
using ArgoBooks.Mobile.ViewModels;

namespace ArgoBooks.Mobile.Views;

public partial class LockView : UserControl
{
    public LockView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Prompt immediately when the lock screen appears, so the user isn't forced to tap Unlock first on the common path. The button stays available to retry after a cancellation/failure.
        if (DataContext is LockViewModel vm)
        {
            await vm.TryUnlockAsync();
        }
    }
}
