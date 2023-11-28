using ZTMZ.PacenoteTool.Base;
using System.Reflection;

namespace ZTMZ.PacenoteTool.Core
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
            var _version = Assembly.GetExecutingAssembly().GetName().Version.ToString();;
            if (System.IO.Directory.Exists(Config.Instance.PythonPath) &&
                     System.IO.Directory.Exists(Config.Instance.SpeechRecogizerModelPath))
            {
                // dev mode
                return ToolVersion.DEV;
                    // GameHacker.HackDLLs(Config.Instance.DirtGamePath);
            }
            
            // For test only
            if (!_version.EndsWith("0"))
            {
                return ToolVersion.TEST;
            }
            return ToolVersion.STANDARD;
        }
    }
}
