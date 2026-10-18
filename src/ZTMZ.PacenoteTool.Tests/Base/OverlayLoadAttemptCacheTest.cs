using System.ComponentModel;
using ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class OverlayLoadAttemptCacheTest
{
    [Fact]
    public async Task BlockedHelperCreationAllowsAnotherAttemptAfterRepair()
    {
        var attempts = new OverlayLoadAttemptCache();
        var calls = 0;
        await Assert.ThrowsAsync<OverlayComponentException>(() => attempts.RunAsync("game", () =>
        {
            calls++;
            throw new OverlayComponentException("injector.exe", new Win32Exception(225));
        }));
        await attempts.RunAsync("game", () => { calls++; return Task.CompletedTask; });
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedOrUncertainLoadNeverStartsAnotherRemoteLoad(bool timedOut)
    {
        var attempts = new OverlayLoadAttemptCache();
        var calls = 0;
        Task Start()
        {
            calls++;
            return timedOut ? Task.FromException(new TimeoutException("Loader may still be running")) : Task.CompletedTask;
        }
        for (var i = 0; i < 2; i++)
        {
            if (timedOut) await Assert.ThrowsAsync<TimeoutException>(() => attempts.RunAsync("game", Start));
            else await attempts.RunAsync("game", Start);
        }
        Assert.Equal(1, calls);
        await attempts.RunAsync("new-process-start-time", () => { calls++; return Task.CompletedTask; });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneLoad()
    {
        var attempts = new OverlayLoadAttemptCache();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tasks = Enumerable.Range(0, 10).Select(_ => attempts.RunAsync("game", () =>
        {
            Interlocked.Increment(ref calls);
            return finished.Task;
        })).ToArray();
        Assert.Equal(1, calls);
        finished.SetResult();
        await Task.WhenAll(tasks);
    }
}
