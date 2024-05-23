using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private double _scriptTiming = 0;
    private int _playpointAdjust = 0;
    private float _playbackSpd = 1.0f;
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private IGame _currentGame;

    public IGame CurrentGame => _currentGame;
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

    public event Action<IGame, string> onTrackNameChanged;

    public event Action<IGame, GameData> onNewGameData;

    public event Action<IGame> onRaceBegin;
    public event Action<IGame> onRaceBegined;

    public event Action<IGame> onRaceEnd;

    public string CurrentScriptAuthor => _profileManager?.CurrentScriptReader?.Author;

    public bool IsInitialized { private set; get; } = false;

    // init the tool, load settings, etc.
    public void Init() {
        this.loadProfileManager();
        this.loadGames();
        this.loadProfiles();
        this.loadCodrivers();
        this.loadOutputDevices();
        // this.initGoogleAnalytics();
        this.initializeProcessWatcher();
        this.IsInitialized = true;
        onToolInitialized?.Invoke();
    }

#region privates

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
            foreach (var file in Directory.EnumerateFiles(Constants.PATH_GAMES, "*.dll")) 
            {
                var assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(file));
                if (assembly.GetName().Name.Equals("ZTMZ.PacenoteTool.Base")) 
                {
                    continue;
                }
                var games = assembly.GetLoadableTypes().Where(t => typeof(IGame).IsAssignableFrom(t)).Select(i => (IGame)Activator.CreateInstance(i));
                this._games.AddRange(games);
            }
            this._games.Sort((g1, g2) => g1.Order.CompareTo(g2.Order));
            this._games.ForEach(g => g.GameConfigurations = Config.Instance.LoadGameConfig(g));
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
        _logger.Info($"Google Analytics is {(Config.Instance.EnableOnlineAnalytics ? "enabled" : "disabled")}");
    }

    private void initializeProcessWatcher()
    {
        _processWatcher = new ProcessWatcher(p => {
            // new process
            var g = _games.FirstOrDefault(g => g.Executable.ToLower().Equals(p.ProcessName.ToLower()));
            if (g == null)
                return;

            g.IsRunning = true;
            if (_currentGame == g) 
            {
                //TODO: raise game started event!!!
                //TODO: turn on the light, current game is running.
                //TODO: start game data pulling
                this.onGameStarted?.Invoke(_currentGame, p);
                
                _logger.Info("Got new process: {0}, trying to initialize game: {1}", p.ProcessName, _currentGame.Name);
                initializeGame(_currentGame);
            }
        }, (pName, pPath) => {
            var g = _games.FirstOrDefault(g => g.Executable.ToLower().Equals(pName));
            if (g == null)
                return;

            g.IsRunning = false;
            if (_currentGame == g) 
            {
                //TODO: raise game UI exit effect
                this.onGameEnded?.Invoke(_currentGame);
            }
            if (_currentGame.Name.Equals(g.Name))
            {
                //TODO: turn off the light, current game is exiting.
                uninitializeGame(_currentGame);
            }
        });

        // watch games starting
        foreach (var game in _games) 
        {
            _processWatcher.AddToWatch(game.Executable, game.WindowTitle);
        }

        // start the watch (threads started)
        _processWatcher.StartWatching();

        _logger.Info("ProcessWatcher started");
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
            //TODO: raise Game not install
            this.onGameInitializeFailed?.Invoke(game, code, null);
        } else {
            try {
                if (game.GameDataReader.Initialize(game))
                {
                    //TODO: inform the Overlay, game is ready to go.
                    game.GameDataReader.onCarDamaged += carDamagedEventHandler;
                    game.GameDataReader.onNewGameData += newGameDataEventHander;
                    game.GameDataReader.onGameStateChanged += this.gamestateChangedHandler;
                    game.GameDataReader.onGameDataAvailabilityChanged += gameDataAvailabilityChangedHandler;
                    _logger.Info("Game {0} initialized.", game.Name);
                    game.IsInitialized = true;
                    this.onGameInitialized?.Invoke(game);
                } else {
                    this.onGameInitializeFailed?.Invoke(game, PrerequisitesCheckResultCode.UNKNOWN, null);
                }
            } catch (Exception e) {
                if (e is PortAlreadyInUseException ex) 
                {
                    //TODO: raise port already in use
                    this.onGameInitializeFailed?.Invoke(game, PrerequisitesCheckResultCode.PORT_ALREADY_IN_USE, new List<object>{ex.Port});
                }
            }
            
            //TODO: raise UI game state.
            this.onGameInitializeFailed?.Invoke(game, code, res.Params);
        }
    }

    private void uninitializeGame(IGame game)
    {
        if (game == null)
            return;

        _logger.Info("Trying to uninitialize game: " + game.Name);
        game.GameDataReader.onCarDamaged -= carDamagedEventHandler;
        game.GameDataReader.onNewGameData -= newGameDataEventHander;
        game.GameDataReader.onGameStateChanged -= this.gamestateChangedHandler;
        game.GameDataReader.Uninitialize(game);
        game.IsInitialized = false;
        this.onGameUninitialized?.Invoke(game);
    }

    private void gamestateChangedHandler(GameStateChangeEvent evt)
    {
        var lastState = evt.LastGameState;
        var state = evt.NewGameState;
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
                    if (Config.Instance.PlayStartAndEndSound)
                    {
                        // play end sound
                        this._profileManager.PlaySystem(Constants.SYSTEM_END_STAGE);
                    }
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
                }
                this._trackName = this._currentGame.GameDataReader.TrackName;
                //TODO: update UI trackname
                this.onTrackNameChanged?.Invoke(_currentGame, this._trackName);
                // disable profile switch, replay device selection
                
                var worker = Task.Run(() => {
                    //TODO: raise overlay text changes

                    if (lastState != GameState.Paused && Config.Instance.PlayStartAndEndSound && state == GameState.RaceBegin)
                    {
                        // play start sound
                        this._profileManager.PlaySystem(Constants.SYSTEM_START_STAGE);
                    }
                    
                    // 1. load sounds
                    this._profileManager.StartReplaying(_currentGame, this._trackName);

                    if (state == GameState.AdHocRaceBegin) 
                    {
                        // relocate the current car's position
                        var distance = this._currentGame.GameDataReader.CurrentGameData.LapDistance;
                        _logger.Info($"tring to relocate the pacenote based on the car's position {distance}");
                        this._profileManager.ReIndex(distance);
                    }

                    //TODO: change overlay script type
                    this.onRaceBegined?.Invoke(_currentGame);
                });

                

                break;
            case GameState.Racing:
                if ((lastState == GameState.RaceBegin || lastState == GameState.CountingDown) && Config.Instance.PlayGoSound)
                {
                    // just go !
                    this._profileManager.PlaySystem(Constants.SYSTEM_GO, true);
                }
                break;
            case GameState.CountingDown:
                if (evt.Parameters != null) 
                {
                    this._profileManager.PlaySystem(Constants.SYSTEM_COUNTDOWNS[(int)evt.Parameters["number"] - 1]);
                }
                break;
        }
    }

    private void gameDataAvailabilityChangedHandler(bool obj)
    {
        if (!obj) 
        {
            // no data, data link grey out.
        }
    }

    private void newGameDataEventHander(GameData oldData, GameData msg)
    {
        //TODO: update UI and Overlay telemetry data
        this.onNewGameData?.Invoke(_currentGame, msg);
        if (_currentGame.GameDataReader.GameState != GameState.Racing) 
        {
            // wont play if it's not in racing state
            return;
        }

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

                if (currentPoint >= playPoint)
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
                    this._profileManager.Play();
                }
            }
        });
    }

    private void carDamagedEventHandler(CarDamageEvent evt)
    {
        
        switch (evt.DamageType) 
        {
            case CarDamage.Collision:
                var lvl = (int)evt.Parameters[CarDamageConstants.SEVERITY];
                //TODO: played in thread originally?
                this._profileManager.PlaySystem(Constants.SYSTEM_COLLISION[lvl]);
                
                break;
            case CarDamage.Wheel:
                var wheelIndex = (int)evt.Parameters[CarDamageConstants.WHEELINDEX];
                
                this._profileManager.PlaySystem(Constants.SYSTEM_PUNCTURE[wheelIndex]);
                
                break;
            default:
                break;
        }
    }

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

    public bool SetGame(IGame game)
    {
        bool gameRunning = false;
        uninitializeGame(_currentGame);
        // if the game was not the current game, and the game is running, need to trigger the gamestarted event
        // to trigger the game overlay.
        Process watchedProcess = _processWatcher.IsWatchedProcessRunning(game.Executable, game.WindowTitle);
        if (_currentGame != null && _currentGame != game && watchedProcess != null) {
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
        this.SetCodriver(Config.Instance.UI_SelectedAudioPackage);
        // this.SetGame(Config.Instance.UI_SelectedGame);
    }

    public void SetGame(int gameIndex) {
        if (gameIndex < this.Games.Count)
            this.SetGame(this.Games[gameIndex]);
        else
            this.SetGame(this.Games.First());
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

    public void SetCodriver(int codriverIndex) {
        if (codriverIndex < this.CoDriverPackages.Count && codriverIndex >= 0)
            this._profileManager.CurrentCoDriverSoundPackagePath = this.CoDriverPackages[codriverIndex].Info.Path;
        else
            this._profileManager.CurrentCoDriverSoundPackagePath = this.CoDriverPackages.First().Info.Path;
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

#endregion

    public void PlayExample() {
        this._profileManager.CurrentPlaySpeed = this._playbackSpd;
        this._profileManager.PlayExample();
    }

    public void PlaySound(string path) {
        this._profileManager.CurrentPlaySpeed = this._playbackSpd;
        this._profileManager.PlaySound(new AutoResampledCachedSound(path), true);
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


