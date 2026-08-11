using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Threading.Tasks;
using System.Threading;
using NAudio.Wave;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;

namespace ZTMZ.PacenoteTool.Core;

public class ZTMZPacenoteTool {
    
    private ProfileManager _profileManager;
    private ProcessWatcher _processWatcher;
    private string _trackName;
    private string _carName;
    private string _carClass;
    private float _trackLength;
    private float _currentReplayTrackLength;
    private Int64 _obsStartTimestamp;
    private Int64 _obsStopTimestamp;
    private bool _isReplaySession;
    private double _scriptTiming = 0;
    private int _playpointAdjust = 0;
    private float _playbackSpd = 1.0f;
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private IGame _currentGame;
    private static readonly TimeSpan CollisionSoundCooldown = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MechanicalDamageSoundCooldown = TimeSpan.FromSeconds(3);
    private readonly object _damageSoundCooldownLock = new();
    private readonly Dictionary<string, DateTime> _lastDamageSoundUtc = new(StringComparer.Ordinal);

    public IGame CurrentGame => _currentGame;
    public ProfileManager ProfileManager => _profileManager;
    private List<IGame> _games = new();

    public List<IGame> Games => _games;
    private List<string> _profiles = new();
    public List<string> Profiles => _profiles;
    private List<CoDriverPackage> _codriverPackages = new();
    public List<CoDriverPackage> CoDriverPackages => _codriverPackages;
    private List<string> _outputDevices = new();
    public List<string> OutputDevices => _outputDevices;

    public event Action<string> onStatusReport;

    public event Action onToolInitialized;

    public event Action onCodriversRefreshed;

    public event Action<IGame> onGameInitialized;
    public event Action<IGame> onGameUninitialized;

    public event Action<IGame, Process> onGameStarted;

    public event Action<IGame> onGameEnded;

    public event Action<IGame, PrerequisitesCheckResultCode, List<object>> onGameInitializeFailed;

    public event Action<IGame, GameStateChangeEvent> onGameStateChanged;

    public event Action<IGame, string, string, string> onTrackAndCarChanged;

    public event Action<IGame, GameData> onNewGameData;
    public event Action<PacenotePlaybackSnapshot>? onPacenoteDispatched;

    public event Action<IGame> onRaceBegin;
    public event Action<IGame> onRaceBegined;

    public event Action<IGame> onRaceEnd;

    /// <summary>
    /// The event raised when a local replay is loaded.
    /// </summary>
    /// <param name="success">Whether the local replay is loaded successfully.</param>
    public event Action<bool> onLocalReplayLoaded;

    public event Action<bool> onOnlineReplayLoaded;

    public string CurrentScriptAuthor => _profileManager?.CurrentScriptReader?.Author ?? "???";

    public bool IsInitialized { private set; get; } = false;

    public List<ReplayDetailsPerCheckpoint> ReplayDetailsPerCheckpoints { set; get; } = new();
    public List<ReplayDetailsPerTime> ReplayDetailsPerTimes  { set; get; } = new();

    public Replay BestLocalReplay { set; get; }
    public List<ReplayDetailsPerCheckpoint> BestLocalReplayDetailsPerCheckpoints { set; get; } = new();
    public List<ReplayDetailsPerTime> BestLocalReplayDetailsPerTimes { set; get; } = new();
    private ReplayTimelineLookup _currentReplayLookup;
    private int _currentReplayLookupCount = -1;
    private ReplayTimelineLookup _bestLocalReplayLookup;
    private int _bestLocalReplayLookupCount = -1;

    public Dictionary<string, List<ReplayDetailsPerCheckpoint>> OnlineReplayDetailsPerCheckpoints { set; get; } = new();
    public Dictionary<string, List<ReplayDetailsPerTime>> OnlineReplayDetailsPerTimes { set; get; } = new();

    // init the tool, load settings, etc.
    public void Init() {
        this.loadPacenoteDefinitions();
        this.loadProfileManager();
        this.loadGames();
        this.loadProfiles();
        this.loadCodrivers();
        this.loadOutputDevices();
        // this.initGoogleAnalytics();
        this.initializeProcessWatcher();
        this.initializeObsConnection();
        this.initializeReplayManager();

        this.IsInitialized = true;
        onToolInitialized?.Invoke();
    }

#region privates

    private void loadPacenoteDefinitions() {
        _logger.Info("Loading pacenote definitions...");
        this.onStatusReport?.Invoke("Loading pacenote definitions...");
        Task.Run(ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance.LoadData).Wait();
        _logger.Info("Pacenote definitions loaded.");
        this.onStatusReport?.Invoke("Pacenote definitions loaded.");
    }

    private void loadProfileManager() {
        _logger.Info("Loading profile manager...");
        this.onStatusReport?.Invoke("Loading profile manager...");
        _profileManager = new();
        _logger.Info("Profile manager loaded.");
        this.onStatusReport?.Invoke("Profile manager loaded.");
    }
    private void loadGames()
    {
        _logger.Info("Loading games...");
        this.onStatusReport?.Invoke("Loading games...");
        this._games.Clear();
        try {
            var gamesDirectory = Path.Combine(AppContext.BaseDirectory, Constants.PATH_GAMES);
            this._games.AddRange(GamePluginLoader.Load(
                Directory.EnumerateFiles(gamesDirectory, "*.dll"),
                _logger));
            this._games.Sort((g1, g2) => g1.Order.CompareTo(g2.Order));
            foreach (var game in this._games)
            {
                game.GameConfigurations = Config.Instance.LoadGameConfig(game);
                if (game is IGameConfigurationMigrator migrator && migrator.MigrateGameConfiguration())
                {
                    Config.Instance.SaveGameConfig(game);
                    _logger.Info("Migrated game configuration for {0}.", game.Name);
                }
            }
        } catch (Exception ex) {
            _logger.Error($"Faield to load games: {ex.ToString()}");
        }
        _logger.Info($"{this._games.Count} games loaded.");
        this.onStatusReport?.Invoke($"{this._games.Count} games loaded.");
    }

