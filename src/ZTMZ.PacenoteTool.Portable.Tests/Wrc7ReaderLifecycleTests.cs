using System.Diagnostics;
using Xunit;
using ZTMZ.PacenoteTool.KT;

public class Wrc7ReaderLifecycleTests
{
    [Fact]
    public async Task SelectionBeforeGameLaunchWaitsAndCanBeCancelledAndReinitialized()
    {
        if (!OperatingSystem.IsWindows()) return;
        var processes = Process.GetProcessesByName("WRC7");
        foreach (var process in processes) process.Dispose();
        if (processes.Length != 0) return; // Never attach to a user's game in this test.
        var game = new Wrc7();
        var reader = (Wrc7GameDataReader)game.GameDataReader;
        try
        {
            Assert.True(reader.Initialize(game));
            var first = reader.Listening;
            await Task.Delay(600);
            Assert.Null(reader.Error);
            Assert.False(first.IsCompleted);
            reader.Uninitialize(game);
            Assert.True(first.IsCanceled);
            Assert.True(reader.Initialize(game));
            Assert.NotSame(first, reader.Listening);
            await Task.Delay(300);
            Assert.False(reader.Listening.IsCompleted);
            Assert.Null(reader.Error);
        }
        finally { reader.Uninitialize(game); }
    }
}
