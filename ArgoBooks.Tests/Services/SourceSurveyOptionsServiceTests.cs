using System.Net;
using System.Text;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for SourceSurveyOptionsService. Mocks HttpClient so we can verify
/// parsing and the bundled-default fallback without standing up the PHP server.
/// </summary>
public class SourceSurveyOptionsServiceTests
{
    private static SourceSurveyOptionsService BuildService(HttpStatusCode status, string body)
        => new(new HttpClient(new StubHandler(status, body)));

    private static SourceSurveyOptionsService BuildThrowingService(Exception ex)
        => new(new HttpClient(new ThrowingHandler(ex)));

    [Fact]
    public async Task GetChoicesAsync_ValidJson_ParsesOptionsIncludingFreeform()
    {
        var service = BuildService(HttpStatusCode.OK,
            "{\"options\":[" +
            "{\"key\":\"google\",\"label\":\"Google\"}," +
            "{\"key\":\"tiktok\",\"label\":\"TikTok\"}," +
            "{\"key\":\"other\",\"label\":\"Other\",\"freeform\":true}" +
            "]}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(3, options.Count);
        Assert.Equal("google", options[0].Key);
        Assert.Equal("TikTok", options[1].Label);
        Assert.False(options[1].Freeform);
        Assert.Equal("other", options[2].Key);
        Assert.True(options[2].Freeform);
    }

    [Fact]
    public async Task GetChoicesAsync_NonSuccessStatus_ReturnsDefaults()
    {
        var service = BuildService(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    [Fact]
    public async Task GetChoicesAsync_MalformedJson_ReturnsDefaults()
    {
        var service = BuildService(HttpStatusCode.OK, "not json at all");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    [Fact]
    public async Task GetChoicesAsync_EmptyList_ReturnsDefaults()
    {
        var service = BuildService(HttpStatusCode.OK, "{\"options\":[]}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    [Fact]
    public async Task GetChoicesAsync_MissingOptionsKey_ReturnsDefaults()
    {
        // 2xx with valid JSON but no "options" property.
        var service = BuildService(HttpStatusCode.OK, "{}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    [Fact]
    public async Task GetChoicesAsync_CallerCancellation_Throws()
    {
        var service = BuildService(HttpStatusCode.OK,
            "{\"options\":[{\"key\":\"google\",\"label\":\"Google\"}]}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetChoicesAsync(cts.Token));
    }

    [Fact]
    public async Task GetChoicesAsync_SkipsEntriesMissingKeyOrLabel()
    {
        var service = BuildService(HttpStatusCode.OK,
            "{\"options\":[" +
            "{\"key\":\"\",\"label\":\"No key\"}," +
            "{\"key\":\"nolabel\"}," +
            "{\"key\":\"reddit\",\"label\":\"Reddit\"}" +
            "]}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Single(options);
        Assert.Equal("reddit", options[0].Key);
    }

    [Fact]
    public async Task GetChoicesAsync_AllEntriesInvalid_ReturnsDefaults()
    {
        var service = BuildService(HttpStatusCode.OK,
            "{\"options\":[{\"key\":\"\",\"label\":\"\"}]}");

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    [Fact]
    public async Task GetChoicesAsync_NetworkError_ReturnsDefaults()
    {
        var service = BuildThrowingService(new HttpRequestException("DNS fail"));

        var options = (await service.GetChoicesAsync()).Sources;

        Assert.Equal(SourceSurveyOptionsService.DefaultOptions, options);
    }

    // A website that has not been updated yet sends no goals. The sources it does send are
    // kept, and the second question falls back to the goals the app ships with.
    [Fact]
    public async Task GetChoicesAsync_TakesGoalsFromTheServer_AndItsOwnWhenThereAreNone()
    {
        var withGoals = BuildService(HttpStatusCode.OK,
            "{\"options\":[{\"key\":\"google\",\"label\":\"Google\"}]," +
            "\"goals\":[{\"key\":\"tips\",\"label\":\"Track tips\"}]}");
        var withoutGoals = BuildService(HttpStatusCode.OK,
            "{\"options\":[{\"key\":\"google\",\"label\":\"Google\"}]}");

        Assert.Equal("tips", Assert.Single((await withGoals.GetChoicesAsync()).Goals).Key);

        var fallback = await withoutGoals.GetChoicesAsync();
        Assert.Equal("google", Assert.Single(fallback.Sources).Key);
        Assert.Equal(SourceSurveyOptionsService.DefaultGoals, fallback.Goals);
    }

    [Fact]
    public void DefaultOptions_IncludeNewPlatformsAndSingleFreeform()
    {
        var defaults = SourceSurveyOptionsService.DefaultOptions;

        Assert.Contains(defaults, o => o.Key == "capterra");
        Assert.Contains(defaults, o => o.Key == "producthunt");
        Assert.Single(defaults, o => o.Freeform);
        Assert.Equal("other", defaults[^1].Key);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw ex;
    }
}
