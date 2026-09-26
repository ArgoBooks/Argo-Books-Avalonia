using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels;

/// <summary>
/// One API key as shown in Settings.
///
/// Carries the hint (ab_1a2b...wxyz) rather than the secret, because the secret
/// is only ever in memory once, at creation. Everything here is safe to display
/// and safe to put in a screenshot on a support ticket.
/// </summary>
public partial class ArgoApiKeyRow : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    public string Hint { get; init; } = string.Empty;

    /// <summary>
    /// The name the merchant gave this key. Settable rather than init-only
    /// because renaming edits the row in place instead of reloading the list,
    /// which would otherwise scroll them back to the top mid-edit.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _label = string.Empty;

    public string Scopes { get; init; } = string.Empty;

    /// <summary>Pre-formatted for display, so the row has no date-formatting logic of its own.</summary>
    public string LastUsedDisplay { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isRevoked;

    /// <summary>Swaps the row between its display and its rename form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    /// <summary>
    /// The in-progress name, kept apart from <see cref="Label"/> so cancelling
    /// leaves the row exactly as it was rather than half-renamed.
    /// </summary>
    [ObservableProperty]
    private string _editLabel = string.Empty;

    /// <summary>What the row shows when it has a name, falling back to the hint.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Hint : Label;
}
