using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class GameTelemetrySessionTest
{
    [Fact]
    public void ActivatingAnotherGameReleasesThePreviousReader()
    {
        var first = new TestGame("First");
        var second = new TestGame("Second");
        using var session = new GameTelemetrySession();

        Assert.True(session.Activate(first));
        Assert.True(first.IsInitialized);
        Assert.Equal(1, first.Reader.InitializeCount);

        Assert.True(session.Activate(second));
        Assert.False(first.IsInitialized);
        Assert.Equal(1, first.Reader.UninitializeCount);
        Assert.True(second.IsInitialized);
        Assert.Same(second, session.CurrentGame);
    }

    [Fact]
    public void TelemetryIsForwardedOnlyForTheActiveGame()
    {
        var game = new TestGame("Active");
        using var session = new GameTelemetrySession();
        var received = new List<(IGame Game, GameData Data)>();
        session.TelemetryReceived += (source, data) => received.Add((source, data));

        Assert.True(session.Activate(game));
        game.Reader.Publish(new GameData { Speed = 123 });
        session.Deactivate();
        game.Reader.Publish(new GameData { Speed = 234 });

        var sample = Assert.Single(received);
        Assert.Same(game, sample.Game);
        Assert.Equal(123, sample.Data.Speed);
    }

    [Fact]
    public void ActivatingRealCodemastersReaderBindsAndReleasesUdpPort()
    {
        var game = new DirtRally();
        var configuration = Assert.IsType<UdpGameConfig>(
            game.GameConfigurations[UdpGameConfig.Name]);
        configuration.IPAddress = "127.0.0.1";
        configuration.Port = GetAvailableUdpPort();
        var reader = Assert.IsType<DirtGameDataReader>(game.GameDataReader);
        using var session = new GameTelemetrySession();

        Assert.True(session.Activate(game));
        Assert.True(reader.UdpStatus.IsListening);
        Assert.Equal(configuration.Port, reader.UdpStatus.Port);

        session.Deactivate();
        Assert.False(reader.UdpStatus.IsListening);
    }

    private static int GetAvailableUdpPort()
    {
        using var client = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private sealed class TestGame : IGame
    {
        public TestGame(string name)
        {
            Name = name;
        }

        public TestGameDataReader Reader { get; } = new();
        public string WindowTitle => string.Empty;
        public string Name { get; }
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => Name;
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => Reader;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = [];
        public Dictionary<string, IGameConfig> DefaultGameConfigurations => [];
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message) =>
            onCustomMessage?.Invoke(level, message);
    }

    private sealed class TestGameDataReader : IGameDataReader
    {
        public int InitializeCount { get; private set; }
        public int UninitializeCount { get; private set; }
        public GameState GameState { get; set; }
        public GameData LastGameData { get; set; }
        public GameData CurrentGameData { get; set; }
        public string TrackName => string.Empty;
        public string CarName => string.Empty;
        public string CarClass => string.Empty;
        public event Action<GameData, GameData>? onNewGameData;
        public event Action<bool>? onGameDataAvailabilityChanged;
        public event Action<GameStateChangeEvent>? onGameStateChanged;
        public event Action<CarDamageEvent>? onCarDamaged;
        public event Action? onCarReset;

        public bool Initialize(IGame game)
        {
            InitializeCount++;
            return true;
        }

        public void Uninitialize(IGame game) => UninitializeCount++;

        public void Publish(GameData data)
        {
            var previous = CurrentGameData;
            LastGameData = previous;
            CurrentGameData = data;
            onNewGameData?.Invoke(previous, data);
        }
    }
}
