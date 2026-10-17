using System.IO;
using System.Text.Json;

namespace ZTMZ.PacenoteTool.WpfGUI;

// Uses only the runtime: configuration, logging and WPF may not be usable yet.
internal sealed class StartupReport(string? path, int processId)
{
    private readonly object _gate = new();
    private string _stage = "managed_entry";
    private bool _terminal;
    private bool _ready;
    public bool IsReady { get { lock (_gate) return _ready; } }

    public void Progress(string stage) => Write("starting", stage, null);
    public void Ready() => Write("ready", "ready", null);
    public void AlreadyRunning() => Write("already_running", "already_running", null);
    public void Failed(Exception error) => Write("failed", null, error.ToString());

    private void Write(string state, string? stage, string? error)
    {
        lock (_gate)
        {
            if (_terminal) return;
            _stage = stage ?? _stage;
            _terminal = state != "starting";
            _ready = state is "ready" or "already_running";
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, pid = processId, state, stage = _stage,
                    timestampUtc = DateTime.UtcNow, error
                });
                File.WriteAllText(path + ".new", json);
                // A native reader or antivirus may briefly hold the old file.
                for (var attempt = 0; ; ++attempt)
                {
                    try { File.Move(path + ".new", path, overwrite: true); break; }
                    catch (IOException) when (attempt < 10) { System.Threading.Thread.Sleep(25); }
                    catch (UnauthorizedAccessException) when (attempt < 10) { System.Threading.Thread.Sleep(25); }
                }
            }
            catch (Exception) { /* Diagnostics must not prevent application startup. */ }
        }
    }
}

internal static class StartupProgress
{
    public static readonly bool HasLauncher;
    public static readonly StartupReport Report;

    static StartupProgress()
    {
        var statusPath = Environment.GetEnvironmentVariable("ZTMZ_STARTUP_STATUS");
        HasLauncher = !string.IsNullOrWhiteSpace(statusPath);
        Report = new StartupReport(statusPath, Environment.ProcessId);
    }
}
