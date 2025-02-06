// Manage the replays

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
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
    public DateTime date;

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            track TEXT NOT NULL,
            car TEXT NOT NULL,
            car_class TEXT NOT NULL,
            finish_time REAL NOT NULL,
            track_length REAL NOT NULL,
            retired INTEGER NOT NULL,
            locked INTEGER NOT NULL,
            checkpoints INTEGER NOT NULL,
            comment TEXT,
            video_path TEXT,
            date TEXT NOT NULL
        );
    ";
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

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay_details_per_time (
            id INTEGER,
            time REAL NOT NULL,
            distance REAL NOT NULL,
            timestamp INTEGER NOT NULL,
            completion_rate REAL NOT NULL,
            speed REAL NOT NULL,
            clutch REAL NOT NULL,
            brake REAL NOT NULL,
            throttle REAL NOT NULL,
            handbrake REAL NOT NULL,
            steering REAL NOT NULL,
            gear REAL NOT NULL,
            max_gears REAL NOT NULL,
            rpm REAL NOT NULL,
            max_rpm REAL NOT NULL,
            pos_x REAL NOT NULL,
            pos_y REAL NOT NULL,
            pos_z REAL NOT NULL
        );
    ";
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
            id INTEGER,
            checkpoint INTEGER NOT NULL,
            time REAL NOT NULL,
            distance REAL NOT NULL
        );
    ";
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
    
    private string getConnectionString(IGame game) {
        var dbpath = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, $"{game.Name}{Constants.FILEEXT_REPLAYS}"));
        if (!File.Exists(dbpath)) {
            // create the database
            using (var connection = new SqliteConnection("Data Source=" + dbpath)) {
                connection.Open();
                using (var command = connection.CreateCommand()) {
                    command.CommandText = Replay.CREATION_SQL;
                    command.ExecuteNonQuery();
                    command.CommandText = ReplayDetailsPerTime.CREATION_SQL;
                    command.ExecuteNonQuery();
                    command.CommandText = ReplayDetailsPerCheckpoint.CREATION_SQL;
                    command.ExecuteNonQuery();
                }
            }
        }
        return "Data Source=" + dbpath;
    }
    public async Task saveReplay(IGame game, Replay replay, List<ReplayDetailsPerTime> detailsPerTime, List<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        _logger.Info($"Saving replay: {replay.track} {replay.car} {replay.car_class} {replay.finish_time} {replay.retired} {replay.checkpoints} {replay.date}");
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                command.CommandText = @"
                    INSERT INTO replay (track, car, car_class, finish_time, retired, checkpoints, date, comment, video_path)
                    VALUES (@track, @car, @car_class, @finish_time, @retired, @checkpoints, @date, @comment, @video_path);
                ";
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
                INSERT INTO replay_details_per_time (id, time, distance, timestamp, completion_rate, speed, clutch, brake, throttle, handbrake, steering, gear, max_gears, rpm, max_rpm, pos_x, pos_y, pos_z)
                VALUES (@id, @time, @distance, @timestamp, @completion_rate, @speed, @clutch, @brake, @throttle, @handbrake, @steering, @gear, @max_gears, @rpm, @max_rpm, @pos_x, @pos_y, @pos_z);
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
            replays = replays.Where(r => r.id != replay.id).ToList();
            
            var replayCount = replays.Count(r => r.track == replay.track && r.car == replay.car && r.locked == false);  // locked replay should not be deleted
            if (replayCount > Config.Instance.ReplayStoredCountLimit) {
                _logger.Info($"Replay count exceeds the limit: {replayCount}");
                var replayToDelete = replays
                    .Where(r => r.track == replay.track && r.car == replay.car && r.locked == false)    // locked replay should not be deleted
                    .OrderBy(r => r.finish_time)
                    .Skip(Config.Instance.ReplayStoredCountLimit)
                    .ToList();
                foreach (var r in replayToDelete) {
                    _logger.Info($"Deleting replay: {r.id} with finish time: {r.finish_time}");
                    await deleteReplay(game, r.id);
                }
            }
        }
        _logger.Info($"Replay saved: {replay.id}");
    }

    public async Task<List<Replay>> getReplays(IGame game) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return (await connection.QueryAsync<Replay>("SELECT * FROM replay")).AsList();
        }
    }

    public async Task<List<ReplayDetailsPerTime>> getReplayDetailsPerTime(IGame game, int id) {
        var connectionString = getConnectionString(game);
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
            ORDER BY time ASC", new { id })).AsList();
        }
    }

    public async Task<List<ReplayDetailsPerCheckpoint>> getReplayDetailsPerCheckpoint(IGame game, int id) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return (await connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint WHERE id = @id ORDER BY checkpoint ASC", new { id })).AsList();
        }
    }

    public async Task deleteReplay(IGame game, int id) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
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
        }
    }

    public async Task<Replay> getBestReplayByTrackAndCarClass(IGame game, string track, string car_class) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE track = @track AND car_class = @car_class ORDER BY finish_time ASC LIMIT 1", new { track, car_class });
        }
    }

    public async Task<Replay> getBestReplayByTrack(IGame game, string track) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE track = @track ORDER BY finish_time ASC LIMIT 1", new { track });
        }
    }

    public async Task<Replay> getBestReplayByTrackAndCarName(IGame game, string track, string car_name) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return await connection.QueryFirstOrDefaultAsync<Replay>("SELECT * FROM replay WHERE track = @track AND car = @car_name ORDER BY finish_time ASC LIMIT 1", new { track, car_name });
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

    public async void LockReplay(IGame game, int id) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            using (var command = connection.CreateCommand()) {
                command.CommandText = "UPDATE replay SET locked = 1 WHERE id = @id";
                command.Parameters.AddWithValue("@id", id);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}


