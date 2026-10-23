using Avalonia.Controls;

namespace ArgoBooks.Views;

/// <summary>
/// Minimal window shown immediately at startup so the app is visibly running while the
/// service graph is built.
///
/// Without it nothing appears until <c>OnFrameworkInitializationCompleted</c> finishes, which
/// takes several seconds. Users read that as a failed launch and click the shortcut again,
/// producing several concurrent instances.
///
/// Deliberately has no DataContext, no bindings and no theme-resource lookups, so it cannot
/// depend on anything that hasn't been constructed yet.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Utilities.AppWindowIcon.Apply(this);
    }
}
