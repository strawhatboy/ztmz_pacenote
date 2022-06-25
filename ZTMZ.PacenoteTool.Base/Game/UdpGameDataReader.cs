using System;
using System.Diagnostics;

namespace ZTMZ.PacenoteTool.Base.Game;

public abstract class UdpGameDataReader : IGameDataReader
{
    protected IGame _game;
    public abstract event Action<GameData, GameData> onNewGameData;
    public event Action<bool> onGameDataAvailabilityChanged;
    public abstract event Action<GameStateChangeEvent> onGameStateChanged;
    public abstract event Action<CarDamageEvent> onCarDamaged;

    private UdpReceiver _udpReceiver;

    public abstract GameState GameState { get; set; }
    public abstract GameData LastGameData { get; set; }
    public abstract string TrackName { get; }

    public void Initialize(IGame game)
    {
        _game = game;
        Debug.Assert(game.GameConfigurations.ContainsKey(UdpGameConfig.Name));

        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;

        Debug.Assert(udpConfig != null);

        _udpReceiver = new UdpReceiver();
        _udpReceiver.onNewMessage += onNewUdpMessage;
        _udpReceiver.onMessageAvailable += availablity => 
        {
            onGameDataAvailabilityChanged?.Invoke(availablity);
        };
        _udpReceiver.StartListening(udpConfig.IPAddress, udpConfig.Port);
    }

    public abstract void onNewUdpMessage(byte[] oldMsg, byte[] newMsg);
}
