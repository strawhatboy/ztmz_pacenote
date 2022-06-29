using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGamePacenoteReader
    {
        ScriptReader ReadPacenoteRecord(string profile, IGame game, string track);
        string GetScriptFileForReplaying(string profile, IGame game, string track, bool fallbackToDefault = true);
        string GetScriptFileForRecording(string profile, IGame game, string track);
    }
}
