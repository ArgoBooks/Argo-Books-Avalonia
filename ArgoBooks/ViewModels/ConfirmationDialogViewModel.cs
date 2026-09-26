using ArgoBooks.Core.Enums;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>The icon beside a dialog's message, for a notice rather than a question.</summary>
public enum DialogIcon
{
    None,
    Info,
    Warning,
    Error
}

/// <summary>
/// Configuration for a confirmation dialog.
/// </summary>
public class ConfirmationDialogOptions
{
    public string Title { get; set; } = "Confirm";
    public DialogIcon Icon { get; set; }
    public string Message { get; set; } = "";
    public string? PrimaryButtonText { get; set; } = "OK";
    public string? SecondaryButtonText { get; set; }
    public string? CancelButtonText { get; set; } = "Cancel";
    public bool IsPrimaryDestructive { get; set; }
    public bool IsSecondaryDestructive { get; set; }

    /// <summary>False for notices and prompts a stray click must not dismiss; only a button or Escape closes them.</summary>
    public bool CloseOnBackdropClick { get; set; } = true;
}

/// <summary>
/// ViewModel for the app's one modal dialog: questions, and notices (info, warning, error).
/// A dialog asked for while another is open waits its turn, so neither caller is left waiting forever.
/// </summary>
public partial class ConfirmationDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "Confirm".Translate();

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _primaryButtonText = "OK".Translate();

    [ObservableProperty]
    private string _secondaryButtonText = "";

    [ObservableProperty]
    private string _cancelButtonText = "Cancel".Translate();

    [ObservableProperty]
    private bool _showPrimaryButton = true;

    [ObservableProperty]
    private bool _showSecondaryButton;

    [ObservableProperty]
    private bool _showCancelButton = true;

    [ObservableProperty]
    private bool _isPrimaryDestructive;

    [ObservableProperty]
    private bool _isSecondaryDestructive;

    [ObservableProperty]
    private bool _closeOnBackdropClick = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IsInfoIcon), nameof(IsWarningIcon), nameof(IsErrorIcon))]
    private DialogIcon _icon;

    public bool HasIcon => Icon != DialogIcon.None;
    public bool IsInfoIcon => Icon == DialogIcon.Info;
    public bool IsWarningIcon => Icon == DialogIcon.Warning;
    public bool IsErrorIcon => Icon == DialogIcon.Error;

    private TaskCompletionSource<ConfirmationResult>? _completionSource;
    private readonly Queue<(ConfirmationDialogOptions Options, TaskCompletionSource<ConfirmationResult> Completion)> _waiting = new();

    /// <summary>
    /// Shows the confirmation dialog with the specified options.
    /// </summary>
    /// <param name="options">Dialog configuration options.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    public Task<ConfirmationResult> ShowAsync(ConfirmationDialogOptions options)
    {
        var completion = new TaskCompletionSource<ConfirmationResult>();
        if (_completionSource != null)
            _waiting.Enqueue((options, completion));
        else
            Present(options, completion);
        return completion.Task;
    }

    private void Present(ConfirmationDialogOptions options, TaskCompletionSource<ConfirmationResult> completion)
    {
        _completionSource = completion;
        Title = options.Title;
        Icon = options.Icon;
        Message = options.Message;
        PrimaryButtonText = options.PrimaryButtonText ?? "OK".Translate();
        SecondaryButtonText = options.SecondaryButtonText ?? "";
        CancelButtonText = options.CancelButtonText ?? "Cancel".Translate();
        ShowPrimaryButton = !string.IsNullOrEmpty(options.PrimaryButtonText);
        ShowSecondaryButton = !string.IsNullOrEmpty(options.SecondaryButtonText);
        ShowCancelButton = !string.IsNullOrEmpty(options.CancelButtonText);
        IsPrimaryDestructive = options.IsPrimaryDestructive;
        IsSecondaryDestructive = options.IsSecondaryDestructive;
        CloseOnBackdropClick = options.CloseOnBackdropClick;

        IsOpen = true;
    }

    /// <summary>
    /// Shows a notice with a single OK button.
    /// </summary>
    public Task<ConfirmationResult> ShowNoticeAsync(DialogIcon icon, string title, string message) =>
        ShowAsync(new ConfirmationDialogOptions
        {
            Icon = icon,
            Title = title,
            Message = message,
            PrimaryButtonText = "OK".Translate(),
            CancelButtonText = null,
            CloseOnBackdropClick = false
        });

    /// <summary>
    /// Shows a simple confirmation dialog.
    /// </summary>
    public Task<ConfirmationResult> ShowAsync(string title, string message,
        string? primaryButton = "OK", string? cancelButton = "Cancel")
    {
        return ShowAsync(new ConfirmationDialogOptions
        {
            Title = title,
            Message = message,
            PrimaryButtonText = primaryButton,
            CancelButtonText = cancelButton
        });
    }

    [RelayCommand]
    private void PrimaryAction() => Finish(ConfirmationResult.Primary);

    [RelayCommand]
    private void SecondaryAction() => Finish(ConfirmationResult.Secondary);

    [RelayCommand]
    private void CancelAction() => Finish(ConfirmationResult.Cancel);

    /// <summary>
    /// Closes the dialog without a result (e.g., when clicking backdrop).
    /// </summary>
    public void Close() => Finish(ConfirmationResult.None);

    private void Finish(ConfirmationResult result)
    {
        IsOpen = false;
        var completion = _completionSource;
        _completionSource = null;
        completion?.TrySetResult(result);

        // The caller's continuation may already have shown the next dialog.
        if (_completionSource == null && _waiting.TryDequeue(out var next))
            Present(next.Options, next.Completion);
    }
}
