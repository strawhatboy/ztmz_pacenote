using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed record WebDashboardImageResource(string Path, string? FormatGuid = null);

public sealed class WebDashboardDefinition
{
    public required string Id { get; init; }
    public required string PackagePath { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? PreviewImagePath { get; init; }
    public bool SupportsWebDashboardClientLua { get; init; }
    public bool UsesPacenoteIcons { get; init; }
    public IReadOnlyDictionary<string, WebDashboardImageResource> ImageResources { get; init; } =
        new Dictionary<string, WebDashboardImageResource>();
    public IReadOnlyDictionary<string, WebDashboardImageResource?> ImageResourceDirectories { get; init; } =
        new Dictionary<string, WebDashboardImageResource?>();
    public Func<DashboardOutputTarget> OutputTargetProvider { get; init; } =
        () => DashboardOutputTarget.None;
    public Func<IReadOnlyDictionary<string, object>> SettingsProvider { get; init; } =
        () => new Dictionary<string, object>();

    public DashboardOutputTarget GetOutputTarget() => OutputTargetProvider();

    public IReadOnlyDictionary<string, object> GetSettings() => SettingsProvider();
}
