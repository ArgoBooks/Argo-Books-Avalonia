using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the PaymentPortalService class.
/// </summary>
[Collection("DotEnv")]
public class PaymentPortalServiceTests
{
    #region CheckStatusAsync Tests

    [Fact]
    public async Task CheckStatusAsync_NotConfigured_ReturnsNotConnected()
    {
        // Remove the portal API key from both the in-memory cache and
        // environment so the service takes the "not configured" path.
        var priorKey = DotEnv.Get(PortalSettings.ApiKeyEnvVar);
        DotEnv.Unset(PortalSettings.ApiKeyEnvVar);

        try
        {
            var service = new PaymentPortalService();

            var result = await service.CheckStatusAsync();

            Assert.False(result.Connected);
            Assert.False(result.Success);
        }
        finally
        {
            if (!string.IsNullOrEmpty(priorKey))
                DotEnv.SetInMemory(PortalSettings.ApiKeyEnvVar, priorKey);
        }
    }

    #endregion
}
