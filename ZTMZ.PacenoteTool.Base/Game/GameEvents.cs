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
    public Dictionary<string, object> Parameters { set; get; }
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
}

