using Avalonia.Controls;
using Avalonia.Platform;

namespace ArgoBooks.Utilities;

/// <summary>The icon the app's windows show in the Windows taskbar and title bar.</summary>
public static class AppWindowIcon
{
    // The .ico holds the logo drawn at each taskbar size, which Windows picks from; shrinking the 1024px PNG to 24px left it jagged.
    public static void Apply(Window window)
    {
        if (OperatingSystem.IsWindows())
            window.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ArgoBooks/Assets/argo-logo.ico")));
    }
}
