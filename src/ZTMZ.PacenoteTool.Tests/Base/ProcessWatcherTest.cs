using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    [Fact]
    public async Task ExistingWatchedProcessWaitsForItsWindowTitle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processName = $"ztmzpww{Guid.NewGuid():N}"[..15];
        var processPath = Path.Combine(Path.GetTempPath(), $"{processName}.exe");
        var processStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new ProcessWatcher(refreshInterval: 50);
        Process? process = null;

        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), processPath);
            process = StartPing(processPath);

            watcher.onNewProcess += observedProcess =>
            {
                if (observedProcess.Id == process.Id)
                {
                    processStarted.TrySetResult(observedProcess.Id);
                }
            };
            watcher.AddToWatch(processName, "window title that does not exist");
            watcher.StartWatching();

            await Task.Delay(300);

            Assert.False(processStarted.Task.IsCompleted);
            Assert.False(watcher.RunningProcesses.ContainsKey(processName));
        }
        finally
        {
            watcher.StopWatching();
            await StopProcessAsync(process);
            File.Delete(processPath);
        }
    }

    [Fact]
    public async Task ProcessExitHandlerFailureDoesNotBlockRestartDetection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processName = $"ztmzpwf{Guid.NewGuid():N}"[..15];
        var processPath = Path.Combine(Path.GetTempPath(), $"{processName}.exe");
        var observedProcessIds = new ConcurrentQueue<int>();
        using var watcher = new ProcessWatcher(refreshInterval: 100);
        Process? firstProcess = null;
        Process? secondProcess = null;

        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), processPath);
            watcher.onNewProcess += observedProcess => observedProcessIds.Enqueue(observedProcess.Id);
            watcher.onProcessExit += (executable, _) =>
            {
                if (string.Equals(executable, processName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Expected test exit handler failure.");
                }
            };

            watcher.AddToWatch(processName);
            watcher.StartWatching();

            firstProcess = StartPing(processPath);
            var firstProcessId = firstProcess.Id;
            await WaitUntilAsync(
                () => observedProcessIds.Contains(firstProcessId),
                TimeSpan.FromSeconds(5));

            firstProcess.Kill(entireProcessTree: true);
            await firstProcess.WaitForExitAsync();
            await WaitUntilAsync(
                () => !watcher.RunningProcesses.ContainsKey(processName),
                TimeSpan.FromSeconds(5));

            secondProcess = StartPing(processPath);
            var secondProcessId = secondProcess.Id;
            await WaitUntilAsync(
                () => observedProcessIds.Contains(secondProcessId),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            watcher.StopWatching();
            await StopProcessAsync(secondProcess);
            await StopProcessAsync(firstProcess);
            File.Delete(processPath);
        }
    }

    private static Process StartPing(string processPath)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = "-t 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process == null)
        {
            return;
        }

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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected process watcher state was not reached.");
            }

            await Task.Delay(25);
        }
    }
}
