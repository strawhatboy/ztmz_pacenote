using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.WpfGUI;

namespace ZTMZ.PacenoteTool.Tests;

public sealed class StartupReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ztmz-startup-" + Guid.NewGuid());
    private string Status => Path.Combine(_directory, "status.json");
    public StartupReportTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void FailurePreservesLastStageAndCannotBeOverwrittenByLateReadiness()
    {
        var report = new StartupReport(Status, 123);
        report.Progress("configuration");
        report.Failed(new InvalidOperationException("bad setting"));
        report.Ready();
        report.Progress("late callback");
        using var json = JsonDocument.Parse(File.ReadAllText(Status));
        Assert.Equal("failed", json.RootElement.GetProperty("state").GetString());
        Assert.Equal("configuration", json.RootElement.GetProperty("stage").GetString());
        Assert.Contains("bad setting", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(123, json.RootElement.GetProperty("pid").GetInt32());
        Assert.False(report.IsReady);
    }

    [Fact]
    public void RuntimeErrorsAfterReadinessAreNotReportedAsStartupFailures()
    {
        var report = new StartupReport(Status, 123);
        report.Ready();
        report.Failed(new Exception("later runtime failure"));
        using var json = JsonDocument.Parse(File.ReadAllText(Status));
        Assert.Equal("ready", json.RootElement.GetProperty("state").GetString());
        Assert.True(report.IsReady);
    }

    [Fact]
    public void ConcurrentProgressNeverOverwritesTerminalFailure()
    {
        var report = new StartupReport(Status, 123);
        Parallel.For(0, 100, index =>
        {
            if (index == 50) report.Failed(new Exception("failure"));
            else report.Progress("stage " + index);
        });
        using var json = JsonDocument.Parse(File.ReadAllText(Status));
        Assert.Equal("failed", json.RootElement.GetProperty("state").GetString());
        Assert.False(File.Exists(Status + ".new"));
    }

    [Fact]
    public async Task ReadinessSurvivesATemporaryReaderLock()
    {
        var report = new StartupReport(Status, 123);
        report.Progress("initialization");
        var reader = new FileStream(Status, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ready = Task.Run(report.Ready);
        await Task.Delay(75);
        reader.Dispose();
        await ready;
        using var json = JsonDocument.Parse(File.ReadAllText(Status));
        Assert.Equal("ready", json.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void UnwritableReportDoesNotPreventStartup()
    {
        var report = new StartupReport(Path.Combine(_directory, "missing", "status.json"), 123);
        report.Progress("entry");
        report.Ready();
        Assert.True(report.IsReady);
    }

    [Fact]
    public void DirectExecutableLaunchNeedsNoStatusFile()
    {
        var report = new StartupReport(null, 123);
        report.Progress("entry");
        report.AlreadyRunning();
        Assert.True(report.IsReady);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }
}
