using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRCarDamageSnapshotFactoryTest
{
    [Fact]
    public void FactoryRetainsRawDataAndBuildsDashboardProjection()
    {
        var source = new RBRDamageTelemetrySnapshot(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            0x008EF660,
            0x01000000,
            new[]
            {
                Part("Radiator", 5, 0.01f),
                Part("Gearbox", 4),
                Part("WheelLF", 3),
                Part("BrakeCaliperLF", 2)
            },
            new[]
            {
                Group("Radiator", 5, "Radiator"),
                Group("Engine", 4, "Gearbox"),
                Group("FrontLeft", 3, "WheelLF", "BrakeCaliperLF")
            });

        var damage = RBRCarDamageSnapshotFactory.Create(source);

        Assert.NotNull(damage);
        Assert.Equal("rbr", damage!.Source);
        Assert.Equal(4, damage.Dashboard.RadiatorSeverity);
        Assert.Equal(3, damage.Dashboard.EngineSeverity);
        Assert.Equal(2, damage.Dashboard.FrontLeftSeverity);
        Assert.Equal(2, damage.Dashboard.FrontLeftWheelSeverity);
        Assert.Equal(1, damage.Dashboard.FrontLeftBrakeSeverity);
        Assert.Contains(damage.Components, component => component.Id == "wheel:front-left");
        Assert.Contains(damage.RawComponents, component => component.SourceName == "Radiator"
            && component.FloatValue == 0.01f);
    }

    private static RBRDamagePartTelemetry Part(string name, int hudLevel, float? floatValue = null)
    {
        return new RBRDamagePartTelemetry(
            name,
            0x01000000,
            true,
            floatValue,
            null,
            Array.Empty<float>(),
            Array.Empty<int>(),
            hudLevel);
    }

    private static RBRDamageGroupTelemetry Group(string name, int hudLevel, params string[] partNames)
    {
        return new RBRDamageGroupTelemetry(
            name,
            partNames,
            true,
            hudLevel,
            partNames);
    }
}
