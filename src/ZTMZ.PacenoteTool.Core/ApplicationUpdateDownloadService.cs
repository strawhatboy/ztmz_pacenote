#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Core;

public sealed record ApplicationUpdateDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond)
{
    public double? Percentage => TotalBytes is > 0
        ? BytesReceived * 100d / TotalBytes.Value
        : null;
}

public sealed class ApplicationUpdateDownloadService
{
    private const int BufferSize = 81920;
    private readonly HttpClient _httpClient;

    public ApplicationUpdateDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> DownloadAsync(
        string url,
        IProgress<ApplicationUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var destinationPath = Path.Combine(
            Path.GetTempPath(),
            $"ztmz-update-{Guid.NewGuid():N}.exe");

        try
        {
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            var buffer = new byte[BufferSize];
            long bytesReceived = 0;
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesReceived += read;
                progress?.Report(new ApplicationUpdateDownloadProgress(
                    bytesReceived,
                    totalBytes,
                    bytesReceived / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001)));
            }

            return destinationPath;
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original download failure.
        }
    }
}
