using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2CarDamageTest
{
    private const ulong Physics = 0x200000000, Owner = Physics + 0x2bd0, Parameters = 0x300000000;
    private readonly Dictionary<ulong, byte> memory = new();

    public Dr2CarDamageTest()
    {
        Put(Owner, new byte[0x448]);
        Put(Owner, BitConverter.GetBytes(Owner));
        Put(Owner + 0x440, BitConverter.GetBytes(Parameters));
        Put(Parameters, new byte[0x33c]);
        foreach (var offset in new[] { 0x1f4, 0x1fc, 0x204, 0x1ec, 0x2a0, 0x334 })
            Put(Parameters + (ulong)offset, BitConverter.GetBytes(0.2f));
        foreach (var offset in new[] { 0x84, 0xb8, 0xc0, 0xec, 0xf4, 0xfc, 0x18c })
            Put(Parameters + (ulong)offset, BitConverter.GetBytes(0.2f));
        Put(Parameters + 0x104, BitConverter.GetBytes(0.9f));
    }

    [Fact]
    public void ProjectsActualDamageUsingVehicleThresholdAndPreservesRawValues()
    {
        Put(Owner + 8, BitConverter.GetBytes(0.6f));
        Put(Owner + 8 + 0x80, BitConverter.GetBytes(1f));
        Put(Owner + 8 + 0xc, BitConverter.GetBytes(0.0001666667f));
        var result = Dr2CarDamage.Read(Read, Physics)!;
        Assert.Equal(20, result.Components.Count);
        Assert.Equal(0.6f, result.Components.Single(p => p.Kind == "engine").Magnitude);
        Assert.Equal(4, result.Dashboard.RadiatorSeverity);
        Assert.Equal(0, result.Components.Single(p => p.Kind == "gearbox").Severity);
        Assert.Equal(new[] { 1f, 0.2f, 1f }, result.RawComponents.Single(p => p.Id == "radiator").FloatValues);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void RejectsInvalidDamage(float value)
    {
        Put(Owner + 8, BitConverter.GetBytes(value));
        Assert.Null(Dr2CarDamage.Read(Read, Physics));
    }

    [Fact]
    public void RejectsChangedOwner()
    {
        Put(Owner, BitConverter.GetBytes(Owner + 16));
        Assert.Null(Dr2CarDamage.Read(Read, Physics));
    }

    [Fact]
    public void SeparatesFourCornersAndPreservesSpringAndDamperSources()
    {
        Put(Owner + 8 + 0x10, BitConverter.GetBytes(.5f)); // rear-left damper
        Put(Owner + 8 + 0x24, BitConverter.GetBytes(1f)); // rear-right spring
        Put(Owner + 8 + 0x38, BitConverter.GetBytes(.5f)); // front-left brake
        Put(Owner + 8 + 0x98, BitConverter.GetBytes(.95f)); // front-right wheel
        Put(Owner + 0x120, new byte[] { 0, 1, 0, 2 });
        Put(Owner + 8 + 0xa4, BitConverter.GetBytes(.7f));
        var damage = Dr2CarDamage.Read(Read, Physics)!;
        Assert.Equal(2, damage.Dashboard.RearLeftSuspensionSeverity);
        Assert.Equal(4, damage.Dashboard.RearRightSuspensionSeverity);
        Assert.Equal(2, damage.Dashboard.FrontLeftBrakeSeverity);
        Assert.Equal(4, damage.Dashboard.FrontRightWheelSeverity);
        Assert.Equal(0, damage.Dashboard.FrontLeftWheelSeverity);
        Assert.Equal(3, damage.Dashboard.RearRightTyreSeverity);
        Assert.Equal(4, damage.Dashboard.FrontRightTyreSeverity);
        Assert.Equal(0, damage.Dashboard.FrontLeftTyreSeverity);
        Assert.Equal(3, damage.Dashboard.BodyworkSeverity);
        Assert.Equal(4, damage.Groups.Count);
        Assert.Contains(damage.RawComponents, c => c.Id == "damper:rear-left" && c.FloatValue == .5f);
        Assert.Contains(damage.RawComponents, c => c.Id == "spring:rear-right" && c.FloatValue == 1f);
        Assert.Equal(2, damage.RawComponents.Single(c => c.Id == "tyre:front-right").IntegerValue);
    }

    [Fact]
    public void UnknownTyreStateAndInvalidWheelValueAreUnavailableNotHealthy()
    {
        Put(Owner + 0x120, new byte[] { 9, 0, 0, 0 });
        Put(Owner + 8 + 0x94, BitConverter.GetBytes(float.NaN));
        var damage = Dr2CarDamage.Read(Read, Physics)!;
        Assert.False(damage.Dashboard.RearLeftTyreAvailable);
        Assert.False(damage.Dashboard.FrontLeftWheelAvailable);
        Assert.True(damage.Dashboard.FrontRightWheelAvailable);
        Assert.True(damage.Dashboard.EngineAvailable);
    }

    private void Put(ulong address, byte[] data)
    {
        for (var i = 0; i < data.Length; i++) memory[address + (ulong)i] = data[i];
    }
    private byte[] Read(ulong address, int count) =>
        Enumerable.Range(0, count).Select(i => memory[address + (ulong)i]).ToArray();
}
