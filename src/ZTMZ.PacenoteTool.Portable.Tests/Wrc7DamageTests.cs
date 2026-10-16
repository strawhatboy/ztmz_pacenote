using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7DamageTests
{
    private static Wrc7Snapshot Sample(int milliseconds, float impact, float wear = 0, string id = "attempt") =>
        new(id, "track", "car", "class", milliseconds) { NativeResult = new(false, false, false, false),
            Telemetry = new() { CarDamage = Wrc7Damage.Create([
                new("Engine", 100, impact, wear), new("Steering", 100, impact, 0)]) } };

    [Fact]
    public void WearContributesToConditionButNeverToCollisionEvents()
    {
        var tracker = new Wrc7DamageEvents();
        Assert.Empty(tracker.Update(Sample(1000, 0, 24), GameState.Racing, false));
        var next = Sample(1100, 0, 25);
        Assert.Equal(.25f, next.Telemetry.CarDamage.Components.Single(c => c.Kind == CarDamageIds.Engine).Magnitude);
        Assert.Equal(CarDamage.Engine, Assert.Single(tracker.Update(next, GameState.Racing, false)).DamageType);
        Assert.Equal(CarDamage.Engine, Assert.Single(tracker.Update(Sample(1200, 0, 75), GameState.Racing, false)).DamageType);
        Assert.Empty(tracker.Update(Sample(1300, 0, 80), GameState.Racing, false));
        Assert.Equal(1, new Wrc7DamagePart("Engine", 100, 120, 40).Magnitude);
    }

    [Fact]
    public void OneImpactAcrossMultiplePartsProducesOneCollision()
    {
        var tracker = new Wrc7DamageEvents(); tracker.Update(Sample(1000, 0), GameState.Racing, false);
        var hit = Assert.Single(tracker.Update(Sample(1100, 10), GameState.Racing, false));
        Assert.Equal(CarDamage.Collision, hit.DamageType);
        Assert.Equal(1, hit.Parameters[CarDamageConstants.SEVERITY]);
        Assert.Empty(tracker.Update(Sample(1200, 10), GameState.Racing, false));
    }

    [Theory]
    [InlineData("pause")] [InlineData("missing")] [InlineData("gap")] [InlineData("restart")]
    [InlineData("owner")] [InlineData("repair")] [InlineData("frozen")] [InlineData("finish")]
    public void DiscontinuitiesDoNotAnnounceOldDamage(string scenario)
    {
        var tracker = new Wrc7DamageEvents(); tracker.Update(Sample(1000, 20, 5), GameState.Racing, false);
        if (scenario == "pause") tracker.Update(Sample(1000, 20), GameState.Paused, false);
        if (scenario == "missing") tracker.Update(null, GameState.Unknown, false);
        var sample = Sample(scenario == "gap" ? 2000 : scenario == "frozen" ? 1000 : 1100,
            40, scenario == "repair" ? 0 : 5, scenario == "owner" ? "other" : "attempt");
        Assert.Empty(tracker.Update(sample, scenario == "finish" ? GameState.RaceEnd : GameState.Racing, scenario == "restart"));
        Assert.Empty(tracker.Update(sample with { RawMilliseconds = sample.RawMilliseconds + 100 }, GameState.Racing, false));
    }

    [Fact]
    public void RecordedRestartAndImpactsHaveCorrectComponentsAndAnnouncements()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "wrc7-damage-restart-20261016.json")));
        var lifecycle = new Wrc7RaceSession(); var tracker = new Wrc7DamageEvents();
        var collisions = 0; var reset = false; var first = true;
        foreach (var row in fixture.RootElement.EnumerateArray())
        {
            var parts = row.GetProperty("parts").EnumerateArray().Select(p => new Wrc7DamagePart(
                p.GetProperty("name").GetString()!, p.GetProperty("maximum").GetSingle(),
                p.GetProperty("impact").GetSingle(), p.GetProperty("wear").GetSingle())).ToArray();
            var damage = Wrc7Damage.Create(parts);
            var sample = Sample(row.GetProperty("milliseconds").GetInt32(), 0) with { Telemetry = new() { CarDamage = damage } };
            var state = lifecycle.Update(sample, row.GetProperty("elapsedMs").GetInt64());
            var events = tracker.Update(sample, state, lifecycle.IsNewSession);
            Assert.Equal(15, damage.Components.Count); Assert.Equal(15, damage.RawComponents.Count);
            Assert.True(damage.Dashboard.FrontLeftWheelAvailable);
            Assert.True(damage.Dashboard.FrontLeftSuspensionAvailable);
            Assert.False(damage.Dashboard.FrontLeftBrakeAvailable);
            Assert.False(damage.Dashboard.FrontLeftTyreAvailable);
            Assert.False(damage.Dashboard.BodyworkAvailable);
            Assert.False(damage.Dashboard.RadiatorAvailable);
            if (sample.RawMilliseconds == -1)
            {
                // The clock resets before the parts on stage restart. Neither
                // the transitional old values nor the cleared values announce.
                reset |= parts.All(p => p.Impact == 0); Assert.Empty(events);
            }
            if (first || lifecycle.IsNewSession || state != GameState.Racing) Assert.Empty(events);
            foreach (var evt in events)
            {
                Assert.True(reset); Assert.Equal(CarDamage.Collision, evt.DamageType); collisions++;
            }
            first = false;
        }
        Assert.True(reset); Assert.Equal(2, collisions);
    }
}
