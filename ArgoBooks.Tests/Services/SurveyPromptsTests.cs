using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// When the app interrupts someone with a question. Getting these wrong means asking a person
/// who is busy, or asking the same thing twice.
/// </summary>
public class SurveyPromptsTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CompanyUse.NeverHadOne, true)]
    [InlineData(CompanyUse.OpenAndEmpty, true)]
    // There is a company that is not open. It may be in daily use, so nothing is assumed.
    [InlineData(CompanyUse.NoneOpen, false)]
    [InlineData(CompanyUse.OpenWithRecords, false)]
    public void TheClosingQuestion_IsOnlyForSomeoneWithNothingRecorded(CompanyUse use, bool asked)
    {
        Assert.Equal(asked, SurveyPrompts.ShouldAskOnExit(new TutorialSettings(), "no_token", hasRecordedBefore: false, use));
    }

    [Fact]
    public void TheClosingQuestion_IsNotAskedOfSomeoneWhoHasAlreadyAnsweredOrRecorded()
    {
        bool Asked(TutorialSettings t, string? reason = "token", bool recorded = false) =>
            SurveyPrompts.ShouldAskOnExit(t, reason, recorded, CompanyUse.OpenAndEmpty);

        Assert.True(Asked(new TutorialSettings()));
        Assert.False(Asked(new TutorialSettings { HasAskedExitSurvey = true }));
        Assert.False(Asked(new TutorialSettings { SurveyGoalAnswer = "invoices" }));
        Assert.False(Asked(new TutorialSettings { SourceSurveyAnswer = "google" }));
        Assert.False(Asked(new TutorialSettings(), recorded: true));
        // No first-run row on the website, so there is nowhere to put an answer.
        Assert.False(Asked(new TutorialSettings(), reason: "gave_up_after_retries"));
        Assert.False(Asked(new TutorialSettings(), reason: null));
    }

    [Fact]
    public void TheGoal_IsAskedOfAnInstallWhoseSourceIsKnown_AndTheSourceIsNot()
    {
        var tutorial = new TutorialSettings();

        Assert.False(SurveyPrompts.ShouldAskSource(tutorial, "token"));
        Assert.True(SurveyPrompts.ShouldAskGoal(tutorial, "token"));
        Assert.True(SurveyPrompts.ShouldAskSource(tutorial, "no_token"));

        tutorial.SurveyGoalAnswer = "payroll";
        Assert.False(SurveyPrompts.ShouldAskGoal(tutorial, "token"));
    }

    [Fact]
    public void AReview_IsAskedForOnce_OfSomeoneWhoHasUsedTheAppForAWhile()
    {
        bool Asked(ReviewPromptSettings prompt, int daysIn = 20, int records = 25, bool sample = false) =>
            SurveyPrompts.ShouldAskForReview(prompt, Now.AddDays(-daysIn), records, sample, Now);

        Assert.True(Asked(new ReviewPromptSettings()));
        Assert.False(Asked(new ReviewPromptSettings(), daysIn: 13));
        Assert.False(Asked(new ReviewPromptSettings(), records: 9));
        Assert.False(Asked(new ReviewPromptSettings(), sample: true));
        Assert.False(Asked(new ReviewPromptSettings { Dismissed = true }));
        Assert.False(Asked(new ReviewPromptSettings { Opened = true }));
        Assert.False(SurveyPrompts.ShouldAskForReview(new ReviewPromptSettings(), null, 25, false, Now));
    }
}
