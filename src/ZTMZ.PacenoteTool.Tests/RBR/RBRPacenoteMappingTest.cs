using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRPacenoteMappingTest
{
    [Fact]
    public void MissingConfiguredDefinitionDoesNotAbortGameInitializationAndCanRecover()
    {
        var game = new ZTMZ.PacenoteTool.RBR.RBR();
        var configurations = game.DefaultGameConfigurations;
        var common = (ZTMZ.PacenoteTool.Base.Game.CommonGameConfigs)
            configurations[ZTMZ.PacenoteTool.Base.Game.CommonGameConfigs.Name];
        var settingIndex = common.PropertyName.Keys.ToList().IndexOf(
            "game.rbr.additional_settings.additional_pacenote_def");
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.zdb");
        common.PropertyValue[settingIndex] = missing;

        game.GameConfigurations = configurations;

        Assert.Equal(missing, common.PropertyValue[settingIndex]);
        Assert.Null(RBRScriptResource.Instance.DBPath);
        var warnings = new List<int>();
        game.onCustomMessage += (level, _) => warnings.Add(level);
        Assert.False(RBRGamePacenoteReader.EnsurePacenoteDefinition(game, missing));
        Assert.Equal(new[] { 2 }, warnings);
        Assert.False(File.Exists(missing));

        common.PropertyValue[settingIndex] = GetRbrDatabasePath("jannemod_v3.zdb");
        game.GameConfigurations = configurations;
        Assert.True(RBRGamePacenoteReader.EnsurePacenoteDefinition(game,
            GetRbrDatabasePath("jannemod_v3.zdb")));
        Assert.NotEmpty(RBRScriptResource.Instance.PacenoteId2ZTMZIds);
        Assert.Single(warnings);
    }

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

        Assert.All(RBRScriptResource.Instance.Pacenotes, pacenote =>
        {
            Assert.NotNull(pacenote.name);
            Assert.NotNull(pacenote.description);
            Assert.NotNull(pacenote.category);
        });

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

    [Theory]
    [InlineData("default.zdb")]
    [InlineData("jannemod_v3.zdb")]
    public void RbrDatabasesDoNotContainAccidentalDuplicateMappings(string databaseName)
    {
        using var connection = OpenReadOnly(GetRbrDatabasePath(databaseName));

        Assert.Equal(0, connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM (
                SELECT id, ztmz_id, order_id
                FROM pacenote_ztmz
                GROUP BY id, ztmz_id, order_id
                HAVING COUNT(*) > 1
            )
            """));
        foreach (var sourceId in new[] { 243, 302 })
        {
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pacenote_ztmz WHERE id = @sourceId",
                new { sourceId }));
        }
    }

    [Fact]
    public void DefaultRbrDatabaseMapsKnownIncorrectCallsCorrectly()
    {
        using var connection = OpenReadOnly(GetRbrDatabasePath("default.zdb"));

        Assert.Equal(
            new[] { 180 },
            connection.Query<int>(
                "SELECT ztmz_id FROM pacenote_ztmz WHERE id = 2156 ORDER BY order_id").ToArray());
        Assert.Equal(
            new[] { 62, 166 },
            connection.Query<int>(
                "SELECT ztmz_id FROM pacenote_ztmz WHERE id = 2190 ORDER BY order_id").ToArray());
    }

    [Fact]
    public void WallPositionFallbacksUseWallRatherThanTree()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var connection = OpenReadOnly(Path.Combine(
            repositoryRoot,
            "src",
            "ZTMZ.PacenoteTool.Base",
            "data.zdb"));

        Assert.Equal(
            new[] { 113, 252 },
            connection.Query<int>(
                "SELECT fallback_id FROM pacenote_fallbacks WHERE id = 253 ORDER BY order_id").ToArray());
        Assert.Equal(
            new[] { 153, 252 },
            connection.Query<int>(
                "SELECT fallback_id FROM pacenote_fallbacks WHERE id = 254 ORDER BY order_id").ToArray());
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

    private static string GetRbrDatabasePath(string databaseName)
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ZTMZ.PacenoteTool.RBR",
            databaseName);
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
