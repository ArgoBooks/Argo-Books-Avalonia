namespace ArgoBooks.Core.Enums;

/// <summary>
/// Result of a confirmation dialog.
/// </summary>
public enum ConfirmationResult
{
    /// <summary>
    /// Dialog was closed without a result (e.g., backdrop click).
    /// </summary>
    None,

    /// <summary>
    /// Primary action was selected (e.g., "Save", "Yes", "OK", "Delete").
    /// </summary>
    Primary,

    /// <summary>
    /// Secondary action was selected (e.g., "Don't Save", "No").
    /// </summary>
    Secondary,

    /// <summary>
    /// Cancel action was selected.
    /// </summary>
    Cancel
}
