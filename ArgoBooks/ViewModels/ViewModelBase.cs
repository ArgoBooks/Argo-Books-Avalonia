using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Base class for all ViewModels in the application.
/// Provides common functionality like property change notifications and busy state.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyMessage;

    #region Search Debounce

    private const int SearchDebounceMs = 150;
    private CancellationTokenSource? _searchDebounceCts;

    /// <summary>
    /// Runs a search filter after a short pause in typing instead of on every keystroke.
    /// The list filters re-sort and rebuild their display rows, so firing per keystroke
    /// makes fast typing feel laggy on large data sets. Call from an OnSearchQueryChanged
    /// handler. The action runs on the UI thread once the user stops typing; if another
    /// keystroke arrives first, the pending run is cancelled. Call <see cref="CancelPendingSearch"/>
    /// from the ViewModel's cleanup so a pending filter can't fire after teardown.
    /// </summary>
    protected void DebounceSearch(Action filter)
    {
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = RunDebouncedSearchAsync(filter, cts.Token);
    }

    /// <summary>
    /// Cancels any pending debounced search so a queued filter won't run.
    /// </summary>
    protected void CancelPendingSearch() => _searchDebounceCts?.Cancel();

    private static async Task RunDebouncedSearchAsync(Action filter, CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
            filter();
    }

    #endregion

    /// <summary>
    /// Shows a "Discard Changes?" confirmation dialog for Add modals.
    /// Returns true if the user confirmed they want to discard.
    /// </summary>
    protected static async Task<bool> ConfirmDiscardNewAsync()
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return true;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Discard Changes?".Translate(),
            Message = "You have entered data that will be lost. Are you sure you want to close?".Translate(),
            PrimaryButtonText = "Discard".Translate(),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = true
        });

        return result == ConfirmationResult.Primary;
    }

    /// <summary>
    /// Shows a "Discard Changes?" confirmation dialog for Edit modals.
    /// Returns true if the user confirmed they want to discard.
    /// </summary>
    protected static async Task<bool> ConfirmDiscardEditsAsync()
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return true;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Discard Changes?".Translate(),
            Message = "You have unsaved changes that will be lost. Are you sure you want to close?".Translate(),
            PrimaryButtonText = "Discard".Translate(),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = true
        });

        return result == ConfirmationResult.Primary;
    }

    /// <summary>
    /// Shows a "Discard Changes?" confirmation dialog for Filter modals.
    /// Returns true if the user confirmed they want to discard.
    /// </summary>
    protected static async Task<bool> ConfirmDiscardFiltersAsync()
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return true;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Discard Changes?".Translate(),
            Message = "You have unapplied filter changes. Are you sure you want to close?".Translate(),
            PrimaryButtonText = "Discard".Translate(),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = true
        });

        return result == ConfirmationResult.Primary;
    }

    #region Delete

    /// <summary>
    /// Shows the standard delete confirmation. Returns true only when the user confirmed.
    /// </summary>
    /// <param name="title">Translated dialog title.</param>
    /// <param name="message">Translated dialog message.</param>
    protected static async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return false;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = title,
            Message = message,
            PrimaryButtonText = "Delete".Translate(),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = true
        });

        return result == ConfirmationResult.Primary;
    }

    /// <summary>
    /// Shows "Cannot Delete" listing the kinds of record that still refer to this one.
    /// Returns true when any check found a use, in which case the delete must not go ahead.
    /// </summary>
    /// <param name="message">Builds the translated message from the comma-joined labels.</param>
    /// <param name="checks">Each kind of record and whether it refers to the one being deleted.</param>
    protected static async Task<bool> BlockIfInUseAsync(Func<string, string> message, params (bool Used, string Label)[] checks)
    {
        var usages = checks.Where(c => c.Used).Select(c => c.Label).ToList();
        if (usages.Count == 0) return false;

        await App.ShowWarningDialogAsync("Cannot Delete".Translate(), message(string.Join(", ", usages)));
        return true;
    }

    /// <summary>
    /// Removes a record, marks the company modified, records an undo entry that puts it back,
    /// and raises <paramref name="notify"/>. <paramref name="onRemove"/> runs after the first
    /// removal and after every redo; <paramref name="onRestore"/> runs after every undo.
    /// </summary>
    protected static void RemoveWithUndo<T>(
        CompanyData companyData,
        IList<T> list,
        T item,
        string description,
        Action? notify,
        Action? onRemove = null,
        Action? onRestore = null) where T : IRecord
    {
        list.RemoveRecord(item);
        onRemove?.Invoke();
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            description,
            () =>
            {
                list.RestoreRecord(item);
                onRestore?.Invoke();
                companyData.MarkAsModified();
                notify?.Invoke();
            },
            () =>
            {
                list.RemoveRecord(item);
                onRemove?.Invoke();
                companyData.MarkAsModified();
                notify?.Invoke();
            }));

        notify?.Invoke();
    }

    #endregion

    /// <summary>
    /// Executes an async operation while showing a busy indicator.
    /// </summary>
    /// <param name="operation">The async operation to execute</param>
    /// <param name="message">Optional message to display while busy</param>
    protected async Task ExecuteBusyAsync(Func<Task> operation, string? message = null)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            BusyMessage = message;
            await operation();
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    /// <summary>
    /// Executes an async operation while showing a busy indicator and returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="operation">The async operation to execute</param>
    /// <param name="message">Optional message to display while busy</param>
    /// <returns>The result of the operation</returns>
    protected async Task<T?> ExecuteBusyAsync<T>(Func<Task<T>> operation, string? message = null)
    {
        if (IsBusy) return default;

        try
        {
            IsBusy = true;
            BusyMessage = message;
            return await operation();
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }
}
