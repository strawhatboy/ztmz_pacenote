using System;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZTMZ.PacenoteTool.Base.Script;

/// <summary>
/// Adds the localization schema to legacy user databases. Official localization
/// data is shipped in data.zdb; these derived rows only preserve compatibility
/// when the executable is copied over an older installation without the database.
/// </summary>
public static class PacenoteLocalizationMigration
{
    public static void Ensure(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS pacenote_localizations (
                pacenote_id INTEGER NOT NULL,
                locale TEXT NOT NULL COLLATE NOCASE,
                display_name TEXT NOT NULL,
                description TEXT,
                PRIMARY KEY (pacenote_id, locale),
                FOREIGN KEY (pacenote_id) REFERENCES pacenote(id) ON DELETE CASCADE
            )
            """,
            transaction: transaction);
        connection.Execute(
            """
            INSERT OR IGNORE INTO pacenote_localizations(pacenote_id, locale, display_name, description)
            SELECT id, 'zh-cn', trim(description), description
            FROM pacenote
            WHERE trim(description) <> ''
            """,
            transaction: transaction);
        connection.Execute(
            """
            INSERT OR IGNORE INTO pacenote_localizations(pacenote_id, locale, display_name, description)
            SELECT id, 'en-us', replace(filename, '_', ' '), NULL
            FROM pacenote_filenames
            WHERE is_primary = 1 AND trim(filename) <> ''
            """,
            transaction: transaction);
        transaction.Commit();
    }
}
