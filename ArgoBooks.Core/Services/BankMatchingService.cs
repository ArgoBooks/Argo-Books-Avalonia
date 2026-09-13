using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Matches imported bank statement lines against recorded book entries (expenses, revenue,
/// invoices, payments). Matching is deterministic and runs locally.
/// </summary>
public class BankMatchingService
{
    // Score weights. amount(0.6) + date(0.3) + description(0.1) = 1.0 maximum.
    private const double AmountExactScore = 0.6;
    private const double AmountToleranceScore = 0.4;
    private const double DateMaxScore = 0.3;
    private const double DescMaxScore = 0.1;

    private static readonly HashSet<string> NoiseTokens =
    [
        "pos", "ach", "debit", "credit", "card", "purchase", "payment", "pmt", "ref",
        "reference", "transaction", "txn", "visa", "mastercard", "amex", "eft", "online",
        "pre", "auth", "authorized", "withdrawal", "deposit", "transfer", "the", "and", "inc", "llc", "ltd"
    ];

    #region Deterministic matching

    public const string StripePayoutIgnoreReason = "Stripe payout (already imported)";

    /// <summary>
    /// Runs deterministic matching over the lines, auto-confirming unambiguous high-confidence
    /// matches and surfacing the rest as suggestions. Mutates the matched book records' flags.
    /// </summary>
    public BankMatchingResult MatchDeterministic(IReadOnlyList<BankStatementLine> lines, CompanyData data, BankMatchingOptions options)
    {
        var result = new BankMatchingResult { Lines = lines.ToList() };

        ReleaseStaleMatches(result.Lines, data);
        AutoIgnoreStripePayouts(result.Lines, data, options);

        // All in-scope book records, aligned to bank sign convention, that aren't already matched.
        var records = BuildRecordRefs(data, options.Scope).Where(r => !IsRecordMatched(data, r)).ToList();

        foreach (var line in result.Lines)
        {
            if (line.MatchStatus is BankLineMatchStatus.Matched or BankLineMatchStatus.Ignored)
                continue;

            var candidates = ScoreCandidates(line, records, options);

            if (candidates.Count == 0)
            {
                line.MatchStatus = BankLineMatchStatus.Unmatched;
                result.UnmatchedLineCount++;
                continue;
            }

            var best = candidates[0];
            var gap = candidates.Count > 1 ? best.Confidence - candidates[1].Confidence : 1.0;

            if (best.Confidence >= options.AutoMatchThreshold && gap >= options.AutoMatchAmbiguityGap)
            {
                best.IsAutoMatch = true;
                ConfirmMatch(line, best, data);
                records.RemoveAll(r => r.Type == best.RecordType && r.Id == best.RecordId);
                result.AutoMatchedCount++;
            }
            else if (best.Confidence >= options.SuggestThreshold)
            {
                line.MatchStatus = BankLineMatchStatus.Suggested;
                result.CandidatesByLineId[line.Id] = candidates.Where(c => c.Confidence >= options.SuggestThreshold).ToList();
                result.SuggestedCount++;
            }
            else
            {
                line.MatchStatus = BankLineMatchStatus.Unmatched;
                result.UnmatchedLineCount++;
            }
        }

        // Reverse view: in-scope book records still unmatched (possibly missing from the statement).
        var stillUnmatched = BuildRecordRefs(data, options.Scope).Where(r => !IsRecordMatched(data, r)).ToList();
        FlagDuplicates(stillUnmatched, options.DateWindowDays);
        result.UnmatchedBookRecords = stillUnmatched;

        return result;
    }

    /// <summary>
    /// Puts a Matched line back to Unmatched when its record no longer points back at it: the
    /// record was deleted, its id was reused by a new record after an undo restored the id
    /// counters, or the record is flagged to a different line. Matching skips Matched lines, so
    /// without this such a line would stay Matched for good.
    /// </summary>
    private void ReleaseStaleMatches(List<BankStatementLine> lines, CompanyData data)
    {
        var released = false;
        foreach (var line in lines)
        {
            if (line.MatchStatus != BankLineMatchStatus.Matched) continue;

            // A flag with no line id can't be checked against the line, so it is trusted.
            if (line.MatchedRecordType is { } type && line.MatchedRecordId is { } id &&
                GetRecordMatchFlags(data, type, id) is { Matched: true } flags &&
                (flags.LineId == null || flags.LineId == line.Id))
                continue;

            RejectMatch(line);
            released = true;
        }

        if (released) data.MarkAsModified();
    }

    /// <summary>
    /// Auto-ignores deposits that are an already-imported Stripe payout (amount within one minor
    /// unit, a cent or a yen, OR 1%, date within DateWindowDays), so they aren't double-counted
    /// against a book record. A payout accounts for one deposit, the closest by date and then by
    /// amount, and the line keeps the payout's id so a later run can't spend the same payout on
    /// another deposit.
    /// </summary>
    private static void AutoIgnoreStripePayouts(List<BankStatementLine> lines, CompanyData data, BankMatchingOptions options)
    {
        var payouts = data.Settings.Integrations.Stripe.ImportedPayouts;
        if (payouts.Count == 0) return;
        var companyCurrency = data.Settings.Localization.Currency;

        var spent = lines
            .Where(l => l.MatchStatus == BankLineMatchStatus.Ignored && l.StripePayoutId != null)
            .Select(l => l.StripePayoutId!)
            .ToHashSet(StringComparer.Ordinal);

        // Lines ignored before the payout id was recorded claim a payout first, so a file saved by
        // an older version doesn't spend that payout again on a different deposit.
        var pairs = new List<(BankStatementLine Line, Models.Integrations.StripePayoutRecord Payout, bool Legacy, double Days, long CentsOff)>();
        foreach (var line in lines)
        {
            var legacy = line.MatchStatus == BankLineMatchStatus.Ignored && line.StripePayoutId == null &&
                         line.IgnoreReason == StripePayoutIgnoreReason;
            var open = line.Amount > 0 &&
                       line.MatchStatus is not (BankLineMatchStatus.Matched or BankLineMatchStatus.Ignored);
            if (!legacy && !open) continue;

            foreach (var payout in payouts)
            {
                if (!spent.Contains(payout.StripePayoutId) && PayoutFit(line, payout, companyCurrency, options) is { } fit)
                    pairs.Add((line, payout, legacy, fit.Days, fit.CentsOff));
            }
        }

        var claimed = new HashSet<BankStatementLine>();
        foreach (var (line, payout, legacy, _, _) in pairs
                     .OrderBy(p => p.Legacy ? 0 : 1).ThenBy(p => p.Days).ThenBy(p => p.CentsOff))
        {
            if (claimed.Contains(line) || !spent.Add(payout.StripePayoutId)) continue;

            claimed.Add(line);
            line.StripePayoutId = payout.StripePayoutId;
            if (legacy) continue;

            line.MatchStatus = BankLineMatchStatus.Ignored;
            line.IgnoreReason = StripePayoutIgnoreReason;
        }
    }

    private static (double Days, long CentsOff)? PayoutFit(
        BankStatementLine line, Models.Integrations.StripePayoutRecord payout, string companyCurrency,
        BankMatchingOptions options)
    {
        var currency = string.IsNullOrWhiteSpace(payout.Currency) ? companyCurrency : payout.Currency;
        var lineUnits = Integrations.ArgoMoney.ToMinorUnits(line.Amount, currency);
        var diff = Math.Abs(payout.AmountCents - lineUnits);
        var within = diff <= 1 || diff <= (long)Math.Round(Math.Abs(payout.AmountCents) * 0.01);
        var days = Math.Abs((payout.Date.Date - line.Date.Date).TotalDays);
        return within && days <= options.DateWindowDays ? (days, diff) : null;
    }

    /// <summary>
    /// Returns ranked match candidates (≥ SuggestThreshold) for a single line against the
    /// currently unmatched, in-scope book records. Pure: does not mutate anything.
    /// </summary>
    public List<BankMatchCandidate> FindCandidates(BankStatementLine line, CompanyData data, BankMatchingOptions options)
    {
        var records = BuildRecordRefs(data, options.Scope).Where(r => !IsRecordMatched(data, r));
        return ScoreCandidates(line, records, options)
            .Where(c => c.Confidence >= options.SuggestThreshold)
            .ToList();
    }

    /// <summary>
    /// Returns every in-scope book record paired with whether it is currently matched to a bank line.
    /// Reads the live BankMatched flags, so it reflects manual matches made during the session. Used to
    /// build the books-side month overview (matched vs total per month).
    /// </summary>
    public List<(BookRecordRef Record, bool IsMatched)> GetBookRecordsWithStatus(CompanyData data, BankMatchingOptions options) =>
        BuildRecordRefs(data, options.Scope).Select(r => (r, IsRecordMatched(data, r))).ToList();

    /// <summary>
    /// Returns all unmatched, in-scope records the user can manually pick to match a line,
    /// filtered to the line's direction (money out -> expenses, money in -> revenue). Not scored.
    /// </summary>
    public List<BankMatchCandidate> GetManualMatchOptions(BankStatementLine line, CompanyData data, BankMatchingOptions options)
    {
        return BuildRecordRefs(data, options.Scope)
            .Where(r => !IsRecordMatched(data, r))
            .Where(r => line.Amount == 0
                        || (line.Amount < 0 && r.Type == BookRecordType.Expense)
                        || (line.Amount > 0 && r.Type != BookRecordType.Expense))
            .OrderByDescending(r => r.Date)
            .Select(r => new BankMatchCandidate
            {
                LineId = line.Id,
                RecordType = r.Type,
                RecordId = r.Id,
                RecordDescription = r.Description,
                RecordDate = r.Date,
                RecordAmount = r.Amount,
                Reason = MatchReason.Manual
            })
            .ToList();
    }

    /// <summary>
    /// Scores every amount-compatible record against a line and returns candidates ranked best-first.
    /// </summary>
    private static List<BankMatchCandidate> ScoreCandidates(BankStatementLine line, IEnumerable<BookRecordRef> records, BankMatchingOptions options)
    {
        var candidates = new List<BankMatchCandidate>();
        foreach (var record in records)
        {
            var amountDiff = Math.Abs(line.Amount - record.Amount);
            double amountScore;
            if (amountDiff == 0m) amountScore = AmountExactScore;
            else if (amountDiff <= options.AmountTolerance) amountScore = AmountToleranceScore;
            else continue; // amount must match (exactly or within tolerance)

            var daysApart = Math.Abs((line.Date.Date - record.Date.Date).TotalDays);
            if (daysApart > options.DateWindowDays && amountScore < AmountExactScore)
                continue; // outside window and not an exact-amount hit
            var dateScore = daysApart <= options.DateWindowDays
                ? DateMaxScore * (1 - daysApart / Math.Max(1, options.DateWindowDays))
                : 0;

            var descScore = DescMaxScore * Similarity(NormalizeDescription(line.Description), NormalizeDescription(record.Description));

            var confidence = amountScore + dateScore + descScore;

            candidates.Add(new BankMatchCandidate
            {
                LineId = line.Id,
                RecordType = record.Type,
                RecordId = record.Id,
                RecordDescription = record.Description,
                RecordDate = record.Date,
                RecordAmount = record.Amount,
                Confidence = Math.Round(confidence, 4),
                Reason = DetermineReason(amountScore, daysApart, descScore)
            });
        }

        // Rank best first; for money-in ties prefer Payment over Invoice to avoid double counting.
        return candidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => RecordTypeRank(c.RecordType, line.Amount))
            .ToList();
    }

    private static int RecordTypeRank(BookRecordType type, decimal lineAmount)
    {
        // For money-in lines, rank Payment ahead of Invoice (payments are the actual cash event).
        if (lineAmount > 0)
            return type switch
            {
                BookRecordType.Payment => 0,
                BookRecordType.Revenue => 1,
                BookRecordType.Invoice => 2,
                _ => 3
            };
        return 0;
    }

    private static MatchReason DetermineReason(double amountScore, double daysApart, double descScore)
    {
        var amountExact = amountScore >= AmountExactScore;
        if (amountExact && daysApart <= 1) return MatchReason.ExactAmountAndDate;
        if (amountExact && descScore > 0) return MatchReason.ExactAmountFuzzyDesc;
        return MatchReason.AmountWithinWindow;
    }

    private static void FlagDuplicates(List<BookRecordRef> records, int dateWindowDays)
    {
        // Group by amount first so we only compare records that could possibly be duplicates,
        // then compare within each (typically tiny) group by date proximity.
        foreach (var group in records.GroupBy(r => r.Amount).Where(g => g.Count() > 1))
        {
            var byDate = group.OrderBy(r => r.Date).ToList();
            for (int i = 0; i < byDate.Count; i++)
            {
                for (int j = i + 1; j < byDate.Count; j++)
                {
                    var daysApart = (byDate[j].Date.Date - byDate[i].Date.Date).TotalDays;
                    if (daysApart > dateWindowDays) break; // sorted by date: no later record can be closer
                    byDate[i].IsPossibleDuplicate = true;
                    byDate[j].IsPossibleDuplicate = true;
                }
            }
        }
    }

    #endregion

    #region Confirm / reject / unlink

    /// <summary>
    /// Confirms a candidate as the match for a line, setting the persisted flag on the book record.
    /// Returns false, changing nothing, when the record is already matched to a different line: one
    /// record backing two lines lets unlinking either clear the flag the other still relies on.
    /// </summary>
    public bool ConfirmMatch(BankStatementLine line, BankMatchCandidate candidate, CompanyData data)
    {
        if (GetRecordMatchFlags(data, candidate.RecordType, candidate.RecordId) is { Matched: true } flags &&
            flags.LineId != line.Id)
            return false;

        // If the line was already matched to a different record, release that record first so it
        // doesn't stay flagged as matched (which would orphan it in the unmatched view).
        if (line.MatchedRecordType is { } prevType && line.MatchedRecordId is { } prevId &&
            (prevType != candidate.RecordType || prevId != candidate.RecordId))
        {
            SetRecordMatchState(data, prevType, prevId, matched: false, null);
        }

        line.MatchStatus = BankLineMatchStatus.Matched;
        line.MatchedRecordType = candidate.RecordType;
        line.MatchedRecordId = candidate.RecordId;
        line.MatchedDate = DateTime.UtcNow;
        line.MatchConfidence = candidate.Confidence;

        SetRecordMatchState(data, candidate.RecordType, candidate.RecordId, matched: true, line.Id);
        data.MarkAsModified();
        return true;
    }

    /// <summary>Marks a line as unmatched without touching any book record (rejects suggestions).</summary>
    public void RejectMatch(BankStatementLine line)
    {
        line.MatchStatus = BankLineMatchStatus.Unmatched;
        line.MatchedRecordType = null;
        line.MatchedRecordId = null;
        line.MatchedDate = null;
        line.MatchConfidence = 0;
    }

    /// <summary>Unlinks a confirmed match, clearing the flag on the previously matched book record.</summary>
    public void UnlinkMatch(BankStatementLine line, CompanyData data)
    {
        if (line.MatchedRecordType is { } type && line.MatchedRecordId is { } id)
        {
            SetRecordMatchState(data, type, id, matched: false, null);
            data.MarkAsModified();
        }
        RejectMatch(line);
    }

    private static void SetRecordMatchState(CompanyData data, BookRecordType type, string id, bool matched, string? lineId)
    {
        var date = matched ? (DateTime?)DateTime.UtcNow : null;
        switch (type)
        {
            case BookRecordType.Expense:
                Apply(data.Expenses.FirstOrDefault(e => e.Id == id));
                break;
            case BookRecordType.Revenue:
                Apply(data.Revenues.FirstOrDefault(r => r.Id == id));
                break;
            case BookRecordType.Invoice:
                ApplyInvoice(data.Invoices.FirstOrDefault(i => i.Id == id));
                break;
            case BookRecordType.Payment:
                ApplyPayment(data.Payments.FirstOrDefault(p => p.Id == id));
                break;
        }

        void Apply(Transaction? t)
        {
            if (t == null) return;
            t.BankMatched = matched;
            t.BankMatchedDate = date;
            t.BankMatchedLineId = lineId;
        }
        void ApplyInvoice(Invoice? inv)
        {
            if (inv == null) return;
            inv.BankMatched = matched;
            inv.BankMatchedDate = date;
            inv.BankMatchedLineId = lineId;
        }
        void ApplyPayment(Payment? p)
        {
            if (p == null) return;
            p.BankMatched = matched;
            p.BankMatchedDate = date;
            p.BankMatchedLineId = lineId;
        }
    }

    private static bool IsRecordMatched(CompanyData data, BookRecordRef r) =>
        GetRecordMatchFlags(data, r.Type, r.Id)?.Matched ?? false;

    /// <summary>The record's bank-match flag and the line it names, or null when no such record exists.</summary>
    private static (bool Matched, string? LineId)? GetRecordMatchFlags(CompanyData data, BookRecordType type, string id)
    {
        switch (type)
        {
            case BookRecordType.Expense when data.Expenses.FirstOrDefault(e => e.Id == id) is { } e:
                return (e.BankMatched, e.BankMatchedLineId);
            case BookRecordType.Revenue when data.Revenues.FirstOrDefault(r => r.Id == id) is { } r:
                return (r.BankMatched, r.BankMatchedLineId);
            case BookRecordType.Invoice when data.Invoices.FirstOrDefault(i => i.Id == id) is { } i:
                return (i.BankMatched, i.BankMatchedLineId);
            case BookRecordType.Payment when data.Payments.FirstOrDefault(p => p.Id == id) is { } p:
                return (p.BankMatched, p.BankMatchedLineId);
            default:
                return null;
        }
    }

    #endregion

    #region Record extraction

    private static List<BookRecordRef> BuildRecordRefs(CompanyData data, HashSet<BookRecordType> scope)
    {
        var refs = new List<BookRecordRef>();

        if (scope.Contains(BookRecordType.Expense))
            refs.AddRange(data.Expenses.Select(e => new BookRecordRef
            {
                Type = BookRecordType.Expense, Id = e.Id, Description = e.Description, Date = e.Date, Amount = -e.Total
            }));

        if (scope.Contains(BookRecordType.Revenue))
            refs.AddRange(data.Revenues.Select(r => new BookRecordRef
            {
                Type = BookRecordType.Revenue, Id = r.Id, Description = r.Description, Date = r.Date, Amount = r.Total
            }));

        if (scope.Contains(BookRecordType.Payment))
            refs.AddRange(data.Payments.Select(p => new BookRecordRef
            {
                Type = BookRecordType.Payment,
                Id = p.Id,
                Description = string.IsNullOrWhiteSpace(p.Notes) ? p.ReferenceNumber ?? string.Empty : p.Notes,
                Date = p.Date,
                Amount = p.Amount // refunds are stored negative => money out
            }));

        if (scope.Contains(BookRecordType.Invoice))
            refs.AddRange(data.Invoices.Select(i => new BookRecordRef
            {
                Type = BookRecordType.Invoice, Id = i.Id, Description = i.InvoiceNumber, Date = i.IssueDate, Amount = i.Total
            }));

        return refs;
    }

    #endregion

    #region Text similarity

    /// <summary>
    /// Normalizes a description for fuzzy comparison: lowercase, strip punctuation and common
    /// bank/noise tokens and bare numbers, collapse whitespace.
    /// </summary>
    internal static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var sb = new StringBuilder(description.Length);
        foreach (var ch in description.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');

        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !NoiseTokens.Contains(t))
            .Where(t => !t.All(char.IsDigit)); // drop bare numbers (card trailers, refs)

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Token-set (Jaccard) similarity of two normalized strings, in the range 0..1.
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (setA.Count == 0 || setB.Count == 0) return 0;

        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    #endregion
}
