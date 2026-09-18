using ArgoBooks.Core.Services;

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Pure free-tier scan-quota math backing <see cref="ScanUsageStore"/>: the monthly free limit,
/// the calendar-month bucket key a scan count is compared against, and whether a given count is
/// over that limit. Split out from ScanUsageStore so the month-rollover/over-limit logic can be
/// unit-tested (see ScanQuotaTests) without an ISecureStore fake.
/// </summary>
public static class ScanQuota
{
    /// <summary>Free-tier scans allowed per calendar month, as the server last reported it
    /// (<see cref="FreePlanLimits"/>). The AI proxy enforces the real figure server-side for a
    /// device's own X-Device-Id; ScanUsageStore's local count is only a best-effort local mirror
    /// of it - see that class's doc comment.</summary>
    public static int FreeMonthlyLimit => FreePlanLimits.ReceiptScanMonthly;

    /// <summary>The calendar-month bucket key (UTC) a scan count is stored/compared against, e.g.
    /// "2026-07". A count recorded under an earlier month key no longer applies.</summary>
    public static string MonthKey(DateTime utcNow) => utcNow.ToString("yyyy-MM");

    /// <summary>Free scans left this month, floored at 0 (never negative).</summary>
    public static int Remaining(int usedThisMonth) => Math.Max(0, FreeMonthlyLimit - usedThisMonth);

    /// <summary>True once the free monthly allotment is used up.</summary>
    public static bool IsOverLimit(int usedThisMonth) => usedThisMonth >= FreeMonthlyLimit;
}
