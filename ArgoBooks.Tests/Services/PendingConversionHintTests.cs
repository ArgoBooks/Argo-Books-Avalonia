using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the friendly explanation shown in the info tooltip next to a "Pending" amount.
/// With no company loaded, the default currency falls back to USD.
/// </summary>
public class PendingConversionHintTests
{
    [Fact]
    public void FutureDate_MentionsItIsInTheFuture_AndPromisesAutoConversion()
    {
        var hint = CurrencyService.BuildPendingConversionHint(750m, "EUR", new DateTime(2099, 12, 15));

        Assert.Contains("in the future", hint);
        Assert.Contains("Dec 15, 2099", hint);
        Assert.Contains("750.00", hint);           // the original amount, formatted
        Assert.Contains("(USD)", hint);            // the default display currency
        Assert.Contains("convert", hint);
    }

    [Fact]
    public void PastDate_PromisesConversionWhenRateAvailable_NotFutureWording()
    {
        var hint = CurrencyService.BuildPendingConversionHint(300m, "GBP", new DateTime(2000, 1, 5));

        Assert.Contains("as soon as the exchange rate", hint);
        Assert.Contains("Jan 5, 2000", hint);
        Assert.DoesNotContain("in the future", hint);
    }

    [Fact]
    public void FutureDate_InTheDefaultCurrency_SaysNothingAboutConverting()
    {
        var hint = CurrencyService.BuildPendingConversionHint(31.69m, "USD", new DateTime(2099, 12, 15));

        Assert.Contains("Dec 15, 2099", hint);
        Assert.Contains("31.69", hint);
        Assert.Contains("totals", hint);
        Assert.DoesNotContain("convert", hint);
        Assert.DoesNotContain("exchange rate", hint);
    }

    [Fact]
    public void PastDate_InTheDefaultCurrency_SaysNothingAboutConverting()
    {
        var hint = CurrencyService.BuildPendingConversionHint(31.69m, "usd", new DateTime(2000, 1, 5));

        Assert.Contains("totals", hint);
        Assert.DoesNotContain("convert", hint);
        Assert.DoesNotContain("exchange rate", hint);
    }

    [Fact]
    public void IncludesTheOriginalCurrencySymbol()
    {
        var hint = CurrencyService.BuildPendingConversionHint(750m, "EUR", new DateTime(2099, 12, 15));

        Assert.Contains("€", hint);
    }
}
