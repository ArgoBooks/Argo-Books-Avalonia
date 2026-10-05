using ArgoBooks.Core.Models;

namespace ArgoBooks.Core.Services;

/// <summary>What the person has in front of them when the app decides whether to ask something.</summary>
public enum CompanyUse
{
    /// <summary>No company open, and none has ever been created or opened on this computer.</summary>
    NeverHadOne,

    /// <summary>No company open now, but there has been one. Nothing can be said about what is in it.</summary>
    NoneOpen,

    /// <summary>A company is open with no expenses, sales or invoices of its own. The sample company counts.</summary>
    OpenAndEmpty,

    OpenWithRecords
}

/// <summary>
/// When the app asks its questions. Kept apart from the screens that ask them so the rules
/// can be read, and tested, in one place.
/// </summary>
public static class SurveyPrompts
{
    /// <summary>
    /// Whether the website holds a first-run row for this install. Every answer is stored on that
    /// row, so without one there is nowhere to put it.
    /// </summary>
    public static bool CanReport(string? firstRunReason) => firstRunReason is "token" or "no_token";

    /// <summary>"Where did you hear about us?" is only asked of an install whose source is unknown.</summary>
    public static bool ShouldAskSource(TutorialSettings tutorial, string? firstRunReason) =>
        tutorial.SourceSurveyAnswer == null
        && !tutorial.IsSourceSurveyDismissed
        && firstRunReason == "no_token";

    /// <summary>"What did you come to do?" is asked of everyone, whether or not their source is known.</summary>
    public static bool ShouldAskGoal(TutorialSettings tutorial, string? firstRunReason) =>
        tutorial.SurveyGoalAnswer == null
        && !tutorial.IsSourceSurveyDismissed
        && CanReport(firstRunReason);

    /// <summary>
    /// Whether to ask, as the app closes, what the person was hoping to do. It is for the people
    /// the survey never reaches: those who open the app, record nothing and leave. So it is asked
    /// once, only of someone who has answered nothing and recorded nothing, and never when there
    /// is a company that is not open, because that company may well be in use.
    /// </summary>
    public static bool ShouldAskOnExit(
        TutorialSettings tutorial, string? firstRunReason, bool hasRecordedBefore, CompanyUse use) =>
        !tutorial.HasAskedExitSurvey
        && tutorial.SourceSurveyAnswer == null
        && tutorial.SurveyGoalAnswer == null
        && !hasRecordedBefore
        && CanReport(firstRunReason)
        && use is CompanyUse.NeverHadOne or CompanyUse.OpenAndEmpty;

    /// <summary>How long someone has had the app before a review is asked for.</summary>
    public static readonly TimeSpan ReviewAfter = TimeSpan.FromDays(14);

    /// <summary>How many expenses, sales and invoices show the app is really in use.</summary>
    public const int ReviewAfterRecords = 10;

    /// <summary>
    /// Whether to ask for a review: once, of someone who has had the app a while and is plainly
    /// using it. Asked any earlier it reaches people with nothing yet to say.
    /// </summary>
    public static bool ShouldAskForReview(
        ReviewPromptSettings prompt, DateTime? firstLaunchUtc, int records, bool isSampleCompany, DateTime nowUtc) =>
        !prompt.Dismissed
        && !prompt.Opened
        && !isSampleCompany
        && records >= ReviewAfterRecords
        && firstLaunchUtc is { } first
        && nowUtc - first >= ReviewAfter;
}
