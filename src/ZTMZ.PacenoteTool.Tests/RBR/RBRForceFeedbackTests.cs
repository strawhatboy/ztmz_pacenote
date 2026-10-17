using System.Reflection;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRForceFeedbackTests
{
    private sealed class Memory
    {
        public readonly Dictionary<uint, byte> Bytes = new();
        public Memory()
        {
            Put(0x4c5f90, RBRForceFeedbackReader.GetterCode);
            UInt(0x7eac48, 0x100000); UInt(0x100728, 1);
            UInt(0x8951d0, 0x200000); UInt(0x200004, 0x300000);
            Put(0x300000, new byte[0x24]); Float(0x300020, -.40652299f);
            Float(0x1660ed8, 1); UInt(0x1660edc, 1);
        }
        public void Put(uint address, byte[] values) { for (var i = 0; i < values.Length; i++) Bytes[address + (uint)i] = values[i]; }
        public void UInt(uint address, uint value) => Put(address, BitConverter.GetBytes(value));
        public void Float(uint address, float value) => Put(address, BitConverter.GetBytes(value));
        public byte[]? Read(uint address, int size)
        {
            var result = new byte[size];
            for (var i = 0; i < size; i++) if (!Bytes.TryGetValue(address + (uint)i, out result[i])) return null;
            return result;
        }
        public ForceFeedbackTelemetrySnapshot? Capture() => new RBRForceFeedbackReader(Read).Read();
    }

    [Theory]
    [InlineData(-.40652299f, 1f, 1, -.40652299f)]
    [InlineData(.13541484f, 1f, 1, .13541484f)]
    [InlineData(0f, 1f, 1, 0f)]
    [InlineData(.5f, .5f, 0, -.25f)]
    [InlineData(-1f, 1f, 1, -1f)]
    [InlineData(1f, 1f, 1, 1f)]
    public void UsesProcessedSignalAndNativeScaleAndInversion(float input, float scale, uint invert, float expected)
    {
        var memory = new Memory(); memory.Float(0x300020, input); memory.Float(0x1660ed8, scale); memory.UInt(0x1660edc, invert);
        var snapshot = memory.Capture();
        Assert.NotNull(snapshot); Assert.Equal("RBR", snapshot.Source); Assert.Equal(expected, snapshot.OutputFraction);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void HidesStaleForceOutsideDriving(uint state)
    {
        var memory = new Memory(); memory.UInt(0x100728, state); Assert.Null(memory.Capture());
        memory.UInt(0x100728, 16); Assert.NotNull(memory.Capture());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(uint.MaxValue)]
    public void DoesNotPublishCalibrationOrInactiveModes(uint mode)
    {
        var memory = new Memory(); memory.UInt(0x300000, mode); Assert.Null(memory.Capture());
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    [InlineData(-1.01f)] [InlineData(1.01f)]
    public void InvalidSignalIsUnavailable(float value)
    {
        var memory = new Memory(); memory.Float(0x300020, value); Assert.Null(memory.Capture());
    }

    [Fact]
    public void InvalidScaleOrOutOfRangeScaledOutputIsNotClampedOrShownAsZero()
    {
        var memory = new Memory(); memory.Float(0x1660ed8, float.NaN); Assert.Null(memory.Capture());
        memory.Float(0x1660ed8, 10); Assert.Null(memory.Capture());
    }

    [Fact]
    public void FailedReadsNullPointersAndModifiedGetterAreUnavailable()
    {
        foreach (uint address in new uint[] { 0x4c5f90, 0x7eac48, 0x100728, 0x8951d0, 0x200004, 0x300020, 0x1660ed8, 0x1660edc })
        {
            var memory = new Memory(); memory.Bytes.Remove(address); Assert.Null(memory.Capture());
        }
        var nullPointer = new Memory(); nullPointer.UInt(0x8951d0, 0); Assert.Null(nullPointer.Capture());
        var hooked = new Memory(); hooked.Bytes[0x4c5f90] = 0xe9; Assert.Null(hooked.Capture());
        var partial = new RBRForceFeedbackReader((_, _) => new byte[1]); Assert.Null(partial.Read());
    }

    [Fact]
    public void RejectsStageChangeDuringReadAndUsesNewControllerAfterRestart()
    {
        var memory = new Memory(); var reads = 0;
        var reader = new RBRForceFeedbackReader((address, size) =>
        {
            if (address == 0x100728 && ++reads == 2) memory.UInt(address, 2);
            return memory.Read(address, size);
        });
        Assert.Null(reader.Read());
        memory.UInt(0x100728, 1); memory.UInt(0x8951d0, 0x400000); memory.UInt(0x400004, 0x500000);
        memory.Put(0x500000, new byte[0x24]); memory.Float(0x500020, .2f);
        Assert.Equal(.2f, memory.Capture()!.OutputFraction);
    }

    [Fact]
    public void ProductionProjectionClearsUnavailableReadingsAndDisconnect()
    {
        using var reader = new RBRGameDataReader();
        var project = typeof(RBRGameDataReader).GetMethod("GetGameDataFromMemory", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var force = new Memory().Capture();
        var valid = (GameData)project.Invoke(reader, [default(GameData), new RBRMemData { ForceFeedback = force }])!;
        Assert.Same(force, valid.ForceFeedback);
        var missing = (GameData)project.Invoke(reader, [valid, default(RBRMemData)])!;
        Assert.Null(missing.ForceFeedback);
        reader.CurrentGameData = valid; reader.Uninitialize(null!); Assert.Null(reader.CurrentGameData.ForceFeedback);
    }
}
