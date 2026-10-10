using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.KT;

public class Wrc7ResultTests
{
    [Fact]
    public void FinishRequiresMatchingProcessedResultIncludingPenalty()
    {
        var profile = new byte[14];
        BinaryPrimitives.WriteInt32LittleEndian(profile, 100000);
        BinaryPrimitives.WriteInt32LittleEndian(profile.AsSpan(4), 118000);
        profile[12] = 1;
        Assert.Equal(118000, Wrc7RaceSession.VerifiedResult(100000, 18000, [1, 0, 0], profile));
        Assert.Null(Wrc7RaceSession.VerifiedResult(100000, 18000, [0, 0, 0], profile));
        Assert.Null(Wrc7RaceSession.VerifiedResult(100000, 18000, [1, 0, 1], profile));
        Assert.Null(Wrc7RaceSession.VerifiedResult(100001, 18000, [1, 0, 0], profile));
        Assert.Null(Wrc7RaceSession.VerifiedResult(100000, 0, [1, 0, 0], profile));
        profile[13] = 1;
        Assert.Null(Wrc7RaceSession.VerifiedResult(100000, 18000, [1, 0, 0], profile));
    }
}
