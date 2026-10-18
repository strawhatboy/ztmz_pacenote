using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core
{


    public class ProfileManager
    {
        private readonly IAudioPlaybackEngineFactory _audioPlaybackEngineFactory;
        public static string DEFAULT_CODRIVER_PACKAGE_ID = "3322c09e-142e-42a1-90fe-2ffc81d03548";
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public string CurrentCoDriverName { set; get; }

        public string CurrentScriptPath { set; get; }
        public ScriptReader CurrentScriptReader { get; private set; }

        private string _currentCoDriverSoundPackagePath = string.Empty;
        public string CurrentCoDriverSoundPackagePath
        {
            set => _currentCoDriverSoundPackagePath = value ?? string.Empty;
            get => _currentCoDriverSoundPackagePath;
        }

        public CoDriverPackageInfo CurrentCoDriverSoundPackageInfo
        {
            get
            {
                if (this.CoDriverPackages.ContainsKey(this.CurrentCoDriverSoundPackagePath))
                {
                    return this.CoDriverPackages[this.CurrentCoDriverSoundPackagePath].Info;
                }

                return null;
            }
        }

        private CoDriverPackage _defaultCoDriverSoundPackage;
        public CoDriverPackage DefaultCoDriverSoundPackage
        {
            get
            {
                if (_defaultCoDriverSoundPackage == null)
                {
                    _defaultCoDriverSoundPackage = this.CoDriverPackages.First(p => p.Value.Info.id == DEFAULT_CODRIVER_PACKAGE_ID).Value;
                }
                return _defaultCoDriverSoundPackage;
            }
        }

        public int CurrentPlayIndex { set; get; } = 0;

        private ConcurrentDictionary<string, AutoResampledCachedSound> soundCache = new();
        private IGamePacenoteAudioProvider _activeGameAudioProvider;
        private readonly ConcurrentDictionary<ExampleAudioCacheKey, Lazy<AutoResampledCachedSound>>
            exampleAudioCache = new();

        public AudioOutputDeviceInfo CurrentPlayDevice { get; private set; }

        public void SetCurrentPlayDevice(AudioOutputDeviceInfo device, int deviceIndex)
        {
            ArgumentNullException.ThrowIfNull(device);

            var nextPlayer = _audioPlaybackEngineFactory.Create(
                new AudioPlaybackEngineOptions(
                    device.Id,
                    deviceIndex,
                    Config.Instance.UseSequentialMixerToHandleAudioConflict,
                    Config.Instance.PlaybackDeviceDesiredLatency));
            nextPlayer.SetMuted(Config.Instance.UI_Mute);

            var previousPlayer = this.Player;
            this.Player = nextPlayer;
            this.CurrentPlayDevice = device;
            try
            {
                previousPlayer?.Dispose();
            }
            catch (Exception exception)
            {
                _logger.Warn(exception, "Failed to dispose the previous audio output after switching devices.");
            }
        }

        public void StopCurrentAudioOutput()
        {
            var previousPlayer = this.Player;
            this.Player = null;
            this.CurrentPlayDevice = null;
            try
            {
                previousPlayer?.Dispose();
            }
            catch (Exception exception)
            {
                _logger.Warn(exception, "Failed to stop the current audio output.");
            }
        }

        public double CurrentPlaybackGainDb { set; get; } = 0;

        public float _currentPlaySpeed = 1.0f;

        public float CurrentPlaySpeed
        {
            set
            {
                if (value < 0)
                {
                    _currentPlaySpeed = 1.0f;
                }
                else if (value > Config.Instance.DynamicPlaybackMaxSpeed)
                {
                    _currentPlaySpeed = Config.Instance.DynamicPlaybackMaxSpeed;
                }
                else
                {
                    _currentPlaySpeed = value;
                }
            }
            get
            {
                return _currentPlaySpeed;
            }
        }
        public float CurrentTension { set; get; } = 0f;

        // private WaveOutEvent _exampleWaveOut;

        public int AudioPacenoteCount { private set; get; }
        public int ScriptPacenoteCount { private set; get; }


        public List<AudioFile> AudioFiles { set; get; } = new List<AudioFile>();

        public ConcurrentDictionary<string, CoDriverPackage> CoDriverPackages { set; get; } = new ConcurrentDictionary<string, CoDriverPackage>();


        //public IEnumerable<Mp3FileReader> Players { set; get; }
        //public IList<MediaPlayer> Players { set; get; } = new List<MediaPlayer>();
        //public IList<SoundPlayer> Players { set; get; } = new List<SoundPlayer>();
        //public IList<WaveOutEvent> Players { set; get; } = new List<WaveOutEvent>();

        public IAudioPlaybackEngine Player { set; get; }

        public void SetMuted(bool muted)
        {
            this.Player?.SetMuted(muted);
        }


        public AudioFile CurrentAudioFile => this.AudioFiles != null && this.CurrentPlayIndex >= 0 && this.CurrentPlayIndex < this.AudioFiles.Count()
            ? this.AudioFiles[this.CurrentPlayIndex]
            : null;
        public AudioFile NextAudioFile => this.AudioFiles != null && this.CurrentPlayIndex > 0 && this.CurrentPlayIndex + 1 < this.AudioFiles.Count()
            ? this.AudioFiles[this.CurrentPlayIndex + 1]
            : null;

        //public SoundPlayer CurrentSoundPlayer => this.Players != null && this.CurrentPlayIndex < this.Players.Count()
        //    ? this.Players[this.CurrentPlayIndex]
        //    : null;        
        //public WaveOutEvent CurrentSoundPlayer => this.Players != null && this.CurrentPlayIndex < this.Players.Count()
        //    ? this.Players[this.CurrentPlayIndex]
        //    : null;

        private object obj = new();

        private readonly ProfileCatalogService _profileCatalog;


        public ProfileManager()
            : this(
                AudioPlatformServices.CreateDefaultPlaybackEngineFactory(),
                new ProfileCatalogService(AppLevelVariables.Instance))
        {
        }

        public ProfileManager(IAudioPlaybackEngineFactory audioPlaybackEngineFactory)
            : this(
                audioPlaybackEngineFactory,
                new ProfileCatalogService(AppLevelVariables.Instance))
        {
        }

        public ProfileManager(
            IAudioPlaybackEngineFactory audioPlaybackEngineFactory,
            ProfileCatalogService profileCatalog)
        {
            _audioPlaybackEngineFactory = audioPlaybackEngineFactory ??
                throw new ArgumentNullException(nameof(audioPlaybackEngineFactory));
            _profileCatalog = profileCatalog ??
                throw new ArgumentNullException(nameof(profileCatalog));
            _logger.Debug("initializing profile manager");
            _profileCatalog.GetProfileDirectories();
            _profileCatalog.GetCoDriverDirectories();
            this.CreateNewProfile(Constants.DEFAULT_PROFILE);

            _logger.Debug("initializing codriver sounds");


            //load supported audio types

            // var jsonContent = File.ReadAllText("supported_audio_types.json");
            // this.SupportedAudioTypes = JsonConvert.DeserializeObject<string[]>(jsonContent).ToList();
            // this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;

            // load all codriver sounds
            this.initCodriverSounds().Wait();
        }

        private AutoResampledCachedSound BuildExampleAudio(ExampleAudioCacheKey cacheKey)
        {
            var stopwatch = Stopwatch.StartNew();
            var exampleAudio = new AutoResampledCachedSound();
            var parts = cacheKey.PacenoteString.Split(
                new char[] { ',', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var sound = this.getSoundByKeyword(part.Trim(), cacheKey.CoDriverPackagePath);
                exampleAudio.Append(sound);
            }

            if (stopwatch.ElapsedMilliseconds >= 250)
            {
                _logger.Info(
                    "Prepared codriver example audio with {0} token(s) in {1} ms.",
                    parts.Length,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.Debug(
                    "Prepared codriver example audio with {0} token(s) in {1} ms.",
                    parts.Length,
                    stopwatch.ElapsedMilliseconds);
            }
            return exampleAudio;
        }

        public void PrepareExampleAudio()
        {
            if (!TryCreateExampleAudioCacheKey(out var cacheKey))
            {
                return;
            }

            var cached = GetOrCreateExampleAudio(cacheKey);
            if (cached.IsValueCreated)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    _ = cached.Value;
                }
                catch (Exception exception)
                {
                    exampleAudioCache.TryRemove(cacheKey, out _);
                    _logger.Warn(exception, "Failed to prepare the codriver example audio.");
                }
            });
        }

        private async Task initCodriverSounds()
        {
            var packages = await _profileCatalog.LoadCoDriversAsync();
            foreach (var package in packages)
            {
                this.CoDriverPackages[package.Key] = package.Value;
            }
        }

        public async Task RefreshCodriverSounds()
        {
            this.CoDriverPackages.Clear();
            this.soundCache.Clear();
            this.exampleAudioCache.Clear();
            await this.initCodriverSounds();
        }

        public async Task AddCodriverSounds(string path)
        {
            var package = await CoDriverPackage.Import(path);
            if (package != null)
            {
                this.CoDriverPackages[package.Info.Path] = package;
            }
        }

        private AutoResampledCachedSound getSoundFromCache(string path)
        {
            var cachedSound = this.soundCache.GetOrAdd(
                path,
                static audioPath => new AutoResampledCachedSound(audioPath));
            if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                return new AutoResampledCachedSound(cachedSound.CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
            return cachedSound;
        }

        private void clearSoundCache()
        {
            this.soundCache.Clear();
        }

        public List<string> GetAllProfiles()
        {
            return _profileCatalog.GetProfileDirectories().ToList();
        }

        public List<string> GetAllCodrivers()
        {
            _logger.Debug("getting all codrivers");
            var dirs = _profileCatalog.GetCoDriverDirectories().ToList();
            _logger.Debug("found {0} codrivers", dirs.Count);
            return dirs;
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(
                AppLevelVariables.Instance.GetPath(
                    Path.Combine(Constants.PATH_PROFILES, profileName)));
            this.CurrentProfile = profileName;
        }

        public string StartRecording(IGame game, string itinerary)
        {
            string filesPath = this.GetRecordingsFolder(game, itinerary);
            this.CurrentScriptPath = game.GamePacenoteReader.GetScriptFileForRecording(this.CurrentProfile, game, itinerary);
            this.CurrentItineraryPath = filesPath;
            return filesPath;
        }

        public string GetRecordingsFolder(IGame game, string itinerary)
        {
            string filesPath = AppLevelVariables.Instance.GetPath(
                Path.Combine(Constants.PATH_PROFILES, this.CurrentProfile, game.Name, itinerary));
            Directory.CreateDirectory(filesPath);
            return filesPath;
        }
        public string GetScriptFile(string itinerary)
        {
            return this.GetScriptFile(itinerary, this.CurrentProfile);
        }

        public string GetScriptFile(string itinerary, string profile)
        {
            string filePath = AppLevelVariables.Instance.GetPath(
                Path.Combine(Constants.PATH_PROFILES, profile, $"{itinerary}.pacenote"));
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "");
            }

            this.CurrentScriptPath = filePath;
            return filePath;
        }

        public void StopRecording(string codriver)
        {
            // save the codriver name to config
            File.WriteAllText(Path.Join(this.CurrentItineraryPath, Constants.CODRIVER_FILENAME), codriver);
        }

        public void StartReplaying(IGame game, string itinerary, int playMode = 1)
        {
            _logger.Trace("Start playing pacenotes: {0} {1}", game.Name, itinerary);
            SetActiveGameAudioProvider(game);
            if (!GamePacenoteSourcePolicy.AllowsDistancePlayback(game))
            {
                this.AudioFiles.Clear();
                this.AudioPacenoteCount = 0;
                this.ScriptPacenoteCount = 0;
                this.CurrentScriptReader = null;
                this.CurrentScriptPath = string.Empty;
                this.CurrentItineraryPath = string.Empty;
                this.CurrentPlayIndex = 0;
                _logger.Info("Using live pacenotes only for {0}; distance scripts are disabled.", game.Name);
                return;
            }
            // bydefault playMode=1, script mode, only.
            this.CurrentItineraryPath = this.GetRecordingsFolder(game, itinerary);
            // this.CurrentScriptPath = this.GetScriptFile(itinerary);
            this.CurrentScriptPath = game.GamePacenoteReader.GetScriptFileForReplaying(this.CurrentProfile, game, itinerary);
            // also need to check flag from config file.
            // if (string.IsNullOrEmpty(File.ReadAllText(this.CurrentScriptPath).Trim()))
            // {
            //     // fallback to default profile
            //     this.CurrentScriptPath = this.GetScriptFile(itinerary, Constants.DEFAULT_PROFILE);
            // }
            List<AudioFile> audioFiles = new List<AudioFile>();

            // clear lists
            // this.Player?.Dispose();
            //foreach (var f in this.AudioFiles)
            //{
            //f.AudioFileReader.Dispose();
            //}
            this.AudioPacenoteCount = 0;
            this.ScriptPacenoteCount = 0;
            this.AudioFiles.Clear();
            this.soundCache.Clear();

            if (playMode == 0 || playMode == 2)
            {
                // load codriver name
                var codirverfilepath = Path.Join(this.CurrentItineraryPath, Constants.CODRIVER_FILENAME);
                if (File.Exists(codirverfilepath))
                {
                    var codriver = File.ReadLines(codirverfilepath).FirstOrDefault();
                    this.CurrentCoDriverName = codriver;
                }
                else
                {
                    this.CurrentCoDriverName = "???";
                }

                //this.Players.Clear();

                // load all files
                IEnumerable<string> filePaths = new List<string>();
                foreach (var supportedFilter in Constants.SupportedAudioTypes)
                {
                    filePaths = filePaths.Concat(Directory.GetFiles(this.CurrentItineraryPath, supportedFilter)
                        .AsEnumerable());
                }

                // get files
                audioFiles = (from path in filePaths
                              select
                                  new AudioFile()
                                  {
                                      //FileName = Path.GetFileName(path),
                                      //FilePath = Path.GetFullPath(path),
                                      //Extension = Path.GetExtension(path),
                                      Distance = int.Parse(Path.GetFileNameWithoutExtension(path)),
                                      //Content = File.ReadAllBytes(path)
                                      //AudioFileReader = new AudioFileReader(path)
                                      Sound = new AutoResampledCachedSound(path)
                                  }).ToList();
                this.AudioPacenoteCount = audioFiles.Count;
            }

            _logger.Trace("Start to get audios when starting playing pacenotes {0} {1} with {2} pacenotes", game.Name, itinerary, this.AudioPacenoteCount);
            if (playMode == 1 || playMode == 2)
            {
                // script mode now!
                // var reader = ScriptReader.ReadFromFile(this.CurrentScriptPath);
                var reader = game.GamePacenoteReader.ReadPacenoteRecord(this.CurrentProfile, game, itinerary);
                this.CurrentScriptReader = reader;
                // filter out all empty pacenote records
                var records = (from p in reader.PacenoteRecords
                               where p.Distance.HasValue
                               select p).ToList();
                var gameAudioProvider = Volatile.Read(ref _activeGameAudioProvider);

                for (int i = 0; i < records.Count; i++)
                {
                    try
                    {
                        var record = records[i];
                        _logger.Trace("Processing pacenote record {0}", record.ToString());

                        // always create new since there's no overlapping issue.
                        var f = new AudioFile()
                        {
                            Distance = (int)record.Distance,
                            PacenoteRecord = record
                        };

                        var sound = BuildPacenoteRecordSound(record, gameAudioProvider);

                        f.Sound = sound;
                        audioFiles.Add(f);
                        this.ScriptPacenoteCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Error when processing pacenote record {0} {1} {2} {3}", i, game.Name, itinerary, ex.ToString());
                    }
                }
            }

            _logger.Trace("Got {0} pacenotes from script", this.ScriptPacenoteCount);


            var sortedAudioFiles = from audioFile in audioFiles
                                   orderby audioFile.Distance ascending
                                   select audioFile;

            this.AudioFiles = sortedAudioFiles.ToList();
            _logger.Trace("Got {0} audio files", this.AudioFiles.Count);

            //this.Players = from audiofile in sortedAudioFiles
            //    select new Mp3FileReader(audiofile.FilePath);
            //foreach (var audiofile in sortedAudioFiles)
            //{
            //    //var player = new SoundPlayer(new MemoryStream(audiofile.Content));
            //    var player = new WaveOutEvent();
            //    player.DeviceNumber = this.CurrentPlayDeviceId;
            //    player.Init(audiofile.AudioFileReader);
            //    this.Players.Add(player);
            //}

            // index to the 1st non-zero distance for now.
            //TODO: play pacenotes with distance less than 0 before start
            // for (int i = 0; i < this.AudioFiles.Count; i++)
            // {
            //     if (this.AudioFiles[i].Distance > 0)
            //     {
            //         this.CurrentPlayIndex = i;
            //         break;
            //     }
            //     else
            //     {
            //         // pre stage audios
            //     }
            // }
            this.CurrentPlayIndex = 0;
            _logger.Trace("Start replaying {0} {1} with {2} pacenotes", game.Name, itinerary, this.AudioFiles.Count);
        }

        private AutoResampledCachedSound BuildPacenoteRecordSound(
            PacenoteRecord record,
            IGamePacenoteAudioProvider gameAudioProvider)
        {
            var sound = new AutoResampledCachedSound();
            var spansByStartIndex = record.SourceAudioCueSpans
                .Where(span => span.PacenoteCount > 0)
                .GroupBy(span => span.PacenoteStartIndex)
                .ToDictionary(group => group.Key, group => group.First());

            for (var noteIndex = 0; noteIndex < record.Pacenotes.Count; noteIndex++)
            {
                if (gameAudioProvider != null &&
                    spansByStartIndex.TryGetValue(noteIndex, out var span) &&
                    TryAppendNativePacenoteAudio(sound, gameAudioProvider, span.Cue))
                {
                    noteIndex += span.PacenoteCount - 1;
                    continue;
                }

                var note = record.Pacenotes[noteIndex];
                sound.Append(this.getSoundByKeyword(note.Note, this.CurrentCoDriverSoundPackagePath));
                foreach (var modifier in note.Modifiers)
                {
                    sound.Append(this.getSoundByKeyword(modifier, this.CurrentCoDriverSoundPackagePath));
                }
            }

            return sound;
        }

        private bool TryAppendNativePacenoteAudio(
            AutoResampledCachedSound target,
            IGamePacenoteAudioProvider provider,
            GamePacenoteAudioCue cue)
        {
            try
            {
                if (!provider.TryResolvePacenoteAudio(cue, out var resolution) ||
                    resolution.Segments.Count == 0 ||
                    resolution.Segments.Any(segment => segment.AlternativePaths.Count == 0))
                {
                    return false;
                }

                foreach (var segment in resolution.Segments)
                {
                    var path = segment.AlternativePaths[Random.Shared.Next(segment.AlternativePaths.Count)];
                    target.Append(this.getSoundFromCache(path));
                }
                return true;
            }
            catch (Exception exception)
            {
                _logger.Warn(
                    exception,
                    "Failed to resolve native pacenote audio from {0} for type {1}; using the selected ZTMZ co-driver.",
                    cue.ProviderId,
                    cue.PacenoteType);
                return false;
            }
        }
        //private AutoResampledCachedSound getSoundByKeywordTryTmp(string keyword)
        //{
        //    var sound = this.getSoundByKeyword(keyword);
        //    if (sound.IsEmpty)
        //    {
        //        return this.getSoundByKeyword("tmp_" + keyword);
        //    }
        //}

        // already cut sound
        private AutoResampledCachedSound getSoundByKeyword(string keyword, string codriverPackage, bool isFinal = false)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return new AutoResampledCachedSound();
            }
            _logger.Trace("Getting sound by keyword {0} {1}", keyword, codriverPackage);
            // keyword is the filename used to find the correct audio file, in new sqlite3 based pacenote structure,
            // we first use this keyword to find the corresponding pacenote id (integer), then random select an audio file
            // related to the pacenote id
            var package = this.CoDriverPackages[codriverPackage];
            var id = -1;
            if (Base.Script.ScriptResource.Instance.FilenameToIdDict.ContainsKey(keyword))
            {
                id = Base.Script.ScriptResource.Instance.FilenameToIdDict[keyword];
                if (Config.Instance.UseReversePacenote)
                {
                    // try to find the reverse pacenote
                    if (Base.Script.ScriptResource.Instance.ReverseCorners.Any(r => r.id == id))
                    {
                        var reverseId = Base.Script.ScriptResource.Instance.ReverseCorners.First(r => r.id == id).replacement_id;
                        if (reverseId != -1)
                        {
                            id = reverseId;
                        }
                    }
                }
            }
            else
            {
                // unknown keyword, try to use the tokensPath
                if (Config.Instance.PreloadSounds && package.tokens.ContainsKey(keyword))
                {
                    var tokens = package.tokens[keyword];

                    if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                    {
                        return new AutoResampledCachedSound(tokens.ElementAt(Random.Shared.Next(0, tokens.Count)).CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
                    }
                    return tokens.ElementAt(Random.Shared.Next(0, tokens.Count));
                }
                if (!Config.Instance.PreloadSounds && package.tokensPath.ContainsKey(keyword))
                {
                    var tokens = package.tokensPath[keyword];
                    if (tokens.Count > 0)
                    {
                        return this.getSoundFromCache(tokens.ElementAt(Random.Shared.Next(0, tokens.Count)));
                    }
                    else
                    {
                        // No sound file in the folder
                        return new AutoResampledCachedSound();
                    }
                }
            }

            // TODO: simplify these id & keyword handling
            HashSet<int> visitedIds = new HashSet<int>();
            return getSoundById(id, package, visitedIds, isFinal);
        }

        /// <summary>
        /// Get the sound by pacenote id
        /// </summary>
        /// <param name="id">The pacenote id defined in sqlite file</param>
        /// <param name="package">the CoDriverPackage</param>
        /// <param name="isFinal">if we should stop the fallback,
        /// 1. We fallback into the default codriver package, no more fallback, isFinal=true
        /// 2. We encounter a </param>
        /// <returns>The sound</returns>
        private AutoResampledCachedSound getSoundById(int id, CoDriverPackage package, HashSet<int> visitedIds, bool isFinal = false)
        {
            if (id == -1)
            {   // wtf?
                return new AutoResampledCachedSound();
            }

            // ugly, if we have into, onto, and, we should remove them when flag is set
            if (Config.Instance.RemoveIntoAndOnto)
            {
                if (id == Constants.PACENOTE_INTO || id == Constants.PACENOTE_ONTO || id == Constants.PACENOTE_AND)
                {
                    return new AutoResampledCachedSound();
                }
            }

            if (Config.Instance.PreloadSounds && package.id2tokens.ContainsKey(id))
            {
                var tokens = package.id2tokens[id];
                if (tokens.Count > 0)
                {
                    if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                    {
                        return new AutoResampledCachedSound(tokens.ElementAt(Random.Shared.Next(0, tokens.Count)).CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
                    }
                    return tokens.ElementAt(Random.Shared.Next(0, tokens.Count));
                }
            }
            if (!Config.Instance.PreloadSounds && package.id2tokensPath.ContainsKey(id))
            {
                var tokens = package.id2tokensPath[id];
                if (tokens.Count > 0)
                {
                    return this.getSoundFromCache(tokens.ElementAt(Random.Shared.Next(0, tokens.Count)));
                }
            }

            // not found, visited, try fallbacks
            visitedIds.Add(id);
            // not found, try fallback ids or default codriver package
            if (ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance.FallbackDict.ContainsKey(id))
            {
                var fallbacks = ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance.FallbackDict[id];
                AutoResampledCachedSound sound = new AutoResampledCachedSound();
                var fallbackSounds = ResolveFallbackSounds(
                    fallbacks,
                    visitedIds,
                    (fallback, branchVisitedIds) => getSoundById(fallback, package, branchVisitedIds));
                foreach (var fallbackSound in fallbackSounds)
                {
                    sound.Append(fallbackSound);
                }

                if (fallbackSounds.Count > 0)
                {
                    return sound;
                }
            }

            if (!isFinal && Config.Instance.UseDefaultSoundPackageForFallback)
            {
                // not found, try default, I mean default codriver sound package
                return getSoundById(id, DefaultCoDriverSoundPackage, visitedIds, true);
            }
            return new AutoResampledCachedSound();
        }

        internal static IReadOnlyList<AutoResampledCachedSound> ResolveFallbackSounds(
            IEnumerable<int> fallbacks,
            IReadOnlySet<int> visitedIds,
            Func<int, HashSet<int>, AutoResampledCachedSound> resolve)
        {
            var sounds = new List<AutoResampledCachedSound>();
            foreach (var fallback in fallbacks)
            {
                if (visitedIds.Contains(fallback))
                {
                    continue;
                }

                var branchVisitedIds = new HashSet<int>(visitedIds);
                var fallbackSound = resolve(fallback, branchVisitedIds);
                if (!fallbackSound.IsEmpty)
                {
                    sounds.Add(fallbackSound);
                }
            }

            return sounds;
        }

        // need to be run in a non-UI thread
        public bool Play()
        {
            // try to amplify the sound.
            var audioFile = this.AudioFiles[this.CurrentPlayIndex++];
            if (PacenoteDropoutPolicy.ShouldDrop(
                Config.Instance.PacenoteDropoutPercentage,
                Random.Shared.Next(100)))
            {
                _logger.Debug("Mischief mode skipped pacenote at {0}m", audioFile.Distance);
                return false;
            }

            var sound = audioFile.Sound;
            sound.PlaySpeed = this.CurrentPlaySpeed;
            sound.Gain = PlaybackGain.ToLinear(this.CurrentPlaybackGainDb);
            sound.Tension = this.CurrentTension;
            this.PlaySound(sound, Config.Instance.UseSequentialMixerToHandleAudioConflict);
            _logger.Trace("Playing {0}", this.CurrentPlayIndex);
            return true;
        }

        // need to be run in a non-UI thread
        public void PlayExample()
        {
            var stopwatch = Stopwatch.StartNew();
            if (!TryCreateExampleAudioCacheKey(out var cacheKey))
            {
                _logger.Warn("Codriver example audio was skipped because no codriver is selected.");
                return;
            }

            var cachedExampleAudio = GetPreparedExampleAudio(cacheKey);
            var preparationMilliseconds = stopwatch.ElapsedMilliseconds;
            var exampleAudio = new AutoResampledCachedSound(cachedExampleAudio.AudioData);
            exampleAudio.Gain = PlaybackGain.ToLinear(this.CurrentPlaybackGainDb);
            exampleAudio.PlaySpeed = this.CurrentPlaySpeed;
            this.PlaySound(exampleAudio, isSequential: false);
            if (stopwatch.ElapsedMilliseconds >= 250)
            {
                _logger.Info(
                    "Queued codriver example audio in {0} ms ({1} ms preparing audio).",
                    stopwatch.ElapsedMilliseconds,
                    preparationMilliseconds);
            }
            else
            {
                _logger.Debug(
                    "Queued codriver example audio in {0} ms ({1} ms preparing audio).",
                    stopwatch.ElapsedMilliseconds,
                    preparationMilliseconds);
            }
        }

        private Lazy<AutoResampledCachedSound> GetOrCreateExampleAudio(
            ExampleAudioCacheKey cacheKey)
        {
            return exampleAudioCache.GetOrAdd(
                cacheKey,
                key => new Lazy<AutoResampledCachedSound>(
                    () => BuildExampleAudio(key),
                    LazyThreadSafetyMode.ExecutionAndPublication));
        }

        private AutoResampledCachedSound GetPreparedExampleAudio(
            ExampleAudioCacheKey cacheKey)
        {
            var cached = GetOrCreateExampleAudio(cacheKey);
            try
            {
                return cached.Value;
            }
            catch
            {
                if (exampleAudioCache.TryGetValue(cacheKey, out var current) &&
                    ReferenceEquals(cached, current))
                {
                    exampleAudioCache.TryRemove(cacheKey, out _);
                }

                throw;
            }
        }

        private bool TryCreateExampleAudioCacheKey(out ExampleAudioCacheKey cacheKey)
        {
            var packagePath = this.CurrentCoDriverSoundPackagePath;
            if (string.IsNullOrWhiteSpace(packagePath) ||
                !this.CoDriverPackages.ContainsKey(packagePath))
            {
                cacheKey = default;
                return false;
            }

            cacheKey = new ExampleAudioCacheKey(
                packagePath,
                Config.Instance.ExamplePacenoteString ?? string.Empty,
                Config.Instance.AudioProcessType,
                Config.Instance.FactorToRemoveSpaceFromAudioFiles,
                Config.Instance.UseReversePacenote,
                Config.Instance.RemoveIntoAndOnto,
                Config.Instance.UseDefaultSoundPackageForFallback);
            return true;
        }

        private readonly record struct ExampleAudioCacheKey(
            string CoDriverPackagePath,
            string PacenoteString,
            int AudioProcessType,
            float SilenceRemovalFactor,
            bool UseReversePacenote,
            bool RemoveIntoAndOnto,
            bool UseDefaultSoundPackageForFallback);

        public void PlayMinusScript()
        {
            if (this.CurrentPlayIndex >= 0)
            {
                for (int i = 0; i < this.AudioFiles.Count; i++)
                {
                    if (this.AudioFiles[i].Distance < 0)
                    {   // play it!
                        // try to amplify the sound.
                        var sound = this.AudioFiles[this.CurrentPlayIndex++].Sound;
                        sound.PlaySpeed = this.CurrentPlaySpeed;
                        sound.Gain = PlaybackGain.ToLinear(this.CurrentPlaybackGainDb);
                        sound.Tension = this.CurrentTension;
                        this.PlaySound(sound, true, true);  // play it as sequential system sound, followed by start stage sound
                        _logger.Trace("Playing {0}", this.CurrentPlayIndex);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public void PlaySystem(string sound, bool isSequential = false, bool isSystem = false)
        {
            _logger.Trace(string.Format("Playing system sound : {0}", sound));
            var audio = TryGetNativeSystemSound(sound, out var nativeAudio)
                ? nativeAudio
                : this.getSoundByKeyword(sound, this.CurrentCoDriverSoundPackagePath);
            audio.PlaySpeed = this.CurrentPlaySpeed;
            audio.Gain = PlaybackGain.ToLinear(this.CurrentPlaybackGainDb);
            audio.Tension = this.CurrentTension;
            this.PlaySound(audio, isSequential, isSystem);
        }

        public void SetActiveGameAudioProvider(IGame game)
        {
            var provider = game as IGamePacenoteAudioProvider;
            if (provider == null)
            {
                Volatile.Write(ref _activeGameAudioProvider, null);
                return;
            }

            try
            {
                provider.PreparePacenoteAudio();
                Volatile.Write(ref _activeGameAudioProvider, provider);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _activeGameAudioProvider, null);
                _logger.Warn(exception, "Failed to prepare native audio for {0}.", game.Name);
            }
        }

        public void ClearActiveGameAudioProvider(IGame game)
        {
            if (game is IGamePacenoteAudioProvider provider)
            {
                Interlocked.CompareExchange(ref _activeGameAudioProvider, null, provider);
            }
        }

        private bool TryGetNativeSystemSound(
            string systemSoundToken,
            out AutoResampledCachedSound audio)
        {
            audio = null;
            var provider = Volatile.Read(ref _activeGameAudioProvider);
            if (provider == null)
            {
                return false;
            }

            try
            {
                if (!provider.TryResolveSystemAudio(systemSoundToken, out var segment) ||
                    segment.AlternativePaths.Count == 0)
                {
                    return false;
                }

                var path = segment.AlternativePaths[Random.Shared.Next(segment.AlternativePaths.Count)];
                audio = this.getSoundFromCache(path);
                _logger.Debug(
                    "Using native game audio {0} for system sound {1}.",
                    path,
                    systemSoundToken);
                return true;
            }
            catch (Exception exception)
            {
                _logger.Warn(
                    exception,
                    "Failed to resolve native game audio for system sound {0}; using the selected ZTMZ co-driver.",
                    systemSoundToken);
                return false;
            }
        }

        public bool PlayLivePacenote(PacenoteRecord record)
        {
            if (record is null || record.Pacenotes.Count == 0 ||
                string.IsNullOrWhiteSpace(this.CurrentCoDriverSoundPackagePath) ||
                !this.CoDriverPackages.ContainsKey(this.CurrentCoDriverSoundPackagePath))
            {
                return false;
            }

            try
            {
                var sound = new AutoResampledCachedSound();
                foreach (var note in record.Pacenotes)
                {
                    sound.Append(this.getSoundByKeyword(note.Note, this.CurrentCoDriverSoundPackagePath));
                    foreach (var modifier in note.Modifiers)
                    {
                        sound.Append(this.getSoundByKeyword(modifier, this.CurrentCoDriverSoundPackagePath));
                    }
                }

                if (sound.IsEmpty)
                {
                    _logger.Warn("No playable audio tokens were found for live pacenote: {0}", record.RawText);
                    return false;
                }

                if (PacenoteDropoutPolicy.ShouldDrop(
                        Config.Instance.PacenoteDropoutPercentage,
                        Random.Shared.Next(100)))
                {
                    _logger.Debug("Mischief mode skipped live pacenote: {0}", record.RawText);
                    return false;
                }

                sound.PlaySpeed = this.CurrentPlaySpeed;
                sound.Gain = PlaybackGain.ToLinear(this.CurrentPlaybackGainDb);
                sound.Tension = this.CurrentTension;
                this.PlaySound(sound, Config.Instance.UseSequentialMixerToHandleAudioConflict);
                _logger.Trace("Playing live pacenote: {0}", record.RawText);
                return true;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to play live pacenote: {0}", record.RawText);
                return false;
            }
        }

        public void PlaySound(AutoResampledCachedSound sound, bool isSequential, bool isSystem = false)
        {
            if (!Config.Instance.UI_Mute)
            {
                if (this.Player == null)
                {
                    _logger.Warn("Pacenote audio was skipped because no output device is available.");
                    return;
                }

                this.Player.PlaybackRate = this.CurrentPlaySpeed;
                this.Player.PlaySound(sound, isSequential, isSystem);
            }
        }

        public void ReIndex(float distance)
        {
            var res = this.AudioFiles.BinarySearch(new AudioFile() { Distance = (int)distance }, Comparer<AudioFile>.Create((a, b) => a.Distance.CompareTo(b.Distance)));
            res = res > 0 ? res - 1 : ~res;
            this.CurrentPlayIndex = res;
            _logger.Info("reindex to {0}", this.CurrentPlayIndex);
        }
    }
}
