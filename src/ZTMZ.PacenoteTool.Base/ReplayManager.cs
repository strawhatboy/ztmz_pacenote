using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base;

public class Replay {
    public int id;
    public string track = "";
    public string car = "";
    public string car_class = "";
    public float finish_time;
    public float track_length;
    public bool retired;
    public bool locked;
    public int checkpoints;
    public string comment = "";
    public string video_path = "";
    public Int64 video_begin_timestamp;
    public Int64 video_end_timestamp;
    public DateTime date;
    public bool available = true;
    public string fingerprint = "";

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
        "ALTER TABLE replay ADD COLUMN video_end_timestamp INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE replay ADD COLUMN available INTEGER NOT NULL DEFAULT 1;",
        "ALTER TABLE replay ADD COLUMN fingerprint TEXT NOT NULL DEFAULT '';"
    };

    public string ExportToCSV(IGame game) {
        return ExportToCSVAsync(game).GetAwaiter().GetResult();
    }

    public async Task<string> ExportToCSVAsync(IGame game) {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,time,distance,completion_rate,speed,clutch,brake,throttle,handbrake,steering,gear,max_gears,rpm,max_rpm,pos_x,pos_y,pos_z");
        var detailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(id);
        foreach (var d in detailsPerTime) {
            sb.AppendLine(string.Join(",",
                d.timestamp.ToString(CultureInfo.InvariantCulture),
                d.time.ToString(CultureInfo.InvariantCulture),
                d.distance.ToString(CultureInfo.InvariantCulture),
                d.completion_rate.ToString(CultureInfo.InvariantCulture),
                d.speed.ToString(CultureInfo.InvariantCulture),
                d.clutch.ToString(CultureInfo.InvariantCulture),
                d.brake.ToString(CultureInfo.InvariantCulture),
                d.throttle.ToString(CultureInfo.InvariantCulture),
                d.handbrake.ToString(CultureInfo.InvariantCulture),
                d.steering.ToString(CultureInfo.InvariantCulture),
                d.gear.ToString(CultureInfo.InvariantCulture),
                d.max_gears.ToString(CultureInfo.InvariantCulture),
                d.rpm.ToString(CultureInfo.InvariantCulture),
                d.max_rpm.ToString(CultureInfo.InvariantCulture),
                d.pos_x.ToString(CultureInfo.InvariantCulture),
                d.pos_y.ToString(CultureInfo.InvariantCulture),
                d.pos_z.ToString(CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }
}

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
        if (a.timestamp == b.timestamp) {
            return Clone(a);
        }

        var ratio = (float)(timestamp - a.timestamp) / (b.timestamp - a.timestamp);
        var result = Interpolate(a, b, ratio);
        result.timestamp = timestamp;
        return result;
    }

    public static ReplayDetailsPerTime Clone(ReplayDetailsPerTime source) {
        return new ReplayDetailsPerTime {
            id = source.id,
            time = source.time,
            distance = source.distance,
            timestamp = source.timestamp,
            completion_rate = source.completion_rate,
            speed = source.speed,
            clutch = source.clutch,
            brake = source.brake,
            throttle = source.throttle,
            handbrake = source.handbrake,
            steering = source.steering,
            gear = source.gear,
            max_gears = source.max_gears,
            rpm = source.rpm,
            max_rpm = source.max_rpm,
            pos_x = source.pos_x,
            pos_y = source.pos_y,
            pos_z = source.pos_z,
            g_long = source.g_long,
            g_lat = source.g_lat,
            brake_temp_rear_left = source.brake_temp_rear_left,
            brake_temp_rear_right = source.brake_temp_rear_right,
            brake_temp_front_left = source.brake_temp_front_left,
            brake_temp_front_right = source.brake_temp_front_right,
            suspension_rear_left = source.suspension_rear_left,
            suspension_rear_right = source.suspension_rear_right,
            suspension_front_left = source.suspension_front_left,
            suspension_front_right = source.suspension_front_right,
            suspension_speed_rear_left = source.suspension_speed_rear_left,
            suspension_speed_rear_right = source.suspension_speed_rear_right,
            suspension_speed_front_left = source.suspension_speed_front_left,
            suspension_speed_front_right = source.suspension_speed_front_right
        };
    }

    internal static ReplayDetailsPerTime Interpolate(ReplayDetailsPerTime a, ReplayDetailsPerTime b, double ratio) {
        return new ReplayDetailsPerTime {
            id = a.id,
            time = Lerp(a.time, b.time, ratio),
            distance = Lerp(a.distance, b.distance, ratio),
            timestamp = (long)Math.Round(Lerp(a.timestamp, b.timestamp, ratio)),
            completion_rate = Lerp(a.completion_rate, b.completion_rate, ratio),
            speed = Lerp(a.speed, b.speed, ratio),
            clutch = Lerp(a.clutch, b.clutch, ratio),
            brake = Lerp(a.brake, b.brake, ratio),
            throttle = Lerp(a.throttle, b.throttle, ratio),
            handbrake = Lerp(a.handbrake, b.handbrake, ratio),
            steering = Lerp(a.steering, b.steering, ratio),
            gear = Lerp(a.gear, b.gear, ratio),
            max_gears = Lerp(a.max_gears, b.max_gears, ratio),
            rpm = Lerp(a.rpm, b.rpm, ratio),
            max_rpm = Lerp(a.max_rpm, b.max_rpm, ratio),
            pos_x = Lerp(a.pos_x, b.pos_x, ratio),
            pos_y = Lerp(a.pos_y, b.pos_y, ratio),
            pos_z = Lerp(a.pos_z, b.pos_z, ratio),
            g_long = Lerp(a.g_long, b.g_long, ratio),
            g_lat = Lerp(a.g_lat, b.g_lat, ratio),
            brake_temp_rear_left = Lerp(a.brake_temp_rear_left, b.brake_temp_rear_left, ratio),
            brake_temp_rear_right = Lerp(a.brake_temp_rear_right, b.brake_temp_rear_right, ratio),
            brake_temp_front_left = Lerp(a.brake_temp_front_left, b.brake_temp_front_left, ratio),
            brake_temp_front_right = Lerp(a.brake_temp_front_right, b.brake_temp_front_right, ratio),
            suspension_rear_left = Lerp(a.suspension_rear_left, b.suspension_rear_left, ratio),
            suspension_rear_right = Lerp(a.suspension_rear_right, b.suspension_rear_right, ratio),
            suspension_front_left = Lerp(a.suspension_front_left, b.suspension_front_left, ratio),
            suspension_front_right = Lerp(a.suspension_front_right, b.suspension_front_right, ratio),
            suspension_speed_rear_left = Lerp(a.suspension_speed_rear_left, b.suspension_speed_rear_left, ratio),
            suspension_speed_rear_right = Lerp(a.suspension_speed_rear_right, b.suspension_speed_rear_right, ratio),
            suspension_speed_front_left = Lerp(a.suspension_speed_front_left, b.suspension_speed_front_left, ratio),
            suspension_speed_front_right = Lerp(a.suspension_speed_front_right, b.suspension_speed_front_right, ratio)
        };
    }

    private static float Lerp(float a, float b, double ratio) {
        return (float)(a + (b - a) * ratio);
    }

    private static double Lerp(long a, long b, double ratio) {
        return a + (b - a) * ratio;
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

public sealed class ReplayTimelineLookup {
    private readonly List<ReplayDetailsPerTime> _byTimestamp;
    private readonly List<ReplayDetailsPerTime> _byTime;
    private readonly List<ReplayDetailsPerTime> _byDistance;

    public IReadOnlyList<ReplayDetailsPerTime> OrderedByTime => _byTime;
    public IReadOnlyList<ReplayDetailsPerTime> OrderedByDistance => _byDistance;

    public ReplayTimelineLookup(IEnumerable<ReplayDetailsPerTime> details) {
        var raw = details?
            .Where(d => d != null)
            .Select(ReplayDetailsPerTime.Clone)
            .ToList() ?? new List<ReplayDetailsPerTime>();

        _byTimestamp = BuildMonotonic(raw, d => d.timestamp, d => d.timestamp > 0);
        _byTime = BuildMonotonic(raw, d => d.time, d => !float.IsNaN(d.time));
        _byDistance = BuildMonotonic(raw, d => d.distance, d => !float.IsNaN(d.distance));
    }

    public static ReplayTimelineLookup From(IEnumerable<ReplayDetailsPerTime> details) {
        return new ReplayTimelineLookup(details);
    }

    public ReplayDetailsPerTime GetByTimestamp(long timestamp) {
        return InterpolateBy(_byTimestamp, timestamp, d => d.timestamp, (a, b, ratio) => {
            var result = ReplayDetailsPerTime.Interpolate(a, b, ratio);
            result.timestamp = timestamp;
            return result;
        });
    }

    public ReplayDetailsPerTime GetByTime(float time) {
        return InterpolateBy(_byTime, time, d => d.time, (a, b, ratio) => {
            var result = ReplayDetailsPerTime.Interpolate(a, b, ratio);
            result.time = time;
            return result;
        });
    }

    public long GetTimestampByDistance(float distance) {
        var sample = InterpolateBy(_byDistance, distance, d => d.distance, (a, b, ratio) => {
            var result = ReplayDetailsPerTime.Interpolate(a, b, ratio);
            result.distance = distance;
            return result;
        });
        return sample?.timestamp ?? 0;
    }

    public float GetTimeByDistance(float distance) {
        var sample = InterpolateBy(_byDistance, distance, d => d.distance, (a, b, ratio) => {
            var result = ReplayDetailsPerTime.Interpolate(a, b, ratio);
            result.distance = distance;
            return result;
        });
        return sample?.time ?? 0;
    }

    public float GetDistanceByTime(float time) {
        var sample = InterpolateBy(_byTime, time, d => d.time, (a, b, ratio) => {
            var result = ReplayDetailsPerTime.Interpolate(a, b, ratio);
            result.time = time;
            return result;
        });
        return sample?.distance ?? 0;
    }

    private static List<ReplayDetailsPerTime> BuildMonotonic<T>(
        IEnumerable<ReplayDetailsPerTime> raw,
        Func<ReplayDetailsPerTime, T> selector,
        Func<ReplayDetailsPerTime, bool> predicate)
        where T : IComparable<T> {
        var ordered = raw
            .Where(predicate)
            .OrderBy(selector)
            .ThenBy(d => d.time)
            .ToList();
        var result = new List<ReplayDetailsPerTime>();
        foreach (var detail in ordered) {
            if (result.Count == 0 || selector(detail).CompareTo(selector(result[^1])) > 0) {
                result.Add(detail);
            }
        }

        if (result.Count == 0 && raw.Any()) {
            result.Add(raw.First());
        }

        return result;
    }

    private static ReplayDetailsPerTime InterpolateBy<T>(
        IReadOnlyList<ReplayDetailsPerTime> details,
        T value,
        Func<ReplayDetailsPerTime, T> selector,
        Func<ReplayDetailsPerTime, ReplayDetailsPerTime, double, ReplayDetailsPerTime> interpolate)
        where T : struct, IComparable<T>, IConvertible {
        if (details.Count == 0) {
            return null;
        }

        if (value.CompareTo(selector(details[0])) <= 0) {
            return ReplayDetailsPerTime.Clone(details[0]);
        }

        if (value.CompareTo(selector(details[^1])) >= 0) {
            return ReplayDetailsPerTime.Clone(details[^1]);
        }

        var low = 0;
        var high = details.Count - 1;
        while (low <= high) {
            var mid = low + ((high - low) / 2);
            var comparison = selector(details[mid]).CompareTo(value);
            if (comparison == 0) {
                return ReplayDetailsPerTime.Clone(details[mid]);
            }

            if (comparison < 0) {
                low = mid + 1;
            } else {
                high = mid - 1;
            }
        }

        var upper = Math.Clamp(low, 1, details.Count - 1);
        var lower = upper - 1;
        var lowerValue = Convert.ToDouble(selector(details[lower]), CultureInfo.InvariantCulture);
        var upperValue = Convert.ToDouble(selector(details[upper]), CultureInfo.InvariantCulture);
        if (Math.Abs(upperValue - lowerValue) < double.Epsilon) {
            return ReplayDetailsPerTime.Clone(details[lower]);
        }

        var ratio = (Convert.ToDouble(value, CultureInfo.InvariantCulture) - lowerValue) / (upperValue - lowerValue);
        return interpolate(details[lower], details[upper], ratio);
    }
}

public class ReplayManager {
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static ReplayManager instance;
    private static readonly object lockObject = new object();
    private readonly SemaphoreSlim _databaseWriteSemaphore = new(1, 1);
    private readonly object _pendingSavesLock = new();
    private readonly List<Task> _pendingSaves = new();
    private string _databasePathOverride = "";

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

    public void Init(string databasePath) {
        if (!string.IsNullOrWhiteSpace(databasePath)) {
            _databasePathOverride = databasePath;
        }

        initDB();
    }

    public async Task ImportLegacyReplaysAsync(IEnumerable<IGame> games) {
        var gameList = games?.Where(g => g != null).ToList() ?? new List<IGame>();
        if (gameList.Count == 0) {
            return;
        }

        await _databaseWriteSemaphore.WaitAsync();
        try {
            using var connection = new SqliteConnection(getConnectionString());
            await connection.OpenAsync();
            await EnsureExistingReplayMetadataAsync(connection);

            var pending = new List<LegacyReplayImport>();
            var pendingSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingAvailableFingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in gameList) {
                var legacyPath = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, $"{game.Name}{Constants.FILEEXT_REPLAYS}"));
                if (!File.Exists(legacyPath)) {
                    continue;
                }

                var imports = await ReadLegacyReplayDatabaseAsync(game.Name, legacyPath);
                foreach (var import in imports) {
                    if (!pendingSourceKeys.Add(GetImportSourceKey(import))) {
                        continue;
                    }

                    if (import.Replay.available && !pendingAvailableFingerprints.Add(import.Fingerprint)) {
                        continue;
                    }

                    if (await IsReplayAlreadyImportedAsync(connection, import)) {
                        continue;
                    }

                    pending.Add(import);
                }
            }

            if (pending.Count == 0) {
                return;
            }

            CreateDatabaseBackup();
            foreach (var import in pending) {
                await SaveReplayCoreAsync(connection, import.GameName, import.Replay, import.DetailsPerTime, import.DetailsPerCheckpoint, false, import);
            }
        } finally {
            _databaseWriteSemaphore.Release();
        }
    }

    public Task saveReplay(IGame game, Replay replay, List<ReplayDetailsPerTime> detailsPerTime, List<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        return SaveReplayAsync(game, replay, detailsPerTime, detailsPerCheckpoint);
    }

    public Task SaveReplayAsync(IGame game, Replay replay, IEnumerable<ReplayDetailsPerTime> detailsPerTime, IEnumerable<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        var snapshot = CreateSaveSnapshot(game.Name, replay, detailsPerTime, detailsPerCheckpoint);
        var task = SaveReplaySnapshotAndUpdateOriginalAsync(snapshot, replay);
        TrackPendingSave(task);
        return task;
    }

    public async Task FlushPendingSavesAsync() {
        while (true) {
            Task[] pending;
            lock (_pendingSavesLock) {
                pending = _pendingSaves.Where(t => !t.IsCompleted).ToArray();
            }

            if (pending.Length == 0) {
                return;
            }

            await Task.WhenAll(pending);
        }
    }

    public async Task<List<Replay>> getReplays(IGame game) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        var sql = await BuildReplaySelectSqlAsync(connection, "WHERE game = @game ORDER BY date DESC, id DESC");
        return (await connection.QueryAsync<Replay>(
            sql,
            new { game = game.Name })).AsList();
    }

    public async Task<Replay> getReplay(int id) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        var sql = await BuildReplaySelectSqlAsync(connection, "WHERE id = @id");
        return await connection.QueryFirstOrDefaultAsync<Replay>(sql, new { id });
    }

    public async Task<List<ReplayDetailsPerTime>> getReplayDetailsPerTime(int id) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        return (await connection.QueryAsync<ReplayDetailsPerTime>(@"SELECT *
            FROM replay_details_per_time
            WHERE id = @id
            ORDER BY CASE WHEN timestamp > 0 THEN timestamp ELSE CAST(time * 10000000 AS INTEGER) END ASC, time ASC, rowid ASC", new { id })).AsList();
    }

    public async Task<List<ReplayDetailsPerCheckpoint>> getReplayDetailsPerCheckpoint(int id) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        return (await connection.QueryAsync<ReplayDetailsPerCheckpoint>(
            "SELECT * FROM replay_details_per_checkpoint WHERE id = @id ORDER BY checkpoint ASC",
            new { id })).AsList();
    }

    public async Task deleteReplay(int id) {
        await _databaseWriteSemaphore.WaitAsync();
        try {
            using var connection = new SqliteConnection(getConnectionString());
            await connection.OpenAsync();
            var replaySql = await BuildReplaySelectSqlAsync(connection, "WHERE id = @id");
            var replay = await connection.QueryFirstOrDefaultAsync<Replay>(replaySql, new { id });
            await DeleteReplayRowsAsync(connection, id);
            if (replay != null && Config.Instance.ReplayDeleteRelatedVideo && !string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
                await Task.Run(() => File.Delete(replay.video_path));
                _logger.Info($"Video deleted: {replay.video_path}");
            }
        } finally {
            _databaseWriteSemaphore.Release();
        }
    }

    public async Task<Replay> getBestReplayByTrackAndCarClass(IGame game, string track, string car_class) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        var sql = await BuildReplaySelectSqlAsync(connection, "WHERE game = @game AND track = @track AND car_class = @car_class AND retired = 0 AND available = 1 ORDER BY finish_time ASC LIMIT 1");
        return await connection.QueryFirstOrDefaultAsync<Replay>(
            sql,
            new { game = game.Name, track, car_class });
    }

    public async Task<Replay> getBestReplayByTrack(IGame game, string track) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        var sql = await BuildReplaySelectSqlAsync(connection, "WHERE game = @game AND track = @track AND retired = 0 AND available = 1 ORDER BY finish_time ASC LIMIT 1");
        return await connection.QueryFirstOrDefaultAsync<Replay>(
            sql,
            new { game = game.Name, track });
    }

    public async Task<Replay> getBestReplayByTrackAndCarName(IGame game, string track, string car_name) {
        using var connection = new SqliteConnection(getConnectionString());
        await connection.OpenAsync();
        var sql = await BuildReplaySelectSqlAsync(connection, "WHERE game = @game AND track = @track AND car = @car_name AND retired = 0 AND available = 1 ORDER BY finish_time ASC LIMIT 1");
        return await connection.QueryFirstOrDefaultAsync<Replay>(
            sql,
            new { game = game.Name, track, car_name });
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

    public Task LockReplay(int id) {
        return LockReplayAsync(id);
    }

    public Task UnlockReplay(int id) {
        return UnlockReplayAsync(id);
    }

    public Task LockReplayAsync(int id) {
        return SetReplayLockedAsync(id, true);
    }

    public Task UnlockReplayAsync(int id) {
        return SetReplayLockedAsync(id, false);
    }

    public async Task SetReplayLockedAsync(int id, bool locked) {
        await _databaseWriteSemaphore.WaitAsync();
        try {
            using var connection = new SqliteConnection(getConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE replay SET locked = @locked WHERE id = @id", new { id, locked = locked ? 1 : 0 });
        } finally {
            _databaseWriteSemaphore.Release();
        }
    }

    public async Task ExportReplay(IGame game, Replay replay, string dirPath) {
        var folder = await ExportStageDetails(game, replay, dirPath);
        if (!string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
            var videoPath = Path.Combine(folder, $"{replay.video_begin_timestamp}{Path.GetExtension(replay.video_path)}");
            await Task.Run(() => File.Copy(replay.video_path, videoPath, overwrite: true));
            _logger.Info($"video exported: {videoPath}");
        }
    }

    public async Task ExportReplayWithAudio(IGame game, Replay replay, string dirPath) {
        var folder = await ExportStageDetails(game, replay, dirPath);
        if (!string.IsNullOrEmpty(replay.video_path) && File.Exists(replay.video_path)) {
            var videoPath = Path.Combine(folder, $"{replay.video_begin_timestamp}{Path.GetExtension(replay.video_path)}");
            var audioPath = Path.ChangeExtension(videoPath, ".wav");
            await FFMpegArguments
                .FromFileInput(replay.video_path)
                .OutputToFile(audioPath, overwrite: true, options => options
                    .DisableChannel(Channel.Video)
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioBitrate(16)
                    .WithAudioSamplingRate(48000)
                    .WithCustomArgument("-ac 1"))
                .ProcessAsynchronously();
            _logger.Info($"audio exported: {audioPath}");
        }
    }

    public static ReplayDetailsPerTime getReplayDetailsPerTimeWithTimeStamp(List<ReplayDetailsPerTime> details, Int64 timestamp) {
        return ReplayTimelineLookup.From(details).GetByTimestamp(timestamp);
    }

    public static Int64 getTimeStampWithDistance(List<ReplayDetailsPerTime> details, float distance) {
        return ReplayTimelineLookup.From(details).GetTimestampByDistance(distance);
    }

    public static float getTimeByDistance(List<ReplayDetailsPerTime> details, float distance) {
        return ReplayTimelineLookup.From(details).GetTimeByDistance(distance);
    }

    public static float getDistanceByTime(List<ReplayDetailsPerTime> details, float time) {
        return ReplayTimelineLookup.From(details).GetDistanceByTime(time);
    }

    private void createDataBase(SqliteCommand cmd, string tableName, string tableSql, List<string> columnsSql) {
        cmd.CommandText = tableSql;
        cmd.ExecuteNonQuery();
        var existingColumns = getColumnNames(cmd, tableName);
        foreach (var columnSql in columnsSql) {
            var columnName = getAlterTableColumnName(columnSql);
            if (string.IsNullOrEmpty(columnName) || existingColumns.Contains(columnName)) {
                continue;
            }

            cmd.CommandText = columnSql;
            cmd.ExecuteNonQuery();
            existingColumns.Add(columnName);
        }
    }

    private void initDB() {
        var databaseDirectory = Path.GetDirectoryName(getDatabasePath());
        if (!string.IsNullOrEmpty(databaseDirectory)) {
            Directory.CreateDirectory(databaseDirectory);
        }

        using var connection = new SqliteConnection(getConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        createDataBase(command, "replay", Replay.CREATION_SQL, Replay.CREATION_ADDITIONAL_COLUMNS_SQL);
        createDataBase(command, "replay_details_per_time", ReplayDetailsPerTime.CREATION_SQL, ReplayDetailsPerTime.CREATION_ADDITIONAL_COLUMNS_SQL);
        createDataBase(command, "replay_details_per_checkpoint", ReplayDetailsPerCheckpoint.CREATION_SQL, ReplayDetailsPerCheckpoint.CREATION_ADDITIONAL_COLUMNS_SQL);
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS replay_import_ledger (
                fingerprint TEXT NOT NULL,
                source_file TEXT NOT NULL,
                source_game TEXT NOT NULL,
                source_replay_id INTEGER NOT NULL,
                imported_replay_id INTEGER NOT NULL,
                imported_at TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
        command.CommandText = "DROP INDEX IF EXISTS idx_replay_import_ledger_fingerprint;";
        command.ExecuteNonQuery();
        foreach (var sql in new[] {
            "CREATE INDEX IF NOT EXISTS idx_replay_game_track_available ON replay(game, track, available, retired, finish_time);",
            "CREATE INDEX IF NOT EXISTS idx_replay_game_track_car_available ON replay(game, track, car, available, retired, finish_time);",
            "CREATE INDEX IF NOT EXISTS idx_replay_game_track_carclass_available ON replay(game, track, car_class, available, retired, finish_time);",
            "CREATE INDEX IF NOT EXISTS idx_replay_fingerprint ON replay(fingerprint);",
            "CREATE INDEX IF NOT EXISTS idx_replay_details_time_id_time ON replay_details_per_time(id, time);",
            "CREATE INDEX IF NOT EXISTS idx_replay_details_time_id_timestamp ON replay_details_per_time(id, timestamp);",
            "CREATE INDEX IF NOT EXISTS idx_replay_details_checkpoint_id_checkpoint ON replay_details_per_checkpoint(id, checkpoint);",
            "CREATE INDEX IF NOT EXISTS idx_replay_import_ledger_fingerprint ON replay_import_ledger(fingerprint);",
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_replay_import_ledger_source ON replay_import_ledger(source_file, source_game, source_replay_id);"
        }) {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        EnsureExistingReplayMetadataAsync(connection).GetAwaiter().GetResult();
    }

    private string getDatabasePath() {
        return string.IsNullOrWhiteSpace(_databasePathOverride)
            ? AppLevelVariables.Instance.GetPath(Constants.FILE_REPLAYS)
            : _databasePathOverride;
    }

    private string getConnectionString() {
        return new SqliteConnectionStringBuilder { DataSource = getDatabasePath() }.ToString();
    }

    private async Task SaveReplaySnapshotAsync(ReplaySaveSnapshot snapshot) {
        _logger.Info($"Saving replay: {snapshot.Replay.track} {snapshot.Replay.car} {snapshot.Replay.car_class} {snapshot.Replay.finish_time} {snapshot.Replay.retired} {snapshot.Replay.checkpoints} {snapshot.Replay.date}");
        await _databaseWriteSemaphore.WaitAsync();
        try {
            using var connection = new SqliteConnection(getConnectionString());
            await connection.OpenAsync();
            await SaveReplayCoreAsync(connection, snapshot.GameName, snapshot.Replay, snapshot.DetailsPerTime, snapshot.DetailsPerCheckpoint, true, null);
        } finally {
            _databaseWriteSemaphore.Release();
        }
        _logger.Info($"Replay saved: {snapshot.Replay.id}");
    }

    private async Task SaveReplaySnapshotAndUpdateOriginalAsync(ReplaySaveSnapshot snapshot, Replay originalReplay) {
        await SaveReplaySnapshotAsync(snapshot);
        if (originalReplay != null) {
            originalReplay.id = snapshot.Replay.id;
            originalReplay.available = snapshot.Replay.available;
            originalReplay.fingerprint = snapshot.Replay.fingerprint;
        }
    }

    private async Task SaveReplayCoreAsync(
        SqliteConnection connection,
        string gameName,
        Replay replay,
        List<ReplayDetailsPerTime> detailsPerTime,
        List<ReplayDetailsPerCheckpoint> detailsPerCheckpoint,
        bool enforceRetention,
        LegacyReplayImport importSource) {
        replay.date = replay.date == default ? DateTime.UtcNow : replay.date;
        replay.available = detailsPerTime.Count > 0;
        replay.fingerprint = ComputeReplayFingerprint(gameName, replay, detailsPerTime, detailsPerCheckpoint);
        var uniqueDetailsPerTime = NormalizeDetailsPerTime(detailsPerTime);
        var uniqueDetailsPerCheckpoint = NormalizeDetailsPerCheckpoint(detailsPerCheckpoint);

        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        try {
            using (var command = connection.CreateCommand()) {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO replay (game, track, car, car_class, finish_time, track_length, retired, checkpoints, date, comment, video_path, locked, video_begin_timestamp, video_end_timestamp, available, fingerprint)
                    VALUES (@game, @track, @car, @car_class, @finish_time, @track_length, @retired, @checkpoints, @date, @comment, @video_path, @locked, @video_begin_timestamp, @video_end_timestamp, @available, @fingerprint);";
                command.Parameters.AddWithValue("@game", gameName);
                command.Parameters.AddWithValue("@track", replay.track ?? "");
                command.Parameters.AddWithValue("@car", replay.car ?? "");
                command.Parameters.AddWithValue("@car_class", replay.car_class ?? "");
                command.Parameters.AddWithValue("@finish_time", replay.finish_time);
                command.Parameters.AddWithValue("@track_length", replay.track_length);
                command.Parameters.AddWithValue("@retired", replay.retired ? 1 : 0);
                command.Parameters.AddWithValue("@locked", replay.locked ? 1 : 0);
                command.Parameters.AddWithValue("@checkpoints", replay.checkpoints);
                command.Parameters.AddWithValue("@date", replay.date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@comment", replay.comment ?? "");
                command.Parameters.AddWithValue("@video_path", replay.video_path ?? "");
                command.Parameters.AddWithValue("@video_begin_timestamp", replay.video_begin_timestamp);
                command.Parameters.AddWithValue("@video_end_timestamp", replay.video_end_timestamp);
                command.Parameters.AddWithValue("@available", replay.available ? 1 : 0);
                command.Parameters.AddWithValue("@fingerprint", replay.fingerprint);
                await command.ExecuteNonQueryAsync();

                command.Parameters.Clear();
                command.CommandText = "SELECT last_insert_rowid()";
                replay.id = Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            foreach (var d in uniqueDetailsPerTime) {
                d.id = replay.id;
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO replay_details_per_time (id, time, distance, timestamp, completion_rate, speed, clutch, brake, throttle, handbrake, steering, gear, max_gears, rpm, max_rpm, pos_x, pos_y, pos_z, g_long, g_lat, brake_temp_rear_left, brake_temp_rear_right, brake_temp_front_left, brake_temp_front_right, suspension_rear_left, suspension_rear_right, suspension_front_left, suspension_front_right, suspension_speed_rear_left, suspension_speed_rear_right, suspension_speed_front_left, suspension_speed_front_right)
                    VALUES (@id, @time, @distance, @timestamp, @completion_rate, @speed, @clutch, @brake, @throttle, @handbrake, @steering, @gear, @max_gears, @rpm, @max_rpm, @pos_x, @pos_y, @pos_z, @g_long, @g_lat, @brake_temp_rear_left, @brake_temp_rear_right, @brake_temp_front_left, @brake_temp_front_right, @suspension_rear_left, @suspension_rear_right, @suspension_front_left, @suspension_front_right, @suspension_speed_rear_left, @suspension_speed_rear_right, @suspension_speed_front_left, @suspension_speed_front_right);";
                AddReplayDetailsPerTimeParameters(command, d);
                await command.ExecuteNonQueryAsync();
            }

            foreach (var d in uniqueDetailsPerCheckpoint) {
                d.id = replay.id;
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO replay_details_per_checkpoint (id, checkpoint, time, distance)
                    VALUES (@id, @checkpoint, @time, @distance);";
                command.Parameters.AddWithValue("@id", d.id);
                command.Parameters.AddWithValue("@checkpoint", d.checkpoint);
                command.Parameters.AddWithValue("@time", d.time);
                command.Parameters.AddWithValue("@distance", d.distance);
                await command.ExecuteNonQueryAsync();
            }

            if (importSource != null) {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT OR IGNORE INTO replay_import_ledger (fingerprint, source_file, source_game, source_replay_id, imported_replay_id, imported_at)
                    VALUES (@fingerprint, @source_file, @source_game, @source_replay_id, @imported_replay_id, @imported_at);";
                command.Parameters.AddWithValue("@fingerprint", replay.fingerprint);
                command.Parameters.AddWithValue("@source_file", importSource.SourceFile);
                command.Parameters.AddWithValue("@source_game", importSource.GameName);
                command.Parameters.AddWithValue("@source_replay_id", importSource.SourceReplayId);
                command.Parameters.AddWithValue("@imported_replay_id", replay.id);
                command.Parameters.AddWithValue("@imported_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        } catch {
            await transaction.RollbackAsync();
            throw;
        }

        if (enforceRetention && replay.available) {
            await EnforceRetentionAsync(connection, gameName, replay);
        }
    }

    private static ReplaySaveSnapshot CreateSaveSnapshot(
        string gameName,
        Replay replay,
        IEnumerable<ReplayDetailsPerTime> detailsPerTime,
        IEnumerable<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        return new ReplaySaveSnapshot(
            gameName,
            CloneReplay(replay),
            detailsPerTime?.Select(ReplayDetailsPerTime.Clone).ToList() ?? new List<ReplayDetailsPerTime>(),
            detailsPerCheckpoint?.Select(CloneReplayDetailsPerCheckpoint).ToList() ?? new List<ReplayDetailsPerCheckpoint>());
    }

    private void TrackPendingSave(Task task) {
        lock (_pendingSavesLock) {
            _pendingSaves.Add(task);
        }

        _ = task.ContinueWith(t => {
            lock (_pendingSavesLock) {
                _pendingSaves.Remove(task);
            }

            if (t.Exception != null) {
                _logger.Error(t.Exception, "Replay save failed.");
            }
        }, TaskScheduler.Default);
    }

    private static HashSet<string> getColumnNames(SqliteCommand command, string tableName) {
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string getAlterTableColumnName(string alterTableSql) {
        var marker = "ADD COLUMN";
        var index = alterTableSql.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
            return "";
        }

        var rest = alterTableSql[(index + marker.Length)..].Trim();
        return rest.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"', '\'', '`', '[', ']') ?? "";
    }

    private async Task EnsureExistingReplayMetadataAsync(SqliteConnection connection) {
        var replaySql = await BuildReplaySelectSqlAsync(connection, "");
        var replays = (await connection.QueryAsync<Replay>(replaySql)).AsList();
        foreach (var replay in replays) {
            var detailsPerTime = (await connection.QueryAsync<ReplayDetailsPerTime>("SELECT * FROM replay_details_per_time WHERE id = @id", new { replay.id })).AsList();
            var detailsPerCheckpoint = (await connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint WHERE id = @id", new { replay.id })).AsList();
            var available = detailsPerTime.Count > 0;
            var gameName = await connection.ExecuteScalarAsync<string>("SELECT game FROM replay WHERE id = @id", new { replay.id }) ?? "";
            var fingerprint = ComputeReplayFingerprint(gameName, replay, detailsPerTime, detailsPerCheckpoint);
            if (replay.available != available || replay.fingerprint != fingerprint) {
                await connection.ExecuteAsync(
                    "UPDATE replay SET available = @available, fingerprint = @fingerprint WHERE id = @id",
                    new { id = replay.id, available = available ? 1 : 0, fingerprint });
            }
        }
    }

    private async Task EnforceRetentionAsync(SqliteConnection connection, string gameName, Replay replay) {
        var limit = Math.Max(1, Config.Instance.ReplayStoredCountLimit);
        var replaySql = await BuildReplaySelectSqlAsync(connection, @"
            WHERE game = @game AND track = @track AND car = @car AND locked = 0 AND available = 1 AND id <> @id
            ORDER BY finish_time ASC, id DESC");
        var candidates = (await connection.QueryAsync<Replay>(replaySql,
            new { game = gameName, track = replay.track, car = replay.car, id = replay.id })).AsList();
        var deleteList = candidates
            .Skip(Math.Max(0, limit - 1))
            .ToList();

        foreach (var r in deleteList) {
            _logger.Info($"Deleting replay due to retention: {r.id} with finish time: {r.finish_time}");
            await DeleteReplayRowsAsync(connection, r.id);
            if (Config.Instance.ReplayDeleteRelatedVideo && !string.IsNullOrEmpty(r.video_path) && File.Exists(r.video_path)) {
                await Task.Run(() => File.Delete(r.video_path));
                _logger.Info($"Video deleted: {r.video_path}");
            }
        }
    }

    private static async Task DeleteReplayRowsAsync(SqliteConnection connection, int id) {
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        try {
            await connection.ExecuteAsync("DELETE FROM replay WHERE id = @id", new { id }, transaction);
            await connection.ExecuteAsync("DELETE FROM replay_details_per_time WHERE id = @id", new { id }, transaction);
            await connection.ExecuteAsync("DELETE FROM replay_details_per_checkpoint WHERE id = @id", new { id }, transaction);
            await transaction.CommitAsync();
            _logger.Info($"Replay deleted: {id}");
        } catch {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static List<ReplayDetailsPerTime> NormalizeDetailsPerTime(IEnumerable<ReplayDetailsPerTime> details) {
        return details
            .Where(d => d != null)
            .Select(ReplayDetailsPerTime.Clone)
            .OrderBy(d => d.timestamp > 0 ? d.timestamp : (long)(d.time * 10000000))
            .ThenBy(d => d.time)
            .ToList();
    }

    private static List<ReplayDetailsPerCheckpoint> NormalizeDetailsPerCheckpoint(IEnumerable<ReplayDetailsPerCheckpoint> details) {
        return details
            .Where(d => d != null)
            .GroupBy(d => d.checkpoint)
            .Select(g => CloneReplayDetailsPerCheckpoint(g.First()))
            .OrderBy(d => d.checkpoint)
            .ToList();
    }

    private static void AddReplayDetailsPerTimeParameters(SqliteCommand command, ReplayDetailsPerTime d) {
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
    }

    private async Task<bool> IsReplayAlreadyImportedAsync(SqliteConnection connection, LegacyReplayImport import) {
        var ledgerSql = import.Replay.available
            ? "SELECT COUNT(1) FROM replay_import_ledger WHERE fingerprint = @fingerprint OR (source_file = @source_file AND source_game = @source_game AND source_replay_id = @source_replay_id)"
            : "SELECT COUNT(1) FROM replay_import_ledger WHERE source_file = @source_file AND source_game = @source_game AND source_replay_id = @source_replay_id";
        var ledgerCount = await connection.ExecuteScalarAsync<long>(
            ledgerSql,
            new { fingerprint = import.Fingerprint, source_file = import.SourceFile, source_game = import.GameName, source_replay_id = import.SourceReplayId });
        if (ledgerCount > 0) {
            return true;
        }

        if (!import.Replay.available) {
            return false;
        }

        var replayCount = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM replay WHERE fingerprint = @fingerprint",
            new { fingerprint = import.Fingerprint });
        return replayCount > 0;
    }

    private static async Task<List<LegacyReplayImport>> ReadLegacyReplayDatabaseAsync(string gameName, string legacyPath) {
        var imports = new List<LegacyReplayImport>();
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = legacyPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        if (!await TableExistsAsync(connection, "replay")) {
            return imports;
        }

        var replaySql = await BuildReplaySelectSqlAsync(connection, "");
        var replays = (await connection.QueryAsync<Replay>(replaySql)).AsList();
        var detailsPerTime = await TableExistsAsync(connection, "replay_details_per_time")
            ? (await connection.QueryAsync<ReplayDetailsPerTime>("SELECT * FROM replay_details_per_time")).AsList()
            : new List<ReplayDetailsPerTime>();
        var detailsPerCheckpoint = await TableExistsAsync(connection, "replay_details_per_checkpoint")
            ? (await connection.QueryAsync<ReplayDetailsPerCheckpoint>("SELECT * FROM replay_details_per_checkpoint")).AsList()
            : new List<ReplayDetailsPerCheckpoint>();

        var detailsByReplay = detailsPerTime.GroupBy(d => d.id).ToDictionary(g => g.Key, g => g.ToList());
        var checkpointsByReplay = detailsPerCheckpoint.GroupBy(d => d.id).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var replay in replays) {
            var sourceId = replay.id;
            var replayDetails = detailsByReplay.TryGetValue(sourceId, out var detailList) ? detailList : new List<ReplayDetailsPerTime>();
            var checkpointDetails = checkpointsByReplay.TryGetValue(sourceId, out var checkpointList) ? checkpointList : new List<ReplayDetailsPerCheckpoint>();
            var replaySnapshot = CloneReplay(replay);
            replaySnapshot.id = 0;
            replaySnapshot.available = replayDetails.Count > 0;
            replaySnapshot.fingerprint = ComputeReplayFingerprint(gameName, replaySnapshot, replayDetails, checkpointDetails);
            imports.Add(new LegacyReplayImport(
                gameName,
                legacyPath,
                sourceId,
                replaySnapshot.fingerprint,
                replaySnapshot,
                replayDetails.Select(ReplayDetailsPerTime.Clone).ToList(),
                checkpointDetails.Select(CloneReplayDetailsPerCheckpoint).ToList()));
        }

        return imports;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName) {
        var result = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
            new { tableName });
        return result > 0;
    }

    private static async Task<string> BuildReplaySelectSqlAsync(SqliteConnection connection, string suffix) {
        var columns = await GetTableColumnsAsync(connection, "replay");
        string column(string name, string fallback) => columns.Contains(name) ? name : fallback;

        var dateColumn = column("date", "''");
        var dateExpression = $"CASE WHEN {dateColumn} IS NULL OR {dateColumn} = '' THEN '1970-01-01 00:00:00' ELSE {dateColumn} END";

        return $@"SELECT
            {column("id", "0")} AS id,
            {column("track", "''")} AS track,
            {column("car", "''")} AS car,
            {column("car_class", "''")} AS car_class,
            {column("finish_time", "0")} AS finish_time,
            {column("track_length", "0")} AS track_length,
            {column("retired", "0")} AS retired,
            {column("locked", "0")} AS locked,
            {column("checkpoints", "0")} AS checkpoints,
            {column("comment", "''")} AS comment,
            {column("video_path", "''")} AS video_path,
            {column("video_begin_timestamp", "0")} AS video_begin_timestamp,
            {column("video_end_timestamp", "0")} AS video_end_timestamp,
            {dateExpression} AS date,
            {column("available", "1")} AS available,
            {column("fingerprint", "''")} AS fingerprint
            FROM replay {suffix}";
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(SqliteConnection connection, string tableName) {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private void CreateDatabaseBackup() {
        var dbPath = getDatabasePath();
        if (!File.Exists(dbPath)) {
            return;
        }

        var backupDirectory = AppLevelVariables.Instance.GetPath(Path.Combine("backups", "replays"));
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"replays-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zdb");
        using var source = new SqliteConnection(getConnectionString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        _logger.Info("Replay database backup created: {0}", backupPath);
    }

    private async Task<string> ExportStageDetails(IGame game, Replay replay, string dirPath) {
        EnsureReplayAvailable(replay);
        var folderName = string.Join("_", new[] {
            SanitizePathSegment(game.Name),
            replay.id.ToString(CultureInfo.InvariantCulture),
            SanitizePathSegment(replay.track),
            SanitizePathSegment(replay.car),
            SanitizePathSegment(replay.car_class),
            replay.finish_time.ToString(CultureInfo.InvariantCulture)
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var folder = Path.Combine(dirPath, folderName);
        _logger.Info($"Exporting replay to: {folder}");
        Directory.CreateDirectory(folder);
        var replayPath = Path.Combine(folder, "stage_details.csv");
        await File.WriteAllTextAsync(replayPath, await replay.ExportToCSVAsync(game), Encoding.UTF8);
        _logger.Info($"stage details exported: {replayPath}");
        return folder;
    }

    private static Replay CloneReplay(Replay replay) {
        return new Replay {
            id = replay.id,
            track = replay.track ?? "",
            car = replay.car ?? "",
            car_class = replay.car_class ?? "",
            finish_time = replay.finish_time,
            track_length = replay.track_length,
            retired = replay.retired,
            locked = replay.locked,
            checkpoints = replay.checkpoints,
            comment = replay.comment ?? "",
            video_path = replay.video_path ?? "",
            video_begin_timestamp = replay.video_begin_timestamp,
            video_end_timestamp = replay.video_end_timestamp,
            date = replay.date,
            available = replay.available,
            fingerprint = replay.fingerprint ?? ""
        };
    }

    private static ReplayDetailsPerCheckpoint CloneReplayDetailsPerCheckpoint(ReplayDetailsPerCheckpoint source) {
        return new ReplayDetailsPerCheckpoint {
            id = source.id,
            checkpoint = source.checkpoint,
            time = source.time,
            distance = source.distance
        };
    }

    private static string ComputeReplayFingerprint(
        string gameName,
        Replay replay,
        IEnumerable<ReplayDetailsPerTime> detailsPerTime,
        IEnumerable<ReplayDetailsPerCheckpoint> detailsPerCheckpoint) {
        var builder = new StringBuilder();
        AppendFingerprintPart(builder, gameName);
        AppendFingerprintPart(builder, replay.track);
        AppendFingerprintPart(builder, replay.car);
        AppendFingerprintPart(builder, replay.car_class);
        AppendFingerprintPart(builder, replay.finish_time.ToString("R", CultureInfo.InvariantCulture));
        AppendFingerprintPart(builder, replay.track_length.ToString("R", CultureInfo.InvariantCulture));
        AppendFingerprintPart(builder, replay.retired ? "1" : "0");
        AppendFingerprintPart(builder, replay.checkpoints.ToString(CultureInfo.InvariantCulture));
        foreach (var detail in NormalizeDetailsPerTime(detailsPerTime)) {
            AppendFingerprintPart(builder, detail.time.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.distance.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.timestamp.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.completion_rate.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.speed.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.throttle.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, detail.brake.ToString("R", CultureInfo.InvariantCulture));
        }

        foreach (var checkpoint in NormalizeDetailsPerCheckpoint(detailsPerCheckpoint)) {
            AppendFingerprintPart(builder, checkpoint.checkpoint.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, checkpoint.time.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintPart(builder, checkpoint.distance.ToString("R", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendFingerprintPart(StringBuilder builder, string value) {
        builder.Append(value?.Replace("\\", "\\\\").Replace("|", "\\|") ?? "");
        builder.Append('|');
    }

    private static string GetImportSourceKey(LegacyReplayImport import) {
        return string.Join("\u001f", import.SourceFile, import.GameName, import.SourceReplayId.ToString(CultureInfo.InvariantCulture));
    }

    private static string SanitizePathSegment(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "";
        }

        var invalid = Path.GetInvalidFileNameChars().Concat(AppLevelVariables.InvalidCharsForWindowsPath).Distinct().ToArray();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return result.Length == 0 ? "_" : result;
    }

    private static void EnsureReplayAvailable(Replay replay) {
        if (replay == null) {
            throw new InvalidOperationException("Replay does not exist.");
        }

        if (!replay.available) {
            throw new InvalidOperationException("Replay data is incomplete and cannot be played or exported.");
        }
    }

    private sealed record ReplaySaveSnapshot(
        string GameName,
        Replay Replay,
        List<ReplayDetailsPerTime> DetailsPerTime,
        List<ReplayDetailsPerCheckpoint> DetailsPerCheckpoint);

    private sealed record LegacyReplayImport(
        string GameName,
        string SourceFile,
        int SourceReplayId,
        string Fingerprint,
        Replay Replay,
        List<ReplayDetailsPerTime> DetailsPerTime,
        List<ReplayDetailsPerCheckpoint> DetailsPerCheckpoint);
}
