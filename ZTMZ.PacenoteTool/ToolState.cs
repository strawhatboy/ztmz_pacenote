using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public enum ToolState
    {
        Recording = 0,
        Replaying = 1,
    }

    public class ToolUtils
    {
        public static ToolVersion GetToolVersion()
        {
            var _version = UpdateManager.CurrentVersion;
            if (System.IO.Directory.Exists(Config.Instance.PythonPath) &&
                     System.IO.Directory.Exists(Config.Instance.SpeechRecogizerModelPath))
            {
                // dev mode
                return ToolVersion.DEV;
                    // GameHacker.HackDLLs(Config.Instance.DirtGamePath);
            }
            
            // For test only
            if (!UpdateManager.CurrentVersion.EndsWith("0"))
            {
                return ToolVersion.TEST;
            }
            return ToolVersion.STANDARD;
        }
    }
}
