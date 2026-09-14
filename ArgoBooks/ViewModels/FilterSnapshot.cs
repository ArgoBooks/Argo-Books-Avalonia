namespace ArgoBooks.ViewModels;

/// <summary>
/// A filter modal's values as they stood when it opened. The filter properties change live while
/// the modal is open, so leaving it without applying has to put them back, or the page later
/// filters by a choice the user cancelled.
/// </summary>
/// <typeparam name="T">A record of the modal's filter values, compared by value.</typeparam>
public sealed class FilterSnapshot<T>(T defaults, Func<T> read, Action<T> write) where T : notnull
{
    private T _original = defaults;

    /// <summary>The values Clear resets to.</summary>
    public T Default { get; } = defaults;

    public T Current => read();

    public bool HasChanges => !EqualityComparer<T>.Default.Equals(read(), _original);

    /// <summary>Records the current values as the ones to return to.</summary>
    public void Capture() => _original = read();

    /// <summary>
    /// Writes values into the modal: seeding it from the page, or pointing selections at freshly
    /// loaded option objects.
    /// </summary>
    public void Set(T values) => write(values);

    public void Restore() => write(_original);

    public void Reset() => write(Default);

    /// <summary>
    /// Asks before discarding unapplied changes. Returns false when the user chose to keep editing;
    /// otherwise the values are back as they were when the modal opened.
    /// </summary>
    public async Task<bool> ConfirmDiscardAsync(Func<Task<bool>> confirm)
    {
        if (!HasChanges) return true;
        if (!await confirm()) return false;
        Restore();
        return true;
    }
}
