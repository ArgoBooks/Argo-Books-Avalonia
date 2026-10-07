using System.Globalization;
using ArgoBooks.Core.Models.Common;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for CurrencyInfo formatting.
/// </summary>
public class CurrencyInfoTests
{
    private static T WithCulture<T>(string culture, Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() =>
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            result = func();
        });
        thread.Start();
        thread.Join();
        return result;
    }

    [Fact]
    public void Format_OnNonUsCulture_UsesInvariantGroupingAndDecimal()
    {
        // The documented output is "$1,234.56" (comma thousands, dot decimal).
        var usd = CurrencyInfo.GetByCode("USD");

        var result = WithCulture("de-DE", () => usd.Format(1234.56m));

        Assert.Equal("$1,234.56", result);
    }
}
