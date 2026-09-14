using ArgoBooks.Controls;
using ArgoBooks.Core.Services;
using Avalonia.Threading;

namespace ArgoBooks.Services;

/// <summary>
/// Implementation of the message box service using ModalOverlay.
/// </summary>
public class MessageBoxService : IMessageBoxService
{
    private ModalOverlay? _overlay;
    private TaskCompletionSource<MessageBoxResult>? _resultTcs;

    /// <summary>
    /// Sets the modal overlay control that this service uses to display message boxes.
    /// </summary>
    /// <param name="overlay">The modal overlay control.</param>
    public void SetOverlay(ModalOverlay overlay)
    {
        _overlay = overlay;
    }

    /// <inheritdoc />
    public async Task<MessageBoxResult> ShowAsync(MessageBoxOptions options)
    {
        if (_overlay == null)
            throw new InvalidOperationException("Modal overlay has not been set. Call SetOverlay first.");

        _resultTcs = new TaskCompletionSource<MessageBoxResult>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var messageBox = new MessageBox
            {
                Title = options.Title,
                Message = options.Message,
                MessageType = options.Type,
                Buttons = options.Buttons,
                ShowIcon = options.ShowIcon
            };

            // Override button text if specified
            if (!string.IsNullOrEmpty(options.PrimaryButtonText))
                messageBox.PrimaryButtonText = options.PrimaryButtonText;
            if (!string.IsNullOrEmpty(options.SecondaryButtonText))
                messageBox.SecondaryButtonText = options.SecondaryButtonText;

            messageBox.ResultSelected += OnResultSelected;

            _overlay.ModalContent = messageBox;
            _overlay.CloseOnBackdropClick = false;
            _overlay.CloseOnEscape = true;
            _overlay.Closed += OnOverlayClosed;
            _overlay.IsOpen = true;
        });

        return await _resultTcs.Task;
    }

    /// <inheritdoc />
    public Task<MessageBoxResult> ShowInfoAsync(string title, string message)
    {
        return ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Info,
            Buttons = MessageBoxButtons.Ok
        });
    }

    /// <inheritdoc />
    public Task<MessageBoxResult> ShowSuccessAsync(string title, string message)
    {
        return ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Success,
            Buttons = MessageBoxButtons.Ok
        });
    }

    /// <inheritdoc />
    public Task<MessageBoxResult> ShowWarningAsync(string title, string message)
    {
        return ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Warning,
            Buttons = MessageBoxButtons.Ok
        });
    }

    /// <inheritdoc />
    public Task<MessageBoxResult> ShowErrorAsync(string title, string message)
    {
        return ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Error,
            Buttons = MessageBoxButtons.Ok
        });
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = await ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Question,
            Buttons = MessageBoxButtons.YesNo
        });

        return result == MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        var result = await ShowAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Type = MessageBoxType.Question,
            Buttons = MessageBoxButtons.OkCancel,
            PrimaryButtonText = confirmText,
            SecondaryButtonText = cancelText
        });

        return result == MessageBoxResult.Ok;
    }

    private void OnResultSelected(object? sender, MessageBoxResultEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_overlay != null)
            {
                _overlay.Closed -= OnOverlayClosed;
                _overlay.IsOpen = false;
            }

            if (sender is MessageBox messageBox)
            {
                messageBox.ResultSelected -= OnResultSelected;
            }

            _resultTcs?.TrySetResult(e.Result);
        });
    }

    private void OnOverlayClosed(object? sender, EventArgs e)
    {
        if (_overlay != null)
        {
            _overlay.Closed -= OnOverlayClosed;
        }

        // If closed via escape or other means, return None/Cancel
        _resultTcs?.TrySetResult(MessageBoxResult.Cancel);
    }
}