    private void loadProfiles() {
        _logger.Info("Loading profiles...");
        this.onStatusReport?.Invoke("Loading profiles...");
        this._profiles = _profileManager.GetAllProfiles();
        _logger.Info($"{this._profiles.Count} profiles loaded.");
        this.onStatusReport?.Invoke($"{this._profiles.Count} profiles loaded.");
    }

    private void loadCodrivers() {
        _logger.Info("Loading codrivers...");
        this.onStatusReport?.Invoke("Loading codrivers...");
        foreach (var codriver in this._profileManager.GetAllCodrivers())
        {
            if (!Config.Instance.UseDefaultSoundPackageByDefault && codriver.EndsWith(Constants.DEFAULT_CODRIVER))
            {
                continue;
            }

            var pkg = this._profileManager.CoDriverPackages[codriver];
            this._codriverPackages.Add(pkg);
        }
        _logger.Info($"{this._codriverPackages.Count} codrivers loaded.");
        this.onStatusReport?.Invoke($"{this._codriverPackages.Count} codrivers loaded.");
    }

    public async void RefreshCodrivers() {
        this._codriverPackages.Clear();
        await this._profileManager.RefreshCodriverSounds();
        this.loadCodrivers();
        this.onCodriversRefreshed?.Invoke();
    }

    private void loadOutputDevices() {
        _logger.Info("Loading output devices...");
        this.onStatusReport?.Invoke("Loading output devices...");
        // WaveOut only works for windows !!! DAMN IT !!!
        for (int i = 0; i < NAudio.Wave.WaveOut.DeviceCount; i++)
        {
            WaveOutCapabilities WOC = NAudio.Wave.WaveOut.GetCapabilities(i);
            this._outputDevices.Add(WOC.ProductName);
        }
        _logger.Info($"{NAudio.Wave.WaveOut.DeviceCount} output devices loaded.");
        this.onStatusReport?.Invoke($"{NAudio.Wave.WaveOut.DeviceCount} output devices loaded.");
    }
    private void initGoogleAnalytics()
    {
        return;
    }

    private void initializeProcessWatcher()
    {
        _processWatcher = new ProcessWatcher(p => {
            // new process
            var g = _games.FirstOrDefault(g => g.Executable.ToLower().Equals(p.ProcessName.ToLower()));
            if (g == null)
                return;

            g.IsRunning = true;
            this.onGameStarted?.Invoke(g, p);
            if (_currentGame == g) 
            {
                // raise game started event!!!
                // turn on the light, current game is running.
                // start game data pulling
                initializeGame(_currentGame);
            }
        }, (pName, pPath) => {
            var g = _games.FirstOrDefault(g => g.Executable.ToLower().Equals(pName));
            if (g == null)
                return;

            g.IsRunning = false;
            if (_currentGame == g) 
            {
                // raise game UI exit effect
                try
                {
                    this.onGameEnded?.Invoke(_currentGame);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Game ended handler failed for {0}.", g.Name);
                }
            }
            if (_currentGame?.Name.Equals(g.Name) == true)
            {
                // turn off the light, current game is exiting.
                uninitializeGame(_currentGame);
            }
        }, Config.Instance.ProcessWatcherInterval);

        // watch games starting
        foreach (var game in _games) 
        {
            _processWatcher.AddToWatch(game.Executable, game.WindowTitle);
        }

        // start the watch (threads started)
        _processWatcher.StartWatching();

        _logger.Info("ProcessWatcher started");
    }

    private void initializeObsConnection() {
        // connect to OBS
        ObsManager.Instance.Connect();
        ObsManager.Instance.RecordStarted += () => {
            // set timestamp
            this._obsStartTimestamp = DateTime.UtcNow.Ticks;
        };
        ObsManager.Instance.RecordStopped += () => {
            // set timestamp
            this._obsStopTimestamp = DateTime.UtcNow.Ticks;
        };
        this.onStatusReport?.Invoke("OBS connection initialized.");
    }

    private void initializeReplayManager() {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Action<string> reportProgress = status => this.onStatusReport?.Invoke(status);
        ReplayManager.Instance.Init(reportProgress);
        try {
            ReplayManager.Instance.ImportLegacyReplaysAsync(this.Games, reportProgress).GetAwaiter().GetResult();
        } catch (Exception ex) {
            _logger.Error(ex, "Failed to import legacy replay databases.");
            reportProgress("Replay manager: legacy replay import failed; continuing startup.");
        }
        var completionStatus = $"Replay manager initialized in {stopwatch.ElapsedMilliseconds} ms.";
        _logger.Info(completionStatus);
        reportProgress(completionStatus);
    }

