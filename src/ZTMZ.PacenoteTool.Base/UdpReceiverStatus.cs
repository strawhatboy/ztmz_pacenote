using System;

namespace ZTMZ.PacenoteTool.Base;

public sealed record UdpReceiverStatus(
    bool IsListening,
    string ListenAddress,
    int Port,
    long PacketCount,
    DateTimeOffset? LastPacketUtc,
    int LastPacketSize,
    DateTimeOffset? LastErrorUtc,
    string LastError)
{
    public static UdpReceiverStatus NotListening { get; } = new(
        false,
        string.Empty,
        0,
        0,
        null,
        0,
        null,
        string.Empty);
}
