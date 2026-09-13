using System.Collections.ObjectModel;
using ArgoBooks.Core.Platform;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single item in the setup checklist.
/// </summary>
public partial class ChecklistItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private string _navigationTarget = "";

    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Gets or sets whether this is the current item to complete (next in sequence).
    /// Only the current item can be clicked.
    /// </summary>
    [ObservableProperty]
    private bool _isCurrentItem;
}

/// <summary>
/// ViewModel for the setup checklist component shown on the dashboard.
/// </summary>
public partial class SetupChecklistViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _isAllCompleted;

    public ObservableCollection<ChecklistItemViewModel> Items { get; } = [];

    /// <summary>
    /// Event raised when a checklist item is clicked for navigation.
    /// </summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>
    /// Hides the free-tier summary and upgrade prompt on the completion card. A paying
    /// customer has no use for either.
    /// </summary>
    [ObservableProperty]
    private bool _hasPremium;

    /// <summary>
    /// Free-tier summary on the completion card, composed from the limits the server
    /// reports. Same source as the upgrade modal.
    /// </summary>
    public string FreeTierSummary =>
        "You can use Argo Books for free: all core features, plus {0} invoices and {1} AI receipt scans every month."
            .TranslateFormat(
                UpgradeModalViewModel.FreeInvoiceMonthlyLimit,
                UpgradeModalViewModel.FreeReceiptScanMonthlyLimit);

    public SetupChecklistViewModel()
    {
        InitializeItems();
        TutorialService.Instance.ChecklistItemCompleted += OnChecklistItemCompleted;
        TutorialService.Instance.TutorialStateChanged += OnTutorialStateChanged;

        // The card may already be on screen with the fallback limits when the plans fetch
        // lands, so re-read them when the server answers.
        UpgradeModalViewModel.FreeLimitsChanged += (_, _) => OnPropertyChanged(nameof(FreeTierSummary));

        App.PlanStatusChanged += OnPlanStatusChanged;
    }

    private void OnPlanStatusChanged(object? sender, PlanStatusChangedEventArgs e)
    {
        HasPremium = e.HasPremium;
    }

    private void InitializeItems()
    {
        Items.Clear();
        // Scanning is first: it is the fastest path to a visible result. A scan does create an
        // expense behind the scenes, but it credits only this step, so the expense form is still
        // walked by hand. There are no category or product steps: the expense form creates both
        // from what is typed into a line, so sending people to those pages first was a detour.
        Items.Add(new ChecklistItemViewModel
        {
            Id = TutorialService.ChecklistItems.ScanReceipt,
            Title = "Scan a receipt",
            Description = "Let Argo Books fill in the details",
            Icon = Icons.ScanReceipt,
            NavigationTarget = TutorialService.Pages.Receipts
        });
        Items.Add(new ChecklistItemViewModel
        {
            Id = TutorialService.ChecklistItems.RecordExpense,
            Title = "Record your first expense",
            Description = "Log a business expense",
            Icon = Icons.Expenses,
            NavigationTarget = "Expenses"
        });
        Items.Add(new ChecklistItemViewModel
        {
            Id = TutorialService.ChecklistItems.VisitAnalytics,
            Title = "Visit the Analytics page",
            Description = "See your business insights",
            Icon = Icons.Analytics,
            NavigationTarget = "Analytics"
        });

        TotalCount = Items.Count;
        RefreshCompletionState();
    }

    /// <summary>
    /// Refreshes the visibility and completion state of the checklist.
    /// </summary>
    public void Refresh()
    {
        RefreshCompletionState();

        var tutorialService = TutorialService.Instance;
        tutorialService.SetCurrentCompanyPath(App.CompanyManager?.CurrentFilePath);

        if (tutorialService.IsSetupChecklistDismissed)
        {
            IsVisible = false;
            return;
        }

        if (!tutorialService.ShouldShowTutorialOnCurrentCompany())
        {
            IsVisible = false;
            return;
        }

        // Hide the checklist until the app tour has been finished or skipped.
        // The tour ends on a slide that explicitly tells the user the checklist
        // is the next step, so showing it earlier would steal attention from the tour.
        if (!tutorialService.HasCompletedAppTour && !tutorialService.HasSkippedTutorial)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
    }

    private void RefreshCompletionState()
    {
        var completedItems = TutorialService.Instance.GetCompletedChecklistItems();

        // Only count items that are in our current visible checklist
        var visibleCompletedCount = 0;
        var foundCurrentItem = false;

        foreach (var item in Items)
        {
            item.IsCompleted = completedItems.Contains(item.Id);
            if (item.IsCompleted)
            {
                visibleCompletedCount++;
                item.IsCurrentItem = false;
            }
            else if (!foundCurrentItem)
            {
                // First incomplete item is the current one
                item.IsCurrentItem = true;
                foundCurrentItem = true;
            }
            else
            {
                // Subsequent incomplete items are not current
                item.IsCurrentItem = false;
            }
        }

        CompletedCount = visibleCompletedCount;
        ProgressText = $"{CompletedCount} of {TotalCount} completed";
        ProgressPercentage = TotalCount > 0 ? (double)CompletedCount / TotalCount * 100 : 0;
        IsAllCompleted = CompletedCount >= TotalCount;
    }

    private void OnChecklistItemCompleted(object? sender, string itemId)
    {
        RefreshCompletionState();
    }

    private void OnTutorialStateChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    [RelayCommand]
    private void NavigateToItem(ChecklistItemViewModel? item)
    {
        if (item == null || string.IsNullOrEmpty(item.NavigationTarget))
            return;

        NavigationRequested?.Invoke(this, item.NavigationTarget);

        // The scan step opens its own workflow rather than just landing the user on a page.
        // Seeing a scan happen is the whole point of the step, and a brand-new user has no
        // receipt to pick, so the sample is offered directly. If the sample can't be
        // produced this is a no-op and they simply stay on the Receipts page.
        if (item.Id == TutorialService.ChecklistItems.ScanReceipt)
            _ = App.ReceiptsModalsViewModel?.OpenScanModalWithSampleAsync();
        else if (item.Id == TutorialService.ChecklistItems.RecordExpense)
            App.ExpenseModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void DismissChecklist()
    {
        // Skipping the tour leaves guidance on, so closing the checklist is where someone opts out
        // of it, page hints included.
        TutorialService.Instance.HideSetupChecklist();
        TutorialService.Instance.DisableFirstVisitHints();
        IsVisible = false;
    }

    [RelayCommand]
    private void CompleteAll()
    {
        // Mark all remaining items as completed (user confirms they've done everything)
        foreach (var item in Items)
        {
            if (!item.IsCompleted)
            {
                TutorialService.Instance.CompleteChecklistItem(item.Id);
            }
        }
    }

    [RelayCommand]
    private void OpenUpgradeUrl()
    {
        UrlHelper.SafeOpenUrl("https://www.argorobots.com/pricing/");
    }
}
