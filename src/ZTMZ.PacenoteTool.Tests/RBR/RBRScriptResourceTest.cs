using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public sealed class RBRScriptResourceTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ztmz-rbr-def-" + Guid.NewGuid().ToString("N"));

    public RBRScriptResourceTest() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing.zdb")]
    [InlineData("missing-parent/missing.zdb")]
    public async Task MissingDefinitionDoesNotThrowOrCreateAFile(string? relativePath)
    {
        var path = string.IsNullOrWhiteSpace(relativePath) ? relativePath : Path.Combine(_root, relativePath);
        var resource = new RBRScriptResource();

        Assert.False(await resource.LoadData(path));

        AssertUnavailable(resource);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("not-sqlite")]
    [InlineData("no-tables")]
    [InlineData("missing-last-table")]
    [InlineData("empty-mapping")]
    public async Task InvalidDefinitionClearsPreviouslyLoadedMappings(string failure)
    {
        var valid = CreateDatabase("valid.zdb");
        var resource = new RBRScriptResource();
        Assert.True(await resource.LoadData(valid));
        Assert.NotEmpty(resource.PacenoteId2ZTMZIds);
        var invalid = Path.Combine(_root, "invalid.zdb");
        if (failure == "not-sqlite")
            File.WriteAllText(invalid, "not a SQLite database");
        else
        {
            CreateDatabase("invalid.zdb");
            using var connection = OpenDatabase(invalid);
            connection.Execute(failure switch
            {
                "no-tables" => "DROP TABLE pacenote; DROP TABLE pacenote_ztmz; DROP TABLE modifier_ztmz;",
                "missing-last-table" => "DROP TABLE modifier_ztmz;",
                _ => "DELETE FROM pacenote_ztmz;"
            });
        }

        Assert.False(await resource.LoadData(invalid));
        AssertUnavailable(resource);
        // A failed read must release the database, even after some tables succeeded.
        using var exclusive = new FileStream(invalid, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task UnreadableFileDoesNotCrashAndCanBeRetried()
    {
        var path = CreateDatabase("locked.zdb");
        var resource = new RBRScriptResource();
        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(await resource.LoadData(path));
            AssertUnavailable(resource);
        }
        Assert.True(await resource.LoadData(path));
    }

    [Fact]
    public async Task MissingReplacementClearsOldMappingAndRestoredFileLoads()
    {
        var resource = new RBRScriptResource();
        Assert.True(await resource.LoadData(CreateDatabase("old.zdb")));
        var missing = Path.Combine(_root, "restored.zdb");

        Assert.False(await resource.LoadData(missing));
        AssertUnavailable(resource);
        CreateDatabase("restored.zdb");
        Assert.True(await resource.LoadData(missing));
        Assert.Equal(missing, resource.DBPath);
        Assert.Equal(new[] { 12, 13 }, resource.PacenoteId2ZTMZIds[7]);
    }

    [Fact]
    public async Task ReadOnlyDefinitionWithSemicolonInPathLoadsAndReleasesFile()
    {
        var path = CreateDatabase("自定义; pacenotes.zdb");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var resource = new RBRScriptResource();
            Assert.True(await resource.LoadData(path));
            Assert.Equal(new[] { 12, 13 }, resource.PacenoteId2ZTMZIds[7]);
            Assert.Equal(new[] { 99 }, resource.ModiferId2ZTMZids[8]);
            Assert.Equal(string.Empty, Assert.Single(resource.Pacenotes).description);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    private string CreateDatabase(string name)
    {
        var path = Path.Combine(_root, name);
        using var connection = OpenDatabase(path);
        connection.Execute("""
            CREATE TABLE pacenote (id INTEGER, name TEXT, description TEXT, category TEXT);
            CREATE TABLE pacenote_ztmz (id INTEGER, ztmz_id INTEGER, order_id INTEGER);
            CREATE TABLE modifier_ztmz (id INTEGER, ztmz_id INTEGER, order_id INTEGER);
            INSERT INTO pacenote VALUES (7, 'test', NULL, NULL);
            INSERT INTO pacenote_ztmz VALUES (7, 13, 2), (7, 12, 1);
            INSERT INTO modifier_ztmz VALUES (8, 99, NULL);
            """);
        return path;
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void AssertUnavailable(RBRScriptResource resource)
    {
        Assert.Null(resource.DBPath);
        Assert.Empty(resource.Pacenotes);
        Assert.Empty(resource.Pacenote2ZTMZs);
        Assert.Empty(resource.Modifier2ZTMZs);
        Assert.Empty(resource.PacenotesDict);
        Assert.Empty(resource.PacenoteId2ZTMZIds);
        Assert.Empty(resource.ModiferId2ZTMZids);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
