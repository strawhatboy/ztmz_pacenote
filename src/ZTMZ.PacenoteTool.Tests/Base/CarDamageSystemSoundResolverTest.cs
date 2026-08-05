using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class CarDamageSystemSoundResolverTest
{
    [Fact]
    public void TryResolve_HandlesLegacyCollisionSeverityEnums()
    {
        var damageEvent = new CarDamageEvent
        {
            DamageType = CarDamage.Collision,
            Parameters = new Dictionary<string, object>
            {
                [CarDamageConstants.SEVERITY] = CollisionSeverity.Severe
            }
        };

        var resolved = CarDamageSystemSoundResolver.TryResolve(damageEvent, out var sound);

        Assert.True(resolved);
        Assert.Equal(Constants.SYSTEM_COLLISION_SEVERE, sound.Token);
        Assert.Equal("collision", sound.CooldownKey);
        Assert.True(sound.RequiresDamageSoundEnabled);
    }

    [Theory]
    [InlineData(CarDamage.Radiator, Constants.SYSTEM_RADIATOR_DAMAGE, "mechanical:radiator")]
    [InlineData(CarDamage.WaterPump, Constants.SYSTEM_WATER_PUMP_DAMAGE, "mechanical:water-pump")]
    [InlineData(CarDamage.Engine, Constants.SYSTEM_ENGINE_DAMAGE, "mechanical:engine")]
    public void TryResolve_MapsMechanicalDamageToSystemTokens(
        CarDamage damageType,
        string expectedToken,
        string expectedCooldownKey)
    {
        var damageEvent = new CarDamageEvent
        {
            DamageType = damageType,
            Parameters = new Dictionary<string, object>()
        };

        var resolved = CarDamageSystemSoundResolver.TryResolve(damageEvent, out var sound);

        Assert.True(resolved);
        Assert.Equal(expectedToken, sound.Token);
        Assert.Equal(expectedCooldownKey, sound.CooldownKey);
        Assert.True(sound.RequiresDamageSoundEnabled);
    }

    [Fact]
    public void TryResolve_LeavesRawBodyworkEventsSilent()
    {
        var damageEvent = new CarDamageEvent
        {
            DamageType = CarDamage.Bodywork,
            Parameters = new Dictionary<string, object>()
        };

        Assert.False(CarDamageSystemSoundResolver.TryResolve(damageEvent, out _));
    }
}
