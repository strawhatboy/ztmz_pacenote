// Manage the replays

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base;

public class Replay {
    public int id;
    public string track;
    public string car;
    public string car_class;
    public float finish_time;
    public bool retired;
    // how many checkpoints in this replay, 256? 128? or just 4?
    public int checkpoints;
    public DateTime date;

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            track TEXT NOT NULL,
            car TEXT NOT NULL,
            car_class TEXT NOT NULL,
            finish_time REAL NOT NULL,
            retired INTEGER NOT NULL,
            checkpoints INTEGER NOT NULL,
            date TEXT NOT NULL
        );
    ";
}

// the distance finished at time
public class ReplayDetailsPerTime {
    public int id;
    public float time;
    public float distance;

    public static readonly string CREATION_SQL = @"
        CREATE TABLE IF NOT EXISTS replay_details_per_time (
            id INTEGER,
            time REAL NOT NULL,
            distance REAL NOT NULL
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
                    INSERT INTO replay (track, car, car_class, finish_time, retired, checkpoints, date)
                    VALUES (@track, @car, @car_class, @finish_time, @retired, @checkpoints, @date);
                ";
                command.Parameters.AddWithValue("@track", replay.track);
                command.Parameters.AddWithValue("@car", replay.car);
                command.Parameters.AddWithValue("@car_class", replay.car_class);
                command.Parameters.AddWithValue("@finish_time", replay.finish_time);
                command.Parameters.AddWithValue("@retired", replay.retired);
                command.Parameters.AddWithValue("@checkpoints", replay.checkpoints);
                command.Parameters.AddWithValue("@date", replay.date.ToString("yyyy-MM-dd HH:mm:ss"));
                await command.ExecuteNonQueryAsync();
                command.CommandText = "SELECT last_insert_rowid()";
                replay.id = Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            
            detailsPerTime.ForEach(d => d.id = replay.id);
            detailsPerCheckpoint.ForEach(d => d.id = replay.id);

            var transaction = connection.BeginTransaction();
            var insertSQL = @"
                INSERT INTO replay_details_per_time (id, time, distance)
                VALUES (@id, @time, @distance);
                ";
            for (int i = 0; i < detailsPerTime.Count; i++) {
                var d = detailsPerTime[i];
                using (var command = connection.CreateCommand()) {
                    command.CommandText = insertSQL;
                    command.Parameters.AddWithValue("@id", d.id);
                    command.Parameters.AddWithValue("@time", d.time);
                    command.Parameters.AddWithValue("@distance", d.distance);
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
            transaction.Commit();
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
            return (await connection.QueryAsync<ReplayDetailsPerTime>("SELECT * FROM replay_details_per_time WHERE id = @id", new { id })).AsList();
        }
    }

    public async Task<List<ReplayDetailsPerCheckpoint>> getReplayDetailsPerCheckpoint(IGame game, int id) {
        var connectionString = getConnectionString(game);
        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();
            return (await connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint WHERE id = @id", new { id })).AsList();
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
}


