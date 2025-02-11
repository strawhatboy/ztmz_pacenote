// Manage the replays

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FFMpegCore;
using FFMpegCore.Enums;
using GoogleAnalyticsTracker.Core;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base;

public class Replay {
    public int id;
    public string track;
    public string car;
    public string car_class;
    public float finish_time;
    public float track_length;
    public bool retired;
    // locked replay should not be deleted when the replay count exceeds the limit
    public bool locked;
    // how many checkpoints in this replay, 256? 128? or just 4?
    public int checkpoints;
    public string comment;
    public string video_path;
    public Int64 video_begin_timestamp;
    public Int64 video_end_timestamp;
    public DateTime date;

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay (
            id INTEGER PRIMARY KEY AUTOINCREMENT
        );
    ";

    public static readonly List<string> CREATION_ADDITIONAL_COLUMNS_SQL = new List<string> {
        "ALTER TABLE replay ADD COLUMN game TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN track TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN car TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN car_class TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN finish_time REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN track_length REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN locked INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN checkpoints INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN date TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN comment TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN video_path TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE replay ADD COLUMN video_begin_timestamp INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN video_end_timestamp INTEGER NOT NULL DEFAULT 0;"
    };

    public string ExportToCSV(IGame game) {
        var sb = new StringBuilder();
        // write the header, lowercase
        sb.AppendLine("timestamp,time,distance,completion_rate,speed,clutch,brake,throttle,handbrake,steering,gear,max_gears,rpm,max_rpm,pos_x,pos_y,pos_z");
        var detailsPerTime = ReplayManager.Instance.getReplayDetailsPerTime(id).Result;
        foreach (var d in detailsPerTime) {
            sb.AppendLine($"{d.timestamp},{d.time},{d.distance},{d.completion_rate},{d.speed},{d.clutch},{d.brake},{d.throttle},{d.handbrake},{d.steering},{d.gear},{d.max_gears},{d.rpm},{d.max_rpm},{d.pos_x},{d.pos_y},{d.pos_z}");
        }
        return sb.ToString();
    }
}

// the distance finished at time
public class ReplayDetailsPerTime {
    public int id;
    public float time;
    public float distance;
    public Int64 timestamp;
    public float completion_rate;
    public float speed;
    public float clutch;
    public float brake;
    public float throttle;
    public float handbrake;
    public float steering;
    public float gear;
    public float max_gears;
    public float rpm;
    public float max_rpm;
    public float pos_x;
    public float pos_y;
    public float pos_z;
    public float g_long;
    public float g_lat;
    public float brake_temp_rear_left;
    public float brake_temp_rear_right;
    public float brake_temp_front_left;
    public float brake_temp_front_right;
    public float suspension_rear_left;
    public float suspension_rear_right;
    public float suspension_front_left;
    public float suspension_front_right;
    public float suspension_speed_rear_left;
    public float suspension_speed_rear_right;
    public float suspension_speed_front_left;
    public float suspension_speed_front_right;

