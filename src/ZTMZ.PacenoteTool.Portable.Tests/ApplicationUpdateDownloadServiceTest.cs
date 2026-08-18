#nullable enable

using System.Net;
using System.Net.Http;
using System.Text;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ApplicationUpdateDownloadServiceTest
{
    [Fact]
    public async Task DownloadAsyncWritesResponseToExecutableTemporaryFile()
    {
        const string expected = "installer-content";
        using var client = new HttpClient(new StaticResponseHandler(expected));
        var service = new ApplicationUpdateDownloadService(client);

        var downloadedFile = await service.DownloadAsync("https://updates/installer");
        try
        {
            Assert.EndsWith(".exe", downloadedFile, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expected, await File.ReadAllTextAsync(downloadedFile));
        }
        finally
        {
            File.Delete(downloadedFile);
        }
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8)
            });
        }
    }
}