    private void initializeGame(IGame game) 
    {
        if (game == null || game.IsInitialized == true)
            return;

        // check prerequisite
        var res = checkPrerequisite(game);
        var code = res.Code;
        if (code == PrerequisitesCheckResultCode.GAME_NOT_INSTALLED)
        {
            // raise Game not install
            this.onGameInitializeFailed?.Invoke(game, code, null);
            return;
        }

        var udpTelemetryIsOptional = game.GameDataReader is UdpGameDataReader udpReader
            && udpReader.IsUdpTelemetryOptional;
        if (!udpTelemetryIsOptional && TryGetUdpPortFailure(game, out var port, out var socketError))
        {
            var failureCode = socketError == SocketError.AddressAlreadyInUse
                ? PrerequisitesCheckResultCode.PORT_ALREADY_IN_USE
                : socketError == SocketError.AccessDenied
                    ? PrerequisitesCheckResultCode.PORT_ACCESS_DENIED
                    : PrerequisitesCheckResultCode.UNKNOWN;

            _logger.Warn(
                "UDP port {0} cannot be bound for {1}. Socket error: {2}.",
                port,
                game.Name,
                socketError);
            this.onGameInitializeFailed?.Invoke(game, failureCode, new List<object> { port });
            return;
        }

        try
        {
            ResetDamageSoundCooldowns();
            if (!game.GameDataReader.Initialize(game))
            {
                this.onGameInitializeFailed?.Invoke(game, PrerequisitesCheckResultCode.UNKNOWN, null);
                return;
            }

            // Inform the overlay that game data can now be received.
            game.GameDataReader.onCarDamaged += carDamagedEventHandler;
            game.GameDataReader.onNewGameData += newGameDataEventHander;
            game.GameDataReader.onGameStateChanged += this.gamestateChangedHandler;
            if (game.GameDataReader is IGameLivePacenoteSource livePacenoteSource)
            {
                livePacenoteSource.onLivePacenote += livePacenoteHandler;
            }
            game.GameDataReader.onGameDataAvailabilityChanged += gameDataAvailabilityChangedHandler;
            game.GameDataReader.onCarReset += carResetHandler;
            _logger.Info("Game {0} initialized.", game.Name);
            game.IsInitialized = true;
            this.onGameInitialized?.Invoke(game);
            ObsManager.Instance.Connect();
        }
        catch (PortAlreadyInUseException exception)
        {
            _logger.Warn(exception, "UDP port {0} is already in use while initializing {1}.", exception.Port, game.Name);
            this.onGameInitializeFailed?.Invoke(
                game,
                PrerequisitesCheckResultCode.PORT_ALREADY_IN_USE,
                new List<object> { exception.Port });
            return;
        }
        catch (PortAccessDeniedException exception)
        {
            _logger.Warn(exception, "Windows denied access to UDP port {0} while initializing {1}.", exception.Port, game.Name);
            this.onGameInitializeFailed?.Invoke(
                game,
                PrerequisitesCheckResultCode.PORT_ACCESS_DENIED,
                new List<object> { exception.Port });
            return;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to initialize game {0}.", game.Name);
            this.onGameInitializeFailed?.Invoke(game, PrerequisitesCheckResultCode.UNKNOWN, null);
            return;
        }

        if (code != PrerequisitesCheckResultCode.OK)
        {
            // The reader is ready, but the game configuration still needs user attention.
            this.onGameInitializeFailed?.Invoke(game, code, res.Params);
        }
    }

    private static bool TryGetUdpPortFailure(IGame game, out int port, out SocketError socketError)
    {
        port = 0;
        socketError = SocketError.Success;

        if (!game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) ||
            configuration is not UdpGameConfig udpConfig ||
            !IPAddress.TryParse(udpConfig.IPAddress, out var ipAddress))
        {
            return false;
        }

        port = udpConfig.Port;
        return !UdpPortFinder.CanBind(ipAddress, port, out socketError);
    }

    private void uninitializeGame(IGame game)
    {
        if (game == null)
            return;

        _logger.Info("Trying to uninitialize game: " + game.Name);
        try
        {
            ResetDamageSoundCooldowns();
            game.GameDataReader.onCarDamaged -= carDamagedEventHandler;
            game.GameDataReader.onNewGameData -= newGameDataEventHander;
            game.GameDataReader.onGameStateChanged -= this.gamestateChangedHandler;
            if (game.GameDataReader is IGameLivePacenoteSource livePacenoteSource)
            {
                livePacenoteSource.onLivePacenote -= livePacenoteHandler;
            }
            game.GameDataReader.onGameDataAvailabilityChanged -= gameDataAvailabilityChangedHandler;
            game.GameDataReader.onCarReset -= carResetHandler;
            game.GameDataReader.Uninitialize(game);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to uninitialize game {0}.", game.Name);
        }
        finally
        {
            game.IsInitialized = false;
            _logger.Info("Stop OBS recording when game uninitialized no matter if it is recording.");

            try
            {
                ObsManager.Instance.AbnormallyStopRecording();
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to stop OBS recording while uninitializing {0}.", game.Name);
            }

            try
            {
                this.onGameUninitialized?.Invoke(game);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Game uninitialized handler failed for {0}.", game.Name);
            }
        }
    }

