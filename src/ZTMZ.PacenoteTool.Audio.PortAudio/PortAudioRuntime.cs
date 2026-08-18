using PortAudioSharp;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

internal static class PortAudioRuntime
{
    private static readonly object SyncRoot = new();
    private static int referenceCount;

    public static IDisposable Acquire()
    {
        lock (SyncRoot)
        {
            if (referenceCount == 0)
            {
                global::PortAudioSharp.PortAudio.Initialize();
            }

            referenceCount++;
            return new Lease();
        }
    }

    private static void Release()
    {
        lock (SyncRoot)
        {
            if (referenceCount == 0)
            {
                return;
            }

            referenceCount--;
            if (referenceCount == 0)
            {
                global::PortAudioSharp.PortAudio.Terminate();
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private int isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) == 0)
            {
                Release();
            }
        }
    }
}
