using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Configuration for showing a modal.
/// </summary>
public class ModalOptions
{
    /// <summary>
    /// Gets or sets the modal title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the modal subtitle.
    /// </summary>
    public string? Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the modal size preset.
    /// </summary>
    public ModalSize Size { get; set; } = ModalSize.Medium;

    /// <summary>
    /// Gets or sets whether clicking the backdrop closes the modal.
    /// </summary>
    public bool CloseOnBackdropClick { get; set; } = true;

    /// <summary>
    /// Gets or sets whether pressing Escape closes the modal.
    /// </summary>
    public bool CloseOnEscape { get; set; } = true;

    public bool ShowCloseButton { get; set; } = true;

    public string? PrimaryButtonText { get; set; }

    public string? SecondaryButtonText { get; set; }
}

/// <summary>
/// Service for displaying modal dialogs.
/// </summary>
public interface IModalService
{
    /// <summary>
    /// Gets whether a modal is currently open.
    /// </summary>
    bool IsModalOpen { get; }

    /// <summary>
    /// Shows a modal with the specified content.
    /// </summary>
    /// <typeparam name="TContent">Type of the content/view model.</typeparam>
    /// <param name="content">The content to display.</param>
    /// <param name="options">Modal options.</param>
    /// <returns>The modal result.</returns>
    Task<ModalResult> ShowAsync<TContent>(TContent content, ModalOptions? options = null) where TContent : class;

    /// <summary>
    /// Shows a modal with the specified content and returns a result value.
    /// </summary>
    /// <typeparam name="TContent">Type of the content/view model.</typeparam>
    /// <typeparam name="TResult">Type of the result value.</typeparam>
    /// <param name="content">The content to display.</param>
    /// <param name="options">Modal options.</param>
    /// <returns>The result value or default if cancelled.</returns>
    Task<TResult?> ShowAsync<TContent, TResult>(TContent content, ModalOptions? options = null) where TContent : class;

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog message.</param>
    /// <param name="confirmText">Confirm button text.</param>
    /// <param name="cancelText">Cancel button text.</param>
    /// <returns>True if confirmed, false otherwise.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel");

    /// <summary>
    /// Shows an alert dialog.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog message.</param>
    /// <param name="buttonText">Button text.</param>
    Task AlertAsync(string title, string message, string buttonText = "OK");

    /// <summary>
    /// Closes the current modal with the specified result.
    /// </summary>
    /// <param name="result">The modal result.</param>
    void Close(ModalResult result = ModalResult.None);

    /// <summary>
    /// Closes the current modal with a result value.
    /// </summary>
    /// <typeparam name="TResult">Type of the result value.</typeparam>
    /// <param name="result">The result value.</param>
    void Close<TResult>(TResult result);

    /// <summary>
    /// Event raised when a modal is opened.
    /// </summary>
    event EventHandler<object>? ModalOpened;
}
