using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc9DamageTests
{
    private static Wrc9Snapshot Sample(int milliseconds, float body, float engine = 0, string id = "attempt") =>
        new(id, "track", "car", "class", milliseconds) { Telemetry = new() { CarDamage = Wrc9Damage.Create([
            new("Body", 100, body, .75f, .25f), new("Engine", 100, engine, .7f, .3f)]) } };

    [Fact]
    public void NativeThresholdsAreRemainingHealthNotLostHealth()
    {
        Assert.Equal(0, new KtMechanicalDamagePart("Engine", 100, 29, .7f, .3f).Severity);
        Assert.Equal(1, new KtMechanicalDamagePart("Engine", 100, 31, .7f, .3f).Severity);
        Assert.Equal(2, new KtMechanicalDamagePart("Engine", 100, 71, .7f, .3f).Severity);
        var damage = Wrc9Damage.Create([new("Mystery", 100, 50, .75f, .25f)]);
        Assert.Empty(damage.Components); Assert.Single(damage.RawComponents); Assert.False(damage.Dashboard.IsAvailable);
    }

    [Fact]
    public void AnnouncementsRequireANewCollisionOrEngineThresholdCrossing()
    {
        var events = new Wrc9DamageEvents();
        Assert.Empty(events.Update(Sample(1000, 10, 29), GameState.Racing, false));
        var next = events.Update(Sample(1100, 16, 31), GameState.Racing, false);
        Assert.Equal(2, next.Count);
        Assert.Equal(1, next.Single(e => e.DamageType == CarDamage.Collision).Parameters[CarDamageConstants.SEVERITY]);
        Assert.Equal(1, next.Single(e => e.DamageType == CarDamage.Engine).Parameters[CarDamageConstants.SEVERITY]);
        Assert.Empty(events.Update(Sample(1200, 16, 31), GameState.Racing, false));
        Assert.Empty(events.Update(Sample(1300, 16.1f, 32), GameState.Racing, false));
        Assert.Equal(CarDamage.Engine, Assert.Single(events.Update(Sample(1400, 16.1f, 72), GameState.Racing, false)).DamageType);
    }

    [Theory]
    [InlineData("pause")] [InlineData("missing")] [InlineData("gap")]
    [InlineData("restart")] [InlineData("owner")] [InlineData("repair")]
    [InlineData("frozen")] [InlineData("finish")]
    public void DiscontinuitiesNeverAnnounceAccumulatedDamage(string scenario)
    {
        var events = new Wrc9DamageEvents(); events.Update(Sample(1000, 10, 10), GameState.Racing, false);
        if (scenario == "pause") events.Update(Sample(1000, 20, 40), GameState.Paused, false);
        if (scenario == "missing") events.Update(null, GameState.Unknown, false);
        var sample = Sample(scenario == "gap" ? 2000 : scenario == "frozen" ? 1000 : 1100, 30, scenario == "repair" ? 0 : 40, scenario == "owner" ? "other" : "attempt");
        Assert.Empty(events.Update(sample, scenario == "finish" ? GameState.RaceEnd : GameState.Racing, scenario == "restart"));
        Assert.Empty(events.Update(sample with { RawMilliseconds = sample.RawMilliseconds + 100 }, GameState.Racing, false));
    }

    [Fact]
    public void RecordedProductionRunClearsDamageOnRestartAndReportsOnlyNewImpacts()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "wrc9-damage-restart-20261016.json")));
        var tracker = new Wrc9DamageEvents(); var collisions = 0; var restarted = false; var before = int.MaxValue; var first = true;
        foreach (var row in fixture.RootElement.EnumerateArray())
        {
            var sample = Sample(row.GetProperty("milliseconds").GetInt32(), row.GetProperty("body").GetSingle(), row.GetProperty("engine").GetSingle());
            var state = Enum.Parse<GameState>(row.GetProperty("state").GetString()!);
            var restart = !first && sample.RawMilliseconds < before;
            var events = tracker.Update(sample, state, first || restart);
            if (restart) { restarted = true; Assert.Equal(0, sample.Telemetry.CarDamage.Components.Single(c => c.Kind == CarDamageIds.Bodywork).Magnitude); }
            if (first || restart || state != GameState.Racing) Assert.Empty(events);
            foreach (var evt in events)
            {
                Assert.True(restarted); Assert.Equal(CarDamage.Collision, evt.DamageType); collisions++;
                Assert.True((float)evt.Parameters[CarDamageConstants.VALUE] > (float)evt.Parameters[CarDamageConstants.PREVIOUS_VALUE]);
            }
            first = false; before = sample.RawMilliseconds;
        }
        Assert.True(restarted); Assert.Equal(5, collisions);
    }
}
