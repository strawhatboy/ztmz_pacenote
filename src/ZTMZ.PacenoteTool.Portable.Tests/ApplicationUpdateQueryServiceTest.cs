#nullable enable

using System.Net;
using System.Net.Http;
using System.Text;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ApplicationUpdateQueryServiceTest
{
    [Fact]
    public async Task FindUpdateAsyncPrefersApplicableStableDefinition()
    {
        using var client = CreateClient(new Dictionary<string, string>
        {
            ["https://updates/stable"] = Definition("2.0.0", "1.0.0"),
            ["https://updates/beta"] = Definition("3.0.0", "1.0.0")
        });
        var service = new ApplicationUpdateQueryService(client);

        var result = await service.FindUpdateAsync(new ApplicationUpdateQueryOptions(
            "https://updates/stable",
            "https://updates/beta",
            new Version("1.5.0"),
            null,
            IncludeBetaChannel: true));

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.version);
    }

    [Fact]
    public async Task FindUpdateAsyncUsesBetaAndLocalizedChangelogFallback()
    {
        using var client = CreateClient(new Dictionary<string, string>
        {
            ["https://updates/stable"] = Definition("1.5.0", "1.0.0"),
            ["https://updates/beta"] = """
                {
                  "version": "2.0.0",
                  "url": "https://updates/installer",
                  "changelog": "https://updates/default",
                  "changelogs": {
                    "en-us": "https://updates/en",
                    "zh-cn": "https://updates/zh"
                  },
                  "minVersionSupported": "1.0.0"
                }
                """
        });
        var service = new ApplicationUpdateQueryService(client);

        var result = await service.FindUpdateAsync(new ApplicationUpdateQueryOptions(
            "https://updates/stable",
            "https://updates/beta",
            new Version("1.5.0"),
            null,
            IncludeBetaChannel: true));

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.version);
        Assert.Equal("https://updates/zh", result.ResolveChangelogUrl("zh-TW"));
    }

    private static HttpClient CreateClient(IReadOnlyDictionary<string, string> responses) =>
        new(new StubHttpMessageHandler(responses));

    private static string Definition(string version, string minimumVersion) => $$"""
        {
          "version": "{{version}}",
          "url": "https://updates/installer",
          "changelog": "https://updates/changelog",
          "minVersionSupported": "{{minimumVersion}}"
        }
        """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _responses;

        public StubHttpMessageHandler(IReadOnlyDictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(_responses.TryGetValue(url, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
