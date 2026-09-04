using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class GameModRepositoryClientTest
{
    [Fact]
    public void ParsesLocalizedRootPackageMetadata()
    {
        var json = """
        {
          "schemaVersion": 1,
          "games": [{
            "id": "rbr",
            "mods": [{
              "id": "codriver",
              "names": { "en-us": "Co-driver", "zh-cn": "中文领航" },
              "descriptions": { "en-us": "Audio", "zh-cn": "语音" },
              "author": "author",
              "version": "2026.8.27+1.069",
              "downloadUrl": "https://example.com/mod.zip",
              "releasePageUrl": "https://example.com/release",
              "sha256": "4818B77E1E7349B9267FA45B0ED9988485A93C19D64750DE37F926FF862146DB",
              "packageRootDirectory": "",
              "externalFolderName": "Luppis 中文语音",
              "archiveEntryCodePage": 936,
              "conflictGroup": "rbr-codriver",
              "credits": [
                {
                  "roles": { "en-us": "Programming", "zh-cn": "程序作者" },
                  "names": [ "Lw_Ziye" ]
                },
                {
                  "roles": { "en-us": "Translation", "zh-cn": "翻译" },
                  "names": [ "译者甲", "译者乙" ]
                }
              ]
            }]
          }]
        }
        """;

        var mod = Assert.Single(GameModRepositoryClient.Parse(Encoding.UTF8.GetBytes(json), "rbr"));

        Assert.Equal("中文领航", mod.GetName("zh-cn"));
        Assert.Equal("Co-driver", mod.GetName("fr-fr"));
        Assert.Equal("2026.8.27+1.069", mod.Version);
        Assert.Equal(string.Empty, mod.PackageRootDirectory);
        Assert.Equal(936, mod.ArchiveEntryCodePage);
        Assert.Equal("rbr-codriver", mod.ConflictGroup);
        Assert.Collection(
            mod.Credits!,
            credit =>
            {
                Assert.Equal("程序作者", credit.GetRole("zh-cn"));
                Assert.Equal("Programming", credit.GetRole("fr-fr"));
                Assert.Equal(["Lw_Ziye"], credit.Names);
            },
            credit =>
            {
                Assert.Equal("翻译", credit.GetRole("zh-cn"));
                Assert.Equal(["译者甲", "译者乙"], credit.Names);
            });
    }

    [Fact]
    public void RejectsUnsafePackagePaths()
    {
        var json = """
        {
          "schemaVersion": 1,
          "games": [{
            "id": "rbr",
            "mods": [{
              "id": "unsafe",
              "names": { "en-us": "Unsafe" },
              "descriptions": {},
              "author": "author",
              "version": "1.0",
              "downloadUrl": "https://example.com/mod.zip",
              "releasePageUrl": "https://example.com/release",
              "sha256": "4818B77E1E7349B9267FA45B0ED9988485A93C19D64750DE37F926FF862146DB",
              "packageRootDirectory": "../outside"
            }]
          }]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            GameModRepositoryClient.Parse(Encoding.UTF8.GetBytes(json), "rbr"));
    }
}
