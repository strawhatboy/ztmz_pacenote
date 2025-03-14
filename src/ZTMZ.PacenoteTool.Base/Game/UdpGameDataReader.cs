using System;
using System.Diagnostics;
using System.Net;
using System.Timers;

namespace ZTMZ.PacenoteTool.Base.Game;

public abstract class UdpGameDataReader : IGameDataReader, IDisposable
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    protected IGame _game;
    public virtual event Action<GameData, GameData> onNewGameData;
    public event Action<bool> onGameDataAvailabilityChanged;
    public virtual event Action<GameStateChangeEvent> onGameStateChanged;
    public virtual event Action<CarDamageEvent> onCarDamaged;
    public virtual event Action onCarReset;

    private UdpReceiver _udpReceiver;

    protected bool isInitialized = false;

    public abstract GameState GameState { get; set; }
    public abstract GameData LastGameData { get; set; }
    public abstract GameData CurrentGameData { get; set; }
    public abstract string TrackName { get; }
    public abstract string CarName { get; }

    public abstract string CarClass { get; }

    private Timer _timer;
    protected int _timerCount = 0;
    private int _timerMessageAvailableCount = 0;

    public virtual bool Initialize(IGame game)
    {
        if (isInitialized) 
            return false;
        _game = game;
        Debug.Assert(game.GameConfigurations.ContainsKey(UdpGameConfig.Name));

        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;

        Debug.Assert(udpConfig != null);

        _udpReceiver = new UdpReceiver();
        _udpReceiver.onNewMessage += onNewUdpMessage;
        _udpReceiver.onNewMessage += (o, n) => _timerMessageAvailableCount = 0;

        IPAddress iPAddress;
        if (IPAddress.TryParse(udpConfig.IPAddress, out iPAddress))
        {
            _udpReceiver.StartListening(iPAddress, udpConfig.Port);
        } else {
            _logger.Error("Failed to start listening UDP on address {0} and port {1}", udpConfig.IPAddress, udpConfig.Port);
        }

        
        this._timer = new Timer();
        this._timer.Interval = 1000;
        this._timer.Elapsed += (sender, args) =>
        {
            if (this.GameState != GameState.Paused && this.GameState != GameState.RaceBegin)
            {
                if (this._timerCount >= 10)
                {
                    // no gamestate change for 20s and the game is not paused.
                    // TODO: new mechanism to get the unkown game state.
                    this.GameState = GameState.Unknown;
                    this._timerCount = 0;
                }
                else
                {
                    this._timerCount++;
                    // _logger.Trace("timerTick: Game state is {0}, timer count is {1}", this.GameState, this._timerCount);
                }
            }

            this._timerMessageAvailableCount++;
            if (this._timerMessageAvailableCount >= 10)
            {
                this.onGameDataAvailabilityChanged?.Invoke(false);
            }
            else
            {
                this.onGameDataAvailabilityChanged?.Invoke(true);
            }
        };
        this._timer.Start();
        isInitialized = true;
        _logger.Debug("UdpGameDataReader initialized");
        return isInitialized;
    }

    public virtual void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        this._timerCount = 0;
        // _logger.Trace("onNewUdpMessage: Game state is {0}, timer count is {1}", this.GameState, this._timerCount);
    }

    public virtual void Uninitialize(IGame game)
    {
        if (!isInitialized) { 
            _logger.Warn("UdpGameDataReader is not initialized when uninitializing");
            return;
        }

        if (_udpReceiver == null) {
            _logger.Warn("UdpGameDataReader is not initialized when uninitializing");
            return;
        }

        _udpReceiver.StopListening();
        _udpReceiver.Dispose();
        _udpReceiver = null;
        isInitialized = false;
        _logger.Debug("UdpGameDataReader uninitialized");
    }

    public void Dispose()
    {
        if (_udpReceiver != null)
            _udpReceiver.Dispose();
    }
}
