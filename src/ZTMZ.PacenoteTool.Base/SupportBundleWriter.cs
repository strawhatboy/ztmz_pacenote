using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base;

public sealed record SupportBundleResult(string BundlePath, IReadOnlyList<string> Entries);

public sealed class SupportBundleWriter
{
    private const long MaximumLogBytesPerFile = 5 * 1024 * 1024;

    public SupportBundleResult Create(
        string outputDirectory,
        string diagnosticsJson,
        IEnumerable<string> logFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        NLog.LogManager.Flush();

        var bundlePath = Path.Combine(
            outputDirectory,
            $"ztmz-support-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zip");
        var entries = new List<string>();
        var addedLogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = new FileStream(bundlePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        AddTextEntry(archive, "diagnostics.json", diagnosticsJson ?? "{}");
        entries.Add("diagnostics.json");

        foreach (var logFile in logFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(logFile) || !File.Exists(logFile))
            {
                continue;
            }

            var fileName = Path.GetFileName(logFile);
            if (string.IsNullOrWhiteSpace(fileName) || !addedLogNames.Add(fileName))
            {
                continue;
            }

            var entryName = Path.Combine("logs", fileName).Replace(Path.DirectorySeparatorChar, '/');
            AddLogTailEntry(archive, entryName, logFile);
            entries.Add(entryName);
        }

        return new SupportBundleResult(bundlePath, entries);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void AddLogTailEntry(ZipArchive archive, string entryName, string logFile)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var output = entry.Open();
        using var input = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (input.Length > MaximumLogBytesPerFile)
        {
            input.Seek(-MaximumLogBytesPerFile, SeekOrigin.End);
        }

        input.CopyTo(output);
    }
}
