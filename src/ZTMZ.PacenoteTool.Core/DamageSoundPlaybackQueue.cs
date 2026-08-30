using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Core;

internal sealed class DamageSoundPlaybackQueue : IDisposable
{
    private readonly Channel<DamageSoundPlaybackRequest> _queue;
    private readonly Action<string> _playSound;
    private readonly Func<long, bool> _isCurrentSession;
    private readonly Action<string, Exception> _onPlaybackFailed;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private int _disposed;

    public DamageSoundPlaybackQueue(
        Action<string> playSound,
        Func<long, bool> isCurrentSession,
        Action<string, Exception> onPlaybackFailed,
        int capacity = 64)
    {
        ArgumentNullException.ThrowIfNull(playSound);
        ArgumentNullException.ThrowIfNull(isCurrentSession);
        ArgumentNullException.ThrowIfNull(onPlaybackFailed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _playSound = playSound;
        _isCurrentSession = isCurrentSession;
        _onPlaybackFailed = onPlaybackFailed;
        _queue = Channel.CreateBounded<DamageSoundPlaybackRequest>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public bool TryEnqueue(string token, long session)
    {
        return Volatile.Read(ref _disposed) == 0 &&
               _queue.Writer.TryWrite(new DamageSoundPlaybackRequest(token, session));
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(_cancellation.Token)
                               .ConfigureAwait(false))
            {
                if (!_isCurrentSession(request.Session))
                {
                    continue;
                }

                try
                {
                    _playSound(request.Token);
                }
                catch (Exception exception)
                {
                    _onPlaybackFailed(request.Token, exception);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();
        _cancellation.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _cancellation.Dispose();
    }

    private sealed record DamageSoundPlaybackRequest(string Token, long Session);
}
