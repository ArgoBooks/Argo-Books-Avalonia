namespace ArgoBooks.ViewModels;

/// <summary>
/// Helper for the "open a create-entity modal, then react once when it saves" pattern shared by the
/// various <c>OpenCreateX</c> commands.
///
/// The create-modal ViewModels (<c>App.ProductModalsViewModel</c>, etc.) are app-lifetime singletons,
/// so the naive "subscribe to <c>XSaved</c>, unsubscribe on the handler's own first fire" approach
/// leaks the handler whenever the user CANCELS the create modal, because cancel closes the modal
/// without ever raising <c>XSaved</c>. A later, unrelated save of that same entity type (including an
/// undo/redo, which also raises <c>XSaved</c>) would then re-fire the stale handler and clobber the
/// caller's current selection, and every cancel adds another permanent handler.
///
/// <see cref="RearmOnce"/> keeps at most one live handler per call site: it detaches any
/// previously-armed handler before arming a new one-shot handler that detaches itself on fire. This
/// mirrors the guard already used in <c>SettingsModalViewModel.OpenCreateCategory</c>.
/// </summary>
internal static class CreateModalSubscription
{
    /// <summary>
    /// Detaches the handler currently stored in <paramref name="slot"/> (if any), then arms a new
    /// one-shot handler that runs <paramref name="onSaved"/> and detaches itself on the first fire.
    /// </summary>
    public static void RearmOnce(
        ref EventHandler? slot,
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Action onSaved)
    {
        if (slot is not null)
            unsubscribe(slot);

        // Cannot capture the ref parameter 'slot' inside the handler lambda (CS1628), so the handler only detaches itself from the event on fire; it does not null out the caller's field.
        EventHandler handler = null!;
        handler = (_, _) =>
        {
            unsubscribe(handler);
            onSaved();
        };
        slot = handler;
        subscribe(handler);
    }
}
