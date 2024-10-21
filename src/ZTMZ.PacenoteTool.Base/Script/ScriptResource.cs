
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZTMZ.PacenoteTool.Base.Script;

public class ScriptResourcePacenote {
    public int id;
    public string description;
    public int type;
    public int complexity;

}

public class ScriptResourceComplexity {
    public int id;
    public string name;
}

public enum ScriptResourceComplexities {
    SIMPLE = 0,
    NORMAL = 1,
    COMPLEX = 2,
    MISC = -1
}

public class ScriptResourceType {
    public int id;
    public string name;
}

public enum ScriptResourceTypes {
    CORNERS         =   0,
    MODIFIER        =   1,
    LINKS           =   2,
    ADJECTIVES      =   3,
    ROAD            =   4,
    LINE            =   5,
    OBSTACLES       =   6,
    CONSTRUCTION    =   7,
    CAUTIONS        =   8,
    DRIVING         =   9,
    PREPOSITIONS    =   10,
    SURFACE         =   11,
    NUMBERS         =   12,
    MISC            =   13,

}

public class ScriptResourceFilenames {
    public int id;
    public string filename;
    public bool is_primary;
}

public class ScriptResourceFallbacks {
    public int id;
    public int fallback_id;
    public int order_id;
}

public class ScriptResourceReverseCorners {
    public int id;
    public int replacement_id;
}

public class ScriptResource
{
    private static ScriptResource _instance;
    public static ScriptResource Instance => _instance ??= new ScriptResource();

    private NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

    public static string DBNAME="data.zdb";

    public SqliteConnection Connection { get; private set; }

    public List<ScriptResourcePacenote> Pacenotes { get; private set; } = new();
    public List<ScriptResourceComplexity> Complexities { get; private set; } = new();
    public List<ScriptResourceType> Types { get; private set; } = new();
    public List<ScriptResourceFilenames> Filenames { get; private set; } = new();
    public List<ScriptResourceFallbacks> Fallbacks { get; private set; } = new();
    public List<ScriptResourceReverseCorners> ReverseCorners { get; private set; } = new();

    public Dictionary<int, ScriptResourcePacenote> PacenoteDict { get; private set; } = new();
    public Dictionary<int, ScriptResourceComplexity> ComplexityDict { get; private set; } = new();
    public Dictionary<int, ScriptResourceType> TypeDict { get; private set; } = new();
    public Dictionary<int, List<string>> FilenameDict { get; private set; } = new();
    public Dictionary<int, List<int>> FallbackDict { get; private set; } = new();
    public Dictionary<string, int> FilenameToIdDict { get; private set; } = new();
    

    private ScriptResource() {
        // load from data.db
        // data.db should be in user profile directory
        var dbFile = AppLevelVariables.Instance.GetPath(DBNAME);
        if (!File.Exists(dbFile)) {
            // ERROR
            logger.Error($"Database file not found: {dbFile}");
            throw new FileNotFoundException($"Database file not found: {dbFile}");
        }

        Connection = new SqliteConnection($"Data Source={dbFile};");
        // load tables

        Connection.Open();
        Connection.EnableExtensions(true);
    }

    public async Task LoadData() {
        Pacenotes = (await Connection.QueryAsync<ScriptResourcePacenote>("SELECT * FROM pacenote")).ToList();
        Complexities = (await Connection.QueryAsync<ScriptResourceComplexity>("SELECT * FROM pacenote_complexity")).ToList();
        Types = (await Connection.QueryAsync<ScriptResourceType>("SELECT * FROM pacenote_type")).ToList();
        Filenames = (await Connection.QueryAsync<ScriptResourceFilenames>("SELECT * FROM pacenote_filenames")).ToList();
        Fallbacks = (await Connection.QueryAsync<ScriptResourceFallbacks>("SELECT * FROM pacenote_fallbacks")).ToList();
        ReverseCorners = (await Connection.QueryAsync<ScriptResourceReverseCorners>("SELECT * FROM reverse_corners")).ToList();

        PacenoteDict = Pacenotes.ToDictionary(x => x.id);
        ComplexityDict = Complexities.ToDictionary(x => x.id);
        TypeDict = Types.ToDictionary(x => x.id);
        // is_primary first
        FilenameDict = Filenames.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.is_primary).Select(y => y.filename).ToList());
        FallbackDict = Fallbacks.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.fallback_id).ToList());
        FilenameToIdDict = Filenames.ToDictionary(x => x.filename, x => x.id);
    }
}
