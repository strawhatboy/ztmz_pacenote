using System;

namespace ZTMZ.PacenoteTool.Base.UI;

public sealed record WebDashboardRuntimeDiagnostics(
    WebDashboardServerStatus Status,
    int ConnectedClientCount,
    int TargetFrameRate,
    long RenderedFrameCount,
    DateTimeOffset? LastRenderedFrameUtc);
