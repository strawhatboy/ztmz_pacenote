#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal static class AvaloniaLocalization
{
    private const string ResourceHomeEnvironmentVariable = "ZTMZ_RESOURCE_HOME";
    private static ResourceDictionary? _currentCultureResources;

    public static event EventHandler? CultureChanging;
    public static event EventHandler? CultureChanged;

    public static string ResourceRoot { get; private set; } = string.Empty;

    public static void Initialize()
    {
        var paths = AppLevelVariables.Instance;
        ResourceRoot = ResolveResourceRoot();
        ResourceBootstrapService.InitializeLocalization(
            GetLocalizationRoots(ResourceRoot, paths),
            Config.Instance.Language);
    }

    public static void ApplyCurrentCulture(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (I18NLoader.Instance.CurrentCulture == null)
        {
            return;
        }

        // Replace the whole culture dictionary at once. Per-key updates invalidate
        // the visual tree once for every localized resource.
        var resources = new ResourceDictionary();
        foreach (var resource in I18NLoader.Instance.CurrentCulture)
        {
            resources[resource.Key] = resource.Value;
        }

        if (_currentCultureResources != null)
        {
            application.Resources.MergedDictionaries.Remove(_currentCultureResources);
        }

        application.Resources.MergedDictionaries.Add(resources);
        _currentCultureResources = resources;
    }

    public static void SetCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        SetCultureCore(culture);
    }

    public static async Task SetCultureAsync(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        CultureChanging?.Invoke(null, EventArgs.Empty);

        // Give the busy overlay a render pass before replacing application resources.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
        SetCultureCore(culture);
    }

    private static void SetCultureCore(string culture)
    {
        I18NLoader.Instance.SetCulture(culture);
        Config.Instance.Language = I18NLoader.Instance.CurrentCultureName;
        if (Application.Current != null)
        {
            ApplyCurrentCulture(Application.Current);
        }

        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) => I18NLoader.Instance[key];

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static IReadOnlyList<string> GetLocalizationRoots(
        string resourceRoot,
        IApplicationPaths paths) =>
    [
        Path.Combine(resourceRoot, Constants.PATH_LANGUAGE),
        Path.Combine(resourceRoot, "..", Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE),
        Path.Combine(AppContext.BaseDirectory, "resources", Constants.PATH_LANGUAGE),
        Path.Combine(AppContext.BaseDirectory, Constants.PATH_GAMES, Constants.PATH_LANGUAGE),
        Path.Combine(AppContext.BaseDirectory, Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE),
        paths.GetPath(Constants.PATH_LANGUAGE)
    ];

    private static string ResolveResourceRoot()
    {
        var configured = Environment.GetEnvironmentVariable(ResourceHomeEnvironmentVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "resources")
            : configured);
    }
}
