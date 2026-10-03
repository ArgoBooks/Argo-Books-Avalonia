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
}

/// <summary>
/// Stands in front of the file pickers that used to be the first thing an import
/// did. Opening an operating system dialog over an empty company tells somebody
/// nothing about which file to go and find, and the ones who had not downloaded a
/// statement yet could only cancel.
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

    private TaskCompletionSource<bool>? _completion;

    /// <summary>Returns true when the person wants to carry on to the file picker.</summary>
    public Task<bool> ShowAsync(ImportFilePromptOptions options)
    {
        // An import cannot start while another is on screen, so a queue would never
        // be used; replacing a live prompt would silently strand its caller instead.
        if (_completion != null)
        {
            return Task.FromResult(false);
        }

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
        IsOpen = false;
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(proceed);
    }
}