#region EventHandlers
    private void gamestateChangedHandler(GameStateChangeEvent evt)
    {
        var lastState = evt.LastGameState;
        var state = evt.NewGameState;
        var shouldPlayExternalPacenotes = ShouldPlayExternalPacenotes(_currentGame);
        _logger.Debug("Game state changed from {0} to {1}", lastState, state);
        this.onGameStateChanged?.Invoke(_currentGame, evt);
        //TODO: update UI game state (in-game state)
        switch (state)
        {
            case GameState.Unknown:
                //TODO raise enable profile, codriver, replay_device switch
                //TODO raise overlay dismiss
                break;
            case GameState.RaceEnd:
                // end recording, unload trace loaded?
                
                if (lastState == GameState.Racing) {
                    
                    // GoogleAnalyticsHelper.Instance.TrackRaceEvent("race_end");
                    this.onRaceEnd?.Invoke(_currentGame);
                    if (shouldPlayExternalPacenotes && Config.Instance.PlayStartAndEndSound)
                    {
                        var finishState = evt.Parameters.TryGetValue(GameStateRaceEndProperty.FINISH_STATE, out var finishStateValue)
                            && finishStateValue is GameStateRaceEnd typedFinishState
                            ? typedFinishState
                            : GameStateRaceEnd.Unknown;
                        var endSound = finishState == GameStateRaceEnd.Disqualified
                            ? Constants.SYSTEM_RETIRED
                            : Constants.SYSTEM_END_STAGE;
                        this._profileManager.PlaySystem(endSound, true, false);
                    }

                    saveReplay(evt);
                }

                // disable telemetry hud, show statistics?

                break;
            case GameState.RaceBegin:
            case GameState.AdHocRaceBegin:
                // load trace, use lastmsg tracklength & startZ
                // this._udpReceiver.LastMessage.TrackLength
                this.onRaceBegin?.Invoke(_currentGame);
                if (lastState != GameState.Paused)
                {
                    // GoogleAnalyticsHelper.Instance.TrackRaceEvent("race_begin", this._currentGame.Name + " - " + this._profileManager.CurrentCoDriverSoundPackageInfo.DisplayText);
                    // no matter if OBS is recording, we stop recording here.
                    _logger.Info("Stop recording video with OBS at racebegin in case of the last session ended abnormally.");
                    ObsManager.Instance.AbnormallyStopRecording();
                }
                this._trackName = this._currentGame.GameDataReader.TrackName;
                this._carName = this._currentGame.GameDataReader.CarName;
                this._carClass = this._currentGame.GameDataReader.CarClass;
                //TODO: update UI trackname
                this.onTrackAndCarChanged?.Invoke(_currentGame, this._trackName, this._carName, this._carClass);
                
                // check if it is replay
                if (evt.Parameters.ContainsKey(GameStateRaceBeginProperty.IS_REPLAY))
                {
                    // is replay
                    this._isReplaySession = (bool)evt.Parameters[GameStateRaceBeginProperty.IS_REPLAY];
                }
                else
                {
                    this._isReplaySession = false;
                }
                
                var worker = Task.Run(() => {
                    try
                    {
                    //TODO: raise overlay text changes

                    if (shouldPlayExternalPacenotes && lastState != GameState.Paused && Config.Instance.PlayStartAndEndSound && state == GameState.RaceBegin)
                    {
                        // play start sound as sequential system sound
                        this._profileManager.PlaySystem(Constants.SYSTEM_START_STAGE, true, true);
                    }
                    
                    if (shouldPlayExternalPacenotes)
                    {
                        // 1. load sounds
                        this._profileManager.StartReplaying(_currentGame, this._trackName);

                        // 2. play minus script sound when in race begin but not AdhocRaceBegin
                        if (state == GameState.RaceBegin)
                        {
                            // play minus distance script sound, some corners before "GO"
                            this._profileManager.PlayMinusScript();
                        }
                    }

                    if (shouldPlayExternalPacenotes && state == GameState.AdHocRaceBegin)
                    {
                        // relocate the current car's position
                        var distance = this._currentGame.GameDataReader.CurrentGameData.LapDistance;
                        _logger.Info($"tring to relocate the pacenote based on the car's position {distance}");
                        this._profileManager.ReIndex(distance);
                    }

                    //TODO: change overlay script type
                    this.onRaceBegined?.Invoke(_currentGame);
                    }
                    catch (Exception exception)
                    {
                        _logger.Error(exception, "Failed to prepare pacenotes for {0} on {1}.", _currentGame?.Name, _trackName);
                    }
                });

                if (state == GameState.AdHocRaceBegin) {
                    // also start to record the replay when in AdHocRaceBegin
                    initReplay();
                }

                break;
            case GameState.Racing:
                if (lastState == GameState.RaceBegin || lastState == GameState.CountingDown)
                {
                    if (shouldPlayExternalPacenotes && Config.Instance.PlayGoSound) {
                        // just go !
                        this._profileManager.PlaySystem(Constants.SYSTEM_GO, true);
                    }

                    // start to record the replay
                    initReplay();

                    // start to record the video with OBS!
                    // Should try to connect to Obs when 
                    // 1. this tool started
                    // 2. game initialized
                    // 3. toggle the obs saving switch
                    if (!this._isReplaySession) {
                        // it's not replay session, start to record the video
                        ObsManager.Instance.StartRecording();
                    }
                    
                    this._trackLength = this._currentGame.GameDataReader.CurrentGameData.TrackLength;
                    _logger.Info("Setting Track length when start racing: " + this._trackLength);
                }
                if (shouldPlayExternalPacenotes && lastState == GameState.Paused) {
                    // paused to racing, probably adhoc, need to relocate pacenotes
                    var distance = this._currentGame.GameDataReader.CurrentGameData.LapDistance;
                    _logger.Info($"game state changed to racing from paused, if in RBR need to relocate pacenote, tring to relocate the pacenote based on the car's position {distance}");
                    this._profileManager.ReIndex(distance);
                }
                break;
            case GameState.CountingDown:
                if (shouldPlayExternalPacenotes && evt.Parameters != null &&
                    evt.Parameters.TryGetValue("number", out var countdownValue) &&
                    countdownValue is int countdownNumber &&
                    countdownNumber >= 1 && countdownNumber <= Constants.SYSTEM_COUNTDOWNS.Count)
                {
                    this._profileManager.PlaySystem(Constants.SYSTEM_COUNTDOWNS[countdownNumber - 1]);
                }
                break;
        }
    }

    private static bool ShouldPlayExternalPacenotes(IGame? game)
    {
        return game is not IGamePacenotePlaybackControl playbackControl ||
               playbackControl.EnableExternalPacenotes;
    }

    private void gameDataAvailabilityChangedHandler(bool obj)
    {
        if (!obj) 
        {
            // no data, data link grey out.
        }
    }

    private void PublishPacenoteDispatch(PacenotePlaybackSnapshot snapshot)
    {
        try
        {
            onPacenoteDispatched?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Pacenote dispatch listener failed.");
        }
    }

    private void newGameDataEventHander(GameData oldData, GameData msg)
    {
        if (float.IsFinite(msg.TrackLength) && msg.TrackLength > 0)
        {
            _currentReplayTrackLength = msg.TrackLength;
        }

        var isRacing = _currentGame.GameDataReader.GameState == GameState.Racing;
        if (isRacing)
        {
            // Keep the current-run lookup in sync before dashboards consume this packet.
            traceReplay(msg);
        }

        // TODO: update UI and Overlay telemetry data
        this.onNewGameData?.Invoke(_currentGame, msg);
        if (!isRacing || !ShouldPlayExternalPacenotes(_currentGame))
        {
            // wont play if it's not in racing state
            return;
        }

        // play sound
        var worker = Task.Run(() => {

            // play in threads. why??? this may cost a lot !!!
            // play sound (maybe state not changed and audio files not loaded.)
            if (this._profileManager.CurrentAudioFile != null)
            {
                var spdMperS = msg.Speed / 3.6f;
                var playPoint = this._profileManager.CurrentAudioFile.Distance + this._playpointAdjust;
                var currentPoint = msg.LapDistance +
                                    spdMperS * (0 - this._scriptTiming);

                if (
                    this._profileManager.CurrentScriptReader != null &&
                    this._profileManager.CurrentScriptReader.IsDynamic)
                {
                    currentPoint += spdMperS * Config.Instance.ScriptMode_PlaySecondsAdvanced;
                }

                if (currentPoint >= playPoint && this._currentGame != null && this._currentGame.GameDataReader.GameState == GameState.Racing)
                {
                    // set spd to 1.0
                    this._profileManager.CurrentPlaySpeed = 1.0f;
                    // can play
                    if (Config.Instance.UseDynamicPlaybackSpeed && this._profileManager.NextAudioFile != null)
                    {
                        var nextPlayPoint = this._profileManager.NextAudioFile.Distance + this._playpointAdjust;
                        var diff = currentPoint +
                                    spdMperS * this._profileManager.CurrentAudioFile.Sound.Duration /
                                    this._playbackSpd
                                    - nextPlayPoint;
                        if (diff >= 0 && (nextPlayPoint - currentPoint) > 0)
                        {
                            this._profileManager.CurrentPlaySpeed =
                                (this._profileManager.CurrentAudioFile.Sound.Duration / this._playbackSpd)
                                / ((float)(nextPlayPoint - currentPoint) / spdMperS);
                        }
                    }

                    this._profileManager.CurrentPlaySpeed *= this._playbackSpd;

                    if (Config.Instance.UseDynamicVolume)
                    {// more speed, more tension
                        this._profileManager.CurrentTension = msg.Speed / 200f;
                    }
                    _logger.Trace("Play sound at {0} because current point is {1} and calculated play point is {2}", msg.LapDistance, currentPoint, playPoint);
                    var record = this._profileManager.CurrentAudioFile?.PacenoteRecord;
                    if (this._profileManager.Play() && record != null)
                    {
                        PublishPacenoteDispatch(PacenotePlaybackSnapshot.FromRecord(
                            record,
                            "script",
                            this._profileManager.CurrentScriptPath));
                    }
                }
            }
        });
    }

    private void livePacenoteHandler(LivePacenoteEvent evt)
    {
        var game = _currentGame;
        if (game is null || !ShouldPlayExternalPacenotes(game))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(evt.SystemSoundToken))
        {
            if (evt.SystemSoundToken == Constants.SYSTEM_GO && !Config.Instance.PlayGoSound)
            {
                return;
            }

            _profileManager.PlaySystem(evt.SystemSoundToken, evt.SystemSoundSequential);
            return;
        }

        if (game.GameDataReader.GameState != GameState.Racing || evt.Record.Pacenotes.Count == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                if (!ReferenceEquals(_currentGame, game) || game.GameDataReader.GameState != GameState.Racing)
                {
                    return;
                }

                if (_profileManager.PlayLivePacenote(evt.Record))
                {
                    PublishPacenoteDispatch(PacenotePlaybackSnapshot.FromRecord(
                        evt.Record,
                        "live",
                        stageId: evt.StageId));
                }
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to play live pacenote {0} for {1}.", evt.Id, game.Name);
            }
        });
    }

    private void carDamagedEventHandler(CarDamageEvent evt)
    {
        if (!ShouldPlayExternalPacenotes(_currentGame))
        {
            return;
        }

        if (!CarDamageSystemSoundResolver.TryResolve(evt, out var systemSound))
        {
            return;
        }

        if (systemSound.RequiresDamageSoundEnabled && !Config.Instance.PlayCollisionSound)
        {
            return;
        }

        if (!TryAcquireDamageSoundCooldown(systemSound))
        {
            return;
        }

        _profileManager.PlaySystem(systemSound.Token);
    }

    private bool TryAcquireDamageSoundCooldown(CarDamageSystemSound systemSound)
    {
        var cooldown = systemSound.CooldownKey switch
        {
            "collision" => CollisionSoundCooldown,
            _ when systemSound.CooldownKey.StartsWith("mechanical:", StringComparison.Ordinal) => MechanicalDamageSoundCooldown,
            _ => TimeSpan.Zero
        };
        if (cooldown == TimeSpan.Zero)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        lock (_damageSoundCooldownLock)
        {
            if (_lastDamageSoundUtc.TryGetValue(systemSound.CooldownKey, out var previous) &&
                now - previous < cooldown)
            {
                return false;
            }

            _lastDamageSoundUtc[systemSound.CooldownKey] = now;
            return true;
        }
    }

    private void ResetDamageSoundCooldowns()
    {
        lock (_damageSoundCooldownLock)
        {
            _lastDamageSoundUtc.Clear();
        }
    }

    private void carResetHandler() {
        if (!ShouldPlayExternalPacenotes(_currentGame))
        {
            return;
        }

        var distance = this._currentGame.GameDataReader.CurrentGameData.LapDistance;
        _logger.Info($"tring to relocate the pacenote based on the car's position {distance} because of car reset.");
        this._profileManager.ReIndex(distance);
    }

