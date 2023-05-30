using System;
using ZTMZ.PacenoteTool.Base;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit.Abstractions;
namespace ZTMZ.PacenoteTool.Tests.Base;

public class ProcessWatcherTest
{  
    private readonly ITestOutputHelper _testOutputHelper;

    public ProcessWatcherTest(ITestOutputHelper testOutputHelper) {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async void TestEventRaised()
    {
        ProcessWatcher watcher = new();
        watcher.onNewProcess += (exe, arg) => {
            Assert.Equal("notepad", exe);
            _testOutputHelper.WriteLine("'new' Event Triggered.");
        };
        watcher.onProcessExit += (exe, arg) => {
            Assert.Equal("notepad", exe);
            _testOutputHelper.WriteLine("'exit' Event Triggered.");
        };
        watcher.AddToWatch("notepad");
        watcher.StartWatching();
        await Task.Delay(3000);
        var p = Process.Start("notepad");
        await Task.Delay(6000);
        p.Kill();
        await Task.Delay(6000);
        watcher.StopWatching();
    }
}
