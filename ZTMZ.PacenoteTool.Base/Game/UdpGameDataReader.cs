using System;
using System.Diagnostics;

namespace ZTMZ.PacenoteTool.Base.Game;

public abstract class UdpGameDataReader : IGameDataReader, IDisposable
{
    protected IGame _game;
    public virtual event Action<GameData, GameData> onNewGameData;
    public event Action<bool> onGameDataAvailabilityChanged;
    public virtual event Action<GameStateChangeEvent> onGameStateChanged;
    public virtual event Action<CarDamageEvent> onCarDamaged;

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

    public void Uninitialize(IGame game)
    {
        if (_udpReceiver == null)
            return;

        _udpReceiver.StopListening();
        _udpReceiver.Dispose();
        _udpReceiver = null;
    }

    public void Dispose()
    {
        if (_udpReceiver != null)
            _udpReceiver.Dispose();
    }
}
