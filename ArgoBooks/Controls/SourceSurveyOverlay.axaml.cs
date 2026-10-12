using System.Collections.ObjectModel;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

public partial class SourceSurveyOverlay : UserControl
{
    private ModalOverlay? _overlay;
    private readonly SourceSurveyOverlayViewModel _viewModel;

    public SourceSurveyOverlay()
    {
        InitializeComponent();

        _viewModel = new SourceSurveyOverlayViewModel();
        DataContext = _viewModel;

        _overlay = this.FindControl<ModalOverlay>("Overlay");

        TutorialService.Instance.SourceSurveyVisibilityChanged += OnVisibilityChanged;
    }

    private async void OnVisibilityChanged(object? sender, bool show)
    {
        if (show)
            _viewModel.DecideWhatToAsk();

        if (_overlay != null)
            _overlay.IsOpen = show;

        if (show)
        {
            // The collections already hold the bundled defaults (instant render);
            // refresh from the server so newly added options appear without a release.
            await _viewModel.LoadOptionsAsync();
        }
        else
        {
            _viewModel.Reset();
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        TutorialService.Instance.SourceSurveyVisibilityChanged -= OnVisibilityChanged;
    }
}

/// <summary>
/// A single selectable survey option in a question's list. Raises
/// <see cref="SelectionRequested"/> when the user picks it so the question can
/// enforce single selection and track the chosen key.
/// </summary>
public partial class SurveyOptionItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public bool Freeform { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event Action<SurveyOptionItem>? SelectionRequested;

    public SurveyOptionItem(string key, string label, bool freeform)
    {
        Key = key;
        Label = label;
        Freeform = freeform;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            SelectionRequested?.Invoke(this);
    }
}

/// <summary>
/// One pick-one question: its choices, which is picked, and the text typed for a freeform choice.
/// </summary>
public partial class SurveyQuestion : ObservableObject
{
    [ObservableProperty]
    private string? _selectedKey;

    // True when the currently selected option is freeform (reveals the text box).
    [ObservableProperty]
    private bool _isFreeformSelected;

    [ObservableProperty]
    private string _otherText = string.Empty;

    /// <summary>The options shown as radio choices.</summary>
    public ObservableCollection<SurveyOptionItem> Options { get; } = new();

    /// <summary>Raised when the answer changes, so whoever owns the question can recheck it.</summary>
    public event Action? Changed;

    public SurveyQuestion(IReadOnlyList<SurveyOption> options) => SetOptions(options);

    /// <summary>A choice is picked, and a freeform one has its text.</summary>
    public bool IsAnswered =>
        !string.IsNullOrEmpty(SelectedKey) && (!IsFreeformSelected || !string.IsNullOrWhiteSpace(OtherText));

    /// <summary>The text that goes with a freeform choice, or null for any other.</summary>
    public string? FreeformText => IsFreeformSelected ? OtherText.Trim() : null;

    partial void OnSelectedKeyChanged(string? value) => Changed?.Invoke();
    partial void OnIsFreeformSelectedChanged(bool value) => Changed?.Invoke();
    partial void OnOtherTextChanged(string value) => Changed?.Invoke();

    public void SetOptions(IReadOnlyList<SurveyOption> options)
    {
        // Keeps any current selection, because the overlay opens on bundled defaults and the user may have picked one before the server refresh lands.
        var previousKey = SelectedKey;

        foreach (var existing in Options)
            existing.SelectionRequested -= OnOptionSelectionRequested;
        Options.Clear();

        SurveyOptionItem? toReselect = null;
        foreach (var o in options)
        {
            var item = new SurveyOptionItem(o.Key, o.Label, o.Freeform);
            item.SelectionRequested += OnOptionSelectionRequested;
            Options.Add(item);
            if (o.Key == previousKey)
                toReselect = item;
        }

        if (toReselect != null)
        {
            // Re-apply the prior choice; OnOptionSelectionRequested restores
            // SelectedKey/IsFreeformSelected. OtherText is intentionally kept.
            toReselect.IsSelected = true;
        }
        else
        {
            // The previously selected key is gone (or nothing was selected).
            SelectedKey = null;
            IsFreeformSelected = false;
        }
    }

