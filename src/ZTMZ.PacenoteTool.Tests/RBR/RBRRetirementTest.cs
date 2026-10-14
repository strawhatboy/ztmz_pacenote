using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRRetirementTest
{
    private sealed class ReaderHarness : RBRGameDataReader
    {
        public ReaderHarness()
        {
            _game = new TestGame();
            _gameState = GameState.Racing;
        }
        public void Feed(RBRNativeResultSnapshot snapshot, float completion = 0.1f)
        {
            CurrentGameData = new GameData { CompletionRate = completion, LapTime = snapshot.RaceTime };
            UpdateGameStateFromMemory(new RBRMemData
            {
                GameStateId = (byte)snapshot.RawState, StageStartCountdown = snapshot.Countdown, NativeResult = snapshot
            });
        }
    }

    private sealed class TestGame : IGame
    {
        public string WindowTitle => "";
        public string Name => "RBR test";
        public string Description => "";
        public string ImageUri => "";
        public string Executable => "";
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new();
        public event Action<int, string>? onCustomMessage { add { } remove { } }
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;
        public void OnCustomMessage(int level, string message) { }
    }

    [Fact]
    public void RealReaderEmitsOneTypedRetirementAfterPauseWithoutFinishTime()
    {
        var reader = new ReaderHarness();
        var events = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += events.Add;
        reader.Feed(Started);
        reader.Feed(Started with { RawState = 2 });
        reader.Feed(Started with { RawState = 9 });
        Assert.DoesNotContain(events, e => e.NewGameState == GameState.RaceEnd);
        reader.Feed(Failed);
        reader.Feed(Failed);
        reader.Feed(Failed with { RawState = 6 });
        var end = Assert.Single(events, e => e.NewGameState == GameState.RaceEnd);
        Assert.Equal(GameState.Paused, end.LastGameState);
        Assert.Equal(GameStateRaceEnd.Retired, end.Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.False(end.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME));
    }

    [Fact]
    public void RealReaderPreservesNormalFinishAndItsTime()
    {
        var reader = new ReaderHarness();
        var events = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += events.Add;
        reader.Feed(Started);
        reader.Feed(Started with { RaceTime = 80 }, completion: 1);
        reader.Feed(Failed);
        var end = Assert.Single(events, e => e.NewGameState == GameState.RaceEnd);
        Assert.Equal(GameStateRaceEnd.Normal, end.Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.Equal(80f, end.Parameters[GameStateRaceEndProperty.FINISH_TIME]);
    }

    private static readonly RBRNativeResultSnapshot Started = new(
        0x200000, 505, 1, 0, 1, 1, true, null, null, 0, 0, -4, 4);
    private static readonly RBRNativeResultSnapshot Failed = Started with
    { RawState = 9, Transition = 2, CurrentPage = 18, UnderlyingPage = 18, RaceFinished = 1 };

    private sealed class Session
    {
        public long Now;
        public readonly RBRGameStateDetector Detector;
        public GameState State;
        public Session() => Detector = new(() => Now);
        public RBRGameStateDetection Feed(RBRNativeResultSnapshot snapshot, float completion = 0.1f, bool replay = false)
        {
            var result = Detector.Detect(new RBRMemData
            {
                GameStateId = (byte)snapshot.RawState,
                StageStartCountdown = snapshot.Countdown,
                NativeResult = snapshot
            }, replay, State, completion, snapshot.RaceTime);
            State = result.State;
            return result;
        }
    }

    [Theory]
    [InlineData(126, 505)] // Practice capture
    [InlineData(94, 505)]  // Hotlap capture
    [InlineData(155, 151)] // Online capture
    public void RecordedRetirementWaitsForPageAndReportsOnlyOnce(int delay, int map)
    {
        var session = new Session();
        session.Feed(Started with { MapId = map });
        session.Feed(Started with { MapId = map, RawState = 2 });
        Assert.Equal(GameState.Paused, session.Feed(Started with { MapId = map, RawState = 9 }).State);
        session.Now += delay;
        var result = session.Feed(Failed with { MapId = map });
        Assert.Equal(GameState.RaceEnd, result.State);
        Assert.Equal(GameStateRaceEnd.Retired, result.FinishState);
        Assert.Equal(0, session.Detector.FinishTime);
        Assert.Null(session.Feed(Failed with { MapId = map }).FinishState);
        Assert.Null(session.Feed(Failed with { MapId = map, RawState = 6 }).FinishState);
        Assert.Null(session.Feed(Failed with { MapId = map, RawState = 3 }).FinishState);
        Assert.Null(session.Feed(Failed with { MapId = map, RawState = 3, OwnsMenu = false, Mode = 0 }).FinishState);
    }

    [Fact]
    public void RecordedRestartAndNextAttemptDoNotReuseOldResult()
    {
        var session = new Session();
        session.Feed(Started);
        Assert.Null(session.Feed(Started with { RawState = 2, Transition = 3 }).FinishState);
        Assert.Null(session.Feed(Started with { RawState = 2, RaceFinished = 1, Countdown = 7, RaceTime = 0 }).FinishState);
        foreach (var raw in new[] { 5, 13, 10 })
            Assert.Null(session.Feed(Failed with { RawState = raw, RaceTime = 0, Countdown = 7 }).FinishState);
        session.Feed(Started with { Countdown = 7, RaceTime = 0, RaceFinished = 1 });
        Assert.Null(session.Feed(Failed).FinishState); // pre-start exit, not a started attempt
        session.Feed(Started with { RawState = 5 });
        session.Feed(Started);
        Assert.Equal(GameStateRaceEnd.Retired, session.Feed(Failed).FinishState);
    }

    [Fact]
    public void ConsecutiveSameStageRetirementsEachReportOnce()
    {
        var session = new Session();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            session.Feed(Failed with { RawState = 5 });
            session.Feed(Started);
            Assert.Equal(GameStateRaceEnd.Retired, session.Feed(Failed).FinishState);
            Assert.Null(session.Feed(Failed).FinishState);
            Assert.Null(session.Feed(Failed with { RawState = 3 }).FinishState);
        }
    }

    [Fact]
    public void ConfirmedNormalFinishTakesPrecedenceOverLaterFailurePage()
    {
        var session = new Session();
        session.Feed(Started);
        session.Feed(Started with { RaceTime = 80 }, completion: 1);
        var end = session.Feed(Failed);
        Assert.Equal(GameState.RaceEnd, end.State);
        Assert.Null(end.FinishState); // existing reader supplies Normal and official finish time
        Assert.True(session.Detector.IsPostGame);
        Assert.Equal(80, session.Detector.FinishTime);
    }

    [Fact]
    public void AttachOnResultsOrReplayNeverAnnouncesRetirement()
    {
        var session = new Session();
        Assert.Null(session.Feed(Failed).FinishState);
        session.Feed(Started with { RawState = 8 }, replay: true);
        Assert.Null(session.Feed(Failed, replay: true).FinishState);
    }

    [Theory]
    [InlineData(0)] // missing menu ownership
    [InlineData(1)] // different native mode
    [InlineData(2)] // plugin stage flag absent
    [InlineData(3)] // only queued/unknown page
    [InlineData(4)] // conflicting underlying page
    [InlineData(5)] // different map
    [InlineData(6)] // replaced race data
    [InlineData(7)] // finish line already crossed
    [InlineData(8)] // not yet marked ended
    public void InvalidOrUnrelatedResultCannotAnnounceRetirement(int kind)
    {
        var session = new Session();
        session.Feed(Started);
        var snapshot = kind switch
        {
            0 => Failed with { OwnsMenu = false },
            1 => Failed with { Mode = 2 },
            2 => Failed with { PluginStage = 0 },
            3 => Failed with { CurrentPage = null },
            4 => Failed with { UnderlyingPage = null },
            5 => Failed with { MapId = 506 },
            6 => Failed with { RaceData = 0x300000 },
            7 => Failed with { FinishLinePassed = 1 },
            _ => Failed with { RaceFinished = 0 }
        };
        Assert.Null(session.Feed(snapshot).FinishState);
        session.Now += RBRGameStateDetector.ResultWaitMilliseconds;
        Assert.Equal(GameState.RaceEnd, session.Feed(snapshot).State);
        Assert.Null(session.Feed(Failed).FinishState);
    }

    [Fact]
    public void MissingMemoryDuringEndHasBoundedWaitAndLatePageCannotReplayEnd()
    {
        var session = new Session();
        session.Feed(Started);
        var noMemory = new RBRMemData { GameStateId = 9 };
        var pending = session.Detector.Detect(noMemory, false, session.State, 0, 0);
        Assert.Equal(GameState.Paused, pending.State);
        session.Now = RBRGameStateDetector.ResultWaitMilliseconds;
        var ended = session.Detector.Detect(noMemory, false, pending.State, 0, 0);
        Assert.Equal(GameState.RaceEnd, ended.State);
        Assert.Null(ended.FinishState);
        Assert.Null(session.Feed(Failed).FinishState);
    }

    [Fact]
    public void LeavingBeforeResultAndUnknownBuildEndSilently()
    {
        var session = new Session();
        session.Feed(Started);
        session.Feed(Started with { RawState = 9 });
        var exit = session.Feed(Failed with { RawState = 3 });
        Assert.Equal(GameState.RaceEnd, exit.State);
        Assert.Null(exit.FinishState);
        Assert.Null(session.Feed(Failed).FinishState);

        var detector = new RBRGameStateDetector();
        detector.Detect(new RBRMemData { GameStateId = 1, StageStartCountdown = -1 }, false, GameState.Unknown, 0.1f, 10);
        var end = detector.Detect(new RBRMemData { GameStateId = 9 }, false, GameState.Racing, 0, 0);
        Assert.Equal(GameState.RaceEnd, end.State);
        Assert.Null(end.FinishState);
    }

    [Fact]
    public void ResumingRacingDiscardsAnEarlierPendingDeadline()
    {
        var session = new Session();
        session.Feed(Started);
        session.Feed(Started with { RawState = 9 });
        session.Now = 3000;
        session.Feed(Started);
        session.Feed(Started with { RawState = 9 });
        session.Now += 155;
        Assert.Equal(GameStateRaceEnd.Retired, session.Feed(Failed).FinishState);
    }
}
