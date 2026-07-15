using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class ProcessWatcherTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public ProcessWatcherTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task TestEventRaised()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processName = $"ztmzpwt{Guid.NewGuid():N}"[..15];
        var processPath = Path.Combine(Path.GetTempPath(), $"{processName}.exe");
        var processStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processExited = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new ProcessWatcher(refreshInterval: 100);
        Process? process = null;

        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), processPath);
            watcher.onNewProcess += observedProcess =>
            {
                if (process != null && observedProcess.Id == process.Id)
                {
                    processStarted.TrySetResult(observedProcess.Id);
                    _testOutputHelper.WriteLine("'new' event triggered.");
                }
            };
            watcher.onProcessExit += (executable, _) =>
            {
                if (string.Equals(executable, processName, StringComparison.OrdinalIgnoreCase))
                {
                    processExited.TrySetResult(executable);
                    _testOutputHelper.WriteLine("'exit' event triggered.");
                }
            };

            watcher.AddToWatch(processName);
            watcher.StartWatching();
            process = Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = "-t 127.0.0.1",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Assert.NotNull(process);
            await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await processExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            watcher.StopWatching();

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await process.WaitForExitAsync();
                }
                finally
                {
                    process.Dispose();
                }
            }

            File.Delete(processPath);
        }
    }
}
