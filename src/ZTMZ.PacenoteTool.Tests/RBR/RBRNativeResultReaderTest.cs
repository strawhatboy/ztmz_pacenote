using Xunit;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRNativeResultReaderTest
{
    [Fact]
    public void UnknownExecutableDisablesNativeReaderBeforeReadingMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Null(RBRNativeResultReader.Create(process, IntPtr.Zero));
    }

    private sealed class Image
    {
        public readonly Dictionary<uint, byte> Data = new();
        public Image()
        {
            Set(0x7eac48, 0x200000); Set(0x165fc68, 0x300000);
            Set(0x165fa90, 0x400000); Set(0x165fc48, 0x500000);
            Set(0x7eac44, 0x500000); Set(0x1660804, 505);
            Fill(0x200724, 8); Set(0x200724, 2); Set(0x200728, 9);
            Set(0x400000, 1); Set(0x400c98, 0x600000); Set(0x61785c, 1);
            Fill(0x500000, 0xcc); Set(0x5000c8, 0x700000);
            Set(0x500008, 0x700000); Set(0x50000c, 0x700000);
            Fill(0x300000, 0x2c8); Set(0x300144, 1);
            Set(0x300140, BitConverter.SingleToInt32Bits(44.582764f));
            Set(0x300244, BitConverter.SingleToInt32Bits(-44.689938f));
        }
        private void Fill(uint address, int count) { for (uint i = 0; i < count; i++) Data[address + i] = 0; }
        public void Set(uint address, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            for (uint i = 0; i < 4; i++) Data[address + i] = bytes[i];
        }
        public byte[]? Read(uint address, int count)
        {
            var bytes = new byte[count];
            for (uint i = 0; i < count; i++)
                if (!Data.TryGetValue(address + i, out bytes[i])) return null;
            return bytes;
        }
    }

    [Fact]
    public void DecodesCurrentFailureDuringDeparture()
    {
        var image = new Image();
        var result = new RBRNativeResultReader(0x400000, image.Read).Read();
        Assert.NotNull(result);
        Assert.True(result.Value.Failed);
        Assert.Equal(505, result.Value.MapId);
        Assert.Equal(44.582764f, result.Value.RaceTime);
        Assert.Equal(0, result.Value.FinishLinePassed);
    }

    [Fact]
    public void QueuedFailurePageIsNotCurrentFailure()
    {
        var image = new Image();
        image.Set(0x500008, 0x710000);
        image.Set(0x50000c, 0x710000);
        image.Set(0x50001c, 0x700000);
        Assert.False(new RBRNativeResultReader(0x400000, image.Read).Read()!.Value.Failed);
    }

    [Fact]
    public void UnreadableDataIsUnavailableInsteadOfZeroResult()
    {
        var image = new Image();
        image.Data.Remove(0x300140);
        Assert.Null(new RBRNativeResultReader(0x400000, image.Read).Read());
    }

    [Fact]
    public void TornStateIsRejected()
    {
        var image = new Image();
        var stateReads = 0;
        byte[]? Read(uint address, int count)
        {
            if (address == 0x200724 && ++stateReads == 2) image.Set(0x200728, 3);
            return image.Read(address, count);
        }
        Assert.Null(new RBRNativeResultReader(0x400000, Read).Read());
    }
}
