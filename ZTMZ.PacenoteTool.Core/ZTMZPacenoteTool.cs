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
    private List<IGame> _games = new();

    public List<IGame> Games => _games;
    private List<string> _profiles = new();
    public List<string> Profiles => _profiles;
    private List<CoDriverPackage> _codriverPackages = new();
    public List<CoDriverPackage> CoDriverPackages => _codriverPackages;
    private List<string> _outputDevices = new();
    public List<string> OutputDevices => _outputDevices;

    public event Action<string> onStatusReport;

    // init the tool, load settings, etc.
    public void Init() {
        var jsonPaths = new List<string>{
                AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
            };
        I18NLoader.Instance.Initialize(jsonPaths);
        I18NLoader.Instance.SetCulture(Config.Instance.Language);
        GoogleAnalyticsHelper.Instance.TrackLaunchEvent("language", Config.Instance.Language);
        this.loadProfileManager();
        this.loadGames();
        this.loadProfiles();
        this.loadCodrivers();
        this.loadOutputDevices();
        this.initGoogleAnalytics();
        this.initializeProcessWatcher();
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
        foreach (var file in Directory.EnumerateFiles(Constants.PATH_GAMES, "*.dll")) 
        {
            var assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(file));
            if (assembly.GetName().Name.Equals("ZTMZ.PacenoteTool.Base")) 
            {
                continue;
            }
            var games = assembly.GetTypes().Where(t => typeof(IGame).IsAssignableFrom(t)).Select(i => (IGame)Activator.CreateInstance(i));
            this._games.AddRange(games);
        }
        this._games.Sort((g1, g2) => g1.Order.CompareTo(g2.Order));
        this._games.ForEach(g => g.GameConfigurations = Config.Instance.LoadGameConfig(g));
        _logger.Info($"{this._games.Count} games loaded.");
        this.onStatusReport?.Invoke("Games loaded.");
    }

    private void loadProfiles() {
        _logger.Info("Loading profiles...");
        this.onStatusReport?.Invoke("Loading profiles...");
        this._profiles = _profileManager.GetAllProfiles();
        _logger.Info($"{this._profiles.Count} profiles loaded.");
        this.onStatusReport?.Invoke("Profiles loaded.");
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
        this.onStatusReport?.Invoke("Output devices loaded.");
    }
    private void initGoogleAnalytics()
    {
        _logger.Info($"Google Analytics is {(Config.Instance.EnableGoogleAnalytics ? "enabled" : "disabled")}");
    }

    private void initializeProcessWatcher()
    {
        _processWatcher = new ProcessWatcher((pName, pPath) => {
            // new process
            var g = _games.FirstOrDefault(g => g.Executable.ToLower().Equals(pName));
            if (g == null)
                return;

            g.IsRunning = true;
            if (_currentGame == g) 
            {
                //TODO: raise game started event!!!
                //TODO: turn on the light, current game is running.
                //TODO: start game data pulling
                
                _logger.Info("Got new process: {0}, trying to initialize game: {1}", pName, _currentGame.Name);
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
            _processWatcher.AddToWatch(game.Executable);
        }

        // start the watch (threads started)
        _processWatcher.StartWatching();

        _logger.Info("ProcessWatcher started");
    }

    private void initializeGame(IGame game) 
    {
        if (game == null)
            return;

        // check prerequisite
        var res = checkPrerequisite(game);
        if (res == PrerequisitesCheckResultCode.GAME_NOT_INSTALLED)
        {
            //TODO: raise Game not install
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
                }    
            } catch (Exception e) {
                if (e is PortAlreadyInUseException) 
                {
                    //TODO: raise port already in use
                }
            }
            
            //TODO: raise UI game state.
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
    }

    private void gamestateChangedHandler(GameStateChangeEvent evt)
    {
        var lastState = evt.LastGameState;
        var state = evt.NewGameState;
        _logger.Debug("Game state changed from {0} to {1}", lastState, state);
        //TODO: update UI game state (in-game state)
        switch (state)
        {
            case GameState.Unknown:
                //TODO raise enable profile, codriver, replay_device switch
                //TODO raise overlay dismiss
                break;
            case GameState.RaceEnd:
                // end recording, unload trace loaded?
                GoogleAnalyticsHelper.Instance.TrackRaceEvent("race_end");
                
                if (lastState == GameState.Racing && Config.Instance.PlayStartAndEndSound)
                {
                    // play end sound
                    this._profileManager.PlaySystem(Constants.SYSTEM_END_STAGE);
                }

                // disable telemetry hud, show statistics?

                break;
            case GameState.RaceBegin:
            case GameState.AdHocRaceBegin:
                // load trace, use lastmsg tracklength & startZ
                // this._udpReceiver.LastMessage.TrackLength
                if (lastState != GameState.Paused)
                {
                    GoogleAnalyticsHelper.Instance.TrackRaceEvent("race_begin", this._currentGame.Name + " - " + this._profileManager.CurrentCoDriverSoundPackageInfo.DisplayText);
                }
                this._trackName = this._currentGame.GameDataReader.TrackName;
                //TODO: update UI trackname
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
                        this._profileManager.ReIndex(this._currentGame.GameDataReader.LastGameData.LapDistance);
                    }

                    //TODO: change overlay script type
                });
                

                break;
            case GameState.Racing:
                if ((lastState == GameState.RaceBegin || lastState == GameState.CountingDown) && Config.Instance.PlayGoSound)
                {
                    // just go !
                    this._profileManager.PlaySystem(Constants.SYSTEM_GO);
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

    private PrerequisitesCheckResultCode checkPrerequisite(IGame game)
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
        return checkResult.Code;
    }

#endregion

    public void SetGame(IGame game)
    {
        this._currentGame = game;
    }


}