#endregion


#region Replay
    private async void initReplay() {
        _currentReplayTrackLength = 0;
        this.ReplayDetailsPerCheckpoints.Clear();
        this.ReplayDetailsPerTimes.Clear();
        this.ReplayDetailsPerCheckpoints.Add(new ReplayDetailsPerCheckpoint() {
            checkpoint = 0,
            time = 0,
            distance = 0
        });
        this.ReplayDetailsPerTimes.Add(new ReplayDetailsPerTime() {
            time = 0,
            distance = 0,
            timestamp = 0,
        });
        InvalidateCurrentReplayLookup();

        Replay replay = await ReplayManager.Instance.GetBestReplay(CurrentGame, this._trackName, this._carClass, this._carName);
        
        if (replay != null) {
            this.BestLocalReplayDetailsPerCheckpoints = await ReplayManager.Instance.getReplayDetailsPerCheckpoint(replay.id);
            this.BestLocalReplayDetailsPerTimes = await ReplayManager.Instance.getReplayDetailsPerTime(replay.id);
            this.BestLocalReplay = replay;
            InvalidateBestLocalReplayLookup();
            if (this.BestLocalReplayDetailsPerCheckpoints.Count > 0 && this.BestLocalReplayDetailsPerTimes.Count > 0) {
                this.onLocalReplayLoaded?.Invoke(true);
                _logger.Info($"Best local replay loaded for {replay.track} - {replay.car_class}");
                return;
            }
        }
    
        _logger.Info("No best local replay found.");
        this.BestLocalReplay = null;
        this.BestLocalReplayDetailsPerCheckpoints.Clear();
        this.BestLocalReplayDetailsPerTimes.Clear();
        InvalidateBestLocalReplayLookup();
        this.onLocalReplayLoaded?.Invoke(false);
    }
    private void traceReplay(GameData msg) {
        if (TraceReplaySample(msg)) {
            return;
        }
        
        if (Config.Instance.ReplaySave)
        {
            // save ReplayDetailsPerTimes
            var index = (int)(msg.LapTime * 1000) / Config.Instance.ReplaySaveInterval;
            // if save without interval, save all
            if (Config.Instance.ReplaySaveWithoutInterval || index >= this.ReplayDetailsPerTimes.Count)
            {   // 妙啊
                this.ReplayDetailsPerTimes.Add(new ReplayDetailsPerTime()
                {
                    time = msg.LapTime,
                    distance = msg.LapDistance,
                    timestamp = msg.TimeStamp.Ticks,
                    speed = msg.Speed,
                    rpm = msg.RPM,
                    gear = msg.Gear,
                    throttle = msg.Throttle,
                    brake = msg.Brake,
                    clutch = msg.Clutch,
                    handbrake = msg.HandBrake,
                    completion_rate = msg.CompletionRate,
                    steering = msg.Steering,
                    max_gears = msg.MaxGears,
                    max_rpm = msg.MaxRPM,
                    pos_x = msg.PosX,
                    pos_y = msg.PosY,
                    pos_z = msg.PosZ,
                    g_long = msg.G_long,
                    g_lat = msg.G_lat,
                    suspension_rear_left = msg.SuspensionRearLeft,
                    suspension_rear_right = msg.SuspensionRearRight,
                    suspension_front_left = msg.SuspensionFrontLeft,
                    suspension_front_right = msg.SuspensionFrontRight,
                    suspension_speed_rear_left = msg.SuspensionSpeedRearLeft,
                    suspension_speed_rear_right = msg.SuspensionSpeedRearRight,
                    suspension_speed_front_left = msg.SuspensionSpeedFrontLeft,
                    suspension_speed_front_right = msg.SuspensionSpeedFrontRight,
                    brake_temp_rear_left = msg.BrakeTempRearLeft,
                    brake_temp_rear_right = msg.BrakeTempRearRight,
                    brake_temp_front_left = msg.BrakeTempFrontLeft,
                    brake_temp_front_right = msg.BrakeTempFrontRight
                });
            }

            // save ReplayDetailsPerCheckpoints
            float splitLength = msg.TrackLength / Config.Instance.ReplaySaveGranularity;
            var checkpointIndex = (int)(msg.LapDistance / splitLength);
            if (checkpointIndex >= this.ReplayDetailsPerCheckpoints.Count && checkpointIndex < Config.Instance.ReplaySaveGranularity)
            {
                this.ReplayDetailsPerCheckpoints.Add(new ReplayDetailsPerCheckpoint()
                {
                    checkpoint = checkpointIndex,
                    time = msg.LapTime,
                    distance = msg.LapDistance
                });
            }
        }
    }

    private bool TraceReplaySample(GameData msg) {
        var replaySaveInterval = Math.Max(1, Config.Instance.ReplaySaveInterval);
        var replaySaveGranularity = Math.Max(1, Config.Instance.ReplaySaveGranularity);

        var index = (int)(msg.LapTime * 1000) / replaySaveInterval;
        if (Config.Instance.ReplaySaveWithoutInterval || index >= this.ReplayDetailsPerTimes.Count)
        {
            this.ReplayDetailsPerTimes.Add(CreateReplayDetailsPerTime(msg, Config.Instance.ReplaySave));
            InvalidateCurrentReplayLookup();
        }

        if (Config.Instance.ReplaySave)
        {
            float splitLength = msg.TrackLength / replaySaveGranularity;
            if (splitLength <= 0) {
                return true;
            }

            var checkpointIndex = (int)(msg.LapDistance / splitLength);
            if (checkpointIndex >= this.ReplayDetailsPerCheckpoints.Count && checkpointIndex < replaySaveGranularity)
            {
                this.ReplayDetailsPerCheckpoints.Add(new ReplayDetailsPerCheckpoint()
                {
                    checkpoint = checkpointIndex,
                    time = msg.LapTime,
                    distance = msg.LapDistance
                });
            }
        }

        return true;
    }

    private ReplayDetailsPerTime CreateReplayDetailsPerTime(GameData msg, bool includeTelemetry) {
        var detail = new ReplayDetailsPerTime()
        {
            time = msg.LapTime,
            distance = msg.LapDistance,
            timestamp = msg.TimeStamp.Ticks
        };

        if (!includeTelemetry) {
            return detail;
        }

        detail.speed = msg.Speed;
        detail.rpm = msg.RPM;
        detail.gear = msg.Gear;
        detail.throttle = msg.Throttle;
        detail.brake = msg.Brake;
        detail.clutch = msg.Clutch;
        detail.handbrake = msg.HandBrake;
        detail.completion_rate = msg.CompletionRate;
        detail.steering = msg.Steering;
        detail.max_gears = msg.MaxGears;
        detail.max_rpm = msg.MaxRPM;
        detail.pos_x = msg.PosX;
        detail.pos_y = msg.PosY;
        detail.pos_z = msg.PosZ;
        detail.g_long = msg.G_long;
        detail.g_lat = msg.G_lat;
        detail.suspension_rear_left = msg.SuspensionRearLeft;
        detail.suspension_rear_right = msg.SuspensionRearRight;
        detail.suspension_front_left = msg.SuspensionFrontLeft;
        detail.suspension_front_right = msg.SuspensionFrontRight;
        detail.suspension_speed_rear_left = msg.SuspensionSpeedRearLeft;
        detail.suspension_speed_rear_right = msg.SuspensionSpeedRearRight;
        detail.suspension_speed_front_left = msg.SuspensionSpeedFrontLeft;
        detail.suspension_speed_front_right = msg.SuspensionSpeedFrontRight;
        detail.brake_temp_rear_left = msg.BrakeTempRearLeft;
        detail.brake_temp_rear_right = msg.BrakeTempRearRight;
        detail.brake_temp_front_left = msg.BrakeTempFrontLeft;
        detail.brake_temp_front_right = msg.BrakeTempFrontRight;
        return detail;
    }

    private void saveReplay(GameStateChangeEvent evt) {
        if (evt.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_STATE)) {
            // finished with state, check if it's normal finish, if so, save the replay
            var finishState = (GameStateRaceEnd)evt.Parameters[GameStateRaceEndProperty.FINISH_STATE];
            if (finishState == GameStateRaceEnd.Normal) {
                if (Config.Instance.ReplaySave) {
                    // save replay
                    var replay = new Replay();
                    replay.car = this._carName;
                    replay.track = this._trackName;
                    replay.checkpoints = Math.Max(1, Config.Instance.ReplaySaveGranularity);
                    replay.date = DateTime.UtcNow;
                    replay.locked = false;
                    replay.track_length = this._trackLength;
                    replay.finish_time = (float)evt.Parameters[GameStateRaceEndProperty.FINISH_TIME];
                    replay.retired = false;
                    replay.car_class = this._carClass;
                    replay.comment = "";
                    replay.video_path = ObsManager.Instance.StopRecording();
                    replay.video_begin_timestamp = Config.Instance.ReplayOBSSave ? this._obsStartTimestamp : 0;
                    // replay.video_end_timestamp = this._obsStopTimestamp;
                    _ = ReplayManager.Instance.SaveReplayAsync(CurrentGame, replay, this.ReplayDetailsPerTimes, this.ReplayDetailsPerCheckpoints);
                }
            }
            else {
                ObsManager.Instance.AbnormallyStopRecording();
            }
        } else {
            // not normal finish, stop recording
            ObsManager.Instance.AbnormallyStopRecording();
        }
    }

    public float GetDelta(float distance) {
        float time = GetCurrentReplayLookup().GetTimeByDistance(distance);
        float bestTime = GetBestTimeByDistance(distance);
        return time - bestTime;
    }

    public float GetDeltaByDistanceAndTime(float distance, float time) {
        float bestTime = GetBestTimeByDistance(distance);
        return time - bestTime;
    }

    public float GetDeltaByCheckpoint(int checkpoint) {
        float time = getTimeByCheckpoint(this.ReplayDetailsPerCheckpoints, checkpoint);
        float bestTime = getTimeByCheckpoint(this.BestLocalReplayDetailsPerCheckpoints, checkpoint);
        return time - bestTime;
    }

    public float GetTimeByDistance(float distance) {
        return GetCurrentReplayLookup().GetTimeByDistance(distance);
    }

    public float GetBestTimeByDistance(float distance) {
        var bestLookup = GetBestLocalReplayLookup();
        var bestTrackLength = ReplayComparisonCalculator.GetReferenceTrackLength(this.BestLocalReplay, bestLookup);
        var bestDistance = ReplayComparisonCalculator.MapCurrentDistanceToReferenceDistance(
            distance,
            GetCurrentReplayTrackLength(),
            bestTrackLength);
        return bestLookup.GetTimeByDistance(bestDistance);
    }

    public float GetBestDistanceByTime(float time) {
        var bestLookup = GetBestLocalReplayLookup();
        var bestTrackLength = ReplayComparisonCalculator.GetReferenceTrackLength(this.BestLocalReplay, bestLookup);
        var bestDistance = bestLookup.GetDistanceByTime(time);
        return ReplayComparisonCalculator.MapReferenceDistanceToCurrentDistance(
            bestDistance,
            GetCurrentReplayTrackLength(),
            bestTrackLength);
    }

    public ReplayComparisonSnapshot GetReplayComparison(GameData currentData) {
        if (this.BestLocalReplay == null || this.BestLocalReplayDetailsPerTimes.Count == 0) {
            return ReplayComparisonSnapshot.Unavailable;
        }

        return ReplayComparisonCalculator.Create(
            currentData,
            this.BestLocalReplay,
            GetBestLocalReplayLookup());
    }

    private void InvalidateCurrentReplayLookup() {
        _currentReplayLookupCount = -1;
    }

    private void InvalidateBestLocalReplayLookup() {
        _bestLocalReplayLookupCount = -1;
    }

    private ReplayTimelineLookup GetCurrentReplayLookup() {
        if (_currentReplayLookup == null || _currentReplayLookupCount != this.ReplayDetailsPerTimes.Count) {
            _currentReplayLookup = ReplayTimelineLookup.From(this.ReplayDetailsPerTimes);
            _currentReplayLookupCount = this.ReplayDetailsPerTimes.Count;
        }

        return _currentReplayLookup;
    }

    private ReplayTimelineLookup GetBestLocalReplayLookup() {
        if (_bestLocalReplayLookup == null || _bestLocalReplayLookupCount != this.BestLocalReplayDetailsPerTimes.Count) {
            _bestLocalReplayLookup = ReplayTimelineLookup.From(
                this.BestLocalReplayDetailsPerTimes,
                ReplayComparisonCalculator.CreateTerminalSample(this.BestLocalReplay));
            _bestLocalReplayLookupCount = this.BestLocalReplayDetailsPerTimes.Count;
        }

        return _bestLocalReplayLookup;
    }

    private float GetCurrentReplayTrackLength() {
        if (float.IsFinite(_currentReplayTrackLength) && _currentReplayTrackLength > 0) {
            return _currentReplayTrackLength;
        }

        return float.IsFinite(_trackLength) && _trackLength > 0 ? _trackLength : 0;
    }

    private float getTimeByCheckpoint(List<ReplayDetailsPerCheckpoint> details, int checkpoint) {
        float time = 0;
        if (details.Count == 0) {
            time = 0;
        }
        if (checkpoint <= details[0].checkpoint) {
            time = 0;
        }
        if (checkpoint >= details[details.Count - 1].checkpoint) {
            time = details[details.Count - 1].time;
        }
        var times = details.Select(r => r.time).ToList();
        var checkpoints = details.Select(r => r.checkpoint).ToList();
        var index = checkpoints.BinarySearch(checkpoint);
        if (index >= 0) {
            time = times[index];
        } else {
            index = ~index;
            if (index == 0) {
                time = 0;
            } else if (index == checkpoints.Count) {
                time = times[times.Count - 1];
            } else {
                time = times[index - 1] + (times[index] - times[index - 1]) / (checkpoints[index] - checkpoints[index - 1]) * (checkpoint - checkpoints[index - 1]);
            }
        }
        return time;
    }
