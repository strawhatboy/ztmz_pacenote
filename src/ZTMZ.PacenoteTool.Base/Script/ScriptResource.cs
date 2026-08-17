
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
    SYSTEM          =   14,

}

public class ScriptResourceFilenames {
    public int id;
    public string filename;
    public bool is_primary;
}

public class ScriptResourcePacenoteLocalization {
    public int pacenote_id;
    public string locale;
    public string display_name;
    public string description;
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

public class ScriptResourcePacenoteView {
    
}

public class ScriptResource
{
    private static ScriptResource _instance;
    public static ScriptResource Instance => _instance ??= new ScriptResource();
    public static ScriptResource LoadedInstance => _instance;

    private NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly string databasePath;

    public static string DBNAME="data.zdb";

    public SqliteConnection Connection { get; private set; }

    public List<ScriptResourcePacenote> Pacenotes { get; private set; } = new();
    public List<ScriptResourceComplexity> Complexities { get; private set; } = new();
    public List<ScriptResourceType> Types { get; private set; } = new();
    public List<ScriptResourceFilenames> Filenames { get; private set; } = new();
    public List<ScriptResourcePacenoteLocalization> Localizations { get; private set; } = new();
    public List<ScriptResourceFallbacks> Fallbacks { get; private set; } = new();
    public List<ScriptResourceReverseCorners> ReverseCorners { get; private set; } = new();

    public Dictionary<int, ScriptResourcePacenote> PacenoteDict { get; private set; } = new();
    public Dictionary<int, ScriptResourceComplexity> ComplexityDict { get; private set; } = new();
    public Dictionary<int, ScriptResourceType> TypeDict { get; private set; } = new();
    public Dictionary<int, List<string>> FilenameDict { get; private set; } = new();
    public Dictionary<int, List<int>> FallbackDict { get; private set; } = new();
    public Dictionary<string, int> FilenameToIdDict { get; private set; } = new();
    public Dictionary<(int PacenoteId, string Locale), ScriptResourcePacenoteLocalization> LocalizationDict { get; private set; } = new();

    public int SimpleTokensCount { get; private set; } = 0;
    public int NormalTokensCount { get; private set; } = 0;
    public int ComplexTokensCount { get; private set; } = 0;
    

    private ScriptResource() {
        // load from data.db
        // data.db should be in user profile directory
        databasePath = AppLevelVariables.Instance.GetPath(DBNAME);
        if (!File.Exists(databasePath)) {
            // ERROR
            logger.Error($"Database file not found: {databasePath}");
            throw new FileNotFoundException($"Database file not found: {databasePath}");
        }

        Connection = new SqliteConnection($"Data Source={databasePath};");
        // load tables

        Connection.Open();
        Connection.EnableExtensions(true);
        try
        {
            SystemSoundTokenMigration.Ensure(Connection);
        }
        catch (Exception exception)
        {
            logger.Warn(exception, "Could not migrate legacy mechanical-damage system sound tokens.");
        }
        try
        {
            PacenoteLocalizationMigration.Ensure(Connection);
        }
        catch (Exception exception)
        {
            logger.Warn(exception, "Could not migrate the legacy pacenote localization schema.");
        }
    }

    public async Task LoadData() {
        var data = await PacenoteDatabaseLoader.LoadAsync(Connection);
        var customDatabasePath = Config.Instance.CustomPacenoteDatabasePath;
        if (!string.IsNullOrWhiteSpace(customDatabasePath))
        {
            try
            {
                var normalizedCustomPath = Path.GetFullPath(customDatabasePath);
                if (string.Equals(
                    Path.GetFullPath(databasePath),
                    normalizedCustomPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    logger.Warn("Ignoring the custom pacenote database because it is the official data.zdb file.");
                }
                else if (!File.Exists(normalizedCustomPath))
                {
                    logger.Warn("Custom pacenote database not found: {0}", normalizedCustomPath);
                }
                else
                {
                    var result = await PacenoteDatabaseLoader.MergeCustomAsync(data, normalizedCustomPath, logger);
                    logger.Info(
                        "Merged custom pacenote database {0}: {1} tokens and {2} localizations added, {3} ids remapped, {4} invalid relations skipped.",
                        normalizedCustomPath,
                        result.AddedTokens,
                        result.AddedLocalizations,
                        result.RemappedIds,
                        result.SkippedRelations);
                }
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Could not merge custom pacenote database {0}; using official definitions only.", customDatabasePath);
            }
        }

        Pacenotes = data.Pacenotes;
        Complexities = data.Complexities;
        Types = data.Types;
        Filenames = data.Filenames;
        Localizations = data.Localizations;
        Fallbacks = data.Fallbacks;
        ReverseCorners = data.ReverseCorners;

        PacenoteDict = Pacenotes.ToDictionary(x => x.id);
        ComplexityDict = Complexities.ToDictionary(x => x.id);
        TypeDict = Types.ToDictionary(x => x.id);
        // is_primary first
        FilenameDict = Filenames.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.is_primary).Select(y => y.filename).ToList());
        FallbackDict = Fallbacks.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.fallback_id).ToList());
        FilenameToIdDict = Filenames.ToDictionary(x => x.filename, x => x.id);
        LocalizationDict = Localizations.ToDictionary(
            item => (item.pacenote_id, NormalizeLocale(item.locale)),
            item => item);

        SimpleTokensCount = Pacenotes.Count(x => ComplexityDict[x.complexity].id == (int)ScriptResourceComplexities.SIMPLE);
        NormalTokensCount = Pacenotes.Count(x => ComplexityDict[x.complexity].id == (int)ScriptResourceComplexities.NORMAL);
        ComplexTokensCount = Pacenotes.Count(x => ComplexityDict[x.complexity].id == (int)ScriptResourceComplexities.COMPLEX);
    }

    public string ResolvePacenoteDisplayName(string token, string locale)
    {
        var normalizedToken = token?.Trim() ?? string.Empty;
        if (normalizedToken.Length == 0)
        {
            return string.Empty;
        }

        if (!FilenameToIdDict.TryGetValue(normalizedToken, out var pacenoteId))
        {
            return HumanizeToken(normalizedToken);
        }

        var normalizedLocale = NormalizeLocale(locale);
        if (normalizedLocale == "zh-hans-cn")
        {
            normalizedLocale = "zh-cn";
        }

        if (LocalizationDict.TryGetValue((pacenoteId, normalizedLocale), out var localized)
            || LocalizationDict.TryGetValue((pacenoteId, I18NLoader.FallbackCultureName), out localized))
        {
            return localized.display_name;
        }

        var primaryToken = Filenames.FirstOrDefault(item => item.id == pacenoteId && item.is_primary)?.filename;
        return HumanizeToken(primaryToken ?? normalizedToken);
    }

    private static string NormalizeLocale(string locale)
    {
        return locale?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
    }

    private static string HumanizeToken(string token)
    {
        return token.Replace('_', ' ');
    }
}
