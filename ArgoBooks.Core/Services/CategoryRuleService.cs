using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Applies and learns bank category rules. Rules pre-fill category/type/counterparty for
/// statement lines, free and instantly, before any AI is consulted.
/// </summary>
public static class CategoryRuleService
{
    public static BankCategoryRule? Match(IReadOnlyList<BankCategoryRule> rules, string description)
    {
        if (rules.Count == 0) return null;
        var token = MerchantNormalizer.Normalize(description);
        if (token.Length == 0) return null;

        BankCategoryRule? best = null;
        foreach (var rule in rules)
        {
            var p = rule.Pattern;
            if (p.Length == 0) continue;
            var isMatch = rule.MatchType == RuleMatchType.Exact
                ? token == p
                : token.Contains(p, StringComparison.Ordinal);
            if (!isMatch) continue;

            // Exact beats Contains; among the same MatchType, the longer pattern is more specific.
            if (best == null
                || (rule.MatchType == RuleMatchType.Exact && best.MatchType != RuleMatchType.Exact)
                || (rule.MatchType == best.MatchType && rule.Pattern.Length > best.Pattern.Length))
            {
                best = rule;
            }
        }
        return best;
    }

    public static BankCategoryRule Learn(CompanyData data, string description, string categoryId,
        BookRecordType type, string? counterpartyId, string? productId = null)
    {
        var token = MerchantNormalizer.Normalize(description);
        var existing = data.Settings.BankCategoryRules.FirstOrDefault(r =>
            r.Pattern == token && r.MatchType == RuleMatchType.Contains);

        if (existing != null)
        {
            existing.CategoryId = categoryId;
            existing.ProductId = productId;
            existing.TransactionType = type;
            existing.CounterpartyId = counterpartyId;
            existing.Source = RuleSource.Learned;
            existing.UpdatedAt = DateTime.UtcNow;
            return existing;
        }

        var rule = new BankCategoryRule
        {
            Id = Guid.NewGuid().ToString("N"),
            Pattern = token,
            MatchType = RuleMatchType.Contains,
            CategoryId = categoryId,
            ProductId = productId,
            TransactionType = type,
            CounterpartyId = counterpartyId,
            Source = RuleSource.Learned
        };
        data.Settings.BankCategoryRules.Add(rule);
        return rule;
    }
}
