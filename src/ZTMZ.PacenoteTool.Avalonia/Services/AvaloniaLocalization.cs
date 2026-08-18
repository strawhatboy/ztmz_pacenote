#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal static class AvaloniaLocalization
{
    private const string ResourceHomeEnvironmentVariable = "ZTMZ_RESOURCE_HOME";

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

        foreach (var resource in I18NLoader.Instance.CurrentCulture)
        {
            application.Resources[resource.Key] = resource.Value;
        }
    }

    public static void SetCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
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
