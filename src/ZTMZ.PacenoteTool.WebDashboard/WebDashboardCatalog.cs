#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed class WebDashboardCatalog
{
    private readonly NLog.Logger _logger;

    public WebDashboardCatalog(NLog.Logger? logger = null)
    {
        _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
    }

    public IReadOnlyList<WebDashboardDefinition> Load(
        string dashboardsPath,
        bool supportsNativeOverlay = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardsPath);
        if (!Directory.Exists(dashboardsPath))
        {
            _logger.Warn("Dashboard package directory does not exist: {0}", dashboardsPath);
            return [];
        }

        var dashboards = new List<WebDashboardDefinition>();
        foreach (var packagePath in Directory.EnumerateDirectories(dashboardsPath))
        {
            var descriptorPath = Path.Combine(packagePath, Constants.DASHBOARD_INFO_FILE_NAME);
            if (!File.Exists(descriptorPath))
            {
                continue;
            }

            try
            {
                var descriptor = JsonConvert.DeserializeObject<DashboardDescriptorData>(
                    File.ReadAllText(descriptorPath)) ?? new DashboardDescriptorData();
                var settings = LoadSettings(packagePath);
                var dashboardId = Path.GetFileName(Path.TrimEndingDirectorySeparator(packagePath));
                var legacyEnabled = ReadBooleanSetting(
                    settings,
                    "dashboards.settings.enabled",
                    descriptor.IsEnabled);
                var outputTarget = Config.Instance.GetDashboardOutputTarget(
                    dashboardId,
                    legacyEnabled);
                if (!supportsNativeOverlay)
                {
                    outputTarget = outputTarget switch
                    {
                        DashboardOutputTarget.Overlay => DashboardOutputTarget.None,
                        DashboardOutputTarget.Both => DashboardOutputTarget.Web,
                        _ => outputTarget
                    };
                }

                dashboards.Add(new WebDashboardDefinition
                {
                    Id = dashboardId,
                    PackagePath = packagePath,
                    Name = descriptor.Name,
                    Description = descriptor.Description,
                    Author = descriptor.Author,
                    Version = descriptor.Version,
                    PreviewImagePath = descriptor.PreviewImagePath,
                    SupportsWebDashboardClientLua = descriptor.SupportsWebDashboardClientLua,
                    UsesPacenoteIcons = descriptor.UsesPacenoteIcons,
                    ImageResources = descriptor.ImageResources.ToDictionary(
                        pair => pair.Key,
                        pair => new WebDashboardImageResource(pair.Value.Path, pair.Value.FormatGUID)),
                    ImageResourceDirectories = descriptor.ImageResourcesInDirectory.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value == null
                            ? null
                            : new WebDashboardImageResource(pair.Value.Path, pair.Value.FormatGUID)),
                    OutputTargetProvider = () => outputTarget,
                    SettingsProvider = () => settings
                });
            }
            catch (Exception exception)
            {
                _logger.Warn(exception, "Failed to load dashboard package: {0}", packagePath);
            }
        }

        return dashboards;
    }

    private static IReadOnlyDictionary<string, object> LoadSettings(string packagePath)
    {
        var settings = ReadSettings(Path.Combine(packagePath, Constants.FILE_SETTINGS));
        var userSettingsPath = Path.Combine(packagePath, Constants.FILE_USER_SETTINGS);
        if (File.Exists(userSettingsPath))
        {
            foreach (var setting in ReadSettings(userSettingsPath))
            {
                settings[setting.Key] = setting.Value;
            }
        }

        return settings;
    }

    private static Dictionary<string, object> ReadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        var root = JObject.Parse(File.ReadAllText(path));
        var names = root["PropertyName"] as JObject;
        var values = root["PropertyValue"] as JArray;
        if (names == null || values == null)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var index = 0;
        foreach (var property in names.Properties())
        {
            if (index >= values.Count)
            {
                break;
            }

            result[property.Name] = values[index].ToObject<object>()!;
            index++;
        }

        return result;
    }

    private static bool ReadBooleanSetting(
        IReadOnlyDictionary<string, object> settings,
        string key,
        bool fallback)
    {
        if (!settings.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value switch
        {
            bool boolean => boolean,
            JValue jsonValue when jsonValue.Type == JTokenType.Boolean => jsonValue.Value<bool>(),
            _ => fallback
        };
    }

    private sealed class DashboardDescriptorData
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? PreviewImagePath { get; set; }
        public Dictionary<string, DashboardResourceData> ImageResources { get; set; } = [];
        public Dictionary<string, DashboardResourceData?> ImageResourcesInDirectory { get; set; } = [];
        public bool SupportsWebDashboardClientLua { get; set; }
        public bool UsesPacenoteIcons { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    private sealed class DashboardResourceData
    {
        public string Path { get; set; } = string.Empty;
        public string? FormatGUID { get; set; }
    }
}