    private void OnOptionSelectionRequested(SurveyOptionItem selected)
    {
        // Enforce single selection: deselect every other item.
        foreach (var item in Options)
        {
            if (!ReferenceEquals(item, selected) && item.IsSelected)
                item.IsSelected = false;
        }

        SelectedKey = selected.Key;
        IsFreeformSelected = selected.Freeform;
    }

    public void Reset()
    {
        foreach (var item in Options)
            item.IsSelected = false;
        SelectedKey = null;
        IsFreeformSelected = false;
        OtherText = string.Empty;
    }
}

public partial class SourceSurveyOverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSubmitting;

    [ObservableProperty]
    private string? _submitError;

    // Each question is asked only of someone who has not answered it. The source is also left
    // out for an install that arrived through a tracked link, where it is already known.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _askSource = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _askGoal = true;

    /// <summary>"Where did you hear about Argo Books?"</summary>
    public SurveyQuestion Source { get; } = new(SourceSurveyOptionsService.DefaultOptions);

    /// <summary>"What do you mainly want to use it for?"</summary>
    public SurveyQuestion Goal { get; } = new(SourceSurveyOptionsService.DefaultGoals);

    public SourceSurveyOverlayViewModel()
    {
        Source.Changed += () => OnPropertyChanged(nameof(CanSubmit));
        Goal.Changed += () => OnPropertyChanged(nameof(CanSubmit));
    }

    public string Title => AskSource && AskGoal
        ? "Two quick questions".Translate()
        : "A quick question".Translate();

    public bool CanSubmit =>
        !IsSubmitting
        && (AskSource || AskGoal)
        && (!AskSource || Source.IsAnswered)
        && (!AskGoal || Goal.IsAnswered);

    partial void OnIsSubmittingChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnAskSourceChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnAskGoalChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    /// <summary>Works out which of the two questions this user still has to answer.</summary>
    public void DecideWhatToAsk()
    {
        AskSource = TutorialService.Instance.ShouldAskSurveySource();
        AskGoal = TutorialService.Instance.ShouldAskSurveyGoal();
    }

    /// <summary>
    /// Fetches the latest options from the server and rebuilds the lists.
    /// The service returns bundled defaults on failure, so this never throws.
    /// </summary>
    public async Task LoadOptionsAsync()
    {
        var service = App.SourceSurveyOptionsService;
        if (service == null) return;
        var choices = await service.GetChoicesAsync();
        Source.SetOptions(choices.Sources);
        Goal.SetOptions(choices.Goals);
    }

    public void Reset()
    {
        Source.Reset();
        Goal.Reset();
        IsSubmitting = false;
        SubmitError = null;
    }

    /// <summary>
    /// Closes the overlay without recording an answer. Only surfaced after a submit
    /// failure so the user isn't trapped; the dashboard banner keeps prompting until
    /// they successfully answer.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        TutorialService.Instance.CloseSourceSurveyOverlay();
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!CanSubmit) return;

        var source = AskSource ? Source.SelectedKey : null;
        var goal = AskGoal ? Goal.SelectedKey : null;

        IsSubmitting = true;
        SubmitError = null;
        try
        {
            var reporter = App.SourceSurveyReporter;
            var machineUuid = InstallAttributionReason.ReadMachineUuid();
            if (reporter == null || machineUuid == null)
            {
                SubmitError = "Could not record your answer. Please try again later.".Translate();
                return;
            }

            var ok = await reporter.ReportAsync(
                source,
                machineUuid,
                AskSource ? Source.FreeformText : null,
                goal,
                AskGoal ? Goal.FreeformText : null);
            if (!ok)
            {
                SubmitError = "Could not record your answer. Please check your connection and try again.".Translate();
                return;
            }

            // Only mark answered after a successful POST so a failure doesn't
            // permanently suppress the survey with no record on the server.
            TutorialService.Instance.MarkSourceSurveyAnswered(source, goal);
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
