using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class SystemSoundTokenMigrationTest
{
    [Fact]
    public void Ensure_AddsDamageSystemTokensAndCautionFallbacksOnce()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "data.zdb");
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            CreateSchema(connection);
            connection.Execute(
                "INSERT INTO pacenote (id, description, type, complexity) VALUES (1, 'Caution', 8, 0)");
            connection.Execute(
                "INSERT INTO pacenote_filenames (id, filename, is_primary) VALUES (1, 'caution', 1)");
            connection.Execute(
                "INSERT INTO pacenote (id, description, type, complexity) VALUES (2, 'Slight collision', 13, -1)");
            connection.Execute(
                "INSERT INTO pacenote_filenames (id, filename, is_primary) VALUES (2, @filename, 1)",
                new { filename = Constants.SYSTEM_COLLISION_SLIGHT });

            SystemSoundTokenMigration.Ensure(connection);
            SystemSoundTokenMigration.Ensure(connection);

            AssertToken(connection, Constants.SYSTEM_RADIATOR_DAMAGE, "radiator_damage");
            AssertToken(connection, Constants.SYSTEM_WATER_PUMP_DAMAGE, "water_pump_damage");
            AssertToken(connection, Constants.SYSTEM_ENGINE_DAMAGE, "engine_damage");
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pacenote_fallbacks WHERE id = 2 AND fallback_id = 1"));
        }
    }

    private static void AssertToken(SqliteConnection connection, string systemToken, string recordingToken)
    {
        var id = connection.QuerySingle<int>(
            "SELECT id FROM pacenote_filenames WHERE filename = @filename",
            new { filename = systemToken });
        Assert.Equal(1, connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pacenote_filenames WHERE id = @id AND filename = @filename",
            new { id, filename = recordingToken }));
        Assert.Equal(1, connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pacenote_fallbacks WHERE id = @id AND fallback_id = 1",
            new { id }));
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        connection.Execute("CREATE TABLE pacenote (id INTEGER PRIMARY KEY, description TEXT NOT NULL, type INTEGER NOT NULL, complexity INTEGER NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_filenames (id INTEGER NOT NULL, filename TEXT NOT NULL, is_primary INTEGER NOT NULL)");
        connection.Execute("CREATE TABLE pacenote_fallbacks (id INTEGER NOT NULL, fallback_id INTEGER NOT NULL, order_id INTEGER NOT NULL)");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZTMZPacenoteSystemTokenTests", Guid.NewGuid().ToString("N"));
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
