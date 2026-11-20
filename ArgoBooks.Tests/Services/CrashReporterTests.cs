using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A task that fails with nobody waiting on it is reported wrapped in an AggregateException
/// with no stack trace, so the report has to describe the failure inside it instead.
/// </summary>
public class CrashReporterTests
{
    [Fact]
    public void Unwrap_ATaskFailure_ReportsTheFailureInside()
    {
        var inner = new InvalidOperationException("WebView2 runtime not found");

        Assert.Same(inner, CrashReporter.Unwrap(new AggregateException(inner)));
    }

    [Fact]
    public void Unwrap_NestedWrappers_ReachTheFailure()
    {
        var inner = new InvalidOperationException("inner");

        Assert.Same(inner, CrashReporter.Unwrap(new AggregateException(new AggregateException(inner))));
    }

    [Fact]
    public void Unwrap_SeveralFailures_KeepsTheWrapper()
    {
        var aggregate = new AggregateException(new InvalidOperationException("a"), new IOException("b"));

        Assert.Same(aggregate, CrashReporter.Unwrap(aggregate));
    }

    [Fact]
    public void Unwrap_AnOrdinaryException_IsUnchanged()
    {
        var exception = new NullReferenceException();

        Assert.Same(exception, CrashReporter.Unwrap(exception));
    }
}
