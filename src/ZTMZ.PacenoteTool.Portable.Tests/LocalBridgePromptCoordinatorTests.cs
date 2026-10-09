using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

public sealed class LocalBridgePromptCoordinatorTests
{
    [Fact]
    public async Task DeclineIsRememberedUntilSessionEnds()
    {
        var game = new BridgeGame();
        var prompts = 0;
        using var coordinator = new LocalBridgePromptCoordinator((_, _, _) => { prompts++; return Task.FromResult(false); }, (_, _) => Task.CompletedTask);
        await coordinator.RequestAsync(game);
        await coordinator.RequestAsync(game);
        Assert.Equal(1, prompts);
        coordinator.Reset(game);
        await coordinator.RequestAsync(game);
        Assert.Equal(2, prompts);
        Assert.Equal(0, game.Injections);
    }

    [Fact]
    public async Task SessionEndingWhileConfirmationIsOpenPreventsInjection()
    {
        var game = new BridgeGame();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new LocalBridgePromptCoordinator((_, _, _) => { shown.SetResult(); return answer.Task; }, (_, _) => Task.CompletedTask);
        var pending = coordinator.RequestAsync(game);
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Reset(game);
        answer.SetResult(true);
        await pending;
        Assert.Equal(0, game.Injections);
    }

    [Fact]
    public async Task AlreadyInjectedAndDisabledGamesDoNotPrompt()
    {
        var game = new BridgeGame { Injected = true };
        var prompts = 0;
        using var coordinator = new LocalBridgePromptCoordinator((_, _, _) => { prompts++; return Task.FromResult(true); }, (_, _) => Task.CompletedTask);
        await coordinator.RequestAsync(game);
        game.Injected = false;
        game.Enabled = false;
        await coordinator.RequestAsync(game);
        game.Enabled = true;
        await coordinator.RequestAsync(game);
        await coordinator.RequestAsync(game);
        Assert.Equal(1, prompts);
        Assert.Equal(1, game.Injections);
    }

    private sealed class BridgeGame : IGame, ILocalGameBridgeControl
    {
        public bool Enabled = true;
        public bool Injected;
        public int Injections;
        public LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics() => new("test", Enabled, true, Injected, "test");
        public Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync()
        { Injections++; Injected = true; return Task.FromResult(new LocalBridgeInjectionResult(true, "ready")); }
        public string Name => "test";
        public string WindowTitle => "test";
        public string Description => "test";
        public string ImageUri => "";
        public string Executable => "test";
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations => new();
        public event Action<int, string>? onCustomMessage;
        public void OnCustomMessage(int level, string message) => onCustomMessage?.Invoke(level, message);
        public bool IsRunning { get; set; } = true;
        public bool IsInitialized { get; set; }
        public int Order => 0;
    }
}
