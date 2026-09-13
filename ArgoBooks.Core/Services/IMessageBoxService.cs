namespace ArgoBooks.Core.Services;

/// <summary>
/// Type of message box which determines the icon and primary button style.
/// </summary>
public enum MessageBoxType
{
    /// <summary>
    /// Information message.
    /// </summary>
    Info,

    /// <summary>
    /// Success message.
    /// </summary>
    Success,

    /// <summary>
    /// Warning message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error message.
    /// </summary>
    Error,

    /// <summary>
    /// Question/confirmation message.
    /// </summary>
    Question
}

/// <summary>
/// Button configurations for message boxes.
/// </summary>
public enum MessageBoxButtons
{
    /// <summary>
    /// Single OK button.
    /// </summary>
    Ok,

    /// <summary>
    /// OK and Cancel buttons.
    /// </summary>
    OkCancel,

    /// <summary>
    /// Yes and No buttons.
    /// </summary>
    YesNo,

    /// <summary>
    /// Yes, No, and Cancel buttons.
    /// </summary>
    YesNoCancel
}

/// <summary>
/// Result from a message box dialog.
/// </summary>
public enum MessageBoxResult
{
    /// <summary>
    /// No result (dialog was closed without selection).
    /// </summary>
    None,

    /// <summary>
    /// OK button was clicked.
    /// </summary>
    Ok,

    /// <summary>
    /// Cancel button was clicked.
    /// </summary>
    Cancel,

    /// <summary>
    /// Yes button was clicked.
    /// </summary>
    Yes,

    /// <summary>
    /// No button was clicked.
    /// </summary>
    No
}

/// <summary>
/// Options for configuring a message box.
/// </summary>
public class MessageBoxOptions
{
    /// <summary>
    /// Gets or sets the message box title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the message content.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the message type (determines icon and styling).
    /// </summary>
    public MessageBoxType Type { get; set; } = MessageBoxType.Info;

    /// <summary>
    /// Gets or sets the button configuration.
    /// </summary>
    public MessageBoxButtons Buttons { get; set; } = MessageBoxButtons.Ok;

    public bool ShowIcon { get; set; } = true;

    /// <summary>
    /// Gets or sets custom primary button text (overrides default).
    /// </summary>
    public string? PrimaryButtonText { get; set; }

    /// <summary>
    /// Gets or sets custom secondary button text (overrides default).
    /// </summary>
    public string? SecondaryButtonText { get; set; }
}

/// <summary>
/// Service for displaying message box dialogs.
/// </summary>
public interface IMessageBoxService
{
    /// <summary>
    /// Shows a message box with the specified options.
    /// </summary>
    /// <param name="options">Message box configuration.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    Task<MessageBoxResult> ShowAsync(MessageBoxOptions options);

    /// <summary>
    /// Shows an error message box.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Message content.</param>
    /// <returns>The result (usually Ok).</returns>
    Task<MessageBoxResult> ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Message content.</param>
    /// <returns>True if Yes was clicked, false otherwise.</returns>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog with custom button text.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Message content.</param>
    /// <param name="confirmText">Text for confirm button.</param>
    /// <param name="cancelText">Text for cancel button.</param>
    /// <returns>True if confirm was clicked, false otherwise.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
