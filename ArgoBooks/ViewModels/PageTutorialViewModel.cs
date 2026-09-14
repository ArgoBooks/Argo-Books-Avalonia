using ArgoBooks.Helpers;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// A page's first-visit tutorial: a few steps shown once, remembered by the page's key.
/// </summary>
public partial class PageTutorialViewModel : TutorialStepperViewModelBase
{
    public static IReadOnlyList<TutorialStep> CategoriesSteps { get; } =
    [
        new()
        {
            Title = "Categories",
            Description = "A category is a label for the money you spend, like supplies, rent or fuel. Every expense gets one.",
            HighlightArea = "none"
        },
        new()
        {
            Title = "Why they help",
            Description = "Once your spending is labelled, your reports can show you what you are actually spending the most on.",
            HighlightArea = "content"
        },
        new()
        {
            Title = "Some examples",
            Description = "A bakery might use Ingredients, Packaging and Equipment.\n\nYou can put a category inside another one too, like Flour inside Ingredients.",
            HighlightArea = "none"
        }
    ];

    public static IReadOnlyList<TutorialStep> ProductsSteps { get; } =
    [
        new()
        {
            Title = "Products",
            Description = "This is the list of things you buy for your business, like supplies and materials.",
            HighlightArea = "none"
        },
        new()
        {
            Title = "What you can store",
            Description = "Give each one a category and a supplier, and Argo Books can warn you when you are running low.",
            HighlightArea = "content"
        },
        new()
        {
            Title = "Not just physical things",
            Description = "A product can also be a service you pay for, like a monthly subscription. Add whatever you need to track.",
            HighlightArea = "none"
        }
    ];

    private readonly string _pageKey;
    private readonly IReadOnlyList<TutorialStep> _steps;

    [ObservableProperty]
    private string _currentHighlightArea = "none";

    public event EventHandler? TutorialCompleted;
    public event EventHandler? HighlightAreaChanged;

    public PageTutorialViewModel(string pageKey, IReadOnlyList<TutorialStep> steps)
    {
        _pageKey = pageKey;
        _steps = steps;
        InitializeStepIndicators();
    }

    protected override int StepCount => _steps.Count;

    public void ShowIfFirstVisit()
    {
        if (!TutorialService.Instance.ShowFirstVisitHints ||
            TutorialService.Instance.HasVisitedPage(_pageKey))
        {
            return;
        }

        StartTutorial();
    }

    public void StartTutorial() => OpenAtFirstStep();

    [RelayCommand]
    private void Skip()
    {
        TutorialService.Instance.MarkPageVisited(_pageKey);
        TutorialService.Instance.DisableFirstVisitHints();
        IsOpen = false;
    }

    protected override void Finish()
    {
        TutorialService.Instance.MarkPageVisited(_pageKey);
        IsOpen = false;
        TutorialCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void ApplyStep(int index)
    {
        var step = _steps[index];
        CurrentTitle = step.Title;
        CurrentDescription = step.Description;
        CurrentHighlightArea = step.HighlightArea;
    }

    protected override void OnStepShown() => HighlightAreaChanged?.Invoke(this, EventArgs.Empty);
}
