#nullable enable

using System;
using Avalonia.Logging;
using NLog;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed class AvaloniaNLogSink : ILogSink
{
    public static void Install() => global::Avalonia.Logging.Logger.Sink = new AvaloniaNLogSink();

    public bool IsEnabled(LogEventLevel level, string area)
    {
        if (level < LogEventLevel.Warning)
        {
            return false;
        }

        var logger = GetLogger(area);
        return logger.IsEnabled(ToNLogLevel(level));
    }

    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate) =>
        Write(level, area, source, messageTemplate, []);

    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        params object?[] propertyValues) =>
        Write(level, area, source, messageTemplate, propertyValues);

    private static void Write(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        object?[] propertyValues)
    {
        if (level < LogEventLevel.Warning)
        {
            return;
        }

        var sourceName = source?.GetType().FullName;
        var message = string.IsNullOrWhiteSpace(sourceName)
            ? messageTemplate
            : $"[{sourceName}] {messageTemplate}";
        GetLogger(area).Log(ToNLogLevel(level), message, propertyValues);
    }

    private static NLog.Logger GetLogger(string area) => NLog.LogManager.GetLogger(
        string.IsNullOrWhiteSpace(area) ? "Avalonia" : $"Avalonia.{area}");

    private static LogLevel ToNLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Info,
        LogEventLevel.Warning => LogLevel.Warn,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Fatal,
        _ => LogLevel.Info
    };
}
