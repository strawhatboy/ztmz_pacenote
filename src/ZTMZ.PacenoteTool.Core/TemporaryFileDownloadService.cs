#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Core;

public sealed record FileDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond)
{
    public double? Fraction => TotalBytes is > 0
        ? (double)BytesReceived / TotalBytes.Value
        : null;

    public double? Percentage => Fraction * 100d;
}

public sealed class TemporaryFileDownloadService
{
    private const int BufferSize = 81920;
    private readonly HttpClient _httpClient;

    public TemporaryFileDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> DownloadAsync(
        string url,
        string fileNamePrefix,
        string extension,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ValidateFileNamePart(fileNamePrefix, nameof(fileNamePrefix));
        ValidateExtension(extension);

        var destinationPath = Path.Combine(
            Path.GetTempPath(),
            $"{fileNamePrefix}-{Guid.NewGuid():N}{extension}");

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
                progress?.Report(new FileDownloadProgress(
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

    public static void TryDelete(string path)
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
            // Cleanup is best-effort and must not hide the primary operation result.
        }
    }

    private static void ValidateFileNamePart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The value must be a valid file-name segment.", parameterName);
        }
    }

    private static void ValidateExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension[0] != '.' ||
            extension.Length == 1 ||
            extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The extension must include a leading dot.", nameof(extension));
        }
    }
}
