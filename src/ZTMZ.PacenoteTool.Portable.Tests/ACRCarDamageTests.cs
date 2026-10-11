using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.AC;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ACRCarDamageTests
{
    [Fact]
    public void ProjectsNativeComponentsWithoutConfusingStateIndexWithSeverity()
    {
        var damage = ACRCarDamage.Create([
            new("Engine", 99, 100, 1, 1), new("BrakesFrontLeft", 75, 100, 2, 2),
            new("SuspensionRearRight", 20, 100, 3, 3), new("WheelGeometryFrontRight", 30, 100, 3, 3),
            new("Radiator", 5, 100, 4, 4), new("GearBox", 0, 100, 4, 4),
            new("DifferentialFront", 100, 100, 0, 1), new("DifferentialRear", 30, 100, 3, 3)]);
        Assert.Equal(0, damage.Dashboard.EngineSeverity);
        Assert.Equal(2, damage.Dashboard.FrontLeftBrakeSeverity);
        Assert.Equal(3, damage.Dashboard.RearRightSuspensionSeverity);
        Assert.Equal(3, damage.Dashboard.FrontRightWheelSeverity);
        Assert.Equal(4, damage.Dashboard.RadiatorSeverity);
        Assert.Equal(3, damage.Dashboard.DifferentialSeverity);
        Assert.Equal(0, damage.Dashboard.FrontRightTyreSeverity);
        Assert.Equal(4, damage.Components.Single(c => c.Id == CarDamageIds.Gearbox).Severity);
        Assert.Equal(4, damage.Dashboard.GearboxSeverity);
        Assert.True(damage.Dashboard.GearboxAvailable);
        Assert.False(damage.Dashboard.FrontRightTyreAvailable);
        Assert.Equal(.01f, damage.Components.Single(c => c.Id == "engine").Magnitude, 5);
        Assert.All(damage.Groups, group => Assert.False(group.IsComplete));
        var converted = ACRGameDataConverter.Convert(new(), new(0, 0, 0) { Damage = damage });
        Assert.Same(damage, converted.CarDamage);
        Assert.False(ACRGameDataConverter.Convert(new()).CarDamage.Dashboard.IsAvailable);
    }

    [Fact]
    public void ReadsCurrentVehiclePartsAndActualStateType()
    {
        var memory = new Fixture();
        var result = ACRCarDamage.Read(memory.Read, Fixture.Image, Fixture.Pawn);
        Assert.NotNull(result);
        Assert.Equal(3, result.Dashboard.EngineSeverity);
        Assert.Equal(.7f, result.Components.Single().Magnitude, 5);
        Assert.Equal(new float[] { 30, 100 }, result.RawComponents.Single().FloatValues);
        Assert.Equal(new[] { 2, 3 }, result.RawComponents.Single().IntegerValues);
    }

    [Theory]
    [InlineData(0x130, -1)]
    [InlineData(0x130, 3)]
    [InlineData(0xc8, 10000)]
    public void RejectsInvalidStateOrCollection(int offset, int value)
    {
        var memory = new Fixture();
        memory.Put(Fixture.Part + (ulong)offset, BitConverter.GetBytes(value));
        Assert.Null(ReadOrUnavailable(memory.Read));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(101f)]
    public void InvalidLifeIsUnavailableRatherThanHealthy(float value)
    {
        var memory = new Fixture();
        memory.Put(Fixture.Part + 0x134, BitConverter.GetBytes(value));
        Assert.Null(ReadOrUnavailable(memory.Read));
    }

    [Fact]
    public void RejectsForeignOwnerAndReplacementDuringRead()
    {
        var memory = new Fixture();
        memory.Put(Fixture.Part + 0x20, BitConverter.GetBytes(Fixture.Component + 0x1000));
        Assert.Null(ReadOrUnavailable(memory.Read));
        memory = new Fixture();
        var reads = 0;
        byte[] Read(ulong address, int count) => address == Fixture.Component + 0x118 && ++reads == 2
            ? new byte[count] : memory.Read(address, count);
        Assert.Null(ReadOrUnavailable(Read));
    }

    private static CarDamageSnapshot? ReadOrUnavailable(Func<ulong, int, byte[]> read)
    {
        try { return ACRCarDamage.Read(read, Fixture.Image, Fixture.Pawn); }
        catch (IOException) { return null; }
    }

    private sealed class Fixture
    {
        internal const ulong Image = 0x140000000, Pawn = 0x100000, Component = 0x300000, Part = 0x500000;
        private readonly Dictionary<ulong, byte> bytes = [];
        public Fixture()
        {
            Put(Pawn + 0x290, BitConverter.GetBytes(0x200000UL));
            Put(Pawn + 0x298, BitConverter.GetBytes(1)); Put(Pawn + 0x29c, BitConverter.GetBytes(1));
            Put(0x200000, BitConverter.GetBytes(Component));
            Put(Component, BitConverter.GetBytes(Image + ACRCarDamage.ComponentVtableRva));
            Put(Component + 0xa0, BitConverter.GetBytes(Pawn)); Put(Component + 0x20, BitConverter.GetBytes(Pawn));
            Put(Component + 0x118, BitConverter.GetBytes(0x400000UL));
            Put(Component + 0x120, BitConverter.GetBytes(1)); Put(Component + 0x124, BitConverter.GetBytes(1));
            Put(0x400000, BitConverter.GetBytes(Part)); Put(Part + 0x20, BitConverter.GetBytes(Component));
            Put(Part + 0x58, BitConverter.GetBytes(10));
            Put(Image + ACRCarDamage.NamePoolRva + 0x10, BitConverter.GetBytes(0x700000UL));
            Put(0x700014, BitConverter.GetBytes((ushort)(6 << 6))); Put(0x700016, Encoding.UTF8.GetBytes("Engine"));
            Put(Part + 0xb8, BitConverter.GetBytes(100f)); Put(Part + 0x134, BitConverter.GetBytes(30f));
            Put(Part + 0x130, BitConverter.GetBytes(2)); Put(Part + 0xc0, BitConverter.GetBytes(0x600000UL));
            Put(Part + 0xc8, BitConverter.GetBytes(3)); Put(Part + 0xcc, BitConverter.GetBytes(3));
            Put(0x600048, [3]);
        }
        public void Put(ulong address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
        public byte[] Read(ulong address, int count) => Enumerable.Range(0, count).Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
    }
}
