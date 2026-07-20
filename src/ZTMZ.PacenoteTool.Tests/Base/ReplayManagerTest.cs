using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

[CollectionDefinition("ReplayManager")]
public sealed class ReplayManagerCollection : ICollectionFixture<ReplayManagerFixture>
{
}

public sealed class ReplayManagerFixture
{
}

[Collection("ReplayManager")]
public class ReplayManagerTest
{
    [Fact]
    public void TimelineLookupHandlesDuplicateAndReverseDistanceSamples()
    {
        var details = new List<ReplayDetailsPerTime>
        {
            new() { time = 0, distance = 0, timestamp = 100 },
            new() { time = 1, distance = 100, timestamp = 200 },
            new() { time = 1, distance = 101, timestamp = 200 },
            new() { time = 2, distance = 90, timestamp = 300 },
            new() { time = 3, distance = 150, timestamp = 400 }
        };

        var lookup = ReplayTimelineLookup.From(details);

        Assert.Equal(1, lookup.GetTimeByDistance(100));
        Assert.Equal(100, lookup.GetDistanceByTime(1));
        Assert.Equal(200, lookup.GetTimestampByDistance(100));
        Assert.Equal(3, lookup.GetByTimestamp(400).time);
        Assert.Equal(90, lookup.GetByTime(2).distance);
    }

