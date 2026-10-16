using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ArgoBooks.Services;

namespace ArgoBooks.Localization;

/// <summary>
/// XAML markup extension for localization.
/// Usage: {loc:Loc Key} or {loc:Loc 'Some English text'}
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public LocExtension(string part1, string part2)
    {
        Key = $"{part1}'{part2}";
    }

    public LocExtension(string part1, string part2, string part3)
    {
        Key = $"{part1}'{part2}'{part3}";
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return string.Empty;

        // Get the target object and property
        var provideTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

        if (provideTarget?.TargetObject is AvaloniaObject targetObject &&
            provideTarget.TargetProperty is AvaloniaProperty targetProperty)
        {
            // Register this binding for refresh on language change
            LocalizationManager.Register(targetObject, targetProperty, Key);

            // Return the current translation
            return LanguageService.Instance.Translate(Key);
        }

        // Fallback: return translated value directly
        return LanguageService.Instance.Translate(Key);
    }
}

/// <summary>
/// Manages localization bindings and refreshes them when language changes.
/// </summary>
public static class LocalizationManager
{
    // Store bindings directly (not weak references to bindings!)
    // Only the Target inside each binding is a weak reference
    private static readonly List<LocalizationBinding> Bindings = [];
    private static bool _initialized;

    private class LocalizationBinding
    {
        public WeakReference<AvaloniaObject> Target { get; init; } = null!;
        public AvaloniaProperty Property { get; init; } = null!;
        public string Key { get; init; } = null!;
    }

    public static void Register(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        EnsureInitialized();

        lock (Bindings)
        {
            Bindings.Add(new LocalizationBinding
            {
                Target = new WeakReference<AvaloniaObject>(target),
                Property = property,
                Key = key
            });
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
        _initialized = true;
    }

    private static void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshAllBindings, DispatcherPriority.Normal);
    }

    private static void RefreshAllBindings()
    {
        List<LocalizationBinding> validBindings = [];
        List<LocalizationBinding> deadBindings = [];

        lock (Bindings)
        {
            // Collect valid bindings and identify dead ones
            foreach (var binding in Bindings)
            {
                if (binding.Target.TryGetTarget(out _))
                {
                    validBindings.Add(binding);
                }
                else
                {
                    deadBindings.Add(binding);
                }
            }

            // Remove dead bindings (targets have been garbage collected)
            foreach (var dead in deadBindings)
            {
                Bindings.Remove(dead);
            }
        }

        // Update all valid bindings
        foreach (var binding in validBindings)
        {
            if (binding.Target.TryGetTarget(out var target))
            {
                var translated = LanguageService.Instance.Translate(binding.Key);
                target.SetValue(binding.Property, translated);
            }
        }

    }
}
