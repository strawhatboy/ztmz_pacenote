using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class BasePacenoteReaderTest
{
    [Fact]
    public void ReadPacenoteRecord_ReturnsAnEmptyScriptWhenNoReplayScriptExists()
    {
        var reader = new MissingReplayScriptReader();

        var script = reader.ReadPacenoteRecord("default", null!, "missing-stage");

        Assert.Empty(script.PacenoteRecords);
    }

    private sealed class MissingReplayScriptReader : BasePacenoteReader
    {
        public override string GetScriptFileForReplaying(
            string profile,
            IGame game,
            string track,
            bool fallbackToDefault = true)
        {
            return string.Empty;
        }
    }
}
