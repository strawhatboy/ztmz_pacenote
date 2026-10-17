using System.IO;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2StartLightsMemoryTest
{
    private const ulong Image = 0x140000000, Services = 0x200000, Table = 0x210000, Lights = 0x220000;
    private readonly Dictionary<ulong, byte> memory = new();
    public Dr2StartLightsMemoryTest()
    {
        Put(Image + 0x16940e4, BitConverter.GetBytes(95));
        U64(Image + 0x1695150, Services);
        U64(Services + 8, Table);
        U64(Table + 95 * 8, Lights);
        Put(Lights, new byte[0x4f0]);
        U64(Lights, Image + 0x127ae38);
        U64(Lights + 0x10, Image + 0x127ae50);
        memory[Lights + 0xa5] = 7;
        for (ulong phase = 0; phase < 7; phase++)
            Put(Lights + 0x128 + phase * 0x88, BitConverter.GetBytes(phase == 0 ? 0 : phase == 6 ? 2 : 1));
    }

    [Fact]
    public void ReadsCapturedWaitingFiveLightsAndGo()
    {
        // The live capture held phase 0 for 26.9 seconds, then advanced 1..6.
        for (byte phase = 0; phase < 7; phase++)
        {
            memory[Lights + 0x20] = phase;
            var snapshot = Dr2StartLightsMemory.Read(Read, Image);
            Assert.Equal(new Dr2StartLightsSnapshot(Lights, phase), snapshot);
            Assert.Equal(6 - phase, snapshot!.Value.Number);
        }
    }

    [Theory]
    [InlineData(0xa5, 0)] // loading/unconfigured
    [InlineData(0xa5, 6)] // different start sequence
    [InlineData(0xa5, 255)]
    [InlineData(0x20, 7)]
    [InlineData(0x20, 255)]
    [InlineData(0x128, 1)] // no waiting phase
    [InlineData(0x1b0, 2)] // not a numbered rally light
    [InlineData(0x458, 1)] // no GO phase
    public void RejectsUnknownOrIncompleteLayouts(int offset, byte value)
    {
        memory[Lights + (ulong)offset] = value;
        Assert.Null(Dr2StartLightsMemory.Read(Read, Image));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x10)]
    public void RejectsWrongComponentType(int offset)
    {
        U64(Lights + (ulong)offset, Image + 0x127ae40);
        Assert.Null(Dr2StartLightsMemory.Read(Read, Image));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4096)]
    public void BoundsServiceIndexBeforeFollowingPointer(int index)
    {
        Put(Image + 0x16940e4, BitConverter.GetBytes(index));
        Assert.Null(Dr2StartLightsMemory.Read(Read, Image));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsChangesDuringSnapshot(int change)
    {
        byte[] ChangingRead(ulong address, int count)
        {
            var bytes = Read(address, count);
            if (address == Lights && count == 0x4f0)
            {
                if (change == 0) memory[Lights + 0x20] = 1;
                if (change == 1) memory[Lights + 0xa5] = 0;
                if (change == 2) U64(Table + 95 * 8, 0x230000);
                if (change == 3) Put(Image + 0x16940e4, BitConverter.GetBytes(96));
            }
            return bytes;
        }
        Assert.Null(Dr2StartLightsMemory.Read(ChangingRead, Image));
    }

    [Fact]
    public void InvalidPointerOrFailedReadCannotBecomeANumber()
    {
        U64(Services + 8, 0);
        Assert.Throws<IOException>(() => Dr2StartLightsMemory.Read(Read, Image));
        Assert.Throws<IOException>(() => Dr2StartLightsMemory.Read((_, _) => throw new IOException(), Image));
    }

    private void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
    private void Put(ulong address, byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++) memory[address + (ulong)i] = bytes[i];
    }
    private byte[] Read(ulong address, int count) => Enumerable.Range(0, count).Select(i => memory[address + (ulong)i]).ToArray();
}
