using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public sealed class RBRLoadedPacenoteTest
{
    private const string Title = "Zloty Las - Witoszow LuppisV3_1";
    private const string PathName = @"L:\Richard Burns Rally\Plugins\NGPCarMenu\MyPacenotes\Zloty Las - Witoszow\" + Title + ".ini";

    private sealed class Image
    {
        public const uint Rsf = 0x50000000;
        public readonly Dictionary<uint, byte> Data = new();
        public Image()
        {
            Set(0x7eac48, 0x200000); Set(0x200724, 0); Set(0x200728, 2);
            Set(0x1660804, 505); Set(Rsf + 0x3c20fc, 505);
            Set(0x7eaba8, 0x300000); Set(0x300010, 0x400000);
            Set(0x400020, 98); Set(0x400024, 0x600000);
            Put(Rsf + 0x3c261d, [1]);
            Put(Rsf + 0x3c27a0, Enumerable.Range(0, 92).Select(i => (byte)i).ToArray());
            String(Rsf + 0x3c2564, 0x700000, PathName);
            String(Rsf + 0x3c2574, 0x710000, Title);
        }
        public void String(uint address, uint buffer, string value)
        {
            var data = Encoding.UTF8.GetBytes(value);
            Set(address, Rsf + 0x2a594c); Set(address + 4, buffer);
            Set(address + 8, (uint)data.Length); Set(address + 12, (uint)data.Length);
            Put(buffer, data.Select((b, i) => (byte)(b ^ (i % 92))).ToArray());
        }
        public void Set(uint address, uint value) => Put(address, BitConverter.GetBytes(value));
        public void Put(uint address, byte[] bytes) { for (uint i = 0; i < bytes.Length; i++) Data[address + i] = bytes[i]; }
        public byte[]? Read(uint address, int count)
        {
            var result = new byte[count];
            for (uint i = 0; i < count; i++) if (!Data.TryGetValue(address + i, out result[i])) return null;
            return result;
        }
        public RBRLoadedPacenote? Capture(int map = 505) => new RBRLoadedPacenoteReader(0x400000, Rsf, Read).Read(map);
    }

    [Fact]
    public void ReadsCurrentTitleAndLongPathUsingRepeatingKey()
    {
        var loaded = new Image().Capture();
        Assert.NotNull(loaded);
        Assert.Equal(Title, loaded.Title);
        Assert.Equal(PathName, loaded.FilePath);
        Assert.True(loaded.IsLuppis);
    }

    [Fact]
    public void UnsupportedProcessIsNotRead()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Null(RBRLoadedPacenoteReader.Create(process, IntPtr.Zero));
    }

    [Fact]
    public void UnicodeTitleAndPathArePreserved()
    {
        var image = new Image();
        const string title = "测试赛道 LuppisV3";
        const string path = @"L:\路书\" + title + ".ini";
        image.String(Image.Rsf + 0x3c2564, 0x700000, path);
        image.String(Image.Rsf + 0x3c2574, 0x710000, title);
        var loaded = image.Capture();
        Assert.NotNull(loaded);
        Assert.Equal(title, loaded.Title);
        Assert.Equal(path, loaded.FilePath);
    }

    [Theory]
    [InlineData(3)] [InlineData(5)] [InlineData(6)] [InlineData(9)] [InlineData(10)] [InlineData(12)]
    public void StaleTitleInMenusAndLoadingIsRejected(uint state)
    {
        var image = new Image(); image.Set(0x200728, state);
        Assert.Null(image.Capture());
    }

    [Theory]
    [InlineData(0x200724, 3)] // restart transition
    [InlineData(0x1660804, 151)] // new native stage
    [InlineData(Image.Rsf + 0x3c20fc, 151)] // RSF still on another stage
    [InlineData(0x400020, 0)] // no loaded pacenotes
    [InlineData(Image.Rsf + 0x3c2580, 0)] // title reset for new load
    [InlineData(Image.Rsf + 0x3c2580, 100000)] // corrupt length
    [InlineData(Image.Rsf + 0x3c2574, 0)] // wrong string layout
    public void IncompleteOrMismatchedContextIsRejected(uint address, uint value)
    {
        var image = new Image(); image.Set(address, value);
        Assert.Null(image.Capture());
    }

    [Fact]
    public void MissingMemoryDoesNotProduceDefaultSelection()
    {
        var image = new Image(); image.Data.Remove(0x710000);
        Assert.Null(image.Capture());
    }

    [Fact]
    public void InPlaceTitleReplacementDuringReadIsRejected()
    {
        var image = new Image(); int titleReads = 0;
        byte[]? Read(uint address, int count)
        {
            if (address == 0x710000 && ++titleReads == 2) image.Data[address] ^= 1;
            return image.Read(address, count);
        }
        Assert.Null(new RBRLoadedPacenoteReader(0x400000, Image.Rsf, Read).Read(505));
    }

    [Fact]
    public void TitleMustMatchActualLoadedFile()
    {
        var image = new Image(); image.String(Image.Rsf + 0x3c2574, 0x710000, "Previous LuppisV3");
        Assert.Null(image.Capture());
    }

    [Theory]
    [InlineData((int)RBRPacenoteSelectionStatus.Unknown)]
    [InlineData((int)RBRPacenoteSelectionStatus.DefaultSelected)]
    [InlineData((int)RBRPacenoteSelectionStatus.NotConfigured)]
    [InlineData((int)RBRPacenoteSelectionStatus.Disabled)]
    public void LoadedLuppisOverridesDiskMarkersAndReplacesStaleWarningWithSuccess(int status)
    {
        var disk = new RBRPacenoteSelection((RBRPacenoteSelectionStatus)status, @"L:\Notes\Stage_default.ini");
        var selected = RBRPacenoteSelectionResolver.PreferLoaded(disk, new Image().Capture(), 505);
        Assert.Equal(RBRPacenoteSelectionStatus.LoadedLuppis, selected.Status);
        Assert.Equal(PathName, selected.FilePath);
        Assert.True(selected.UseCustomDefinition);
        Assert.True(selected.ShowMessage);
        Assert.Equal(1, selected.MessageLevel);
        Assert.Equal(Title, selected.LoadedTitle);
    }

    [Theory]
    [InlineData("Stage luPPisV3", (int)RBRPacenoteSelectionStatus.LoadedLuppis, true, true)]
    [InlineData("Stage_DEFAULT", (int)RBRPacenoteSelectionStatus.LoadedDefault, false, true)]
    [InlineData("Stage MyNotes", (int)RBRPacenoteSelectionStatus.LoadedOther, true, true)]
    public void ClassifiesNamesWithoutCallingEveryNonLuppisBookDefault(string title, int status, bool custom, bool show)
    {
        var selected = RBRPacenoteSelectionResolver.PreferLoaded(new(RBRPacenoteSelectionStatus.Unknown),
            new(505, title, @"L:\Notes\" + title + ".ini"), 505);
        Assert.Equal((RBRPacenoteSelectionStatus)status, selected.Status);
        Assert.Equal(custom, selected.UseCustomDefinition);
        Assert.Equal(show, selected.ShowMessage);
    }

    [Fact]
    public void UnknownRuntimeOrAnotherStageKeepsDiskEvidence()
    {
        var disk = new RBRPacenoteSelection(RBRPacenoteSelectionStatus.Unknown);
        Assert.Same(disk, RBRPacenoteSelectionResolver.PreferLoaded(disk, null, 505));
        Assert.Same(disk, RBRPacenoteSelectionResolver.PreferLoaded(disk, new Image().Capture(), 151));
    }
}
