using System.Runtime.CompilerServices;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The web view guard catches faults in our own event handlers as well as a missing runtime, and
/// only the second is the user's machine; the first has to be reported as ours.
/// </summary>
public class WebViewEnvironmentTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromOurCode() => throw new InvalidOperationException("our handler");

    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    [Fact]
    public void ThrownByOurCode_AFaultInOurHandler_IsOurs()
    {
        Assert.True(WebViewEnvironment.ThrownByOurCode(Catch(ThrowFromOurCode)));
    }

    [Fact]
    public void ThrownByOurCode_AFaultInsideTheFramework_IsNotOurs()
    {
        Assert.False(WebViewEnvironment.ThrownByOurCode(Catch(() => int.Parse("not a number"))));
    }

    [Fact]
    public void ThrownByOurCode_AFailureNeverThrown_IsNotOurs()
    {
        // With no stack there is no method to blame, so it stays with the web view.
        Assert.False(WebViewEnvironment.ThrownByOurCode(new InvalidOperationException("WebView2 runtime not found")));
    }

    [Fact]
    public void ThrownByOurCode_LooksInsideATaskWrapper()
    {
        Assert.True(WebViewEnvironment.ThrownByOurCode(new AggregateException(Catch(ThrowFromOurCode))));
    }
}
