using System;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGameDataReader
    {
        GameState GameState { set; get; }
        GameData LastGameData { set; get; }

        string TrackName { get; }
        event Action<GameData, GameData> onNewGameData;

        event Action<bool> onGameDataAvailabilityChanged;

        event Action<GameStateChangeEvent> onGameStateChanged;
        event Action<CarDamageEvent> onCarDamaged;

        bool Initialize(IGame game);
        void Uninitialize(IGame game);
    }
}
