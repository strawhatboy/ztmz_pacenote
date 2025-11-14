

using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public class ACRGameDataReader : SharedMemoryGameReader, IGameDataReader
{
    public ACRGameDataReader() : base(
        (typeof(ACRGraphicsData), "Local\\acpmf_graphics"),
        (typeof(ACRPhysicsData), "Local\\acpmf_physics"),
        (typeof(ACRStaticInfo), "Local\\acpmf_static"))
    {
    }

    public GameState GameState { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public GameData LastGameData { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public GameData CurrentGameData { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string TrackName => throw new NotImplementedException();

    public string CarName => throw new NotImplementedException();

    public string CarClass => throw new NotImplementedException();

    public event Action<GameData, GameData> onNewGameData;
    public event Action<bool> onGameDataAvailabilityChanged;
    public event Action<GameStateChangeEvent> onGameStateChanged;
    public event Action<CarDamageEvent> onCarDamaged;
    public event Action onCarReset;

    public bool Initialize(IGame game)
    {
        if (TryOpenAllDebounced())
        {
            return true;
        }
        return false;
    }

    public ACRGameRawData ReadAll() {
        var data = new ACRGameRawData();
        if (TryReadUnsafe<ACRGraphicsData>(out var graphicsData)) {
            data.GraphicsData = graphicsData;
        }
        if (TryReadUnsafe<ACRPhysicsData>(out var physicsData)) {
            data.PhysicsData = physicsData;
        }
        if (TryReadUnsafe<ACRStaticInfo>(out var staticInfo)) {
            data.StaticInfo = staticInfo;
        }
        return data;
    }

    public void Uninitialize(IGame game)
    {
        CloseAll();
    }
}
