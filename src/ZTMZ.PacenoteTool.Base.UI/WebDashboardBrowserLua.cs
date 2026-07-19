using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Global configuration values required by the browser-compatible Lua facade.
/// </summary>
public sealed class WebDashboardBrowserLuaConfig
{
    public WebDashboardBrowserLuaConfig(
        bool hudChromaKeyMode,
        float hudShiftGreenZone,
        float hudShiftYellowZone,
        float hudShiftRedZone,
        int frameRate = Config.WebDashboardFrameRateDefault)
    {
        HudChromaKeyMode = hudChromaKeyMode;
        HudShiftGreenZone = hudShiftGreenZone;
        HudShiftYellowZone = hudShiftYellowZone;
        HudShiftRedZone = hudShiftRedZone;
        FrameRate = Math.Clamp(
            frameRate,
            Config.WebDashboardFrameRateMinimum,
            Config.WebDashboardFrameRateMaximum);
    }

    public bool HudChromaKeyMode { get; }

    public float HudShiftGreenZone { get; }

    public float HudShiftYellowZone { get; }

    public float HudShiftRedZone { get; }

    public int FrameRate { get; }
}

/// <summary>
/// An opt-in dashboard package rendered by the browser rather than the server-side
/// command capture path.
/// </summary>
public sealed class WebDashboardBrowserLuaDashboard
{
    public WebDashboardBrowserLuaDashboard(
        int id,
        int order,
        long configurationRevision,
        IReadOnlyDictionary<string, object> settings)
    {
        Id = id;
        Order = order;
        ConfigurationRevision = configurationRevision;
        Settings = settings ?? new Dictionary<string, object>();
    }

    public int Id { get; }

    public int Order { get; }

    public long ConfigurationRevision { get; }

    public IReadOnlyDictionary<string, object> Settings { get; }
}

/// <summary>
/// The current browser-Lua dashboard arrangement and the configuration shared by
/// all dashboards in that scene.
/// </summary>
public sealed class WebDashboardBrowserLuaScene
{
    public WebDashboardBrowserLuaScene(
        IReadOnlyList<WebDashboardBrowserLuaDashboard> dashboards,
        WebDashboardBrowserLuaConfig config)
    {
        Dashboards = dashboards ?? new List<WebDashboardBrowserLuaDashboard>();
        Config = config;
    }

    public IReadOnlyList<WebDashboardBrowserLuaDashboard> Dashboards { get; }

    public WebDashboardBrowserLuaConfig Config { get; }
}
