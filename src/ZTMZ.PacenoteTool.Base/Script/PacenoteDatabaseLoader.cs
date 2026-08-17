using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZTMZ.PacenoteTool.Base.Script;

internal sealed class PacenoteDatabaseData
{
    public List<ScriptResourcePacenote> Pacenotes { get; } = [];
    public List<ScriptResourceComplexity> Complexities { get; } = [];
    public List<ScriptResourceType> Types { get; } = [];
    public List<ScriptResourceFilenames> Filenames { get; } = [];
    public List<ScriptResourcePacenoteLocalization> Localizations { get; } = [];
    public List<ScriptResourceFallbacks> Fallbacks { get; } = [];
    public List<ScriptResourceReverseCorners> ReverseCorners { get; } = [];
}

internal sealed record PacenoteDatabaseMergeResult(
    int AddedTokens,
    int AddedLocalizations,
    int RemappedIds,
    int SkippedRelations);

internal static class PacenoteDatabaseLoader
{
    private static readonly string[] RequiredTables =
    [
        "pacenote",
        "pacenote_complexity",
        "pacenote_type",
        "pacenote_filenames",
        "pacenote_fallbacks",
        "reverse_corners"
    ];

    public static async Task<PacenoteDatabaseData> LoadAsync(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await ValidateSchemaAsync(connection);

        var data = new PacenoteDatabaseData();
        data.Pacenotes.AddRange(await connection.QueryAsync<ScriptResourcePacenote>("SELECT * FROM pacenote"));
        data.Complexities.AddRange(await connection.QueryAsync<ScriptResourceComplexity>("SELECT * FROM pacenote_complexity"));
        data.Types.AddRange(await connection.QueryAsync<ScriptResourceType>("SELECT * FROM pacenote_type"));
        data.Filenames.AddRange(await connection.QueryAsync<ScriptResourceFilenames>("SELECT * FROM pacenote_filenames"));
        if (await HasTableAsync(connection, "pacenote_localizations"))
        {
            data.Localizations.AddRange(await connection.QueryAsync<ScriptResourcePacenoteLocalization>(
                "SELECT pacenote_id, locale, display_name, description FROM pacenote_localizations"));
        }
        data.Fallbacks.AddRange(await connection.QueryAsync<ScriptResourceFallbacks>("SELECT * FROM pacenote_fallbacks"));
        data.ReverseCorners.AddRange(await connection.QueryAsync<ScriptResourceReverseCorners>("SELECT * FROM reverse_corners"));
        return data;
    }

    public static async Task<PacenoteDatabaseMergeResult> MergeCustomAsync(
        PacenoteDatabaseData target,
        string customDatabasePath,
        NLog.Logger logger)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(customDatabasePath);
        ArgumentNullException.ThrowIfNull(logger);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(customDatabasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var custom = await LoadAsync(connection);

        MergeNamedDefinitions(target.Complexities, custom.Complexities, item => item.id);
        MergeNamedDefinitions(target.Types, custom.Types, item => item.id);

        var validComplexities = target.Complexities.Select(item => item.id).ToHashSet();
        var validTypes = target.Types.Select(item => item.id).ToHashSet();
        var occupiedIds = target.Pacenotes.Select(item => item.id).ToHashSet();
        var filenameToId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var filename in target.Filenames)
        {
            filenameToId.TryAdd(filename.filename, filename.id);
        }

