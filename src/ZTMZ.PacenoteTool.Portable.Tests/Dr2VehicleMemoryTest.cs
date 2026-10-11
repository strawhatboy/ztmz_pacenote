using ZTMZ.PacenoteTool.Codemasters;
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