    public static ReplayDetailsPerTime linearInterpolation(ReplayDetailsPerTime a, ReplayDetailsPerTime b, Int64 timestamp) {
        var result = new ReplayDetailsPerTime();
        var ratio = (float)(timestamp - a.timestamp) / (b.timestamp - a.timestamp);
        result.time = a.time + (b.time - a.time) * ratio;
        result.distance = a.distance + (b.distance - a.distance) * ratio;
        result.timestamp = timestamp;
        result.completion_rate = a.completion_rate + (b.completion_rate - a.completion_rate) * ratio;
        result.speed = a.speed + (b.speed - a.speed) * ratio;
        result.clutch = a.clutch + (b.clutch - a.clutch) * ratio;
        result.brake = a.brake + (b.brake - a.brake) * ratio;
        result.throttle = a.throttle + (b.throttle - a.throttle) * ratio;
        result.handbrake = a.handbrake + (b.handbrake - a.handbrake) * ratio;
        result.steering = a.steering + (b.steering - a.steering) * ratio;
        result.gear = a.gear + (b.gear - a.gear) * ratio;
        result.max_gears = a.max_gears + (b.max_gears - a.max_gears) * ratio;
        result.rpm = a.rpm + (b.rpm - a.rpm) * ratio;
        result.max_rpm = a.max_rpm + (b.max_rpm - a.max_rpm) * ratio;
        result.pos_x = a.pos_x + (b.pos_x - a.pos_x) * ratio;
        result.pos_y = a.pos_y + (b.pos_y - a.pos_y) * ratio;
        result.pos_z = a.pos_z + (b.pos_z - a.pos_z) * ratio;
        result.g_long = a.g_long + (b.g_long - a.g_long) * ratio;
        result.g_lat = a.g_lat + (b.g_lat - a.g_lat) * ratio;
        result.brake_temp_rear_left = a.brake_temp_rear_left + (b.brake_temp_rear_left - a.brake_temp_rear_left) * ratio;
        result.brake_temp_rear_right = a.brake_temp_rear_right + (b.brake_temp_rear_right - a.brake_temp_rear_right) * ratio;
        result.brake_temp_front_left = a.brake_temp_front_left + (b.brake_temp_front_left - a.brake_temp_front_left) * ratio;
        result.brake_temp_front_right = a.brake_temp_front_right + (b.brake_temp_front_right - a.brake_temp_front_right) * ratio;
        result.suspension_rear_left = a.suspension_rear_left + (b.suspension_rear_left - a.suspension_rear_left) * ratio;
        result.suspension_rear_right = a.suspension_rear_right + (b.suspension_rear_right - a.suspension_rear_right) * ratio;
        result.suspension_front_left = a.suspension_front_left + (b.suspension_front_left - a.suspension_front_left) * ratio;
        result.suspension_front_right = a.suspension_front_right + (b.suspension_front_right - a.suspension_front_right) * ratio;
        result.suspension_speed_rear_left = a.suspension_speed_rear_left + (b.suspension_speed_rear_left - a.suspension_speed_rear_left) * ratio;
        result.suspension_speed_rear_right = a.suspension_speed_rear_right + (b.suspension_speed_rear_right - a.suspension_speed_rear_right) * ratio;
        result.suspension_speed_front_left = a.suspension_speed_front_left + (b.suspension_speed_front_left - a.suspension_speed_front_left) * ratio;
        result.suspension_speed_front_right = a.suspension_speed_front_right + (b.suspension_speed_front_right - a.suspension_speed_front_right) * ratio;
        return result;
    }

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay_details_per_time (
            id INTEGER
        );
    ";
    public static readonly List<string> CREATION_ADDITIONAL_COLUMNS_SQL = new List<string> {
        "ALTER TABLE replay_details_per_time ADD COLUMN time REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN distance REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN timestamp INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN completion_rate REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN speed REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN clutch REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN brake REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN throttle REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN handbrake REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN steering REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN gear REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN max_gears REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN rpm REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN max_rpm REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN pos_x REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN pos_y REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN pos_z REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN g_long REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN g_lat REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN brake_temp_rear_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN brake_temp_rear_right REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN brake_temp_front_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN brake_temp_front_right REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_rear_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_rear_right REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_front_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_front_right REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_speed_rear_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_speed_rear_right REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_speed_front_left REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_time ADD COLUMN suspension_speed_front_right REAL NOT NULL DEFAULT 0;"
    };
}

