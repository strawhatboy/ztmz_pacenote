using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.AC;

public class ACRBridgeCompatibilityTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void ChecksExportTableRatherThanStringPresence(bool exported, bool forwarded, bool expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            var image = new byte[1024];
            using var stream = new MemoryStream(image);
            using var writer = new BinaryWriter(stream);
            void U16(int offset, ushort value) { stream.Position = offset; writer.Write(value); }
            void U32(int offset, uint value) { stream.Position = offset; writer.Write(value); }
            U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x4550);
            U16(0x84, 0x8664); U16(0x86, 1); U16(0x94, 240); U16(0x96, 0x2022);
            U16(0x98, 0x20b); U32(0xb8, 0x1000); U32(0xbc, 0x200);
            U32(0xd0, 0x2000); U32(0xd4, 0x200); U32(0x104, 16);
            U32(0x108, 0x1000); U32(0x10c, 0x100);
            U32(0x190, 0x200); U32(0x194, 0x1000); U32(0x198, 0x200);
            U32(0x19c, 0x200); U32(0x1ac, 0x60000020);
            U32(0x214, 1); U32(0x218, exported ? 1u : 0u);
            U32(0x21c, 0x1040); U32(0x220, 0x1044); U32(0x224, 0x1048);
            U32(0x240, forwarded ? 0x1080u : 0x11f0u); U32(0x244, 0x1080);
            Encoding.ASCII.GetBytes(ACRBridgeCompatibility.RequiredExport).CopyTo(image, 0x280);
            File.WriteAllBytes(path, image);
            Assert.Equal(expected, ACRBridgeCompatibility.HasRequiredExport(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsInvalidOrMissingLibrary()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, ACRBridgeCompatibility.RequiredExport);
            Assert.False(ACRBridgeCompatibility.HasRequiredExport(path));
        }
        finally { File.Delete(path); }
        Assert.False(ACRBridgeCompatibility.HasRequiredExport(path));
    }
}
