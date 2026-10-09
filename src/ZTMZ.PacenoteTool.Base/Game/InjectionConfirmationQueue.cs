#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>Serializes bridge and renderer consent flows across desktop hosts.</summary>
public static class InjectionConfirmationQueue
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> EnterAsync()
    {
        await Gate.WaitAsync();
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Gate.Release();
        }
    }
}
