// load jannemod_v3.zdb for pacenote id mapping

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRPacenote
{
    public int id;
    public string name = string.Empty;
    public string description = string.Empty;
    public string category = string.Empty;
}

public class RBRPacenote2ZTMZ
{
    public int id;
    public int ztmz_id;
    public int order_id;

}

public class RBRModifier2ZTMZ
{
    public int id;
    public int ztmz_id;
    public int order_id;
}

public class RBRScriptResource
{
    private static RBRScriptResource _instance;
    public static RBRScriptResource Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new RBRScriptResource();
            }
            return _instance;
        }
    }

    private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    public List<RBRPacenote> Pacenotes { get; private set; } = new();
    public List<RBRPacenote2ZTMZ> Pacenote2ZTMZs { get; private set; } = new();
    public List<RBRModifier2ZTMZ> Modifier2ZTMZs { get; private set; } = new();
    public Dictionary<int, List<RBRPacenote>> PacenotesDict { get; private set; } = new();
    public Dictionary<int, List<int>> PacenoteId2ZTMZIds { get; private set; } = new();
    public Dictionary<int, List<int>> ModiferId2ZTMZids { get; private set; } = new();

    public string? DBPath { get; private set; }

    internal RBRScriptResource() { }

    public async Task<bool> LoadData(string? dbPath)
    {
        logger.Info("Loading RBR pacenote data from {0}", dbPath);
        try
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                throw new FileNotFoundException("RBR pacenote definition file is missing.", dbPath);

            // Never create a database or keep a file handle after reading the mappings.
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await connection.OpenAsync().ConfigureAwait(false);
            var pacenotes = (await connection.QueryAsync<RBRPacenote>(
                """
                SELECT id,
                       COALESCE(name, '') AS name,
                       COALESCE(description, '') AS description,
                       COALESCE(category, '') AS category
                FROM pacenote
                """).ConfigureAwait(false)).ToList();
            var pacenoteMappings = (await connection.QueryAsync<RBRPacenote2ZTMZ>(
                """
                SELECT id, ztmz_id, COALESCE(order_id, 0) AS order_id
                FROM pacenote_ztmz
                WHERE id IS NOT NULL AND ztmz_id IS NOT NULL
                """).ConfigureAwait(false)).ToList();
            var modifierMappings = (await connection.QueryAsync<RBRModifier2ZTMZ>(
                """
                SELECT id, ztmz_id, COALESCE(order_id, 0) AS order_id
                FROM modifier_ztmz
                WHERE id IS NOT NULL AND ztmz_id IS NOT NULL
                """).ConfigureAwait(false)).ToList();
            if (pacenoteMappings.Count == 0)
                throw new InvalidDataException("RBR pacenote definition contains no pacenote mappings.");

            // Publish only after every table has been read successfully.
            var pacenotesById = pacenotes.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.ToList());
            var pacenoteIds = pacenoteMappings.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.ztmz_id).ToList());
            var modifierIds = modifierMappings.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.ztmz_id).ToList());
            Pacenotes = pacenotes;
            Pacenote2ZTMZs = pacenoteMappings;
            Modifier2ZTMZs = modifierMappings;
            PacenotesDict = pacenotesById;
            PacenoteId2ZTMZIds = pacenoteIds;
            ModiferId2ZTMZids = modifierIds;
            DBPath = dbPath;
            logger.Info("RBR pacenote data loaded.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or SqliteException or DataException or ArgumentException or NotSupportedException)
        {
            // A previous definition can mean different calls: never keep stale mappings.
            DBPath = null;
            Pacenotes = new();
            Pacenote2ZTMZs = new();
            Modifier2ZTMZs = new();
            PacenotesDict = new();
            PacenoteId2ZTMZIds = new();
            ModiferId2ZTMZids = new();
            logger.Warn(exception,
                "Unable to load RBR pacenote definition '{0}'. Calls requiring this definition are unavailable; check the RBR pacenote definition setting. ZTMZ can continue running.", dbPath);
            return false;
        }
    }
}
