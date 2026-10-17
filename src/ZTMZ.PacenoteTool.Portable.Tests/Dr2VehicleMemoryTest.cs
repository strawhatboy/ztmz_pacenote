using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Base.Game;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2VehicleMemoryTest
{
    private const ulong Image = 0x140000000, Vehicle = 0x200000000, Physics = 0x300000000;
    private readonly Dictionary<ulong, byte> memory = new();

    public Dr2VehicleMemoryTest()
    {
        Put(Image + 0x201b930, BitConverter.GetBytes(1));
        Put(Image + 0x201b7b0, BitConverter.GetBytes(Vehicle));
        Put(Vehicle, BitConverter.GetBytes(Image + 0x1400c30));
        Put(Vehicle + 8, BitConverter.GetBytes(Physics));
        Put(Vehicle + 0xd48, BitConverter.GetBytes(0));
        Put(Physics + 0x4d0, "fr5\0"u8.ToArray());
        Encoded(Physics + 0x6d0, 188.49556f, 0xb05c6c43);
        Encoded(Physics + 0x6d4, 774.9262f, 0x6fa85a41);
        Encoded(Physics + 0x804, 736.4941f, 0xb0ef7c77);
        Put(Physics + 0x390, BitConverter.GetBytes(0.75f));
        Put(Physics + 0x1378, BitConverter.GetBytes(0ul));
    }

    [Fact]
    public void ReadsNativeHandbrakeAndShiftWarning()
    {
        var result = Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f);
        Assert.NotNull(result);
        Assert.Equal(0.75f, result.Value.HandBrake);
        Assert.Equal("fr5", result.Value.CarCode);
        Assert.Equal(7033f, result.Value.ShiftRPM, precision: 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NeverReadsGhostOrOtherVehicleModes(int mode)
    {
        Put(Vehicle + 0xd48, BitConverter.GetBytes(mode));
        Assert.Null(Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f));
    }

    [Fact]
    public void RejectsVehicleThatDoesNotMatchUdp()
    {
        Assert.Null(Dr2VehicleMemory.Read(Read, Image, 180f, 774.9262f));
        Assert.Null(Dr2VehicleMemory.Read(Read, Image, 188.49556f, 800f));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(1.5f)]
    public void RejectsInvalidHandbrake(float value)
    {
        Put(Physics + 0x390, BitConverter.GetBytes(value));
        Assert.Null(Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f));
    }

    private const ulong Parameters = 0x400000000;

    private void Turbo(float maximum, float pressure)
    {
        Put(Physics + 0x1378, BitConverter.GetBytes(Parameters));
        Encoded(Parameters + 0x344, maximum, 0x16889185);
        Put(Physics + 0x2538, BitConverter.GetBytes(pressure));
    }

    [Theory]
    [InlineData(-0.11217296f)] // Captured Fiesta R5 idle.
    [InlineData(0f)]
    [InlineData(2.4999995f)] // Captured Fiesta R5 full boost.
    public void ReadsHudGaugePressureWithoutSubtractingAtmosphereAgain(float pressure)
    {
        Turbo(3.5f, pressure);
        var before = DateTimeOffset.UtcNow;
        var result = Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f);
        var turbo = Assert.IsType<TurboTelemetrySnapshot>(result!.Value.Turbo);
        Assert.Equal(pressure, turbo.PressureBar);
        Assert.Equal(TurboPressureReference.Gauge, turbo.PressureReference);
        Assert.Equal("DR2", turbo.Source);
        Assert.InRange(turbo.CapturedAtUtc, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(0f, 2.5f)] // Non-turbo car must not inherit an old pressure.
    [InlineData(-1f, 0f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(3.5f, -1f)] // Native initialization sentinel.
    [InlineData(3.5f, -2f)]
    [InlineData(3.5f, 3f)] // Above the decoded physical limit.
    [InlineData(3.5f, float.NaN)]
    [InlineData(3.5f, float.PositiveInfinity)]
    public void InvalidOrUnavailableTurboPreservesOtherVehicleData(float maximum, float pressure)
    {
        Turbo(maximum, pressure);
        var result = Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f);
        Assert.NotNull(result);
        Assert.Null(result.Value.Turbo);
        Assert.Equal(.75f, result.Value.HandBrake);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrPartialTurboReadDoesNotDisableHandbrake(bool partial)
    {
        Turbo(3.5f, 2.5f);
        byte[] ReadFailing(ulong address, int count) => address == Physics + 0x2538
            ? partial ? new byte[3] : throw new IOException("read failure") : Read(address, count);
        var result = Dr2VehicleMemory.Read(ReadFailing, Image, 188.49556f, 774.9262f);
        Assert.NotNull(result);
        Assert.Null(result.Value.Turbo);
        Assert.Equal(.75f, result.Value.HandBrake);
    }

    [Fact]
    public void DoesNotRetainTurboWhenCarLosesAvailability()
    {
        Turbo(3.5f, 2.5f);
        Assert.NotNull(Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f)!.Value.Turbo);
        Turbo(0, 2.5f);
        Assert.Null(Dr2VehicleMemory.Read(Read, Image, 188.49556f, 774.9262f)!.Value.Turbo);
    }

    [Fact]
    public void RejectsParameterReplacementDuringTurboRead()
    {
        Turbo(3.5f, 2.5f);
        var reads = 0;
        byte[] Replacing(ulong address, int count) => address == Physics + 0x1378 && ++reads == 2
            ? BitConverter.GetBytes(Parameters + 0x1000) : Read(address, count);
        Assert.Null(Dr2VehicleMemory.Read(Replacing, Image, 188.49556f, 774.9262f)!.Value.Turbo);
    }

    [Fact]
    public void RejectsPlayerReplacementDuringOptionalRead()
    {
        Turbo(3.5f, 2.5f);
        byte[] Replacing(ulong address, int count)
        {
            var bytes = Read(address, count);
            if (address == Physics + 0x2538) Put(Vehicle + 8, BitConverter.GetBytes(Physics + 0x10000));
            return bytes;
        }
        Assert.Null(Dr2VehicleMemory.Read(Replacing, Image, 188.49556f, 774.9262f));
    }

    private void Encoded(ulong address, float value, uint multiplier)
    {
        // Multiplicative inverse modulo 2^32 of the game's odd multiplier.
        uint inverse = multiplier;
        for (var i = 0; i < 5; i++) inverse = unchecked(inverse * (2 - multiplier * inverse));
        Put(address, BitConverter.GetBytes(unchecked((uint)BitConverter.SingleToInt32Bits(value) * inverse)));
    }
    private void Put(ulong address, byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++) memory[address + (ulong)i] = bytes[i];
    }
    private byte[] Read(ulong address, int count) =>
        Enumerable.Range(0, count).Select(i => memory[address + (ulong)i]).ToArray();
}
