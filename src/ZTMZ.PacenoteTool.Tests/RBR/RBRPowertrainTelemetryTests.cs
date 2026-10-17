using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRPowertrainTelemetryTests
{
    [Fact]
    public void ProductionProjectionClearsPreviousReadingsWhenMemoryBecomesUnavailable()
    {
        using var reader = new RBRGameDataReader();
        var project = typeof(RBRGameDataReader).GetMethod("GetGameDataFromMemory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var engine = new EngineTelemetrySnapshot("rbr", DateTimeOffset.UtcNow) { CoolantTemperatureCelsius = 90 };
        var turbo = new TurboTelemetrySnapshot("rbr", engine.CapturedAtUtc) { PressureBar = 1.2f };
        var valid = (GameData)project.Invoke(reader, [default(GameData), new RBRMemData { Engine = engine, Turbo = turbo }])!;
        Assert.Same(engine, valid.Engine);
        Assert.Same(turbo, valid.Turbo);
        var missing = (GameData)project.Invoke(reader, [valid, default(RBRMemData)])!;
        Assert.Null(missing.Engine);
        Assert.Null(missing.Turbo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125000)]
    public void PreservesPressureConversionWithoutInventingAtmosphericReference(float pascals)
    {
        var now = DateTimeOffset.UtcNow;
        var (engine, turbo) = RBRPowertrainTelemetry.Read((address, buffer) =>
        {
            Assert.Equal(0x100014, address);
            BitConverter.GetBytes(92f).CopyTo(buffer, 0);
            BitConverter.GetBytes(pascals).CopyTo(buffer, 4);
            return true;
        }, 0x100000, now);
        Assert.Equal(92, engine!.CoolantTemperatureCelsius);
        Assert.Equal(pascals / 100000f, turbo!.PressureBar);
        Assert.Equal(TurboPressureReference.Unknown, turbo.PressureReference);
        Assert.Equal(now, engine.CapturedAtUtc);
        Assert.Equal(now, turbo.CapturedAtUtc);
        Assert.Null(engine.OilTemperatureCelsius);
        Assert.Null(engine.EngineWarning);
        Assert.Null(engine.CoolantTemperatureWarning);
    }

    [Fact]
    public void FailedOrPartialReadCannotBecomeZeroTelemetry()
    {
        var snapshot = RBRPowertrainTelemetry.Read((_, buffer) =>
        {
            BitConverter.GetBytes(90f).CopyTo(buffer, 0);
            return false;
        }, 0x100000, DateTimeOffset.UtcNow);
        Assert.Null(snapshot.Engine);
        Assert.Null(snapshot.Turbo);
        var nullPointer = RBRPowertrainTelemetry.Read((_, _) => throw new Exception("must not read"), 0, DateTimeOffset.UtcNow);
        Assert.Null(nullPointer.Engine);
        Assert.Null(nullPointer.Turbo);
    }

    [Fact]
    public void InvalidFieldsAreIndependentlyUnavailable()
    {
        var snapshot = RBRPowertrainTelemetry.Read((_, buffer) =>
        {
            BitConverter.GetBytes(float.NaN).CopyTo(buffer, 0);
            BitConverter.GetBytes(200000f).CopyTo(buffer, 4);
            return true;
        }, 0x100000, DateTimeOffset.UtcNow);
        Assert.Null(snapshot.Engine);
        Assert.Equal(2, snapshot.Turbo!.PressureBar);
    }
}
