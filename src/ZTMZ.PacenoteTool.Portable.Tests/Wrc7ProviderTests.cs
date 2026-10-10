using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7ProviderTests
{
    private static Wrc7Snapshot Snapshot(int time, string identity = "race") => new(identity, "stage", "car", "R2", time);
    [Fact]
    public void PauseAndLossOfSessionAreNeverReportedAsFinish()
    {
        var session = new Wrc7RaceSession();
        Assert.Equal(GameState.Racing, session.Update(Snapshot(1000), 0));
        Assert.True(session.IsNewSession);
        Assert.Equal(GameState.Racing, session.Update(Snapshot(1050), 50));
        Assert.False(session.IsNewSession);
        Assert.Equal(GameState.Paused, session.Update(Snapshot(1050), 600));
        Assert.Equal(GameState.Paused, session.Update(Snapshot(1050), 60000));
        Assert.Equal(GameState.Racing, session.Update(Snapshot(1100), 60050));
        Assert.False(session.IsNewSession);
        Assert.Equal(GameState.Unknown, session.Update(null, 60100));
    }
    [Fact]
    public void TimerResetOrChangedSessionRequiresNewPreparation()
    {
        var session = new Wrc7RaceSession();
        session.Update(Snapshot(10000), 0);
        Assert.Equal(GameState.CountingDown, session.Update(Snapshot(0), 50));
        Assert.True(session.IsNewSession);
        session.Update(Snapshot(0), 100);
        Assert.False(session.IsNewSession);
        session.Update(Snapshot(20, "other"), 150);
        Assert.True(session.IsNewSession);
    }
    [Fact]
    public void ProviderUsesActualProcessWindowAndSharedLivePolicy()
    {
        var game = new Wrc7();
        Assert.Equal("WRC7", game.Executable);
        Assert.Equal("WRC7", game.WindowTitle);
        Assert.Equal(621830u, game.SteamApplicationId);
        Assert.IsAssignableFrom<IGameLivePacenoteSource>(game.GameDataReader);
        Assert.True(game.UseLivePacenotesOnly);
        Assert.False(game.UseTelemetryGoSound);
        Assert.False(((Wrc7Config)game.GameConfigurations[Wrc7Config.Name]).ControlsEnabled);
        Assert.IsAssignableFrom<ILocalGameBridgeControl>(game);
    }
    [Fact]
    public void SharedNameCatalogReadsNestedWrc7CarsAndLocaleIds()
    {
        var catalog = new WrcGenerationsSessionCatalog(new byte[4].Concat(Encoding.Unicode.GetBytes("Stage\0Fiesta R2\0")).ToArray(),
            "LocalesId = {\n STAGENAME_MC_SS2 = 0,\n CAR_FORD = 1,\n}",
            "Cars = {\n Ford = {\n Name = LocalesId.CAR_FORD,\n Category = WrcChampionships.JWRC,\n Wheels = {},\n },\n}");
        Assert.Equal("Stage", catalog.Name("STAGENAME_MC_SS2"));
        Assert.Equal("JWRC", catalog.Class(1));
    }
}
