using System.Collections.Concurrent;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

/// <summary>Deduplicates remote loads while allowing known pre-load failures to recover.</summary>
internal sealed class OverlayLoadAttemptCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> attempts = new();

    public async Task RunAsync(string key, Func<Task> load)
    {
        var attempt = attempts.GetOrAdd(key, _ => new Lazy<Task>(load));
        try { await attempt.Value.ConfigureAwait(false); }
        catch (OverlayComponentException)
        {
            // This exception is only raised by file validation or failed helper
            // creation, before a remote loader can start. Keep uncertain outcomes.
            attempts.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, attempt));
            throw;
        }
    }
}