    [Fact]
    public void ReplayComparisonNormalizesProgressAndUsesTheRecordedFinish()
    {
        var replay = new Replay
        {
            finish_time = 100,
            track_length = 1000
        };
        var lookup = ReplayTimelineLookup.From(
            new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0 },
                new() { time = 50, distance = 500 },
                new() { time = 90, distance = 900 }
            },
            ReplayComparisonCalculator.CreateTerminalSample(replay));
        var current = new GameData
        {
            LapTime = 52,
            LapDistance = 400,
            CompletionRate = 0.5f,
            TrackLength = 800
        };

        var comparison = ReplayComparisonCalculator.Create(current, replay, lookup);

        Assert.True(comparison.Valid);
        Assert.InRange(comparison.CurrentCompletionRate, 0.499f, 0.501f);
        Assert.InRange(comparison.BestTimeAtCurrentProgress, 49.999f, 50.001f);
        Assert.InRange(comparison.DeltaAtCurrentProgress, 1.999f, 2.001f);
        Assert.InRange(comparison.BestCompletionRateAtCurrentTime, 0.519f, 0.521f);
        Assert.InRange(comparison.BestDistanceAtCurrentTime, 415.9f, 416.1f);
        Assert.InRange(lookup.GetTimeByDistance(1000), 99.999f, 100.001f);
    }

    [Fact]
    public void ReplayComparisonFallsBackToDistanceWhenProgressIsNotReported()
    {
        var replay = new Replay
        {
            finish_time = 100,
            track_length = 1000
        };
        var lookup = ReplayTimelineLookup.From(
            new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0 },
                new() { time = 100, distance = 1000 }
            });
        var current = new GameData
        {
            LapTime = 55,
            LapDistance = 400,
            CompletionRate = 0,
            TrackLength = 800
        };

        var comparison = ReplayComparisonCalculator.Create(current, replay, lookup);

        Assert.True(comparison.Valid);
        Assert.InRange(comparison.CurrentCompletionRate, 0.499f, 0.501f);
        Assert.InRange(comparison.BestTimeAtCurrentProgress, 49.999f, 50.001f);
        Assert.InRange(comparison.DeltaAtCurrentProgress, 4.999f, 5.001f);
    }

    [Fact]
    public void TimelineLookupAddsTheRecordedFinishWhenTelemetryContainsNonFiniteValues()
    {
        var replay = new Replay
        {
            finish_time = 100,
            track_length = 1000
        };

        var lookup = ReplayTimelineLookup.From(
            new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0 },
                new() { time = float.NaN, distance = float.NaN },
                new() { time = 90, distance = 900 }
            },
            ReplayComparisonCalculator.CreateTerminalSample(replay));

        Assert.InRange(lookup.GetTimeByDistance(1000), 99.999f, 100.001f);
        Assert.InRange(lookup.GetDistanceByTime(100), 999.999f, 1000.001f);
    }

    [Fact]
    public async Task SaveReplayAsyncPersistsDuplicateTimeSamples()
    {
        var databasePath = GetTempDatabasePath();
        var game = new TestGame("Replay Test Game");

        try
        {
            ReplayManager.Instance.Init(databasePath);

            var replay = new Replay
            {
                track = "Stage",
                car = "Car",
                car_class = "Class",
                finish_time = 123.45f,
                track_length = 1000,
                checkpoints = 2,
                date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            };
            var details = new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0, timestamp = 100 },
                new() { time = 1, distance = 100, timestamp = 200 },
                new() { time = 1, distance = 101, timestamp = 201 },
                new() { time = 2, distance = 200, timestamp = 300 }
            };
            var checkpoints = new List<ReplayDetailsPerCheckpoint>
            {
                new() { checkpoint = 0, time = 0, distance = 0 },
                new() { checkpoint = 1, time = 1, distance = 100 }
            };

            await ReplayManager.Instance.SaveReplayAsync(game, replay, details, checkpoints);
            await ReplayManager.Instance.FlushPendingSavesAsync();

            var saved = await ReplayManager.Instance.getReplay(replay.id);
            var savedDetails = await ReplayManager.Instance.getReplayDetailsPerTime(replay.id);

            Assert.True(saved.available);
            Assert.False(string.IsNullOrWhiteSpace(saved.fingerprint));
            Assert.Equal(4, savedDetails.Count);
            Assert.Equal(2, savedDetails.Count(d => d.time == 1));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task ReplayFingerprintIgnoresReplayDate()
    {
        var databasePath = GetTempDatabasePath();
        var game = new TestGame("Replay Test Game");

        try
        {
            ReplayManager.Instance.Init(databasePath);

            var details = new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0, timestamp = 100 },
                new() { time = 1, distance = 100, timestamp = 200 }
            };
            var checkpoints = new List<ReplayDetailsPerCheckpoint>
            {
                new() { checkpoint = 0, time = 0, distance = 0 }
            };
            var first = CreateReplay(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var second = CreateReplay(new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc));

            await ReplayManager.Instance.SaveReplayAsync(game, first, details, checkpoints);
            await ReplayManager.Instance.SaveReplayAsync(game, second, details, checkpoints);
            await ReplayManager.Instance.FlushPendingSavesAsync();

            Assert.False(string.IsNullOrWhiteSpace(first.fingerprint));
            Assert.Equal(first.fingerprint, second.fingerprint);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task HeaderOnlyReplayIsUnavailableAndExcludedFromBestReplay()
    {
        var databasePath = GetTempDatabasePath();
        var game = new TestGame("Replay Test Game");

        try
        {
            ReplayManager.Instance.Init(databasePath);
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO replay (game, track, car, car_class, finish_time, track_length, retired, checkpoints, date, comment, video_path, locked, video_begin_timestamp, video_end_timestamp)
                    VALUES ('Replay Test Game', 'Stage', 'Car', 'Class', 10, 1000, 0, 2, '', '', '', 0, 0, 0);";
                await command.ExecuteNonQueryAsync();
            }

            ReplayManager.Instance.Init(databasePath);

            var replays = await ReplayManager.Instance.getReplays(game);
            var best = await ReplayManager.Instance.GetBestReplay(game, "Stage", "Class", "Car");

            var replay = Assert.Single(replays);
            Assert.False(replay.available);
            Assert.Null(best);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    private static Replay CreateReplay(DateTime date)
    {
        return new Replay
        {
            track = "Stage",
            car = "Car",
            car_class = "Class",
            finish_time = 123.45f,
            track_length = 1000,
            checkpoints = 2,
            date = date
        };
    }

    private static string GetTempDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZTMZReplayManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "replays.zdb");
    }

    private static void TryDelete(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class TestGame : IGame
    {
        public TestGame(string name)
        {
            Name = name;
        }

        public string WindowTitle => string.Empty;
        public string Name { get; }
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => string.Empty;
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations => GameConfigurations;
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }
}
