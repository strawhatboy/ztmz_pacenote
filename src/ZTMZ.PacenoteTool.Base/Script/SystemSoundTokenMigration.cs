using System;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZTMZ.PacenoteTool.Base.Script;

/// <summary>
/// Keeps optional system-call tokens available in existing user databases.
/// New voice packages can provide the dedicated recording names; older packages
/// fall back to the established caution token.
/// </summary>
public static class SystemSoundTokenMigration
{
    private const string FallbackToken = "caution";

    private static readonly SystemSoundTokenDefinition[] Definitions =
    {
        new(Constants.SYSTEM_RADIATOR_DAMAGE, "radiator_damage", "散热器损坏"),
        new(Constants.SYSTEM_WATER_PUMP_DAMAGE, "water_pump_damage", "水泵损坏"),
        new(Constants.SYSTEM_ENGINE_DAMAGE, "engine_damage", "发动机损坏")
    };

    private static readonly string[] ExistingTokensRequiringFallback =
    {
        Constants.SYSTEM_COLLISION_SLIGHT,
        Constants.SYSTEM_COLLISION_MEDIUM,
        Constants.SYSTEM_COLLISION_SEVERE
    };

    public static void Ensure(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var transaction = connection.BeginTransaction();
        var fallbackId = connection.QueryFirstOrDefault<int?>(
            "SELECT id FROM pacenote_filenames WHERE filename = @filename LIMIT 1",
            new { filename = FallbackToken },
            transaction);

        if (fallbackId is not null)
        {
            foreach (var systemToken in ExistingTokensRequiringFallback)
            {
                var tokenId = connection.QueryFirstOrDefault<int?>(
                    "SELECT id FROM pacenote_filenames WHERE filename = @filename LIMIT 1",
                    new { filename = systemToken },
                    transaction);
                if (tokenId is not null)
                {
                    EnsureFallbackIfMissing(connection, transaction, tokenId.Value, fallbackId.Value);
                }
            }
        }

        foreach (var definition in Definitions)
        {
            var tokenId = connection.QueryFirstOrDefault<int?>(
                "SELECT id FROM pacenote_filenames WHERE filename = @filename LIMIT 1",
                new { filename = definition.SystemToken },
                transaction);

            if (tokenId is null)
            {
                tokenId = connection.ExecuteScalar<int>(
                    "SELECT COALESCE(MAX(id), 0) + 1 FROM pacenote",
                    transaction: transaction);
                connection.Execute(
                    "INSERT INTO pacenote (id, description, type, complexity) VALUES (@id, @description, @type, @complexity)",
                    new
                    {
                        id = tokenId.Value,
                        description = definition.Description,
                        type = (int)ScriptResourceTypes.MISC,
                        complexity = (int)ScriptResourceComplexities.MISC
                    },
                    transaction);
            }

            EnsureFilename(connection, transaction, tokenId.Value, definition.SystemToken, isPrimary: true);
            EnsureFilename(connection, transaction, tokenId.Value, definition.RecordingToken, isPrimary: false);
            if (fallbackId is not null)
            {
                EnsureFallback(connection, transaction, tokenId.Value, fallbackId.Value);
            }
        }

        transaction.Commit();
    }

    private static void EnsureFilename(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        string filename,
        bool isPrimary)
    {
        connection.Execute(
            """
            INSERT INTO pacenote_filenames (id, filename, is_primary)
            SELECT @id, @filename, @isPrimary
            WHERE NOT EXISTS (
                SELECT 1 FROM pacenote_filenames WHERE id = @id AND filename = @filename
            )
            """,
            new { id, filename, isPrimary = isPrimary ? 1 : 0 },
            transaction);
    }

    private static void EnsureFallback(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        int fallbackId)
    {
        connection.Execute(
            """
            INSERT INTO pacenote_fallbacks (id, fallback_id, order_id)
            SELECT @id, @fallbackId, 0
            WHERE NOT EXISTS (
                SELECT 1 FROM pacenote_fallbacks WHERE id = @id AND fallback_id = @fallbackId
            )
            """,
            new { id, fallbackId },
            transaction);
    }

    private static void EnsureFallbackIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        int fallbackId)
    {
        connection.Execute(
            """
            INSERT INTO pacenote_fallbacks (id, fallback_id, order_id)
            SELECT @id, @fallbackId, 0
            WHERE NOT EXISTS (
                SELECT 1 FROM pacenote_fallbacks WHERE id = @id
            )
            """,
            new { id, fallbackId },
            transaction);
    }

    private sealed record SystemSoundTokenDefinition(
        string SystemToken,
        string RecordingToken,
        string Description);
}
