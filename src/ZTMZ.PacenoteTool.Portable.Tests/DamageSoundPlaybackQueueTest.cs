using System.Collections.Concurrent;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class DamageSoundPlaybackQueueTest
{
    [Fact]
    public async Task EnqueueDoesNotWaitForSoundPreparation()
    {
        using var playbackStarted = new ManualResetEventSlim();
        using var releasePlayback = new ManualResetEventSlim();
        using var queue = new DamageSoundPlaybackQueue(
            _ =>
            {
                playbackStarted.Set();
                releasePlayback.Wait();
            },
            _ => true,
            (_, exception) => throw exception);

        try
        {
            var enqueueTask = Task.Run(() => queue.TryEnqueue("system_damage_engine", 1));
            var completedTask = await Task.WhenAny(enqueueTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(enqueueTask, completedTask);
            Assert.True(await enqueueTask);
            Assert.True(playbackStarted.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            releasePlayback.Set();
        }
    }

    [Fact]
    public async Task SoundsArePreparedInEnqueueOrder()
    {
        var played = new ConcurrentQueue<string>();
        var allPlayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new DamageSoundPlaybackQueue(
            token =>
            {
                played.Enqueue(token);
                if (played.Count == 3)
                {
                    allPlayed.TrySetResult();
                }
            },
            _ => true,
            (_, exception) => throw exception);

        Assert.True(queue.TryEnqueue("first", 1));
        Assert.True(queue.TryEnqueue("second", 1));
        Assert.True(queue.TryEnqueue("third", 1));

        await allPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["first", "second", "third"], played);
    }

    [Fact]
    public async Task StaleGameSessionSoundsAreSkipped()
    {
        long currentSession = 2;
        var played = new ConcurrentQueue<string>();
        var currentPlayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new DamageSoundPlaybackQueue(
            token =>
            {
                played.Enqueue(token);
                currentPlayed.TrySetResult();
            },
            session => session == Volatile.Read(ref currentSession),
            (_, exception) => throw exception);

        Assert.True(queue.TryEnqueue("stale", 1));
        Assert.True(queue.TryEnqueue("current", 2));

        await currentPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["current"], played);
    }
}
