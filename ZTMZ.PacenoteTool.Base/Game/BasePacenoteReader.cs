using System.Collections.Generic;
using System.IO;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public class BasePacenoteReader : IGamePacenoteReader
    {
        public virtual ScriptReader ReadPacenoteRecord(string profile, IGame game, string track)
        {
            var script = ScriptReader.ReadFromFile(GetScriptFileForReplaying(profile, game, track));
            return script;
        }

        public virtual string GetScriptFileForReplaying(string profile, IGame game, string track, bool fallbackToDefault = true)
        {
            string filePath = AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}\\{1}\\{2}.pacenote", profile, game.Name, track));
            if (!File.Exists(filePath))
            {
                if (fallbackToDefault) 
                {
                    // when replaying, if not exist, create new
                    return GetScriptFileForReplaying(Constants.DEFAULT_PROFILE, game, track, false);
                } else {
                    return null;    // not found
                }
            }

            return filePath;
        }

        // If not exist, ask user to create one or readonly open the replay script.
        public virtual string GetScriptFileForRecording(string profile, IGame game, string track)
        {
            string filePath = AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}\\{1}\\{2}.pacenote", profile, game.Name, track));
            return filePath;
        }
    }
}
