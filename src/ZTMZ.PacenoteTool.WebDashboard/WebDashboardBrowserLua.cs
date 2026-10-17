using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WebDashboard;

/// <summary>
/// Global configuration values required by the browser-compatible Lua facade.
/// </summary>
public sealed class WebDashboardBrowserLuaConfig
{
    // SetCulture replaces the effective dictionary. Reuse its immutable wire snapshot
    // across telemetry frames; a language change naturally changes the scene signature.
    private static readonly ConditionalWeakTable<IDictionary<string, string>, ReadOnlyDictionary<string, string>> TranslationCache = new();
    private static readonly IReadOnlyDictionary<string, string> EmptyTranslations = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public WebDashboardBrowserLuaConfig(
        bool hudChromaKeyMode,
        float hudShiftGreenZone,
        float hudShiftYellowZone,
        float hudShiftRedZone,
        int frameRate = Config.WebDashboardFrameRateDefault,
        int canvasWidth = WebDashboardCanvasResolution.DefaultWidth,
        int canvasHeight = WebDashboardCanvasResolution.DefaultHeight)
    {
        HudChromaKeyMode = hudChromaKeyMode;
        HudShiftGreenZone = hudShiftGreenZone;
        HudShiftYellowZone = hudShiftYellowZone;
        HudShiftRedZone = hudShiftRedZone;
        FrameRate = Math.Clamp(
            frameRate,
            Config.WebDashboardFrameRateMinimum,
            Config.WebDashboardFrameRateMaximum);
        CanvasWidth = Math.Max(1, canvasWidth);
        CanvasHeight = Math.Max(1, canvasHeight);
        var loader = I18NLoader.Instance;
        CultureName = loader.CurrentCultureName ?? I18NLoader.FallbackCultureName;
        Translations = loader.CurrentCulture is { } culture
            ? TranslationCache.GetValue(culture, CreateTranslations)
            : EmptyTranslations;
    }

    public bool HudChromaKeyMode { get; }

    public float HudShiftGreenZone { get; }

    public float HudShiftYellowZone { get; }

    public float HudShiftRedZone { get; }

    public int FrameRate { get; }

    public int CanvasWidth { get; }

    public int CanvasHeight { get; }

    public string CultureName { get; }

    public IReadOnlyDictionary<string, string> Translations { get; }

    private static ReadOnlyDictionary<string, string> CreateTranslations(IDictionary<string, string> culture)
    {
        var translations = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in culture)
        {
            if (entry.Key.StartsWith("dashboards.", StringComparison.OrdinalIgnoreCase)
                || entry.Key.StartsWith("dashboard.", StringComparison.OrdinalIgnoreCase)
                || entry.Key.StartsWith("overlay.", StringComparison.OrdinalIgnoreCase)
                || entry.Key.Equals("ui.tb_gamestate", StringComparison.OrdinalIgnoreCase))
                translations[entry.Key] = entry.Value;
        }
        return new ReadOnlyDictionary<string, string>(translations);
    }
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
