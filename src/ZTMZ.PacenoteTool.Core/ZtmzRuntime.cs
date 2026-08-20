#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed record ZtmzRuntimeOptions(
    string GamesDirectory,
    ResourceBootstrapOptions Resources,
    bool CheckPrerequisites = false);

public sealed record ZtmzRuntimeState(
    ResourceBootstrapResult Resources,
    ProfileManager ProfileManager,
    IReadOnlyList<IGame> Games,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<CoDriverPackage> CoDrivers,
    IReadOnlyList<AudioOutputDeviceInfo> OutputDevices,
    IReadOnlyList<GamePrerequisiteStatus> Prerequisites);

public sealed class ZtmzRuntime
{
    private readonly ResourceBootstrapService _resourceBootstrap;
    private readonly GameCatalogService _gameCatalog;
    private readonly ProfileCatalogService _profileCatalog;
    private readonly PrerequisiteService _prerequisites;
    private readonly IAudioOutputDeviceEnumerator _audioDevices;
    private readonly IAudioPlaybackEngineFactory _audioPlayback;

    public ZtmzRuntime(
        IApplicationPaths paths,
        IAudioOutputDeviceEnumerator audioDevices,
        IAudioPlaybackEngineFactory audioPlayback,
        NLog.Logger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _resourceBootstrap = new ResourceBootstrapService(paths);
        _gameCatalog = new GameCatalogService(logger);
        _profileCatalog = new ProfileCatalogService(paths);
        _prerequisites = new PrerequisiteService();
    }

    public PrerequisiteService Prerequisites => _prerequisites;

    public async Task<ZtmzRuntimeState> InitializeAsync(
        ZtmzRuntimeOptions options,
        Action<string>? reportStatus = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GamesDirectory);

        reportStatus?.Invoke("Loading pacenote definitions...");
        var resources = await _resourceBootstrap.InitializeAsync(
            options.Resources,
            cancellationToken);
        reportStatus?.Invoke("Pacenote definitions loaded.");

        reportStatus?.Invoke("Loading profile manager...");
        var profileManager = await Task.Run(
            () => new ProfileManager(_audioPlayback, _profileCatalog),
            cancellationToken);
        reportStatus?.Invoke("Profile manager loaded.");

        reportStatus?.Invoke("Loading games...");
        // Plugin discovery may load managed assemblies and migrate per-game files.
        // Keep that synchronous work off a UI host's startup dispatcher so its splash can render.
        var games = await Task.Run(
            () => _gameCatalog.Load(options.GamesDirectory),
            cancellationToken).ConfigureAwait(false);
        reportStatus?.Invoke($"{games.Count} games loaded.");

        reportStatus?.Invoke("Loading profiles...");
        var profiles = profileManager.GetAllProfiles();
        reportStatus?.Invoke($"{profiles.Count} profiles loaded.");

        reportStatus?.Invoke("Loading codrivers...");
        var coDrivers = SelectCoDrivers(profileManager);
        reportStatus?.Invoke($"{coDrivers.Count} codrivers loaded.");

        reportStatus?.Invoke("Loading output devices...");
        var outputDevices = _audioDevices.EnumerateActiveRenderDevices().ToList();
        SelectConfiguredOutputDevice(outputDevices);
        reportStatus?.Invoke($"{outputDevices.Count} output devices loaded.");

        var prerequisiteStatuses = options.CheckPrerequisites
            ? _prerequisites.CheckAll(games)
            : [];

        return new ZtmzRuntimeState(
            resources,
            profileManager,
            games,
            profiles,
            coDrivers,
            outputDevices,
            prerequisiteStatuses);
    }

    private static List<CoDriverPackage> SelectCoDrivers(ProfileManager profileManager)
    {
        var coDrivers = new List<CoDriverPackage>();
        foreach (var directory in profileManager.GetAllCodrivers())
        {
            if (!Config.Instance.UseDefaultSoundPackageByDefault &&
                directory.EndsWith(Constants.DEFAULT_CODRIVER, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (profileManager.CoDriverPackages.TryGetValue(directory, out var package))
            {
                coDrivers.Add(package);
            }
        }

        return coDrivers;
    }

    private static void SelectConfiguredOutputDevice(IReadOnlyList<AudioOutputDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            return;
        }

        var configuredIndex = ResolveConfiguredOutputDeviceIndex(
            devices,
            Config.Instance.UI_SelectedPlaybackDeviceId,
            Config.Instance.UI_SelectedPlaybackDevice);

        Config.Instance.UI_SelectedPlaybackDevice = configuredIndex;
        Config.Instance.UI_SelectedPlaybackDeviceId = devices[configuredIndex].Id;
    }

    internal static int ResolveConfiguredOutputDeviceIndex(
        IReadOnlyList<AudioOutputDeviceInfo> devices,
        string configuredDeviceId,
        int legacyConfiguredIndex)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count == 0)
        {
            return -1;
        }

        var configuredIndex = devices.ToList().FindIndex(device =>
            string.Equals(
                device.Id,
                configuredDeviceId,
                StringComparison.OrdinalIgnoreCase));
        if (configuredIndex < 0)
        {
            configuredIndex = string.IsNullOrWhiteSpace(configuredDeviceId)
                ? Math.Clamp(
                    legacyConfiguredIndex,
                    0,
                    devices.Count - 1)
                : devices.ToList().FindIndex(device => device.IsDefault);

            if (configuredIndex < 0)
            {
                configuredIndex = 0;
            }
        }

        return configuredIndex;
    }
}
