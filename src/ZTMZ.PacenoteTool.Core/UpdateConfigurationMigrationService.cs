#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed class UpdateConfigurationMigrationService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Version _currentVersion;
    private readonly IApplicationPaths _paths;
    private readonly IReadOnlyDictionary<Version, Action<ZTMZPacenoteTool>> _migrations;

    public UpdateConfigurationMigrationService(
        Version currentVersion,
        IApplicationPaths paths)
    {
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _migrations = CreateMigrations();
    }

    public void Apply(ZTMZPacenoteTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var updateFlagDirectory = _paths.GetPath(Constants.PATH_UPDATEFLAG);
        Directory.CreateDirectory(updateFlagDirectory);

        foreach (var migration in _migrations.OrderBy(item => item.Key))
        {
            var legacyFlag = _paths.GetPath(migration.Key.ToString());
            var currentFlag = Path.Combine(updateFlagDirectory, migration.Key.ToString());
            if (_currentVersion < migration.Key ||
                File.Exists(legacyFlag) ||
                File.Exists(currentFlag))
            {
                continue;
            }

            Logger.Info("[Update] Setting configurations for version {0}", migration.Key);
            try
            {
                migration.Value(tool);
                using (File.Create(currentFlag))
                {
                }
                Logger.Info("[Update] Set configurations for version {0}", migration.Key);
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    "Failed to set configurations for version {0}",
                    migration.Key);
            }
        }
    }

    private Dictionary<Version, Action<ZTMZPacenoteTool>> CreateMigrations()
    {
        return new Dictionary<Version, Action<ZTMZPacenoteTool>>
        {
            [new Version("2.99.99.51")] = _ => MispackagedAcrProfileMigration.Remove(_paths.GetPath("")),
            [new Version("2.99.99.20")] = tool =>
            {
                SetUdpPort(tool, "Dirt Rally 1.0", 30000);
                SetUdpPort(tool, "Dirt Rally 2.0", 30000);
            },
            [new Version("2.99.99.21")] = _ =>
            {
                Config.Instance.CollisionSpeedChangeThreshold_Slight = 160f;
                Config.Instance.CollisionSpeedChangeThreshold_Medium = 280f;
                Config.Instance.CollisionSpeedChangeThreshold_Severe = 400f;
                Config.Instance.SaveUserConfig();
            },
            [new Version("2.99.99.29")] = tool =>
                SetUdpPort(tool, "Richard Burns Rally - RSF", 30001),
            [new Version("2.99.99.30")] = ConfigureRbrPacenoteDatabase,
            [new Version("2.99.99.32")] = _ =>
            {
                Config.Instance.VrUseZTMZHud = false;
                Config.Instance.SaveUserConfig();
            },
            [new Version("2.99.99.33")] = _ =>
            {
                Config.Instance.ReplaySaveWithoutInterval = true;
                Config.Instance.SaveUserConfig();
            },
            [new Version("2.99.99.34")] = _ =>
            {
                Config.Instance.ReplaySaveWithoutInterval = false;
                Config.Instance.SaveUserConfig();
            }
        };
    }

    private static void SetUdpPort(ZTMZPacenoteTool tool, string gameName, int port)
    {
        var game = tool.Games.FirstOrDefault(candidate => candidate.Name == gameName);
        if (game?.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) != true ||
            configuration is not UdpGameConfig udp)
        {
            return;
        }

        udp.Port = port;
        Config.Instance.SaveGameConfig(game);
    }

    private void ConfigureRbrPacenoteDatabase(ZTMZPacenoteTool tool)
    {
        var game = tool.Games.FirstOrDefault(candidate =>
            candidate.Name == "Richard Burns Rally - RSF");
        if (game?.GameConfigurations.TryGetValue(CommonGameConfigs.Name, out var configuration) != true ||
            configuration is not CommonGameConfigs common)
        {
            return;
        }

        const string propertyName = "game.rbr.additional_settings.additional_pacenote_def";
        const string tooltip = "game.rbr.additional_settings.tooltip.additional_pacenote_def";
        var index = common.PropertyName.Keys.ToList().IndexOf(propertyName);
        if (index < 0)
        {
            common.PropertyName.Add(propertyName, tooltip);
            common.PropertyValue.Add(
                _paths.GetPath(Path.Combine(Constants.PATH_GAMES, "jannemod_v3.zdb")));
            index = common.PropertyValue.Count - 1;
        }
        else
        {
            common.PropertyName[propertyName] = tooltip;
            common.PropertyValue[index] =
                _paths.GetPath(Path.Combine(Constants.PATH_GAMES, "jannemod_v3.zdb"));
        }

        if (common.PropertyType.Count <= index)
        {
            var defaultConfiguration =
                (CommonGameConfigs)game.DefaultGameConfigurations[CommonGameConfigs.Name];
            common.PropertyType = defaultConfiguration.PropertyType.ToList();
        }

        while (common.PropertyType.Count <= index)
        {
            common.PropertyType.Add(string.Empty);
        }

        common.PropertyType[index] = "file:zdb";
        Config.Instance.SaveGameConfig(game);
    }
}
