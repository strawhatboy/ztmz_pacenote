using System.Collections.Generic;
using System.IO;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public class BasePacenoteReader : IGamePacenoteReader
    {
        public IList<PacenoteRecord> ReadPacenoteRecord(string profile, IGame game, string track)
        {
            var script = ScriptReader.ReadFromFile(GetScriptFile(profile, game, track));
            return script.PacenoteRecords;
        }

        public string GetScriptFile(string profile, IGame game, string track, bool fallbackToDefault = true)
        {
            string filePath = AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}\\{1}\\{2}.pacenote", profile, game.Name, track));
            if (!File.Exists(filePath))
            {
                if (fallbackToDefault) 
                {
                    // when replaying, if not exist, create new
                    return GetScriptFile(Constants.DEFAULT_PROFILE, game, track, false);
                } else {
                    // when recording
                    File.WriteAllText(filePath, "");
                }
            }

            return filePath;
        }
    }
}
