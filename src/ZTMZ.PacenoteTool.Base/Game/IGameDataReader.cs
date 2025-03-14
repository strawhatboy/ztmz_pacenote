using System;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGameDataReader
    {
        GameState GameState { set; get; }
        GameData LastGameData { set; get; }
        GameData CurrentGameData { set; get; }

        string TrackName { get; }
        string CarName { get; }
        string CarClass { get; }
        event Action<GameData, GameData> onNewGameData;

        event Action<bool> onGameDataAvailabilityChanged;

        event Action<GameStateChangeEvent> onGameStateChanged;
        event Action<CarDamageEvent> onCarDamaged;

        event Action onCarReset;

        bool Initialize(IGame game);
        void Uninitialize(IGame game);
    }
}
