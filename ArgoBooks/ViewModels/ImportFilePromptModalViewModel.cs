using System.Collections.ObjectModel;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>What an import prompt says before the file picker opens.</summary>
public sealed class ImportFilePromptOptions
{
    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>What to fetch and where, one line each.</summary>
    public List<string> Points { get; init; } = [];

    public string ChooseButtonText { get; init; } = "";

    /// <summary>Names the prompt, so "Don't show again" can be remembered for it.</summary>
    public string Key { get; init; } = "";
}

/// <summary>
/// Stands in front of the file pickers, because an operating system dialog over an empty
/// company says nothing about which file to find, and somebody without one can only cancel.
/// </summary>
public partial class ImportFilePromptModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _chooseButtonText = "";

    public ObservableCollection<string> Points { get; } = [];

    [ObservableProperty]
    private bool _dontShowAgain = true;

    public string DontShowAgainLabel => "Don't show again".Translate();

    private TaskCompletionSource<bool>? _completion;
    private string _key = "";

    /// <summary>Returns true when the person wants to carry on to the file picker.</summary>
    public Task<bool> ShowAsync(ImportFilePromptOptions options)
    {
        // An import cannot start while another is on screen, so a queue would never
        // be used; replacing a live prompt would silently strand its caller instead.
        if (_completion != null)
        {
            return Task.FromResult(false);
        }

        if (options.Key.Length > 0 && App.SettingsService?.GlobalSettings.HiddenImportPrompts.Contains(options.Key) == true)
        {
            return Task.FromResult(true);
        }

        _key = options.Key;
        DontShowAgain = true;
        Title = options.Title;
        Description = options.Description;
        ChooseButtonText = options.ChooseButtonText.Length > 0
            ? options.ChooseButtonText
            : "Choose file".Translate();

        Points.Clear();
        foreach (var point in options.Points)
        {
            Points.Add(point);
        }

        _completion = new TaskCompletionSource<bool>();
        IsOpen = true;
        return _completion.Task;
    }

    [RelayCommand]
    private void Choose() => Complete(true);

    [RelayCommand]
    private void Cancel() => Complete(false);

    private void Complete(bool proceed)
    {
        // Only going on to the file remembers the choice, so a prompt cancelled before it was read comes back.
        if (proceed && DontShowAgain && _key.Length > 0 && App.SettingsService is { } settings
            && !settings.GlobalSettings.HiddenImportPrompts.Contains(_key))
        {
            settings.GlobalSettings.HiddenImportPrompts.Add(_key);
            _ = settings.SaveGlobalSettingsAsync();
        }

        IsOpen = false;
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(proceed);
    }
}
