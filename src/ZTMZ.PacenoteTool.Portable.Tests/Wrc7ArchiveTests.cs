using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7ArchiveTests
{
    [Fact]
    public void ReadsPlainV2AndUsesNewestPatch()
    {
        InDirectory(root =>
        {
            Write(root, "CHUNK_0_PART0000.PKG", "COMMON\\CARS.CFG", "base");
            Write(root, "CHUNK_0_PATCH0001.PKG", "COMMON\\CARS.CFG", "patch1");
            Write(root, "CHUNK_0_PATCH0003.PKG", "COMMON\\CARS.CFG", "patch3");
            var data = WrcGenerationsArchive.ReadResources(root,
                new HashSet<string> { "CARS.CFG" }, legacyWrc7: true);
            Assert.Equal("patch3", Encoding.UTF8.GetString(data["CARS.CFG"]));
        });
    }

    [Fact]
    public void FullResourcePathsDistinguishStagesWithIdenticalFilenames()
    {
        InDirectory(root =>
        {
            Write(root, "CHUNK_0_PART0000.PKG", "PACENOTES\\MC\\SS2.CFG", "monaco");
            Write(root, "CHUNK_1_PART0000.PKG", "PACENOTES\\FI\\SS2.CFG", "finland");
            var data = WrcGenerationsArchive.ReadResources(root,
                new HashSet<string> { "PACENOTES\\MC\\SS2.CFG", "PACENOTES\\FI\\SS2.CFG" }, legacyWrc7: true);
            Assert.Equal("monaco", Encoding.UTF8.GetString(data["PACENOTES\\MC\\SS2.CFG"]));
            Assert.Equal("finland", Encoding.UTF8.GetString(data["PACENOTES\\FI\\SS2.CFG"]));
        });
    }

    [Fact]
    public void RejectsTruncatedPayload()
    {
        InDirectory(root =>
        {
            var path = Write(root, "CHUNK_0_PART0000.PKG", "CARS.CFG", "base");
            using (var stream = File.OpenWrite(path)) stream.SetLength(stream.Length - 1);
            Assert.Throws<InvalidDataException>(() => WrcGenerationsArchive.ReadResources(root,
                new HashSet<string> { "CARS.CFG" }, legacyWrc7: true));
        });
    }

    private static void InDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "ztmz-wrc7-archive-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "WIN32", "PKG"));
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Write(string root, string file, string name, string text)
    {
        var path = Path.Combine(root, "WIN32", "PKG", file);
        var key = Encoding.UTF8.GetBytes(name);
        var bytes = Encoding.UTF8.GetBytes(text);
        var indexSize = 4 + key.Length + 80;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("PPKG"));
        writer.Write(2u);
        writer.Write(0u);
        writer.Write((uint)indexSize);
        writer.Write(new byte[32]);
        writer.Write((uint)key.Length);
        writer.Write(key);
        writer.Write(0u);
        writer.Write((ulong)(48 + indexSize));
        writer.Write(0UL);
        writer.Write((ulong)bytes.Length);
        writer.Write((ulong)bytes.Length);
        writer.Write(0u);
        writer.Write(new byte[40]);
        writer.Write(Encoding.ASCII.GetBytes("PKGB"));
        writer.Write(bytes);
        writer.Write(Encoding.ASCII.GetBytes("PKGE"));
        return path;
    }
}
