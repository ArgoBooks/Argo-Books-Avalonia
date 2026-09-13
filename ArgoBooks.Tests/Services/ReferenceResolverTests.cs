using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class ReferenceResolverTests
{
    [Fact]
    public void Resolve_NormalizedExactMatch_ReturnsId()
    {
        var index = ReferenceResolver.BuildIndex([("CUS-1", "Acme Ltd."), ("CUS-2", "Globex")]);
        Assert.Equal("CUS-1", ReferenceResolver.Resolve("  acme  ltd ", index).MatchedId);
    }

    // "Smith" vs "Smith Plumbing"/"Smith Electrical": Levenshtein ratios are
    // 5/14 = 0.36 and 5/17 = 0.29 respectively — both well below 0.92.
    // Neither candidate qualifies, so the result is (null, false) — no mis-link.
    [Fact]
    public void Resolve_ShortQueryAgainstMultipleLongerNames_ReturnsNoMatch()
    {
        var index = ReferenceResolver.BuildIndex([("S-1", "Smith Plumbing"), ("S-2", "Smith Electrical")]);
        var r = ReferenceResolver.Resolve("Smith", index);
        Assert.Null(r.MatchedId);
        Assert.False(r.IsAmbiguous);
    }

    // A long name with a single-character typo scores 15/16 = 0.9375, above the 0.92 bar,
    // and is the only candidate — returns the id confidently.
    [Fact]
    public void Resolve_LongNameOneCharTypo_ReturnsId()
    {
        // "Acme Corporaton" (15 chars) vs "Acme Corporation" (16 chars): distance=1, ratio=15/16=0.9375
        var index = ReferenceResolver.BuildIndex([("CUS-1", "Acme Corporation"), ("CUS-2", "Globex")]);
        var r = ReferenceResolver.Resolve("Acme Corporaton", index);
        Assert.Equal("CUS-1", r.MatchedId);
        Assert.False(r.IsAmbiguous);
    }

    // Short-name typos must NOT auto-link. "jones" vs "janes": distance=1, maxLen=5, ratio=0.80.
    // That is below 0.92, so no match is returned.
    [Fact]
    public void Resolve_SmallTypoInShortName_DoesNotMatch()
    {
        var index = ReferenceResolver.BuildIndex([("P-1", "janes")]);
        var r = ReferenceResolver.Resolve("jones", index);
        Assert.Null(r.MatchedId);
        Assert.False(r.IsAmbiguous);
    }

    // Substring/partial queries must NOT mis-link. "office" vs "Office Depot":
    // distance=6, maxLen=12, ratio=0.50 — well below 0.92.
    [Fact]
    public void Resolve_PartialQueryAgainstLongerName_DoesNotMatch()
    {
        var index = ReferenceResolver.BuildIndex([("V-1", "Office Depot")]);
        var r = ReferenceResolver.Resolve("office", index);
        Assert.Null(r.MatchedId);
        Assert.False(r.IsAmbiguous);
    }

    // Two candidates that both score >= 0.92 vs the query and are within 0.05 of each other
    // trigger the ambiguity rule: (null, IsAmbiguous=true).
    // "Acme Corp East" (14) vs "Acme Corp East" (14) = 1.0; vs "Acme Corp Wast" (14) = 13/14 = 0.929.
    // Both exceed 0.92, gap = 1.0 - 0.929 = 0.071 >= 0.05 — so this is NOT a tie.
    // Use query "Acme Corp Xast": vs "Acme Corp East" dist=1 -> 13/14=0.929; vs "Acme Corp Wast" dist=1 -> 13/14=0.929.
    // Gap = 0.0 < 0.05 -> IsAmbiguous=true.
    [Fact]
    public void Resolve_TwoNearlyEqualHighScoreCandidates_ReturnsAmbiguous()
    {
        var index = ReferenceResolver.BuildIndex([("C-1", "Acme Corp East"), ("C-2", "Acme Corp Wast")]);
        var r = ReferenceResolver.Resolve("Acme Corp Xast", index);
        Assert.Null(r.MatchedId);
        Assert.True(r.IsAmbiguous);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNullNotAmbiguous()
    {
        var index = ReferenceResolver.BuildIndex([("CUS-1", "Acme Ltd."), ("CUS-2", "Globex")]);
        var r = ReferenceResolver.Resolve("Totally Different", index);
        Assert.Null(r.MatchedId);
        Assert.False(r.IsAmbiguous);
    }

    [Fact]
    public void BuildIndex_NormalizesDuplicateNames_KeepsFirst()
    {
        // Two entities with names that normalize to the same key: only the first survives
        var index = ReferenceResolver.BuildIndex([("A-1", "Alpha Corp."), ("A-2", "Alpha Corp")]);
        Assert.Single(index);
        Assert.True(index.ContainsKey("alpha corp"));
        Assert.Equal("A-1", index["alpha corp"]);
    }

    [Fact]
    public void Resolve_ExactMatchWithTrailingPunctuation_ReturnsId()
    {
        // The name "Globex" is stored; query "Globex," (trailing comma) normalizes to "globex"
        var index = ReferenceResolver.BuildIndex([("CUS-2", "Globex")]);
        Assert.Equal("CUS-2", ReferenceResolver.Resolve("Globex,", index).MatchedId);
    }
}