        var customFilenames = custom.Filenames
            .Where(item => !string.IsNullOrWhiteSpace(item.filename))
            .GroupBy(item => item.id)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(item => item.filename, StringComparer.Ordinal)
                    .Select(items => items.OrderByDescending(item => item.is_primary).First())
                    .ToList());
        var idMap = new Dictionary<int, int>();
        var addedCustomIds = new HashSet<int>();
        var nextId = occupiedIds.Count == 0 ? 1 : occupiedIds.Max() + 1;
        var remappedIds = 0;

        foreach (var pacenote in custom.Pacenotes.OrderBy(item => item.id))
        {
            if (!customFilenames.TryGetValue(pacenote.id, out var filenames) || filenames.Count == 0)
            {
                logger.Warn("Skipping custom pacenote id {0}: it has no filename tokens.", pacenote.id);
                continue;
            }

            var matchingIds = filenames
                .Where(item => filenameToId.ContainsKey(item.filename))
                .Select(item => filenameToId[item.filename])
                .Distinct()
                .ToList();
            if (matchingIds.Count == 1)
            {
                idMap[pacenote.id] = matchingIds[0];
                continue;
            }
            if (matchingIds.Count > 1)
            {
                logger.Warn(
                    "Skipping custom pacenote id {0}: its filenames resolve to multiple official tokens ({1}).",
                    pacenote.id,
                    string.Join(", ", matchingIds));
                continue;
            }
            if (!validTypes.Contains(pacenote.type) || !validComplexities.Contains(pacenote.complexity))
            {
                logger.Warn(
                    "Skipping custom pacenote id {0}: type {1} or complexity {2} is undefined.",
                    pacenote.id,
                    pacenote.type,
                    pacenote.complexity);
                continue;
            }

            var runtimeId = pacenote.id;
            if (occupiedIds.Contains(runtimeId))
            {
                while (occupiedIds.Contains(nextId))
                {
                    nextId++;
                }
                runtimeId = nextId++;
                remappedIds++;
            }

            occupiedIds.Add(runtimeId);
            idMap[pacenote.id] = runtimeId;
            addedCustomIds.Add(pacenote.id);
            target.Pacenotes.Add(new ScriptResourcePacenote
            {
                id = runtimeId,
                description = pacenote.description,
                type = pacenote.type,
                complexity = pacenote.complexity
            });
            foreach (var filename in filenames)
            {
                target.Filenames.Add(new ScriptResourceFilenames
                {
                    id = runtimeId,
                    filename = filename.filename,
                    is_primary = filename.is_primary
                });
                filenameToId[filename.filename] = runtimeId;
            }
        }

        var fallbackKeys = target.Fallbacks.Select(item => (item.id, item.fallback_id)).ToHashSet();
        var skippedRelations = 0;
        foreach (var fallback in custom.Fallbacks.OrderBy(item => item.order_id))
        {
            if (!addedCustomIds.Contains(fallback.id))
            {
                continue;
            }
            if (!idMap.TryGetValue(fallback.id, out var runtimeId)
                || !idMap.TryGetValue(fallback.fallback_id, out var runtimeFallbackId))
            {
                skippedRelations++;
                continue;
            }
            if (fallbackKeys.Add((runtimeId, runtimeFallbackId)))
            {
                target.Fallbacks.Add(new ScriptResourceFallbacks
                {
                    id = runtimeId,
                    fallback_id = runtimeFallbackId,
                    order_id = fallback.order_id
                });
            }
        }

        var reverseKeys = target.ReverseCorners.Select(item => (item.id, item.replacement_id)).ToHashSet();
        foreach (var reverse in custom.ReverseCorners)
        {
            if (!addedCustomIds.Contains(reverse.id))
            {
                continue;
            }
            if (!idMap.TryGetValue(reverse.id, out var runtimeId)
                || (reverse.replacement_id != -1
                    && !idMap.TryGetValue(reverse.replacement_id, out _)))
            {
                skippedRelations++;
                continue;
            }
            var runtimeReplacementId = reverse.replacement_id == -1 ? -1 : idMap[reverse.replacement_id];
            if (reverseKeys.Add((runtimeId, runtimeReplacementId)))
            {
                target.ReverseCorners.Add(new ScriptResourceReverseCorners
                {
                    id = runtimeId,
                    replacement_id = runtimeReplacementId
                });
            }
        }

        var localizationKeys = target.Localizations
            .Select(item => (item.pacenote_id, NormalizeLocale(item.locale)))
            .ToHashSet();
        var addedLocalizations = 0;
        foreach (var localization in custom.Localizations)
        {
            if (!idMap.TryGetValue(localization.pacenote_id, out var runtimeId))
            {
                continue;
            }

            var locale = NormalizeLocale(localization.locale);
            if (locale.Length == 0 || string.IsNullOrWhiteSpace(localization.display_name))
            {
                continue;
            }

            if (localizationKeys.Add((runtimeId, locale)))
            {
                target.Localizations.Add(new ScriptResourcePacenoteLocalization
                {
                    pacenote_id = runtimeId,
                    locale = locale,
                    display_name = localization.display_name.Trim(),
                    description = localization.description
                });
                addedLocalizations++;
            }
        }

        return new PacenoteDatabaseMergeResult(
            addedCustomIds.Count,
            addedLocalizations,
            remappedIds,
            skippedRelations);
    }

    private static async Task ValidateSchemaAsync(SqliteConnection connection)
    {
        var tableNames = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table'"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTables = RequiredTables.Where(table => !tableNames.Contains(table)).ToList();
        if (missingTables.Count > 0)
        {
            throw new InvalidDataException(
                $"The pacenote database is missing required tables: {string.Join(", ", missingTables)}");
        }
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string tableName)
    {
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
            new { tableName }) > 0;
    }

    private static string NormalizeLocale(string locale)
    {
        return locale?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
    }

    private static void MergeNamedDefinitions<T>(List<T> target, IEnumerable<T> custom, Func<T, int> idSelector)
    {
        var knownIds = target.Select(idSelector).ToHashSet();
        foreach (var item in custom)
        {
            if (knownIds.Add(idSelector(item)))
            {
                target.Add(item);
            }
        }
    }
}
