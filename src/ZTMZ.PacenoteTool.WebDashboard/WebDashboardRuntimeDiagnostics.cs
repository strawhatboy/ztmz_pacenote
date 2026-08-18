using System;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed record WebDashboardRuntimeDiagnostics(
    WebDashboardServerStatus Status,
    int ConnectedClientCount,
    int TargetFrameRate,
    long RenderedFrameCount,
    DateTimeOffset? LastRenderedFrameUtc);
