using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

public partial class ExitSurveyOverlay : UserControl
{
    private readonly ModalOverlay? _overlay;
    private readonly ExitSurveyOverlayViewModel _viewModel;

    public ExitSurveyOverlay()
    {
        InitializeComponent();

        _viewModel = new ExitSurveyOverlayViewModel();
        _viewModel.Finished += OnFinished;
        DataContext = _viewModel;

        _overlay = this.FindControl<ModalOverlay>("Overlay");

        TutorialService.Instance.ExitSurveyRequested += OnRequested;
    }

    private async void OnRequested(object? sender, EventArgs e)
    {
        if (_overlay == null)
        {
            TutorialService.Instance.CompleteExitSurvey();
            return;
        }

        _overlay.IsOpen = true;
        await _viewModel.LoadOptionsAsync();
    }

    private void OnFinished()
    {
        if (_overlay != null)
            _overlay.IsOpen = false;
        TutorialService.Instance.CompleteExitSurvey();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        TutorialService.Instance.ExitSurveyRequested -= OnRequested;
    }
}

public partial class ExitSurveyOverlayViewModel : ObservableObject
{
    // The app is closing behind this, so the answer gets a few seconds to send and no more.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool _isSending;

    /// <summary>The same goals the survey offers, asked here of someone who never reached it.</summary>
    public SurveyQuestion Goal { get; } = new(SourceSurveyOptionsService.DefaultGoals);

    /// <summary>Raised once the question is answered or skipped, so the app can carry on closing.</summary>
    public event Action? Finished;

    public ExitSurveyOverlayViewModel()
    {
        Goal.Changed += () => OnPropertyChanged(nameof(CanSend));
    }

    /// <summary>A goal, a note, or both. The note is never required, whichever goal is picked.</summary>
    public bool CanSend =>
        !IsSending && (!string.IsNullOrEmpty(Goal.SelectedKey) || !string.IsNullOrWhiteSpace(Note));

    public async Task LoadOptionsAsync()
    {
        var service = App.SourceSurveyOptionsService;
        if (service == null) return;
        Goal.SetOptions((await service.GetChoicesAsync()).Goals);
    }

    [RelayCommand]
    private void Skip() => Finished?.Invoke();

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!CanSend) return;

        IsSending = true;
        try
        {
            var reporter = App.SourceSurveyReporter;
            var machineUuid = InstallAttributionReason.ReadMachineUuid();
            if (reporter != null && machineUuid != null)
            {
                using var timeout = new CancellationTokenSource(SendTimeout);
                var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
                await reporter.ReportExitAsync(Goal.SelectedKey, note, machineUuid, timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Too slow. The person asked to close the app, and that comes first.
        }
        finally
        {
            IsSending = false;
            Finished?.Invoke();
        }
    }
}
