
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using NLog;

namespace ZTMZ.PacenoteTool.Base
{
    public class NLogManager
    {
        public static string RULE_NAME = "NLog";
        public static void init(ToolVersion toolVersion)
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = AppLevelVariables.Instance.GetPath("logs/" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".log") };
            var theRule = new NLog.Config.LoggingRule(RULE_NAME);
            theRule.LoggerNamePattern = "*";
            theRule.Targets.Add(logfile);
            theRule.Targets.Add(new NLog.Targets.ConsoleTarget("logconsole"));

            // also init microsoft.applicationinsights with nlogtarget here
            // TelemetryConfiguration telemetryConfiguration = TelemetryConfiguration.CreateDefault();
            // telemetryConfiguration.ConnectionString = "InstrumentationKey=6ca7ac93-0219-4c20-b7dd-758f1ecb2d5b;IngestionEndpoint=https://eastasia-0.in.applicationinsights.azure.com/;LiveEndpoint=https://eastasia.livediagnostics.monitor.azure.com/";
            // Microsoft.ApplicationInsights.NLogTarget.ApplicationInsightsTarget aiTarget = new Microsoft.ApplicationInsights.NLogTarget.ApplicationInsightsTarget();
            // aiTarget.InstrumentationKey = "6ca7ac93-0219-4c20-b7dd-758f1ecb2d5b";
            // aiTarget.Name = "ZTMZ Next Generation Pacenote Tool";
            // theRule.Targets.Add(aiTarget);

#if DEBUG
            theRule.SetLoggingLevels(LogLevel.Trace, LogLevel.Fatal);
#else
            if (toolVersion == ToolVersion.TEST) 
            {
                if (LogLevel.FromOrdinal(Config.Instance.LogLevel) >= LogLevel.Debug) {
                    theRule.SetLoggingLevels(LogLevel.Debug, LogLevel.Fatal);
                } else {
                    theRule.SetLoggingLevels(LogLevel.FromOrdinal(Config.Instance.LogLevel), LogLevel.Fatal);
                }
            } else {
                theRule.SetLoggingLevels(LogLevel.FromOrdinal(Config.Instance.LogLevel), LogLevel.Fatal);
            }
#endif
            config.AddRule(theRule);
            NLog.LogManager.Configuration = config;
        }

        public static void setLogLevel(int level)
        {
            var config = NLog.LogManager.Configuration;
            var logfile = config.FindRuleByName(RULE_NAME);
            if (logfile != null)
            {
                logfile.SetLoggingLevels(LogLevel.FromOrdinal(level), LogLevel.Fatal);
            }
        }
    }
}