#endregion

    private PrerequisitesCheckResult checkPrerequisite(IGame game)
    {
        // check the file
        var preCheck = game.GamePrerequisiteChecker;
        var checkResult = preCheck.CheckPrerequisites(game);
        switch (checkResult.Code)
        {
            case PrerequisitesCheckResultCode.PORT_NOT_OPEN:
                

                break;
            case PrerequisitesCheckResultCode.PORT_NOT_MATCH:
                

                break;
            case PrerequisitesCheckResultCode.GAME_NOT_INSTALLED:
                break;
        }
        return checkResult;
    }

#endregion

#region Sets

    public async Task<bool> SetGame(IGame game)
    {
        bool gameRunning = false;
        uninitializeGame(_currentGame);
        // IsWatchedProcessRunning uses the watcher's configured window-title match, so this
        // compensation only starts after the game's window is visible enough to identify.
        // GameOverlayManager then waits for a usable HWND before creating the overlay.
        Process watchedProcess = _processWatcher.IsWatchedProcessRunning(game.Executable, game.WindowTitle);
        if (watchedProcess != null) {
            game.IsRunning = true;
            this.onGameStarted?.Invoke(game, watchedProcess);
            gameRunning = true;
        }

        this._currentGame = game;

        _logger.Info("Game selection changed to {0}, trying to initialize it.", this._currentGame.Name);
        initializeGame(_currentGame);
        return gameRunning;
    }

    public void SetFromConfiguration() {
        this.SetProfile(Config.Instance.UI_SelectedProfile);
        this.SetOutputDevice(Config.Instance.UI_SelectedPlaybackDevice);
        var codriverInfos = this.CoDriverPackages.Select(package => package.Info).ToList();
        var codriverIndex = CoDriverSelectionIdentity.ResolveIndex(
            codriverInfos,
            Config.Instance.UI_SelectedAudioPackageIdentity,
            Config.Instance.UI_SelectedAudioPackage);

        if (codriverIndex < 0 && codriverInfos.Count > 0)
        {
            codriverIndex = 0;
        }

        if (codriverIndex >= 0)
        {
            this.SetCodriver(codriverIndex);
            var selectedIdentity = CoDriverSelectionIdentity.Create(codriverInfos[codriverIndex]);
            var needsMigration = Config.Instance.UI_SelectedAudioPackage != codriverIndex ||
                                 !string.Equals(
                                     Config.Instance.UI_SelectedAudioPackageIdentity,
                                     selectedIdentity,
                                     StringComparison.OrdinalIgnoreCase);

            if (needsMigration)
            {
                Config.Instance.UI_SelectedAudioPackage = codriverIndex;
                Config.Instance.UI_SelectedAudioPackageIdentity = selectedIdentity;
                Config.Instance.SaveUserConfig();
            }
        }

        this.SetMuted(Config.Instance.UI_Mute);
        // this.SetGame(Config.Instance.UI_SelectedGame);
    }

    public void SetGame(int gameIndex) {
        if (gameIndex < this.Games.Count)
            _ = this.SetGame(this.Games[gameIndex]);
        else
            _ = this.SetGame(this.Games.First());
    }

    public void SetProfile(int profileIndex) {
        if (profileIndex < this.Profiles.Count && profileIndex >= 0)
            this._profileManager.CurrentProfile = this.Profiles[profileIndex].ToString().Split('\\').Last();
        else
            this._profileManager.CurrentProfile = this.Profiles.First().Split('\\').Last();
    }

    public void SetOutputDevice(int outputDeviceIndex) {
        if (outputDeviceIndex < this.OutputDevices.Count && outputDeviceIndex >= 0)
            this._profileManager.CurrentPlayDeviceId = outputDeviceIndex;
        else
            this._profileManager.CurrentPlayDeviceId = 0;
    }

    public bool TrySetCodriver(string selectionIdentity)
    {
        var codriverIndex = CoDriverSelectionIdentity.ResolveIndex(
            this.CoDriverPackages.Select(package => package.Info).ToList(),
            selectionIdentity,
            -1);

        if (codriverIndex < 0)
        {
            return false;
        }

        this.SetCodriver(codriverIndex);
        return true;
    }

    public void SetCodriver(int codriverIndex) {
        if (this.CoDriverPackages.Count == 0)
        {
            _logger.Warn("No codriver packages are available for selection.");
            return;
        }

        var selectedIndex = codriverIndex < this.CoDriverPackages.Count && codriverIndex >= 0
            ? codriverIndex
            : 0;
        this._profileManager.CurrentCoDriverSoundPackagePath = this.CoDriverPackages[selectedIndex].Info.Path;
    }

    public void SetPlayBackSpeed(float speed) {
        this._playbackSpd = speed;
    }

    public void SetPlaybackAdjustSeconds(float seconds) {
        this._scriptTiming = seconds;
    }

    public void SetPlaybackVolume(int volume) {
        this._profileManager.CurrentPlayAmplification = volume;
    }

    public void SetMuted(bool muted)
    {
        this._profileManager?.SetMuted(muted);
    }

#endregion

    public void PlayExample() {
        this._profileManager.CurrentPlaySpeed = this._playbackSpd;
        this._profileManager.PlayExample();
    }

    public void PlaySound(string path) {
        this._profileManager.CurrentPlaySpeed = this._playbackSpd;
        this._profileManager.PlaySound(new AutoResampledCachedSound(path), isSequential: false);
    }
}

public static class Extensions {

    public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
    {
        // TODO: Argument validation
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }
}
