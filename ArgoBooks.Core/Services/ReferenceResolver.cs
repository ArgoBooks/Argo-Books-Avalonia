using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Conservative name-to-entity-ID resolver.
/// Prefers a normalized-exact match; only returns a fuzzy match when the best candidate
/// exceeds a high similarity threshold AND no other candidate is within 0.05 of it.
/// A wrong link is worse than no link.
/// </summary>
public static class ReferenceResolver
{
    // A candidate must score at least this to be returned as a match.
    // Set at 0.92 to accept only very close matches (e.g. a long name one character off)
    // while rejecting short-name typos, substrings, and partial-word matches.
    // Conservative: a wrong link is worse than no link.
    private const double FuzzyAcceptThreshold = 0.92;

    // The winning candidate must beat the runner-up by at least this margin.
    // If the gap is smaller, the result is ambiguous.
    private const double TieBreakMargin = 0.05;

    // Regex used to collapse runs of whitespace to a single space.
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    // Trailing characters that are stripped during normalization.
    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':'];

    /// <summary>
    /// Builds a lookup dictionary from normalized name -> entity ID.
    /// Normalization: trim, lower-invariant, collapse internal whitespace, strip trailing punctuation.
    /// On duplicate normalized names the first entry wins.
    /// </summary>
    public static Dictionary<string, string> BuildIndex(IEnumerable<(string Id, string Name)> entities)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, name) in entities)
        {
            var key = Normalize(name);
            if (key.Length > 0 && !index.ContainsKey(key))
                index[key] = id;
        }
        return index;
    }

    /// <summary>
    /// Resolves a raw value against the pre-built index.
    /// Returns (MatchedId, IsAmbiguous=false) on a clear win; (null, IsAmbiguous=true) when
    /// multiple candidates tie near the top; (null, false) when nothing is close enough.
    /// </summary>
    public static (string? MatchedId, bool IsAmbiguous) Resolve(
        string value,
        IReadOnlyDictionary<string, string> index)
    {
        var normalized = Normalize(value);

        // 1. Exact normalized match — always wins.
        if (index.TryGetValue(normalized, out var exactId))
            return (exactId, false);

        if (index.Count == 0)
            return (null, false);

        // 2. Levenshtein-only scoring: score every key and find best + second-best.
        double best = 0, secondBest = 0;
        string? bestKey = null;

        foreach (var key in index.Keys)
        {
            double score = LevenshteinRatio(normalized, key);
            if (score > best)
            {
                secondBest = best;
                best = score;
                bestKey = key;
            }
            else if (score > secondBest)
            {
                secondBest = score;
            }
        }

        // Not close enough to accept at all.
        if (best < FuzzyAcceptThreshold)
            return (null, false);

        // Close enough but tied with another — ambiguous.
        if (best - secondBest < TieBreakMargin)
            return (null, true);

        // Single high-confidence winner.
        return (index[bestKey!], false);
    }

    // --- Helpers ---

    /// <summary>
    /// Normalizes a name for comparison: trim, lower-invariant, collapse whitespace,
    /// strip trailing punctuation characters.
    /// </summary>
    internal static string Normalize(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        s = WhitespaceRun.Replace(s, " ");
        s = s.TrimEnd(TrailingPunctuation);
        return s;
    }

    /// <summary>
    /// Levenshtein similarity ratio in [0, 1]: 1 - distance / max(lenA, lenB).
    ///
    /// Using max-length as the denominator is conservative for partial-substring queries:
    /// a short query against a longer candidate is penalised by the length difference,
    /// so "office" (6 chars) vs "office depot" (12 chars) scores 0.50, well below the
    /// 0.92 threshold, and is correctly rejected.
    /// </summary>
    private static double LevenshteinRatio(string query, string candidate)
    {
        if (query == candidate) return 1.0;
        if (query.Length == 0 || candidate.Length == 0) return 0.0;

        int dist = LevenshteinDistance(query, candidate);
        return 1.0 - (double)dist / Math.Max(query.Length, candidate.Length);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int la = a.Length, lb = b.Length;
        // Use two-row DP to keep memory O(min(la,lb)).
        if (la < lb)
        {
            // Swap so a is always the shorter string.
            (a, b, la, lb) = (b, a, lb, la);
        }

        var prev = new int[lb + 1];
        var curr = new int[lb + 1];

        for (int j = 0; j <= lb; j++) prev[j] = j;

        for (int i = 1; i <= la; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= lb; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            Array.Copy(curr, prev, lb + 1);
        }

        return prev[lb];
    }
}
