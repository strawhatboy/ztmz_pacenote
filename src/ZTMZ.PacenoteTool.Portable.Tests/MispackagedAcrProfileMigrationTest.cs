using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class MispackagedAcrProfileMigrationTest
{
    [Fact]
    public void RemovesEntireDefaultAcrDirectoryOnlyAndCanRunAgain()
    {
        var root = Directory.CreateTempSubdirectory("ztmz-acr-cleanup-").FullName;
        try
        {
            var acr = Path.Combine(root, "profiles", "default", "Assetto Corsa Rally");
            Directory.CreateDirectory(Path.Combine(acr, "nested"));
            File.WriteAllText(Path.Combine(acr, "modified.pacenote"), "modified content");
            File.WriteAllText(Path.Combine(acr, "nested", "other.txt"), "anything");
            var other = Path.Combine(root, "profiles", "default", "Dirt Rally 2.0");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "keep.pacenote"), "keep");
            var custom = Path.Combine(root, "profiles", "custom", "Assetto Corsa Rally");
            Directory.CreateDirectory(custom);
            File.WriteAllText(Path.Combine(custom, "keep.pacenote"), "keep");

            MispackagedAcrProfileMigration.Remove(root);
            MispackagedAcrProfileMigration.Remove(root);

            Assert.False(Directory.Exists(acr));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(other, "keep.pacenote")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(custom, "keep.pacenote")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingProfilesAreAllowed()
    {
        var root = Directory.CreateTempSubdirectory("ztmz-acr-cleanup-").FullName;
        try { MispackagedAcrProfileMigration.Remove(root); }
        finally { Directory.Delete(root); }
    }
}
