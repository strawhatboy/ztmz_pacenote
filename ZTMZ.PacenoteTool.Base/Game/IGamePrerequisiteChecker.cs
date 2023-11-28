
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGamePrerequisiteChecker
{
    PrerequisitesCheckResult CheckPrerequisites(IGame game);
    void ForceFix(IGame game);
}

public enum PrerequisitesCheckResultCode
{
    OK = 100,
    GAME_NOT_INSTALLED = 300,
    PORT_NOT_OPEN = 400,
    PORT_NOT_MATCH = 401,
    PORT_ALREADY_IN_USE = 402,
    UNKNOWN = 800,
}
public class PrerequisitesCheckResult
{
    public PrerequisitesCheckResultCode Code { set; get; }
    public bool IsOK { set; get; } = true;
    public string Msg { set; get; } = "";
    public List<object> Params { set; get; }
}

