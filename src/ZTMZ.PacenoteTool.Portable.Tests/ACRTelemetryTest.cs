using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Xunit;
using ZTMZ.PacenoteTool.AC;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ACRTelemetryTest
{
    [Theory]
    [InlineData("00:48.090", 48.09f)]
    [InlineData("12:34.567", 754.567f)]
    [InlineData("1:02:03.456", 3723.456f)]
    [InlineData("", 0)]
    [InlineData("00:60.000", 0)]
    [InlineData("00:NaN", 0)]
    [InlineData("-1:20", 0)]
    [InlineData("1.5:20", 0)]
    [InlineData("24:00:00", 0)]
    public void ReadsAcrFormattedClockWhenIntegerClockIsUnused(string text, float expected)
        => Assert.Equal(expected, ACRGameDataConverter.ReadLapTime(new() { CurrentLapTime = text }), 3);

    [Fact]
    public void IntegerClockTakesPrecedenceWhenAvailable()
        => Assert.Equal(12.345f, ACRGameDataConverter.ReadLapTime(new() { CurrentTimeMillis = 12345, CurrentLapTime = "00:01.000" }), 3);

    [Fact]
    public void CorrectsActualAcrLayoutUnitsAndSinglePlayerPosition()
    {
        var raw = new ACRGameRawData
        {
            GraphicsData = new() { CurrentLapTime = "00:48.090", TotalDistanceTraveled = 316,
                OpponentCarPositions = [new() { XCoord = -1047, YCoord = 477, ZCoord = 945 }] },
            StaticInfo = new() { TrackLengthMeters = 5600, TyreRadiusValues = [0, 0, 0, 0] },
            PhysicsData = new() { CurrentGear = 1, MaximumRPM = 7500, EngineRPM = 7000,
                WheelAngularVelocity = [10, 20, 30, 40], BrakeTemperatures = [273.15f, 283.15f, 288.15f, 373.15f] }
        };
        var vehicle = new ACRVehicleTelemetry(.75f, 6700, 7300) { MaxGears = 5, IdleRPM = 525, WheelRadii = [.324f, .324f, .324f, .324f] };
        var data = ACRGameDataConverter.Convert(raw, vehicle);
        Assert.Equal(48.09f, data.Time, 3);
        Assert.Equal(data.Time, data.LapTime);
        Assert.Equal(316f / 5600, data.CompletionRate, 5);
        Assert.Equal((-1047f, 477f, 945f), (data.PosX, data.PosY, data.PosZ));
        Assert.Equal(11.664f, data.SpeedFrontLeft, 3);
        Assert.Equal(46.656f, data.SpeedRearRight, 3);
        Assert.Equal(0, data.Gear);
        Assert.Equal(5, data.MaxGears);
        Assert.Equal(7500, data.MaxRPM);
        Assert.Equal(525, data.IdleRPM);
        Assert.Equal(15, data.BrakeTempRearLeft, 3);
        Assert.Equal(100, data.BrakeTempRearRight, 3);
        Assert.True(data.HandBrakeValid);
        Assert.Equal(.75f, data.HandBrake);
        Assert.True(data.ShiftLightsRPMValid);
        Assert.Equal(.5f, data.ShiftLightsFraction);
        Assert.NotEqual(default, data.TimeStamp);
        Assert.Equal(588, Marshal.OffsetOf<ACRPhysicsData>(nameof(ACRPhysicsData.MaximumRPM)).ToInt32());
        Assert.Equal(800, Marshal.SizeOf<ACRPhysicsData>());
    }

    [Fact]
    public void MissingSupplementDoesNotInventValuesOrCarryPreviousVehicle()
    {
        var raw = new ACRGameRawData { PhysicsData = new() { WheelAngularVelocity = [10, 20, 30, 40] } };
        var data = ACRGameDataConverter.Convert(raw);
        Assert.False(data.HandBrakeValid);
        Assert.False(data.ShiftLightsRPMValid);
        Assert.Equal(0, data.SpeedFrontLeft);
        Assert.Equal(0, data.MaxRPM);
        Assert.Equal(0, data.MaxGears);
        Assert.Equal(0, data.IdleRPM);
    }

    [Fact]
    public void HandlesShortArraysNonFiniteInputsAndProgressBeyondFinish()
    {
        var data = ACRGameDataConverter.Convert(new()
        {
            GraphicsData = new() { TotalDistanceTraveled = 101 }, StaticInfo = new() { TrackLengthMeters = 100 },
            PhysicsData = new() { ThrottleInput = float.NaN, BrakeInput = 2, SteeringAngle = float.PositiveInfinity,
                AccelerationG = [float.NaN], BrakeTemperatures = [], SuspensionTravelDistance = [float.NaN] }
        });
        Assert.Equal(1, data.CompletionRate);
        Assert.Equal(0, data.Throttle);
        Assert.Equal(1, data.Brake);
        Assert.Equal(0, data.Steering);
        Assert.Equal(0, data.G_lat);
        Assert.Equal(0, data.G_long);
        Assert.Equal(0, data.BrakeTempRearRight);
        Assert.Equal(0, data.SuspensionFrontLeft);
    }

    [Fact]
    public void ReadsOwnedVehicleAndDoesNotCountReverseOrNeutralAsForwardGears()
    {
        var fixture = new MemoryFixture();
        var data = ACRVehicleMemory.Read(fixture.Read, fixture.Image);
        Assert.NotNull(data);
        Assert.Equal(.6f, data.HandBrake);
        Assert.Equal(5, data.MaxGears);
        Assert.Equal(525, data.IdleRPM);
        Assert.Equal(7300, data.ShiftEnd);
        Assert.Equal(new[] { .324f, .324f, .324f, .324f }, data.WheelRadii);
    }

    [Fact]
    public void RejectsVehicleReplacementDuringRead()
    {
        var fixture = new MemoryFixture();
        var calls = 0;
        byte[] Read(ulong address, int count)
        {
            if (address == fixture.Controller + 0x2e8 && ++calls == 2)
                return BitConverter.GetBytes(fixture.Pawn + 0x1000);
            return fixture.Read(address, count);
        }
        Assert.Null(ACRVehicleMemory.Read(Read, fixture.Image));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsMenusAndAmbiguousPlayerSelection(int players)
    {
        var fixture = new MemoryFixture();
        fixture.Put(fixture.Instance + 0x40, players);
        Assert.Null(ACRVehicleMemory.Read(fixture.Read, fixture.Image));
    }

    [Fact]
    public void RejectsEngineFromAnotherVehicle()
    {
        var fixture = new MemoryFixture();
        fixture.Put(fixture.Engine + 8, fixture.Simulation + 0x1000);
        Assert.Null(ACRVehicleMemory.Read(fixture.Read, fixture.Image));
    }

    [Fact]
    public void ForcedHandbrakeIsReportedEvenWithoutPhysicalInput()
    {
        var fixture = new MemoryFixture();
        fixture.Bytes[fixture.Simulation + 0x131d] = 1;
        Assert.Equal(1, ACRVehicleMemory.Read(fixture.Read, fixture.Image)!.HandBrake);
    }

    private sealed class MemoryFixture
    {
        public readonly Dictionary<ulong, byte> Bytes = [];
        public readonly ulong Image = 0x140000000, Instance = 0x200000, Controller = 0x500000,
            Pawn = 0x600000, Simulation = 0x700000, Engine = 0xa00000;
        public MemoryFixture()
        {
            Put(Image + ACRVehicleMemory.WorldRva, 0x100000UL);
            Put(0x100228, Instance); Put(Instance + 0x38, 0x300000UL); Put(Instance + 0x40, 1);
            Put(0x300000, 0x400000UL); Put(0x400030, Controller); Put(Controller + 0x2e8, Pawn);
            Put(Pawn + 0x580, Simulation); Put(Simulation, Image + 0x96fb538);
            Put(Pawn + 0x488, 0x800000UL); Put(Simulation + 0x1330, .6f);
            Put(0x8002f0, 6700f); Put(0x8002fc, 7300f);
            Put(Simulation + 0x28c0, 0x900000UL); Put(0x900000, Engine);
            Put(Engine + 8, Simulation); Put(Engine + 0xf0, Simulation); Put(Engine + 0x1c, 525f);
            Put(Engine + 0xf8, 0xb00000UL); Put(Engine + 0x100, 0xb0001cUL);
            float[] ratios = [-2.538f, 0, 3.167f, 2.133f, 1.571f, 1.25f, 1.043f];
            for (var i = 0; i < ratios.Length; i++) Put(0xb00000UL + (ulong)i * 4, ratios[i]);
            Put(Simulation + 0x15e8, 0xc00000UL); Put(0xc00000, 0xd00000UL);
            for (var i = 0; i < 4; i++)
            {
                var wheel = 0xe00000UL + (ulong)i * 0x1000;
                Put(0xd00000UL + (ulong)i * 8, wheel); Put(wheel + 8, Simulation);
                Put(wheel + 0x10, i); Put(wheel + 0x64, .324f);
            }
        }
        public void Put(ulong address, ulong value) => PutBytes(address, BitConverter.GetBytes(value));
        public void Put(ulong address, int value) => PutBytes(address, BitConverter.GetBytes(value));
        public void Put(ulong address, float value) => PutBytes(address, BitConverter.GetBytes(value));
        private void PutBytes(ulong address, byte[] bytes) { for (var i = 0; i < bytes.Length; i++) Bytes[address + (ulong)i] = bytes[i]; }
        public byte[] Read(ulong address, int count) => Enumerable.Range(0, count).Select(i => Bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
    }
}
