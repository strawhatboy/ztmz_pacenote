using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.KT;

public static class WrcGenerationsTelemetrySettings
{
    private static Regex Assignment(string key) => new(
        @"(?m)^(?<prefix>[ \t]*WRC\.Telemetry\." + Regex.Escape(key) + @"[ \t]*=[ \t]*)(?<value>[^;\r\n]*)(?<suffix>;)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool Matches(string text, string address, int port)
    {
        string? Value(string key)
        {
            var matches = Assignment(key).Matches(text);
            return matches.Count == 1 ? matches[0].Groups["value"].Value.Trim() : null;
        }
        return Value("EnableTelemetry") == "true" &&
            Value("TelemetryAdress") == $"\"{address}\"" &&
            int.TryParse(Value("TelemetryPort"), out var actualPort) && actualPort == port &&
            int.TryParse(Value("TelemetryRate"), out var rate) && rate > 0;
    }

    public static string Configure(string text, string address, int port)
    {
        if (!IPAddress.TryParse(address, out _) || port is < 1 or > 65535)
            throw new ArgumentException("Invalid telemetry endpoint");
        var values = new Dictionary<string, string>
        {
            ["TelemetryRate"] = "60", ["TelemetryAdress"] = $"\"{address}\"",
            ["EnableTelemetry"] = "true", ["TelemetryPort"] = port.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var (key, value) in values)
        {
            var pattern = Assignment(key);
            var count = pattern.Matches(text).Count;
            if (count != 1) throw new InvalidDataException($"Expected exactly one WRC.Telemetry.{key} assignment, found {count}");
            text = pattern.Replace(text, m => m.Groups["prefix"].Value + value + m.Groups["suffix"].Value);
        }
        return text;
    }
}
