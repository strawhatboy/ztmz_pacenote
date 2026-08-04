namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// Supplies a stable, read-only game-specific diagnostic snapshot for support bundles.
/// </summary>
public interface IGameRuntimeDiagnosticsProvider
{
    object CaptureRuntimeDiagnostics();
}
