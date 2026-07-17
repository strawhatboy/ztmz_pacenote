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

    public UdpReceiverStatus UdpStatus => _udpReceiver?.Status ?? UdpReceiverStatus.NotListening;

    public virtual bool IsUdpTelemetryOptional => false;

    public int SecondsWithoutUdpMessage => Math.Max(
        0,
        System.Threading.Interlocked.CompareExchange(ref _timerMessageAvailableCount, 0, 0));

    public virtual bool Initialize(IGame game)
    {
        if (isInitialized)
            return false;

        ArgumentNullException.ThrowIfNull(game);
        _game = game;

        if (!game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var config) ||
            config is not UdpGameConfig udpConfig)
        {
            _logger.Error("Failed to initialize UDP reader because game {0} has no UDP configuration.", game.Name);
            return false;
        }

        if (!IPAddress.TryParse(udpConfig.IPAddress, out var ipAddress))
        {
            _logger.Error("Failed to initialize UDP reader because {0} is not a valid IP address.", udpConfig.IPAddress);
            return false;
        }

        var udpReceiver = new UdpReceiver();
        udpReceiver.onNewMessage += onNewUdpMessage;
        udpReceiver.onNewMessage += OnUdpMessageReceived;

        try
        {
            udpReceiver.StartListening(ipAddress, udpConfig.Port);
        }
        catch
        {
            udpReceiver.Dispose();
            throw;
        }

        System.Threading.Interlocked.Exchange(ref _timerMessageAvailableCount, 10);
        _udpReceiver = udpReceiver;
        _timer = new Timer(1000);
        _timer.Elapsed += OnTimerElapsed;
        isInitialized = true;
        _timer.Start();
        _logger.Debug("UdpGameDataReader initialized");
        return true;
    }

    private void OnUdpMessageReceived(byte[] oldMessage, byte[] newMessage)
    {
        System.Threading.Interlocked.Exchange(ref _timerMessageAvailableCount, 0);
    }

    private void OnTimerElapsed(object sender, ElapsedEventArgs args)
    {
        if (GameState != GameState.Paused && GameState != GameState.RaceBegin)
        {
            if (_timerCount >= 10)
            {
                GameState = GameState.Unknown;
                _timerCount = 0;
            }
            else
            {
                _timerCount++;
            }
        }

        var secondsWithoutMessage = System.Threading.Interlocked.Increment(ref _timerMessageAvailableCount);
        onGameDataAvailabilityChanged?.Invoke(secondsWithoutMessage < 10);
    }

    public virtual void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        this._timerCount = 0;
        // _logger.Trace("onNewUdpMessage: Game state is {0}, timer count is {1}", this.GameState, this._timerCount);
    }

    public virtual void Uninitialize(IGame game)
    {
        if (!isInitialized)
        {
            _logger.Warn("UdpGameDataReader is not initialized when uninitializing");
        }

        isInitialized = false;

        var timer = _timer;
        _timer = null;
        if (timer != null)
        {
            timer.Stop();
            timer.Elapsed -= OnTimerElapsed;
            timer.Dispose();
        }

        var udpReceiver = _udpReceiver;
        _udpReceiver = null;
        if (udpReceiver != null)
        {
            udpReceiver.onNewMessage -= onNewUdpMessage;
            udpReceiver.onNewMessage -= OnUdpMessageReceived;
            udpReceiver.Dispose();
        }

        _logger.Debug("UdpGameDataReader uninitialized");
    }

    public void Dispose()
    {
        Uninitialize(_game);
        GC.SuppressFinalize(this);
    }
}
