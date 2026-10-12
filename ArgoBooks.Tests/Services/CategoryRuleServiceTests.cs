using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class CategoryRuleServiceTests
{
    [Fact]
    public void Match_ContainsRule_MatchesNormalizedDescription()
    {
        var rules = new List<BankCategoryRule>
        {
            new() { Id = "R1", Pattern = "amzn mktp", MatchType = RuleMatchType.Contains, CategoryId = "CAT-PUR-001" }
        };
        var match = CategoryRuleService.Match(rules, "AMZN MKTP US*2H8KL");
        Assert.NotNull(match);
        Assert.Equal("CAT-PUR-001", match.CategoryId);
    }

    [Fact]
    public void Match_NoRule_ReturnsNull()
    {
        Assert.Null(CategoryRuleService.Match([], "UNKNOWN VENDOR"));
    }

    [Fact]
    public void Learn_AddsNewRule_FromNormalizedToken()
    {
        var data = new CompanyData();
        var rule = CategoryRuleService.Learn(data, "SHELL OIL 574123", "CAT-PUR-002", BookRecordType.Expense, null);

        Assert.Equal("shell oil", rule.Pattern);
        Assert.Equal(RuleSource.Learned, rule.Source);
        Assert.Single(data.BankCategoryRules);
    }

    [Fact]
    public void Learn_SameToken_UpdatesInsteadOfDuplicating()
    {
        var data = new CompanyData();
        CategoryRuleService.Learn(data, "SHELL OIL 1", "CAT-PUR-002", BookRecordType.Expense, null);
        CategoryRuleService.Learn(data, "SHELL OIL 2", "CAT-PUR-009", BookRecordType.Expense, null);

        Assert.Single(data.BankCategoryRules);
        Assert.Equal("CAT-PUR-009", data.BankCategoryRules[0].CategoryId);
    }

    // Exact must beat Contains for the same pattern whichever rule comes first, so this places Contains first.
    [Fact]
    public void Match_ExactRule_BeatsContainsRule_WhenContainsIsFirst()
    {
        var rules = new List<BankCategoryRule>
        {
            new() { Id = "CONTAINS_FIRST", Pattern = "shell", MatchType = RuleMatchType.Contains, CategoryId = "CAT-CONTAINS" },
            new() { Id = "EXACT_SECOND",   Pattern = "shell", MatchType = RuleMatchType.Exact,    CategoryId = "CAT-EXACT" }
        };
        // "SHELL" normalizes to "shell": both rules match.
        var match = CategoryRuleService.Match(rules, "SHELL");
        Assert.NotNull(match);
        Assert.Equal("CAT-EXACT", match.CategoryId);
    }
}
