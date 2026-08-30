using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public sealed class ScriptReaderNativeAudioCueTest
{
    [Fact]
    public void DynamicRecordPreservesOneCueAcrossMultipleMappedTokens()
    {
        var cue = CreateCue(5);

        var record = PacenoteRecord.FromCrewChiefPacenoteRecord(new DynamicPacenoteRecord
        {
            Distance = 100,
            Pacenote = "corner_6_left,detail_plus",
            Modifier = "none",
            SourceAudioCue = cue
        });

        Assert.Equal(2, record.Pacenotes.Count);
        var span = Assert.Single(record.SourceAudioCueSpans);
        Assert.Equal(0, span.PacenoteStartIndex);
        Assert.Equal(2, span.PacenoteCount);
        Assert.Same(cue, span.Cue);
    }

    [Fact]
    public void SameDistanceMergeOffsetsEachSourceCueSpan()
    {
        var firstCue = CreateCue(5);
        var secondCue = CreateCue(6);
        var reader = ScriptReader.ReadFromDynamicPacenoteRecords(
        [
            new DynamicPacenoteRecord
            {
                Distance = 100,
                Pacenote = "corner_6_left,detail_plus",
                Modifier = "none",
                SourceAudioCue = firstCue
            },
            new DynamicPacenoteRecord
            {
                Distance = 100,
                Pacenote = "corner_6_right",
                Modifier = "none",
                SourceAudioCue = secondCue
            }
        ]);

        var merged = Assert.Single(reader.PacenoteRecords);
        Assert.Equal(3, merged.Pacenotes.Count);
        Assert.Collection(
            merged.SourceAudioCueSpans,
            span =>
            {
                Assert.Equal(0, span.PacenoteStartIndex);
                Assert.Equal(2, span.PacenoteCount);
                Assert.Same(firstCue, span.Cue);
            },
            span =>
            {
                Assert.Equal(2, span.PacenoteStartIndex);
                Assert.Equal(1, span.PacenoteCount);
                Assert.Same(secondCue, span.Cue);
            });
    }

    private static GamePacenoteAudioCue CreateCue(int pacenoteType) => new()
    {
        ProviderId = "test",
        PacenoteType = pacenoteType
    };
}
