using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

public sealed record UdpListenerDiagnostics(
    string Name,
    UdpReceiverStatus Status,
    bool IsOptional,
    int SecondsWithoutMessage,
    string ConfiguredEndpoint);
