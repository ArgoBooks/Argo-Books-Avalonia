using System.Net;
using System.Net.Sockets;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which failures are the user's computer or connection, reported as warnings, and which are ours.
/// </summary>
public class EnvironmentFailureTests
{
    [Fact]
    public void ARequestThatCouldNotConnect_IsTheConnection()
    {
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "refused", new SocketException());

        Assert.Equal("ConnectionRefused", EnvironmentFailure.Classify(ex)?.Code);
    }

    /// <summary>
    /// These are thrown after our server has answered, so the request did reach it.
    /// </summary>
    [Fact]
    public void AFailureOurServerAnsweredWith_IsOurs()
    {
        Assert.Null(EnvironmentFailure.Classify(new HttpRequestException("Request to /sync failed with status code 500.")));
        Assert.Null(EnvironmentFailure.Classify(new ServerRateLimitedException("Too many requests")));
        Assert.Null(EnvironmentFailure.Classify(new StatementExtractionException("Not a statement")));
    }

    [Fact]
    public void AServerErrorWithItsStatus_IsOurs()
    {
        Assert.Null(EnvironmentFailure.Classify(new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError)));
    }
}
