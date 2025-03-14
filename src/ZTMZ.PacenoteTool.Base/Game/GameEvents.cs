using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameEvent
{
    Dictionary<string, object> Parameters { set; get; }
}

public class CarDamageEvent : IGameEvent
{
    public CarDamage DamageType { set; get; }
    public Dictionary<string, object> Parameters { set; get; }
}

public class CarDamageConstants 
{
    public static string SEVERITY = "severity";
    public static string WHEELINDEX = "wheelindex";
}

public class GameStateChangeEvent : IGameEvent
{
    public GameState LastGameState { set; get; }
    public GameState NewGameState { set; get; }
    public Dictionary<string, object> Parameters { set; get; } = new();
}
public enum CarDamage 
{
    Wheel = 0,
    Collision = 1,
    Radiator = 2,
    WaterPump = 3,
    Bodywork = 4,
    Engine = 5,
}

public enum GameState
{
    Unknown = 0,
    RaceBegin = 1,
    CountingDown = 2,
    Racing = 3,
    Paused = 4,
    RaceEnd = 5,
    AdHocRaceBegin = 6, // race begin when the tool was opened after the race started.
}

public enum GameStateRaceEnd {
    Normal = 1, // normal finish
    TimeOut = 2,
    Crashed = 3,
    Retired = 4,
    Disqualified = 5,
    Unknown = 6,
}
public static class GameStateRaceBeginProperty {
    public static readonly string IS_REPLAY = "is_replay";
}

public static class GameStateRaceEndProperty {
    public static readonly string FINISH_TIME = "finish_time";
    public static readonly string FINISH_STATE = "finish_state";   // GameStateRaceEnd
    public static readonly string FINISH_TIME_PANALTY = "finish_time_penalty";
}

