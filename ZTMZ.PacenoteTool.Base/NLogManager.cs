
using NLog;

namespace ZTMZ.PacenoteTool.Base
{
    public class NLogManager
    {
        public static void init(ToolVersion toolVersion)
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = AppLevelVariables.Instance.GetPath("logs/" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".log") };

#if DEBUG
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
#else
            if (toolVersion == ToolVersion.TEST) 
            {
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            } else {
                config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);
            }
#endif
            NLog.LogManager.Configuration = config;
        }
    }
}
