using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>One schedule row on a Recurring tab.</summary>
public partial class RecurringDisplayItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FrequencyLabel { get; set; } = string.Empty;
    public string AmountFormatted { get; set; } = string.Empty;
    public string NextDateFormatted { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsPaused { get; set; }
    public string PauseTooltip { get; set; } = string.Empty;
}

/// <summary>
/// The schedules for one side of the books, shown on that page's Recurring tab. The editor lives
/// on the shell and is shared, so it is reached through <see cref="App.RecurringScheduleEditor"/>.
/// </summary>
public partial class RecurringSchedulesViewModel : ViewModelBase, ICleanupViewModel
{
    private readonly CategoryType _side;

    public ObservableCollection<RecurringDisplayItem> Schedules { get; } = [];

    public bool HasSchedules => Schedules.Count > 0;

    public RecurringSchedulesViewModel() : this(CategoryType.Expense)
    {
    }

    public RecurringSchedulesViewModel(CategoryType side)
    {
        _side = side;
        Load();

        if (App.RecurringScheduleEditor != null)
            App.RecurringScheduleEditor.Saved += Load;

        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
    }

    private void OnUndoRedoStateChanged(object? sender, EventArgs e) => Load();

    /// <summary>
    /// The editor and the undo manager both outlive this list, and a page view model is rebuilt on
    /// every company switch, so a stale instance would keep reloading against the new company.
    /// </summary>
    public void Cleanup()
    {
        if (App.RecurringScheduleEditor != null)
            App.RecurringScheduleEditor.Saved -= Load;

        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
    }

    public void Load()
    {
        Schedules.Clear();

        var data = App.CompanyManager?.CompanyData;
        if (data != null)
        {
            foreach (var schedule in data.RecurringTransactions
                         .Where(s => s.Type == _side)
                         .OrderBy(s => s.NextDate))
            {
                var paused = schedule.Status == RecurringTransactionStatus.Paused;
                Schedules.Add(new RecurringDisplayItem
                {
                    Id = schedule.Id,
                    Description = schedule.Template?.Description ?? string.Empty,
                    FrequencyLabel = schedule.Frequency.ToString().Translate(),
                    AmountFormatted = CurrencyService.Format(schedule.Template?.Total ?? 0m),
                    NextDateFormatted = schedule.NextDate.ToString("MMM d, yyyy"),
                    StatusLabel = schedule.Status.ToString().Translate(),
                    IsActive = schedule.Status == RecurringTransactionStatus.Active,
                    IsPaused = paused,
                    PauseTooltip = (paused ? "Resume" : "Pause").Translate()
                });
            }
        }

        OnPropertyChanged(nameof(HasSchedules));
    }

    [RelayCommand]
    private void AddSchedule() => App.RecurringScheduleEditor?.ShowNew(_side);

    [RelayCommand]
    private void EditSchedule(RecurringDisplayItem? item)
    {
        var schedule = Find(item);
        if (schedule != null) App.RecurringScheduleEditor?.ShowEdit(schedule);
    }

    [RelayCommand]
    private void TogglePause(RecurringDisplayItem? item)
    {
        var schedule = Find(item);
        if (schedule == null || schedule.Status == RecurringTransactionStatus.Completed) return;

        var before = schedule.Status;
        var after = before == RecurringTransactionStatus.Active
            ? RecurringTransactionStatus.Paused
            : RecurringTransactionStatus.Active;

        // Generation catches up from the next date, so resuming with it still in the paused months
        // would create an entry for each of them.
        var dateBefore = schedule.NextDate;
        var dateAfter = after == RecurringTransactionStatus.Active
            ? RecurrenceSchedule.FirstOnOrAfter(dateBefore, schedule.Frequency, schedule.StartDate.Day, DateTime.Today)
            : dateBefore;

        schedule.Status = after;
        schedule.NextDate = dateAfter;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            after == RecurringTransactionStatus.Paused
                ? $"Pause schedule {schedule.Id}"
                : $"Resume schedule {schedule.Id}",
            () =>
            {
                schedule.Status = before;
                schedule.NextDate = dateBefore;
            },
            () =>
            {
                schedule.Status = after;
                schedule.NextDate = dateAfter;
            }));

        App.CompanyManager?.MarkAsChanged();
        Load();
    }

    [RelayCommand]
    private void SkipNext(RecurringDisplayItem? item)
    {
        var schedule = Find(item);
        if (schedule == null) return;

        var skipped = schedule.NextDate;
        var advanced = RecurrenceSchedule.AdvanceDate(
            schedule.NextDate, schedule.Frequency, schedule.StartDate.Day);

        RecurringTransactionService.SkipOccurrence(schedule, skipped);
        schedule.NextDate = advanced;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Skip {skipped:MMM d} on {schedule.Id}",
            () =>
            {
                RecurringTransactionService.UnskipOccurrence(schedule, skipped);
                schedule.NextDate = skipped;
            },
            () =>
            {
                RecurringTransactionService.SkipOccurrence(schedule, skipped);
                schedule.NextDate = advanced;
            }));

        App.CompanyManager?.MarkAsChanged();
        Load();
    }

    [RelayCommand]
    private async Task DeleteSchedule(RecurringDisplayItem? item)
    {
        var data = App.CompanyManager?.CompanyData;
        var dialog = App.ConfirmationDialog;
        var schedule = Find(item);
        if (data == null || dialog == null || schedule == null) return;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Delete schedule".Translate(),
            Message = "This stops future entries being generated. Entries it already created stay in your books.".Translate(),
            PrimaryButtonText = "Delete".Translate(),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = true
        });

        if (result != ConfirmationResult.Primary) return;

        var index = data.RecurringTransactions.IndexOf(schedule);
        data.RecurringTransactions.Remove(schedule);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Delete schedule {schedule.Id}",
            () => data.RecurringTransactions.Insert(Math.Min(index, data.RecurringTransactions.Count), schedule),
            () => data.RecurringTransactions.Remove(schedule)));

        App.CompanyManager?.MarkAsChanged();
        Load();
    }

    private RecurringTransaction? Find(RecurringDisplayItem? item) =>
        item == null
            ? null
            : App.CompanyManager?.CompanyData?.RecurringTransactions.FirstOrDefault(s => s.Id == item.Id);
}