public class ReplayDetailsPerCheckpoint {
    public int id;
    // index of the checkpoint, if checkpoint == 0, then it's the start point
    // checkpoint < Replay.checkpoints, the last checkpoint+1 is the finish point if not retired
    public int checkpoint;
    public float time;
    public float distance;

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay_details_per_checkpoint (
            id INTEGER
        );
    ";
    public static readonly List<string> CREATION_ADDITIONAL_COLUMNS_SQL = new List<string> {
        "ALTER TABLE replay_details_per_checkpoint ADD COLUMN checkpoint INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_checkpoint ADD COLUMN time REAL NOT NULL DEFAULT 0;",
        "ALTER TABLE replay_details_per_checkpoint ADD COLUMN distance REAL NOT NULL DEFAULT 0;"
    };
}

public class ReplayManager {
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static ReplayManager instance;
    private static readonly object lockObject = new object();
    public static ReplayManager Instance {
        get {
            if (instance == null) {
                lock (lockObject) {
                    if (instance == null) {
                        instance = new ReplayManager();
                    }
                }
            }
            return instance;
        }
    }

    private ReplayManager() {
    }

    public void Init() {
        initDB();
    }

    private void createDataBase(SqliteCommand cmd, string tableSql, List<string> columnsSql) {
        cmd.CommandText = tableSql;
        cmd.ExecuteNonQuery();
        foreach (var columnSql in columnsSql) {
            cmd.CommandText = columnSql;
            try {
                cmd.ExecuteNonQuery();
            } catch (SqliteException ex) {
                if (ex.Message.Contains("duplicate column name")) {
                    // ignore
                }
            }
        }
    }

