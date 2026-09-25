using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A row waiting for its USD value counts 0 in every USD total (Calculations.md §3). The display sum
/// has to agree even for a row already in the display currency, or a card counts an expense its own
/// % change and Net Profit leave out.
/// </summary>
public class DisplaySumPendingTests
{
    [Fact]
    public void RowInTheDisplayCurrency_StillWaitingForItsRate_CountsZero()
    {
        // With no company open the display currency is USD.
        var expenses = new[]
        {
            new Expense { Id = "E1", Date = new DateTime(2026, 5, 1), OriginalCurrency = "USD", Total = 10m, TotalUSD = 10m },
            new Expense { Id = "E2", Date = new DateTime(2026, 5, 2), OriginalCurrency = "USD", Total = 5m, IsPendingConversion = true }
        };

        var complete = CurrencyService.TrySumDisplayFromUSD(
            expenses, e => e.Total, e => e.OriginalCurrency, e => e.TotalUSD, e => e.Date, out var total);

        Assert.True(complete);
        Assert.Equal(10m, total);
    }
}
