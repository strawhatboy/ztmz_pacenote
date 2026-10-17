using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class PacenoteDatabaseLoaderTest
{
    [Fact]
    public async Task CustomDatabaseAddsTokensWithoutOverridingOfficialDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var officialPath = Path.Combine(directory.Path, "official.zdb");
        var customPath = Path.Combine(directory.Path, "custom.zdb");
        CreateDatabase(officialPath, connection =>
        {
            InsertDefinitions(connection);
            InsertToken(connection, 1, "Official", "official", type: 14, complexity: 0);
            InsertToken(connection, 10, "Caution", "caution", type: 8, complexity: 0);
            InsertLocalization(connection, 1, "en-us", "Official label");
        });
        CreateDatabase(customPath, connection =>
        {
            InsertDefinitions(connection);
            InsertToken(connection, 50, "Official clone", "official", type: 14, complexity: 0);
            connection.Execute(
                "INSERT INTO pacenote_filenames(id, filename, is_primary) VALUES(50, 'rogue_official_alias', 0)");
            InsertToken(connection, 1, "Custom", "custom_token", type: 14, complexity: 0);
            InsertLocalization(connection, 50, "en-us", "Malicious override");
            InsertLocalization(connection, 50, "ja-jp", "公式ラベル");
            InsertLocalization(connection, 1, "en-us", "Custom label");
            InsertLocalization(connection, 1, "zh-cn", "自定义标记");
            connection.Execute("INSERT INTO pacenote_fallbacks(id, fallback_id, order_id) VALUES(1, 50, 0)");
            connection.Execute("INSERT INTO reverse_corners(id, replacement_id) VALUES(1, 50)");
        });

        await using var official = OpenDatabase(officialPath, SqliteOpenMode.ReadWrite);
        var data = await PacenoteDatabaseLoader.LoadAsync(official);
        var result = await PacenoteDatabaseLoader.MergeCustomAsync(
            data,
            customPath,
            NLog.LogManager.GetCurrentClassLogger());

        Assert.Equal(1, result.AddedTokens);
        Assert.Equal(3, result.AddedLocalizations);
        Assert.Equal(1, result.RemappedIds);
        Assert.Equal(0, result.SkippedRelations);
        var customFilename = Assert.Single(data.Filenames, item => item.filename == "custom_token");
        Assert.Equal(11, customFilename.id);
        Assert.DoesNotContain(data.Filenames, item => item.filename == "rogue_official_alias");
        Assert.Contains(data.Fallbacks, item => item.id == 11 && item.fallback_id == 1);
        Assert.Contains(data.ReverseCorners, item => item.id == 11 && item.replacement_id == 1);
        Assert.Contains(data.Localizations, item =>
            item.pacenote_id == 1 && item.locale == "en-us" && item.display_name == "Official label");
        Assert.DoesNotContain(data.Localizations, item => item.display_name == "Malicious override");
        Assert.Contains(data.Localizations, item =>
            item.pacenote_id == 1 && item.locale == "ja-jp" && item.display_name == "公式ラベル");
        Assert.Contains(data.Localizations, item =>
            item.pacenote_id == 11 && item.locale == "zh-cn" && item.display_name == "自定义标记");
    }

    [Fact]
    public async Task CustomDatabaseWithoutLocalizationTableRemainsCompatible()
    {
        using var directory = new TemporaryDirectory();
        var officialPath = Path.Combine(directory.Path, "official.zdb");
        var customPath = Path.Combine(directory.Path, "legacy-custom.zdb");
        CreateDatabase(officialPath, connection =>
        {
            InsertDefinitions(connection);
            InsertToken(connection, 1, "Official", "official", type: 14, complexity: 0);
        });
        CreateDatabase(customPath, connection =>
        {
            InsertDefinitions(connection);
            InsertToken(connection, 2, "Legacy custom", "legacy_custom", type: 14, complexity: 0);
        }, includeLocalizationTable: false);

        await using var official = OpenDatabase(officialPath, SqliteOpenMode.ReadWrite);
        var data = await PacenoteDatabaseLoader.LoadAsync(official);
        var result = await PacenoteDatabaseLoader.MergeCustomAsync(
            data,
            customPath,
            NLog.LogManager.GetCurrentClassLogger());

        Assert.Equal(1, result.AddedTokens);
        Assert.Equal(0, result.AddedLocalizations);
        Assert.Contains(data.Filenames, item => item.filename == "legacy_custom");
    }

    [Fact]
    public async Task CustomDatabaseWithMissingSchemaIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var customPath = Path.Combine(directory.Path, "invalid.zdb");
        await using (var connection = OpenDatabase(customPath, SqliteOpenMode.ReadWriteCreate))
        {
            connection.Execute("CREATE TABLE pacenote (id INTEGER PRIMARY KEY)");
        }

        var data = new PacenoteDatabaseData();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            PacenoteDatabaseLoader.MergeCustomAsync(
                data,
                customPath,
                NLog.LogManager.GetCurrentClassLogger()));
        Assert.Contains("missing required tables", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialDatabaseContainsMechanicalDamageTokensAndFallbacks()
    {
        var databasePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ZTMZ.PacenoteTool.Base",
            "data.zdb");
        using var connection = OpenDatabase(databasePath, SqliteOpenMode.ReadOnly);
        Assert.Equal("ok", connection.QuerySingle<string>("PRAGMA integrity_check"));
        foreach (var locale in new[]
                 {
                     "de-de", "en-us", "es-es", "fr-fr", "it-it",
                     "ja-jp", "ko-kr", "pt-br", "ru-ru", "zh-cn"
                 })
        {
            Assert.Equal(
                connection.Query<int>("SELECT id FROM pacenote ORDER BY id"),
                connection.Query<int>(
                    "SELECT pacenote_id FROM pacenote_localizations WHERE locale = @locale AND TRIM(display_name) <> '' ORDER BY pacenote_id",
                    new { locale }));
        }
        Assert.Equal("左六", connection.QuerySingle<string>(
            "SELECT display_name FROM pacenote_localizations WHERE pacenote_id = 10 AND locale = 'zh-cn'"));
        Assert.Equal("6 left", connection.QuerySingle<string>(
            "SELECT display_name FROM pacenote_localizations WHERE pacenote_id = 10 AND locale = 'en-us'"));
        Assert.Equal("gauche 6", ResolveOfficialDisplayName(connection, "6_left", "fr-fr"));
        Assert.Equal("nicht schneiden", ResolveOfficialDisplayName(connection, "dont_cut", "de-de"));
        Assert.Equal("カット禁止", ResolveOfficialDisplayName(connection, "dont_cut", "ja-jp"));
        Assert.Equal("컷 금지", ResolveOfficialDisplayName(connection, "dont_cut", "ko-kr"));
        Assert.Equal("fecha para 3", ResolveOfficialDisplayName(connection, "tightens_to_3", "pt-br"));
        Assert.Equal("через гребень", ResolveOfficialDisplayName(connection, "over_crest", "ru-ru"));
        var cautionId = connection.QuerySingle<int>(
            "SELECT id FROM pacenote_filenames WHERE filename = 'caution'");

        AssertOfficialToken(connection, 1011, "radiator_damage", Constants.SYSTEM_RADIATOR_DAMAGE, cautionId);
        AssertOfficialToken(connection, 1012, "water_pump_damage", Constants.SYSTEM_WATER_PUMP_DAMAGE, cautionId);
        AssertOfficialToken(connection, 1013, "engine_damage", Constants.SYSTEM_ENGINE_DAMAGE, cautionId);
        foreach (var collisionId in new[] { 1008, 1009, 1010 })
        {
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pacenote_fallbacks WHERE id = @id AND fallback_id = @cautionId",
                new { id = collisionId, cautionId }));
        }
    }

    [Fact]
    public void LocalizationMigrationSeedsLegacyDatabaseOnce()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "legacy.zdb");
        CreateDatabase(databasePath, connection =>
        {
            InsertDefinitions(connection);
            InsertToken(connection, 10, "左六", "6_left", type: 0, complexity: 0);
        }, includeLocalizationTable: false);

        using var connection = OpenDatabase(databasePath, SqliteOpenMode.ReadWrite);
        PacenoteLocalizationMigration.Ensure(connection);
        PacenoteLocalizationMigration.Ensure(connection);

        Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pacenote_localizations"));
        Assert.Equal("左六", connection.QuerySingle<string>(
            "SELECT display_name FROM pacenote_localizations WHERE pacenote_id = 10 AND locale = 'zh-cn'"));
        Assert.Equal("6 left", connection.QuerySingle<string>(
            "SELECT display_name FROM pacenote_localizations WHERE pacenote_id = 10 AND locale = 'en-us'"));
    }

    private static void AssertOfficialToken(
        SqliteConnection connection,
        int id,
        string recordingToken,
        string systemToken,
        int cautionId)
    {
        var token = connection.QuerySingle<(int type, int complexity)>(
            "SELECT type, complexity FROM pacenote WHERE id = @id",
            new { id });
        Assert.Equal((int)ScriptResourceTypes.SYSTEM, token.type);
        Assert.Equal((int)ScriptResourceComplexities.SIMPLE, token.complexity);
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT is_primary FROM pacenote_filenames WHERE id = @id AND filename = @recordingToken",
            new { id, recordingToken }));
        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT is_primary FROM pacenote_filenames WHERE id = @id AND filename = @systemToken",
            new { id, systemToken }));
        Assert.Equal(1, connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pacenote_fallbacks WHERE id = @id AND fallback_id = @cautionId",
            new { id, cautionId }));
    }

    private static string ResolveOfficialDisplayName(
        SqliteConnection connection,
        string token,
        string locale)
    {
        return connection.QuerySingle<string>(
            """
            SELECT localization.display_name
            FROM pacenote_localizations localization
            JOIN pacenote_filenames filename ON filename.id = localization.pacenote_id
            WHERE filename.filename = @token AND localization.locale = @locale
            """,
            new { token, locale });
    }

    private static void CreateDatabase(
        string path,
        Action<SqliteConnection> seed,
        bool includeLocalizationTable = true)
    {
        using var connection = OpenDatabase(path, SqliteOpenMode.ReadWriteCreate);
        connection.Execute("CREATE TABLE pacenote (id INTEGER PRIMARY KEY, description TEXT NOT NULL, type INTEGER NOT NULL, complexity INTEGER NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_complexity (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_type (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_filenames (id INTEGER NOT NULL, filename TEXT NOT NULL, is_primary INTEGER NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_fallbacks (id INTEGER NOT NULL, fallback_id INTEGER NOT NULL, order_id INTEGER NOT NULL)");
        connection.Execute("CREATE TABLE reverse_corners (id INTEGER NOT NULL, replacement_id INTEGER NOT NULL)");
        if (includeLocalizationTable)
        {
            connection.Execute("CREATE TABLE pacenote_localizations (pacenote_id INTEGER NOT NULL, locale TEXT NOT NULL COLLATE NOCASE, display_name TEXT NOT NULL, description TEXT, PRIMARY KEY (pacenote_id, locale))");
        }
        seed(connection);
    }

    private static void InsertLocalization(
        SqliteConnection connection,
        int pacenoteId,
        string locale,
        string displayName)
    {
        connection.Execute(
            "INSERT INTO pacenote_localizations(pacenote_id, locale, display_name) VALUES(@pacenoteId, @locale, @displayName)",
            new { pacenoteId, locale, displayName });
    }

    private static void InsertDefinitions(SqliteConnection connection)
    {
        connection.Execute("INSERT INTO pacenote_complexity(id, name) VALUES(0, 'simple')");
        connection.Execute("INSERT INTO pacenote_type(id, name) VALUES(8, 'CAUTIONS'), (14, 'SYSTEM')");
    }

    private static void InsertToken(
        SqliteConnection connection,
        int id,
        string description,
        string filename,
        int type,
        int complexity)
    {
        connection.Execute(
            "INSERT INTO pacenote(id, description, type, complexity) VALUES(@id, @description, @type, @complexity)",
            new { id, description, type, complexity });
        connection.Execute(
            "INSERT INTO pacenote_filenames(id, filename, is_primary) VALUES(@id, @filename, 1)",
            new { id, filename });
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ZTMZPacenoteDatabaseTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
