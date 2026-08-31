using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRPacenoteMappingTest
{
    [Fact]
    public void JannemodEncodingCarriesCustomTypeAndModifiersInTheFlag()
    {
        var encoding = RBRGamePacenoteReader.DecodePacenoteEncoding(
            standardPacenoteType: 12,
            encodedFlags: 205537536);

        Assert.Equal(new RBRPacenoteEncoding(12, 392, 0x4100), encoding);
    }

    [Fact]
    public void RuntimeConsumedBitDoesNotChangeTheEncodedCustomType()
    {
        var encoding = RBRGamePacenoteReader.DecodePacenoteEncoding(
            standardPacenoteType: 12,
            encodedFlags: unchecked((int)0x8C404100));

        Assert.Equal(new RBRPacenoteEncoding(12, 392, 0x4100), encoding);
    }

    [Fact]
    public void StandardPacenoteWithoutCustomEncodingKeepsItsType()
    {
        var encoding = RBRGamePacenoteReader.DecodePacenoteEncoding(
            standardPacenoteType: 12,
            encodedFlags: 0x4100);

        Assert.Equal(new RBRPacenoteEncoding(12, 0, 0x4100), encoding);
    }

    [Fact]
    public async Task LeftEntryChicaneMapsToItsDedicatedZtmzTokenWithoutFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var rbrDatabasePath = Path.Combine(
            repositoryRoot,
            "src",
            "ZTMZ.PacenoteTool.RBR",
            "jannemod_v3.zdb");
        var ztmzDatabasePath = Path.Combine(
            repositoryRoot,
            "src",
            "ZTMZ.PacenoteTool.Base",
            "data.zdb");

        await RBRScriptResource.Instance.LoadData(rbrDatabasePath);

        var ztmzIds = Assert.Contains(392, RBRScriptResource.Instance.PacenoteId2ZTMZIds);
        var ztmzId = Assert.Single(ztmzIds);
        Assert.Equal(126, ztmzId);

        using var connection = OpenReadOnly(ztmzDatabasePath);
        var tokens = connection.Query<(string filename, bool is_primary)>(
                "SELECT filename, is_primary FROM pacenote_filenames WHERE id = @ztmzId",
                new { ztmzId })
            .ToList();

        Assert.Contains(tokens, token => token.filename == "left_entry_chicane");
        Assert.Contains(tokens, token => token.filename == "detail_left_entry_chicane");
        Assert.Equal("left_entry_chicane", Assert.Single(tokens, token => token.is_primary).filename);
        Assert.DoesNotContain(tokens, token => token.filename is "twisty" or "detail_twisty" or "kinks" or "detail_kinks");
        Assert.Equal(0, connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pacenote_fallbacks WHERE id = @ztmzId",
            new { ztmzId }));
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
