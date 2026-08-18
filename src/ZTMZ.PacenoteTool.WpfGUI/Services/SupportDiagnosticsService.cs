using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed class SupportDiagnosticsService
{
    private const int MaximumLogFiles = 7;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly RuntimeDiagnosticsService _diagnostics;
    private readonly IApplicationPaths _paths;
    private readonly SupportBundleWriter _bundleWriter = new();

    public SupportDiagnosticsService(
        RuntimeDiagnosticsService diagnostics,
        IApplicationPaths paths)
    {
        _diagnostics = diagnostics;
        _paths = paths;
    }

    public Task<SupportDiagnosticsSnapshot> CaptureAsync() =>
        Task.Run(() => _diagnostics.Capture(includePrerequisiteCheck: true));

    public Task<SupportDiagnosticsSnapshot> CaptureRuntimeAsync() =>
        Task.Run(() => _diagnostics.Capture(includePrerequisiteCheck: false));

    public async Task<SupportBundleResult> CreateBundleAsync()
    {
        var snapshot = await CaptureAsync();
        var diagnosticsJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var outputDirectory = GetSupportFolder();
        return await Task.Run(() => _bundleWriter.Create(
            outputDirectory,
            diagnosticsJson,
            GetRecentLogFiles()));
    }

    public string GetSupportFolder() => _paths.GetPath("support");

    private IReadOnlyList<string> GetRecentLogFiles()
    {
        var logDirectory = _paths.GetPath("logs");
        if (!Directory.Exists(logDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(logDirectory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumLogFiles)
            .Select(file => file.FullName)
            .ToArray();
    }
}
