#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WebDashboard;

/// <summary>Reads and writes the established dashboard settings.json/usersettings.json contract.</summary>
public sealed record DashboardPackageSetting(
    string Key,
    string TooltipKey,
    object? Value,
    IReadOnlyList<double> ValueRange);

public static class DashboardPackageSettings
{
    public static IReadOnlyList<DashboardPackageSetting> Load(string packagePath)
    {
        var root = LoadMergedRoot(packagePath);
        var names = root["PropertyName"] as JObject;
        var values = root["PropertyValue"] as JArray;
        var ranges = root["ValueRange"] as JArray;
        if (names == null || values == null)
        {
            return [];
        }

        return names.Properties().Select((property, index) => new DashboardPackageSetting(
            property.Name,
            property.Value.Value<string>() ?? string.Empty,
            index < values.Count ? values[index].ToObject<object>() : null,
            index < (ranges?.Count ?? 0) && ranges![index] is JArray range
                ? range.Select(token => token.Value<double?>())
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToArray()
                : [])).ToArray();
    }

    public static void SaveValue(string packagePath, string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var root = LoadMergedRoot(packagePath);
        var names = root["PropertyName"] as JObject
            ?? throw new InvalidDataException("Dashboard settings do not contain PropertyName.");
        var values = root["PropertyValue"] as JArray
            ?? throw new InvalidDataException("Dashboard settings do not contain PropertyValue.");
        var index = names.Properties().Select((property, position) => new { property, position })
            .FirstOrDefault(item => string.Equals(item.property.Name, key, StringComparison.Ordinal))?.position;
        if (index == null)
        {
            throw new KeyNotFoundException($"Dashboard setting '{key}' was not found.");
        }

        while (values.Count <= index.Value)
        {
            values.Add(JValue.CreateNull());
        }
        values[index.Value] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
        File.WriteAllText(
            GetUserSettingsPath(packagePath),
            root.ToString(Formatting.Indented));
    }

    public static string GetUserSettingsPath(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var packageId = Path.GetFileName(Path.TrimEndingDirectorySeparator(packagePath));
        var settingsDirectory = AppLevelVariables.Instance.GetPath("dashboard-settings");
        Directory.CreateDirectory(settingsDirectory);
        return Path.Combine(settingsDirectory, packageId + ".json");
    }

    private static JObject LoadMergedRoot(string packagePath)
    {
        var defaultPath = Path.Combine(packagePath, Constants.FILE_SETTINGS);
        if (!File.Exists(defaultPath))
        {
            throw new FileNotFoundException("Dashboard settings file was not found.", defaultPath);
        }

        var root = JObject.Parse(File.ReadAllText(defaultPath));
        MergeUserValues(root, Path.Combine(packagePath, Constants.FILE_USER_SETTINGS));
        MergeUserValues(root, GetUserSettingsPath(packagePath));
        return root;
    }

    private static void MergeUserValues(JObject root, string userPath)
    {
        if (!File.Exists(userPath))
        {
            return;
        }

        var userRoot = JObject.Parse(File.ReadAllText(userPath));
        if (userRoot["PropertyValue"] is JArray userValues)
        {
            root["PropertyValue"] = userValues.DeepClone();
        }
    }
}
