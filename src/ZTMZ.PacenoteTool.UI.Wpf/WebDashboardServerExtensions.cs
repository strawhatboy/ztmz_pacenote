#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.UI.Windows;

public static class WebDashboardServerExtensions
{
    public static void Initialize(
        this WebDashboardServer server,
        IReadOnlyList<Dashboard> dashboards,
        Func<Dashboard, DashboardOutputTarget>? outputTargetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(dashboards);

        var definitions = dashboards.Select(dashboard => new WebDashboardDefinition
        {
            Id = GetDashboardId(dashboard),
            PackagePath = dashboard.Descriptor.Path,
            Name = dashboard.Descriptor.Name,
            Description = dashboard.Descriptor.Description,
            Author = dashboard.Descriptor.Author,
            Version = dashboard.Descriptor.Version,
            PreviewImagePath = dashboard.Descriptor.PreviewImagePath,
            SupportsWebDashboardClientLua = dashboard.Descriptor.SupportsWebDashboardClientLua,
            UsesPacenoteIcons = dashboard.Descriptor.UsesPacenoteIcons,
            ImageResources = dashboard.Descriptor.ImageResources?.ToDictionary(
                    pair => pair.Key,
                    pair => new WebDashboardImageResource(pair.Value.Path, pair.Value.FormatGUID))
                ?? new Dictionary<string, WebDashboardImageResource>(),
            ImageResourceDirectories = dashboard.Descriptor.ImageResourcesInDirectory?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value == null
                        ? null
                        : new WebDashboardImageResource(pair.Value.Path, pair.Value.FormatGUID))
                ?? new Dictionary<string, WebDashboardImageResource?>(),
            OutputTargetProvider = () => outputTargetResolver?.Invoke(dashboard)
                ?? (dashboard.Descriptor.IsEnabled
                    ? DashboardOutputTarget.Both
                    : DashboardOutputTarget.None),
            SettingsProvider = () => GetSettings(dashboard)
        }).ToList();

        server.Initialize(
            definitions,
            AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS),
            AppContext.BaseDirectory);
    }

    private static string GetDashboardId(Dashboard dashboard)
    {
        var path = dashboard.Descriptor.Path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (!string.IsNullOrWhiteSpace(directoryName))
            {
                return directoryName;
            }
        }

        return dashboard.Descriptor.Name ?? string.Empty;
    }

    private static IReadOnlyDictionary<string, object> GetSettings(Dashboard dashboard)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var configurations = dashboard.DashboardConfigurations;
        if (configurations?.PropertyName == null || configurations.PropertyValue == null)
        {
            return result;
        }

        var index = 0;
        foreach (var property in configurations.PropertyName)
        {
            if (index >= configurations.PropertyValue.Count)
            {
                break;
            }

            result[property.Key] = configurations.PropertyValue[index];
            index++;
        }

        return result;
    }
}