    private void initDB() {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                createDataBase(command, Replay.CREATION_SQL, Replay.CREATION_ADDITIONAL_COLUMNS_SQL);
                createDataBase(command, ReplayDetailsPerTime.CREATION_SQL, ReplayDetailsPerTime.CREATION_ADDITIONAL_COLUMNS_SQL);
                createDataBase(command, ReplayDetailsPerCheckpoint.CREATION_SQL, ReplayDetailsPerCheckpoint.CREATION_ADDITIONAL_COLUMNS_SQL);
            }
        }
    }
    
    private string getConnectionString() {
        var dbpath = AppLevelVariables.Instance.GetPath(Constants.FILE_REPLAYS);
        return "Data Source=" + dbpath;
    }
    public async Task saveReplay(IGame game, Replay replay, List<ReplayDetailsPerTime> detailsPerTime, List<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        _logger.Info($"Saving replay: {replay.track} {replay.car} {replay.car_class} {replay.finish_time} {replay.retired} {replay.checkpoints} {replay.date}");
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                command.CommandText = @"
                    INSERT INTO replay (game, track, car, car_class, finish_time, track_length, retired, checkpoints, date, comment, video_path, locked, video_begin_timestamp, video_end_timestamp)
                    VALUES (@game, @track, @car, @car_class, @finish_time, @track_length, @retired, @checkpoints, @date, @comment, @video_path, @locked, @video_begin_timestamp, @video_end_timestamp);
                ";
                command.Parameters.AddWithValue("@game", game.Name);
                command.Parameters.AddWithValue("@track", replay.track);
                command.Parameters.AddWithValue("@car", replay.car);
                command.Parameters.AddWithValue("@car_class", replay.car_class);
                command.Parameters.AddWithValue("@finish_time", replay.finish_time);
                command.Parameters.AddWithValue("@track_length", replay.track_length);
                command.Parameters.AddWithValue("@retired", replay.retired);
                command.Parameters.AddWithValue("@locked", replay.locked);
                command.Parameters.AddWithValue("@checkpoints", replay.checkpoints);
                command.Parameters.AddWithValue("@date", replay.date.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@comment", replay.comment);
                command.Parameters.AddWithValue("@video_path", replay.video_path);
                command.Parameters.AddWithValue("@video_begin_timestamp", replay.video_begin_timestamp);
                command.Parameters.AddWithValue("@video_end_timestamp", replay.video_end_timestamp);

                await command.ExecuteNonQueryAsync();
                command.CommandText = "SELECT last_insert_rowid()";
                replay.id = Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            
            detailsPerTime.ForEach(d => d.id = replay.id);
            detailsPerCheckpoint.ForEach(d => d.id = replay.id);

            // get the first record of each time to avoid duplicate records
            var uniqueDetailsPerTime = detailsPerTime
                .GroupBy(d => d.time)
                .Select(g => g.First())
                .OrderBy(d => d.time)
                .ToList();

            var transaction = await connection.BeginTransactionAsync();
            var insertSQL = @"
                INSERT INTO replay_details_per_time (id, time, distance, timestamp, completion_rate, speed, clutch, brake, throttle, handbrake, steering, gear, max_gears, rpm, max_rpm, pos_x, pos_y, pos_z, g_long, g_lat, brake_temp_rear_left, brake_temp_rear_right, brake_temp_front_left, brake_temp_front_right, suspension_rear_left, suspension_rear_right, suspension_front_left, suspension_front_right, suspension_speed_rear_left, suspension_speed_rear_right, suspension_speed_front_left, suspension_speed_front_right)
                VALUES (@id, @time, @distance, @timestamp, @completion_rate, @speed, @clutch, @brake, @throttle, @handbrake, @steering, @gear, @max_gears, @rpm, @max_rpm, @pos_x, @pos_y, @pos_z, @g_long, @g_lat, @brake_temp_rear_left, @brake_temp_rear_right, @brake_temp_front_left, @brake_temp_front_right, @suspension_rear_left, @suspension_rear_right, @suspension_front_left, @suspension_front_right, @suspension_speed_rear_left, @suspension_speed_rear_right, @suspension_speed_front_left, @suspension_speed_front_right);
                ";
            for (int i = 0; i < uniqueDetailsPerTime.Count; i++) {
                var d = uniqueDetailsPerTime[i];
                using (var command = connection.CreateCommand()) {
                    command.CommandText = insertSQL;
                    command.Parameters.AddWithValue("@id", d.id);
                    command.Parameters.AddWithValue("@time", d.time);
                    command.Parameters.AddWithValue("@distance", d.distance);
                    command.Parameters.AddWithValue("@timestamp", d.timestamp);
                    command.Parameters.AddWithValue("@completion_rate", d.completion_rate);
                    command.Parameters.AddWithValue("@speed", d.speed);
                    command.Parameters.AddWithValue("@clutch", d.clutch);
                    command.Parameters.AddWithValue("@brake", d.brake);
                    command.Parameters.AddWithValue("@throttle", d.throttle);
                    command.Parameters.AddWithValue("@handbrake", d.handbrake);
                    command.Parameters.AddWithValue("@steering", d.steering);
                    command.Parameters.AddWithValue("@gear", d.gear);
                    command.Parameters.AddWithValue("@max_gears", d.max_gears);
                    command.Parameters.AddWithValue("@rpm", d.rpm);
                    command.Parameters.AddWithValue("@max_rpm", d.max_rpm);
                    command.Parameters.AddWithValue("@pos_x", d.pos_x);
                    command.Parameters.AddWithValue("@pos_y", d.pos_y);
                    command.Parameters.AddWithValue("@pos_z", d.pos_z);
                    command.Parameters.AddWithValue("@g_long", d.g_long);
                    command.Parameters.AddWithValue("@g_lat", d.g_lat);
                    command.Parameters.AddWithValue("@brake_temp_rear_left", d.brake_temp_rear_left);
                    command.Parameters.AddWithValue("@brake_temp_rear_right", d.brake_temp_rear_right);
                    command.Parameters.AddWithValue("@brake_temp_front_left", d.brake_temp_front_left);
                    command.Parameters.AddWithValue("@brake_temp_front_right", d.brake_temp_front_right);
                    command.Parameters.AddWithValue("@suspension_rear_left", d.suspension_rear_left);
                    command.Parameters.AddWithValue("@suspension_rear_right", d.suspension_rear_right);
                    command.Parameters.AddWithValue("@suspension_front_left", d.suspension_front_left);
                    command.Parameters.AddWithValue("@suspension_front_right", d.suspension_front_right);
                    command.Parameters.AddWithValue("@suspension_speed_rear_left", d.suspension_speed_rear_left);
                    command.Parameters.AddWithValue("@suspension_speed_rear_right", d.suspension_speed_rear_right);
                    command.Parameters.AddWithValue("@suspension_speed_front_left", d.suspension_speed_front_left);
                    command.Parameters.AddWithValue("@suspension_speed_front_right", d.suspension_speed_front_right);

                    await command.ExecuteNonQueryAsync();
                }
            }

            insertSQL = @"
                INSERT INTO replay_details_per_checkpoint (id, checkpoint, time, distance)
                VALUES (@id, @checkpoint, @time, @distance);
                ";
            for (int i = 0; i < detailsPerCheckpoint.Count; i++) {
                var d = detailsPerCheckpoint[i];
                using (var command = connection.CreateCommand()) {
                    command.CommandText = insertSQL;
                    command.Parameters.AddWithValue("@id", d.id);
                    command.Parameters.AddWithValue("@checkpoint", d.checkpoint);
                    command.Parameters.AddWithValue("@time", d.time);
                    command.Parameters.AddWithValue("@distance", d.distance);
                    await command.ExecuteNonQueryAsync();
                }
            }
            await transaction.CommitAsync();

            // check if the replay exceeds the limit Config.Instance.ReplayStoredCountLimit by trackname and carname, delete the slowest ones, should always keep the latest one
            var replays = await getReplays(game);
            // remove the latest one from the list to avoid deleting the latest one
            var replaysDeleteCondidate = replays.Where(r => r.id != replay.id && r.track == replay.track && r.car == replay.car && r.locked == false).ToList();
            
            var replayCount = replaysDeleteCondidate.Count();  // locked replay should not be deleted
            if (replayCount > Config.Instance.ReplayStoredCountLimit) {
                _logger.Info($"Replay count exceeds the limit: {replayCount}");
                var replayToDelete = replaysDeleteCondidate
                    .OrderBy(r => r.finish_time)
                    .Skip(Config.Instance.ReplayStoredCountLimit)
                    .ToList();
                foreach (var r in replayToDelete) {
                    _logger.Info($"Deleting replay: {r.id} with finish time: {r.finish_time}");
                    await deleteReplay(r.id);
                }
            }
        }
        _logger.Info($"Replay saved: {replay.id}");
    }

    public async Task<List<Replay>> getReplays(IGame game) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return (await connection.QueryAsync<Replay>("SELECT * FROM replay WHERE game = @game", new { game=game.Name })).AsList();
        }
    }

    public async Task<Replay> getReplay(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE id = @id", new { id });
        }
    }

    public async Task<List<ReplayDetailsPerTime>> getReplayDetailsPerTime(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            // get the first record of each time
            return (await connection.QueryAsync<ReplayDetailsPerTime>(@"SELECT r.* 
            FROM replay_details_per_time r
            INNER JOIN (
                SELECT MIN(rowid) as rowid 
                FROM replay_details_per_time 
                WHERE id = @id 
                GROUP BY time
            ) m ON r.rowid = m.rowid
            ORDER BY timestamp ASC", new { id })).AsList();
        }
    }

    public async Task<List<ReplayDetailsPerCheckpoint>> getReplayDetailsPerCheckpoint(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return (await connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint WHERE id = @id ORDER BY checkpoint ASC", new { id })).AsList();
        }
    }

    public async Task deleteReplay(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            // read the replay first
            var replay = await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE id = @id", new { id });
            using (var command = connection.CreateCommand()) {
                command.CommandText = "DELETE FROM replay WHERE id = @id";
                command.Parameters.AddWithValue("@id", id);
                await command.ExecuteNonQueryAsync();
                command.CommandText = "DELETE FROM replay_details_per_time WHERE id = @id";
                await command.ExecuteNonQueryAsync();
                command.CommandText = "DELETE FROM replay_details_per_checkpoint WHERE id = @id";
                await command.ExecuteNonQueryAsync();
                _logger.Info($"Replay deleted: {id}");
            }
            if (Config.Instance.ReplayDeleteRelatedVideo && !string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
                // async delete
                await Task.Run(() => File.Delete(replay.video_path));
                _logger.Info($"Video deleted: {replay.video_path}");
            }
        }
    }

    public async Task<Replay> getBestReplayByTrackAndCarClass(IGame game, string track, string car_class) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE game = @game AND track = @track AND car_class = @car_class ORDER BY finish_time ASC LIMIT 1", new { game=game.Name, track, car_class });
        }
    }

    public async Task<Replay> getBestReplayByTrack(IGame game, string track) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE game = @game AND track = @track ORDER BY finish_time ASC LIMIT 1", new { game=game.Name, track });
        }
    }

    public async Task<Replay> getBestReplayByTrackAndCarName(IGame game, string track, string car_name) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE game = @game AND track = @track AND car = @car_name ORDER BY finish_time ASC LIMIT 1", new { game=game.Name, track, car_name });
        }
    }

    public async Task<Replay> GetBestReplay(IGame game, string track, string car_class, string car_name) {
        if (Config.Instance.ReplayPreferredFilter == 0) {
            return await getBestReplayByTrack(game, track);
        } else if (Config.Instance.ReplayPreferredFilter == 1) {
            return await getBestReplayByTrackAndCarClass(game, track, car_class);
        } else {
            return await getBestReplayByTrackAndCarName(game, track, car_name);
        }
    }

    public async void LockReplay(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                command.CommandText = "UPDATE replay SET locked = 1 WHERE id = @id";
                command.Parameters.AddWithValue("@id", id);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async void UnlockReplay(int id) {
        var connectionString = getConnectionString();
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                command.CommandText = "UPDATE replay SET locked = 0 WHERE id = @id";
                command.Parameters.AddWithValue("@id", id);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task<string> ExportStageDetails(IGame game, Replay replay, string dirPath) {
        var folderName = $"{game.Name}_{replay.track}_{replay.car}_{replay.car_class}_{replay.finish_time}";
        var folder = Path.Combine(dirPath, folderName);
        _logger.Info($"Exporting replay to: {folder}");
        Directory.CreateDirectory(folder);
        var replayPath = Path.Combine(folder, "stage_details.csv");
        await File.WriteAllTextAsync(replayPath, replay.ExportToCSV(game));
        _logger.Info($"stage details exported: {replayPath}");
        return folder;
    }

    public async Task ExportReplay(IGame game, Replay replay, string dirPath) {
        var folder = await ExportStageDetails(game, replay, dirPath);
        if (!string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
            // with timestamp as filename
            var videoPath = Path.Combine(folder, $"{replay.video_begin_timestamp}{Path.GetExtension(replay.video_path)}");
            // async copy
            await Task.Run(() => File.Copy(replay.video_path, videoPath));
            _logger.Info($"video exported: {videoPath}");
        }
    }

    public async Task ExportReplayWithAudio(IGame game, Replay replay, string dirPath) {
        var folder = await ExportStageDetails(game, replay, dirPath);
        if (!string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
            var videoPath = Path.Combine(folder, $"{replay.video_begin_timestamp}{Path.GetExtension(replay.video_path)}");
            var audioPath = Path.ChangeExtension(videoPath, ".wav");
            // async convert
            await FFMpegArguments
                .FromFileInput(replay.video_path)
                .OutputToFile(audioPath, overwrite: true, options => options
                    .DisableChannel(Channel.Video)
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioBitrate(16)
                    .WithAudioSamplingRate(48000)
                    .WithCustomArgument("-ac 1") // mono
                )
                .ProcessAsynchronously();
            _logger.Info($"audio exported: {audioPath}");
        }
    }

    public static ReplayDetailsPerTime getReplayDetailsPerTimeWithTimeStamp(List<ReplayDetailsPerTime> details, Int64 timestamp) {
        // binary searchfloat time = 0;
        if (details.Count == 0) {
            return null;
        }
        if (timestamp <= details[0].timestamp) {
            return details[0];
        }
        if (timestamp >= details[details.Count - 1].timestamp) {
            return details[details.Count - 1];
        }
        var timestamps = details.Select(r => r.timestamp).ToList();
        var index = timestamps.BinarySearch(timestamp);
        ReplayDetailsPerTime result = null;
        if (index >= 0) {
            result = details[index];
        } else {
            index = ~index;
            if (index == 0) {
                result = details[0];
            } else if (index == timestamps.Count) {
                result = details[details.Count - 1];
            } else {
                result = ReplayDetailsPerTime.linearInterpolation(details[index - 1], details[index], timestamp);
            }
        }
        return result;
    }

    public static Int64 getTimeStampWithDistance(List<ReplayDetailsPerTime> details, float distance) {
        // binary searchfloat time = 0;
        if (details.Count == 0) {
            return 0;
        }
        if (distance <= details[0].distance) {
            return details[0].timestamp;
        }
        if (distance >= details[details.Count - 1].distance) {
            return details[details.Count - 1].timestamp;
        }
        var distances = details.Select(r => r.distance).ToList();
        var index = distances.BinarySearch(distance);
        ReplayDetailsPerTime result = null;
        if (index >= 0) {
            result = details[index];
        } else {
            index = ~index;
            if (index == 0) {
                result = details[0];
            } else if (index == distances.Count) {
                result = details[details.Count - 1];
            } else {
                // 这tm还有精度问题，66666，先算出前部分转Int64后再加之前timestamp，不能加完了再转？什么鬼
                return (Int64)((details[index].timestamp - details[index - 1].timestamp) / (details[index].distance - details[index - 1].distance) * (distance - details[index - 1].distance)) + details[index - 1].timestamp;
            }
        }
        return result.timestamp;
    }

    public static float getTimeByDistance(List<ReplayDetailsPerTime> details, float distance) {
        float time = 0;
        if (details.Count == 0) {
            return 0;
        }
        if (distance <= details[0].distance) {
            return 0;
        }
        if (distance >= details[details.Count - 1].distance) {
            return details[details.Count - 1].time;
        }
        var times = details.Select(r => r.time).ToList();
        var distances = details.Select(r => r.distance).ToList();
        var index = distances.BinarySearch(distance);
        if (index >= 0) {
            time = times[index];
        } else {
            index = ~index;
            if (index == 0) {
                time = 0;
            } else if (index == distances.Count) {
                time = times[times.Count - 1];
            } else {
                time = times[index - 1] + (times[index] - times[index - 1]) / (distances[index] - distances[index - 1]) * (distance - distances[index - 1]);
            }
        }
        return time;
    }

    public static float getDistanceByTime(List<ReplayDetailsPerTime> details, float time) {
        float distance = 0;
        if (details.Count == 0) {
            return 0;
        }
        if (time <= details[0].time) {
            return 0;
        }
        if (time >= details[details.Count - 1].time) {
            return details[details.Count - 1].distance;
        }
        var times = details.Select(r => r.time).ToList();
        var distances = details.Select(r => r.distance).ToList();
        var index = times.BinarySearch(time);
        if (index >= 0) {
            distance = distances[index];
        } else {
            index = ~index;
            if (index == 0) {
                distance = 0;
            } else if (index == times.Count) {
                distance = distances[distances.Count - 1];
            } else {
                distance = distances[index - 1] + (distances[index] - distances[index - 1]) / (times[index] - times[index - 1]) * (time - times[index - 1]);
            }
        }
        return distance;
    }
}


