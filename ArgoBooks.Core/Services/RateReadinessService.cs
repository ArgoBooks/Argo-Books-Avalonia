using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>Whether all required exact-date rates are cached.</summary>
public enum RateReadinessStatus { Ready, Unavailable }

/// <summary>Why rates could not be made ready (for the user-facing message).</summary>
public enum RateUnavailableReason { None, NoInternet, ServerUnreachable, Unknown, RateLimited }

/// <summary>
/// Result of <see cref="RateReadinessService.EnsureRatesAsync"/>. <see cref="FutureDatesDeferred"/>
/// lists dates that cannot be priced (future) so the caller can mark those rows pending.
/// </summary>
public sealed record RateReadiness(
    RateReadinessStatus Status,
    RateUnavailableReason Reason,
    IReadOnlyList<DateTime> FutureDatesDeferred);

/// <summary>
/// Ensures the exact-date USD rates needed for a set of transaction dates are cached, fetching any
/// that are missing. Used before import (and other bulk operations) so money converts at the exact
/// date and never falls back to a wrong date. Future dates are never required (unpriceable) and are
/// returned for deferral. The server returns USD-&gt;all per date, so caching a date covers every
/// currency for that date. See docs/Calculations.md (Rule 4).
/// </summary>
public sealed class RateReadinessService
{
    private const string ProbeCurrency = "EUR"; // any always-present currency confirms a date is cached

    private readonly ExchangeRateService _rates;
    private readonly IConnectivityService _connectivity;
    private readonly IErrorLogger? _errorLogger;

    public RateReadinessService(ExchangeRateService rates, IConnectivityService connectivity, IErrorLogger? errorLogger = null)
    {
        _rates = rates;
        _connectivity = connectivity;
        _errorLogger = errorLogger;
    }

    public async Task<RateReadiness> EnsureRatesAsync(
        IEnumerable<DateTime> dates, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var distinct = dates.Select(d => d.Date).Distinct().ToList();
        var future = distinct.Where(d => d > today).ToList();
        var required = distinct.Where(d => d <= today).ToList();

        var missing = required.Where(d => _rates.GetExchangeRate("USD", ProbeCurrency, d) <= 0).ToList();
        _errorLogger?.LogInfo(
            $"[RateGate] {distinct.Count} distinct dates: {required.Count} required, {future.Count} future-deferred, {missing.Count} missing from cache" +
            (missing.Count > 0 ? $": {string.Join(", ", missing.OrderBy(d => d).Take(40).Select(d => d.ToString("yyyy-MM-dd")))}" : "."));
        if (missing.Count == 0)
            return new RateReadiness(RateReadinessStatus.Ready, RateUnavailableReason.None, future);

        // Quick offline check: if there is clearly no internet, surface the pause prompt immediately
        // rather than grinding through a slow per-date fetch-and-retry loop that is bound to fail.
        if (!await _connectivity.IsInternetAvailableAsync(ct))
            return new RateReadiness(RateReadinessStatus.Unavailable, RateUnavailableReason.NoInternet, future);

        // Try to fetch the missing dates (PreloadRatesAsync batches and falls back per-date). It
        // reports 0-100% progress so the import overlay can show a determinate bar for this phase.
        try
        {
            await _rates.PreloadRatesAsync(missing, progress, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (RateLimitedException)
        {
            // The server's rate limiter rejected us. We backed off immediately (no per-date fanout),
            // so tell the user to wait a moment rather than showing a misleading connection error.
            _errorLogger?.LogError(
                $"[RateGate] Rate-limited by the server while fetching {missing.Count} dates; backed off without fanning out. Wait and retry.",
                ErrorCategory.Import, "RateReadiness");
            return new RateReadiness(RateReadinessStatus.Unavailable, RateUnavailableReason.RateLimited, future);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"[RateGate] PreloadRatesAsync threw: {ex.Message}", "RateReadiness");
            /* fall through to re-check + reason classification */
        }

        var stillMissing = missing.Where(d => _rates.GetExchangeRate("USD", ProbeCurrency, d) <= 0).ToList();
        if (stillMissing.Count == 0)
            return new RateReadiness(RateReadinessStatus.Ready, RateUnavailableReason.None, future);

        var reason = await ClassifyAsync(ct);
        // LogError (not Warning) so it reaches telemetry and survives the session: this is the exact
        // set of dates that blocked the import, the single most useful clue for "could not get rates".
        _errorLogger?.LogError(
            $"[RateGate] BLOCKED: {stillMissing.Count} of {missing.Count} required dates still unpriced after fetch (reason={reason}): " +
            $"{string.Join(", ", stillMissing.OrderBy(d => d).Take(40).Select(d => d.ToString("yyyy-MM-dd")))}",
            ErrorCategory.Import, "RateReadiness");
        return new RateReadiness(RateReadinessStatus.Unavailable, reason, future);
    }

    private async Task<RateUnavailableReason> ClassifyAsync(CancellationToken ct)
    {
        if (!await _connectivity.IsInternetAvailableAsync(ct))
            return RateUnavailableReason.NoInternet;
        if (!await _connectivity.IsHostReachableAsync(ApiConfig.BaseUrl, ct))
            return RateUnavailableReason.ServerUnreachable;
        return RateUnavailableReason.Unknown;
    }
}
