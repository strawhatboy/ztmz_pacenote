// load jannemod_v3.zdb for pacenote id mapping

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRPacenote
{
    public int id;
    public string name;
    public string description;
    public string category;
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

    private NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    private string DBNAME = "jannemod_v3.zdb";

    public SqliteConnection Connection { get; private set; }

    public List<RBRPacenote> Pacenotes { get; private set; } = new();
    public List<RBRPacenote2ZTMZ> Pacenote2ZTMZs { get; private set; } = new();
    public List<RBRModifier2ZTMZ> Modifier2ZTMZs { get; private set; } = new();
    public Dictionary<int, RBRPacenote> PacenotesDict { get; private set; } = new();
    public Dictionary<int, List<int>> PacenoteId2ZTMZIds { get; private set; } = new();
    public Dictionary<int, List<int>> ModiferId2ZTMZids { get; private set; } = new();
    private RBRScriptResource() {
        var dbPath = AppLevelVariables.Instance.GetPath(Path.Join(Constants.PATH_GAMES, DBNAME));
        if (!File.Exists(dbPath))
        {
            logger.Error("DB file not found: " + dbPath);
            throw new FileNotFoundException("DB file not found: " + dbPath);
        }
        Connection = new SqliteConnection($"Data Source={dbPath}");
        Connection.Open();
        Connection.EnableExtensions(true);
    }

    public async Task LoadData() {
        Pacenotes = (await Connection.QueryAsync<RBRPacenote>("SELECT * FROM pacenote")).ToList();
        Pacenote2ZTMZs = (await Connection.QueryAsync<RBRPacenote2ZTMZ>("SELECT * FROM pacenote_ztmz")).ToList();
        Modifier2ZTMZs = (await Connection.QueryAsync<RBRModifier2ZTMZ>("SELECT * FROM modifier_ztmz")).ToList();

        PacenotesDict = Pacenotes.ToDictionary(x => x.id, x => x);
        PacenoteId2ZTMZIds = Pacenote2ZTMZs.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.ztmz_id).ToList());
        ModiferId2ZTMZids = Modifier2ZTMZs.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.OrderBy(y => y.order_id).Select(y => y.ztmz_id).ToList());
    }
}
