using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

/// <summary>
/// Set some specific configurations when updated to a specific version
/// </summary>
public class UpdateConfigSetter
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private Dictionary<Version, Action<ZTMZPacenoteTool>> _configSetters = new ();

    private Version currentVersion = new Version(UpdateService.CurrentVersion);

    public UpdateConfigSetter(ZTMZPacenoteTool tool)
    {
        this._configSetters.Add(new Version("2.99.99.20"), tool => {
            // set default udp port to 59996 for Dirt Rally 1.0/2.0
            var dr1 = tool.Games.FirstOrDefault(a => a.Name == "Dirt Rally 1.0");
            var dr2 = tool.Games.FirstOrDefault(a => a.Name == "Dirt Rally 2.0");

            var setGamePort = (IGame game) => {
                if (game != null) {
                    UdpGameConfig cfg = (UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name];
                    cfg.Port = 59996;
                    Config.Instance.SaveGameConfig(game);
                }
            };
            
            setGamePort(dr1);
            setGamePort(dr2);
        });

        this._configSetters.Add(new Version("2.99.99.21"), tool => {
            Config.Instance.CollisionSpeedChangeThreshold_Slight = 160f;
            Config.Instance.CollisionSpeedChangeThreshold_Medium = 280f;
            Config.Instance.CollisionSpeedChangeThreshold_Severe = 400f;
            Config.Instance.SaveUserConfig();
        });

        this._configSetters.Add(new Version("2.99.99.29"), tool => {
            // force rbr port to 59986
            var rbr = tool.Games.FirstOrDefault(a => a.Name == "Richard Burns Rally - RSF");
            if (rbr != null) {
                UdpGameConfig cfg = (UdpGameConfig)rbr.GameConfigurations[UdpGameConfig.Name];
                cfg.Port = 59986;
                Config.Instance.SaveGameConfig(rbr);
            }
        });

        // this._configSetters.Add(new Version("2.99.99.27"), tool => {
        // still force for 2.99.99.30 version
        this._configSetters.Add(new Version("2.99.99.30"), tool => {
            var rbr = tool.Games.FirstOrDefault(a => a.Name == "Richard Burns Rally - RSF");
            if (rbr != null) {
                // use jannemod_v3.zdb as default pacenote
                CommonGameConfigs cfg = (CommonGameConfigs)rbr.GameConfigurations[CommonGameConfigs.Name];

                // locate the index of the property
                int index = cfg.PropertyName.Keys.ToList().IndexOf("game.rbr.additional_settings.additional_pacenote_def");
                if (cfg.PropertyType.Count == 0 || cfg.PropertyType.Count <= index) {
                    // no propertyType
                    var defaultConfig = (CommonGameConfigs)rbr.DefaultGameConfigurations[CommonGameConfigs.Name];
                    cfg.PropertyType = defaultConfig.PropertyType;
                }
                cfg.PropertyType[index] = "file:zdb";
                if (index == -1) {
                    cfg.PropertyName.Add("game.rbr.additional_settings.additional_pacenote_def", "game.rbr.additional_settings.tooltip.additional_pacenote_def");
                    cfg.PropertyValue.Add(AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, "jannemod_v3.zdb")));
                } else {
                    cfg.PropertyName["game.rbr.additional_settings.additional_pacenote_def"] = "game.rbr.additional_settings.tooltip.additional_pacenote_def";
                    cfg.PropertyValue[index] = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, "jannemod_v3.zdb"));
                }

                Config.Instance.SaveGameConfig(rbr);
            }
        });

        this._configSetters.Add(new Version("2.99.99.32"), tool => {
            // force vr ztmz hud to false
            Config.Instance.VrUseZTMZHud = false;
            Config.Instance.SaveUserConfig();
        });

        this._configSetters.Add(new Version("2.99.99.33"), tool => {
            // alter replay tables to include the new columns
            // locate replay databases 
            Config.Instance.ReplaySaveWithoutInterval = true;
            Config.Instance.SaveUserConfig();
            for (int i = 0; i < tool.Games.Count; i++) {
                var game = tool.Games[i];
                var replayDbPath = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, $"{game.Name}{Constants.FILEEXT_REPLAYS}"));
                if (File.Exists(replayDbPath)) {
                    // alter tables
                    using (var connection = new SqliteConnection($"Data Source={replayDbPath}")) {
                        connection.Open();
                        using (var command = connection.CreateCommand()) {
                            try {
                                // alter replay table
                                var sql = @"-- new columns
ALTER TABLE replay ADD COLUMN track_length REAL NOT NULL DEFAULT 0;
ALTER TABLE replay ADD COLUMN locked INTEGER NOT NULL DEFAULT 0;
ALTER TABLE replay ADD COLUMN comment TEXT DEFAULT NULL;
ALTER TABLE replay ADD COLUMN video_path TEXT DEFAULT NULL;";
                                command.CommandText = sql;
                                command.ExecuteNonQuery();

                                // alter replay_details_per_time table
                                sql = @"-- new columns
ALTER TABLE replay_details_per_time ADD COLUMN timestamp INTEGER NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN completion_rate REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN speed REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN clutch REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN brake REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN throttle REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN handbrake REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN steering REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN gear REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN max_gears REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN rpm REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN max_rpm REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN pos_x REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN pos_y REAL NOT NULL DEFAULT 0;
ALTER TABLE replay_details_per_time ADD COLUMN pos_z REAL NOT NULL DEFAULT 0;";
                                command.CommandText = sql;
                                command.ExecuteNonQuery();
                            } catch (SqliteException ex) {
                                _logger.Error(ex, "Failed to alter tables with new columns defined in 2.99.99.33 for {0}", game.Name);
                            }
                        }
                    }
                }
            }
        });

        this._configSetters.Add(new Version("2.99.99.34"), tool => {
            Config.Instance.ReplaySaveWithoutInterval = false;  // for the performance
            Config.Instance.SaveUserConfig();
            // move the replay database to the new location? damn
            Dictionary<string, Dictionary<int, List<ReplayDetailsPerTime>>> replayDetailsPerTimesPerGame = new();
            Dictionary<string, Dictionary<int, List<ReplayDetailsPerCheckpoint>>> replayDetailsPerCheckpointsPerGame = new();
            Dictionary<string, List<Replay>> replaysPerGame = new();
            for (int i = 0; i < tool.Games.Count; i++) {
                Dictionary<int, List<ReplayDetailsPerTime>> replayDetailsPerTimes = new();
                Dictionary<int, List<ReplayDetailsPerCheckpoint>> replayDetailsPerCheckpoints = new();
                List<Replay> replays = new();
                var game = tool.Games[i];
                var replayDbPath = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, $"{game.Name}{Constants.FILEEXT_REPLAYS}"));
                if (File.Exists(replayDbPath)) {
                    // read all data from the old database
                    using (var connection = new SqliteConnection($"Data Source={replayDbPath}")) {
                        // read replays
                        connection.Open();
                        connection.QueryAsync<Replay>("SELECT * FROM replay").Result.ToList().ForEach(a => replays.Add(a));

                        // read replay details per time
                        connection.QueryAsync<ReplayDetailsPerTime>("SELECT * FROM replay_details_per_time").Result.ToList().ForEach(a => {
                            if (!replayDetailsPerTimes.ContainsKey(a.id)) {
                                replayDetailsPerTimes.Add(a.id, new List<ReplayDetailsPerTime>());
                            }
                            replayDetailsPerTimes[a.id].Add(a);
                        });

                        // read replay details per checkpoint
                        connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint").Result.ToList().ForEach(a => {
                            if (!replayDetailsPerCheckpoints.ContainsKey(a.id)) {
                                replayDetailsPerCheckpoints.Add(a.id, new List<ReplayDetailsPerCheckpoint>());
                            }
                            replayDetailsPerCheckpoints[a.id].Add(a);
                        });
                    }
                }
                replayDetailsPerTimesPerGame.Add(game.Name, replayDetailsPerTimes);
                replayDetailsPerCheckpointsPerGame.Add(game.Name, replayDetailsPerCheckpoints);
                replaysPerGame.Add(game.Name, replays);
            }

            // write to the new database
            for (int i = 0; i < tool.Games.Count; i++) {
                var game = tool.Games[i];
                for (int j = 0; j < replaysPerGame[game.Name].Count; j++) {
                    var replay = replaysPerGame[game.Name][j];
                    var replayDetailsPerTimes = replayDetailsPerTimesPerGame[game.Name][replay.id];
                    var replayDetailsPerCheckpoints = replayDetailsPerCheckpointsPerGame[game.Name][replay.id];
                    ReplayManager.Instance.saveReplay(game, replay, replayDetailsPerTimes, replayDetailsPerCheckpoints).Wait();
                }
            }
        });
    }

    public void SetConfiguration(ZTMZPacenoteTool tool)
    {
        foreach (var item in _configSetters)
        {
            var updateFlagFile = AppLevelVariables.Instance.GetPath(item.Key.ToString());
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(Constants.PATH_UPDATEFLAG));
            var updateFLagFileNew = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_UPDATEFLAG, item.Key.ToString()));
            // current version is greater than or equal to the version in the dictionary
            // and the update flag file does not exist, then set the configurations
            if (currentVersion >= item.Key && !File.Exists(updateFlagFile) && !File.Exists(updateFLagFileNew))
            {
                _logger.Info("[Update] Setting configurations for version {0}", item.Key);
                try {
                    item.Value(tool);
                    _logger.Info("[Update] Set configurations for version {0}", item.Key);
                } catch (System.Exception ex) {
                    _logger.Error(ex, "Failed to set configurations for version {0}", item.Key);
                }
                File.Create(updateFLagFileNew);
            }
        }
    }
    
}

