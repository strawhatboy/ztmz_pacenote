
using NLog;

namespace ZTMZ.PacenoteTool.Base
{
    public class NLogManager
    {
        public static void init()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = AppLevelVariables.Instance.GetPath("logs/" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".log") };

            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            NLog.LogManager.Configuration = config;
        }
    }
}
