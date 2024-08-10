using NAudio.Wave;
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
        public static string DEFAULT_CODRIVER_PACKAGE_ID = "3322c09e-142e-42a1-90fe-2ffc81d03548";
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public string CurrentCoDriverName { set; get; }

        public string CurrentScriptPath { set; get; }
        public ScriptReader CurrentScriptReader { get; private set; }

        public string CurrentCoDriverSoundPackagePath { set; get; }

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

        private int _currentPlayDeviceId = 0;

        public int CurrentPlayDeviceId
        {
            set
            {
                this.Player?.Dispose();
                this._currentPlayDeviceId = value;
                this.Player = new ZTMZAudioPlaybackEngine(this._currentPlayDeviceId,
                    Config.Instance.UseSequentialMixerToHandleAudioConflict,
                    Config.Instance.PlaybackDeviceDesiredLatency);
            }
            get => this._currentPlayDeviceId;
        }

        public int CurrentPlayAmplification { set; get; } = 100;

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

        private AutoResampledCachedSound _exampleAudio;

        // private WaveOutEvent _exampleWaveOut;
        private Random _random = new Random();

        public int AudioPacenoteCount { private set; get; }
        public int ScriptPacenoteCount { private set; get; }


        public List<AudioFile> AudioFiles { set; get; } = new List<AudioFile>();

        public ConcurrentDictionary<string, CoDriverPackage> CoDriverPackages { set; get; } = new ConcurrentDictionary<string, CoDriverPackage>();


        //public IEnumerable<Mp3FileReader> Players { set; get; }
        //public IList<MediaPlayer> Players { set; get; } = new List<MediaPlayer>();
        //public IList<SoundPlayer> Players { set; get; } = new List<SoundPlayer>();
        //public IList<WaveOutEvent> Players { set; get; } = new List<WaveOutEvent>();

        public ZTMZAudioPlaybackEngine Player { set; get; }


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


        public ProfileManager()
        {
            _logger.Debug("initializing profile manager");
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(string.Format("profiles")));
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(string.Format("codrivers")));
            this.CreateNewProfile(Constants.DEFAULT_PROFILE);

            _logger.Debug("initializing codriver sounds");


            //load supported audio types

            // var jsonContent = File.ReadAllText("supported_audio_types.json");
            // this.SupportedAudioTypes = JsonConvert.DeserializeObject<string[]>(jsonContent).ToList();
            // this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;

            // load all codriver sounds
            this.initCodriverSounds().Wait();
        }

        private void initExampleAudio()
        {
            this._exampleAudio = new AutoResampledCachedSound();
            var parts = Config.Instance.ExamplePacenoteString.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var s = this.getSoundByKeyword(part, this.CurrentCoDriverSoundPackagePath);
                this._exampleAudio.Append(s);
            }
            //this._exampleAudio = new AudioFileReader("20210715_152916.m4a");
            // this._exampleWaveOut = new WaveOutEvent();
        }

        private async Task initCodriverSounds()
        {
            List<Task> tasks = new List<Task>();
            foreach (var codriverPath in this.GetAllCodrivers())
            {
                tasks.Add(Task.Run(async () =>
                {
                    this.CoDriverPackages[codriverPath] = await CoDriverPackage.Load(codriverPath);
                }));
            }
            await Task.WhenAll(tasks.ToArray());
        }

        public async Task RefreshCodriverSounds()
        {
            this.CoDriverPackages.Clear();
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
            if (!this.soundCache.ContainsKey(path))
            {
                this.soundCache.AddOrUpdate(path, new AutoResampledCachedSound(path), (key, oldValue) => oldValue);
            }
            if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                return new AutoResampledCachedSound(this.soundCache[path].CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
            return this.soundCache[path];
        }

        private void clearSoundCache()
        {
            this.soundCache.Clear();
        }

        public List<string> GetAllProfiles()
        {
            return Directory.GetDirectories(AppLevelVariables.Instance.GetPath("profiles\\")).ToList();
        }

        public List<string> GetAllCodrivers()
        {
            _logger.Debug("getting all codrivers");
            var dirs = Directory.GetDirectories(AppLevelVariables.Instance.GetPath("codrivers\\")).ToList();
            if (!string.IsNullOrEmpty(Config.Instance.AdditionalCoDriverPackagesSearchPath) &&
                Directory.Exists(Config.Instance.AdditionalCoDriverPackagesSearchPath))
            {
                dirs.AddRange(Directory.GetDirectories(Config.Instance.AdditionalCoDriverPackagesSearchPath));
            }

            _logger.Debug("found {0} codrivers", dirs.Count);
            return dirs;
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}", profileName)));
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
            string filesPath = AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}\\{1}\\{2}", this.CurrentProfile, game.Name, itinerary));
            Directory.CreateDirectory(filesPath);
            return filesPath;
        }
        public string GetScriptFile(string itinerary)
        {
            return this.GetScriptFile(itinerary, this.CurrentProfile);
        }

        public string GetScriptFile(string itinerary, string profile)
        {
            string filePath = AppLevelVariables.Instance.GetPath(string.Format("profiles\\{0}\\{1}.pacenote", profile, itinerary));
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

                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];

                    // always create new since there's no overlapping issue.
                    var f = new AudioFile() { Distance = (int)record.Distance };

                    var sound = new AutoResampledCachedSound();
                    foreach (var note in record.Pacenotes)
                    {
                        sound.Append(this.getSoundByKeyword(note.Note, this.CurrentCoDriverSoundPackagePath));
                        foreach (var mod in note.Modifiers)
                        {
                            sound.Append(this.getSoundByKeyword(mod, this.CurrentCoDriverSoundPackagePath));
                        }
                    }

                    f.Sound = sound;
                    audioFiles.Add(f);
                    this.ScriptPacenoteCount++;
                }
            }


            var sortedAudioFiles = from audioFile in audioFiles
                                   orderby audioFile.Distance ascending
                                   select audioFile;

            this.AudioFiles = sortedAudioFiles.ToList();

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
            for (int i = 0; i < this.AudioFiles.Count; i++)
            {
                if (this.AudioFiles[i].Distance > 0)
                {
                    this.CurrentPlayIndex = i;
                    break;
                } else {
                    // pre stage audios
                }
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
            // keyword is the filename used to find the correct audio file, in new sqlite3 based pacenote structure,
            // we first use this keyword to find the corresponding pacenote id (integer), then random select an audio file
            // related to the pacenote id
            var package = this.CoDriverPackages[codriverPackage];
            var id = -1;
            if (Base.Script.ScriptResource.Instance.FilenameToIdDict.ContainsKey(keyword))
            {
                id = Base.Script.ScriptResource.Instance.FilenameToIdDict[keyword];
            } else {
                // unknown keyword, try to use the tokensPath
                if (Config.Instance.PreloadSounds && package.tokens.ContainsKey(keyword))
                {
                    var tokens = package.tokens[keyword];

                    if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                    {
                        return new AutoResampledCachedSound(tokens.ElementAt(this._random.Next(0, tokens.Count)).CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
                    }
                    return tokens.ElementAt(this._random.Next(0, tokens.Count));
                }
                if (!Config.Instance.PreloadSounds && package.tokensPath.ContainsKey(keyword))
                {
                    var tokens = package.tokensPath[keyword];
                    if (tokens.Count > 0)
                    {
                        return this.getSoundFromCache(tokens.ElementAt(this._random.Next(0, tokens.Count)));
                    }
                    else
                    {
                        // No sound file in the folder
                        return new AutoResampledCachedSound();
                    }
                }
            }

            // TODO: simplify these id & keyword handling
            return getSoundById(id, package, isFinal);
        }

        private AutoResampledCachedSound getSoundById(int id, CoDriverPackage package, bool isFinal = false) {
            if (id == -1)
            {   // wtf?
                return new AutoResampledCachedSound();
            }

            if (Config.Instance.PreloadSounds && package.id2tokens.ContainsKey(id))
            {
                var tokens = package.id2tokens[id];
                if (tokens.Count > 0) {
                    if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail)
                    {
                        return new AutoResampledCachedSound(tokens.ElementAt(this._random.Next(0, tokens.Count)).CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
                    }
                    return tokens.ElementAt(this._random.Next(0, tokens.Count));
                }
            }
            if (!Config.Instance.PreloadSounds && package.id2tokensPath.ContainsKey(id))
            {
                var tokens = package.id2tokensPath[id];
                if (tokens.Count > 0)
                {
                    return this.getSoundFromCache(tokens.ElementAt(this._random.Next(0, tokens.Count)));
                }
            }

            // not found, try fallback ids
            if (ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance.FallbackDict.ContainsKey(id))
            {
                var fallbacks = ZTMZ.PacenoteTool.Base.Script.ScriptResource.Instance.FallbackDict[id];
                AutoResampledCachedSound sound = new AutoResampledCachedSound();
                foreach(var fallback in fallbacks)
                {
                    sound.Append(getSoundById(fallback, package));
                }
                return sound;
            }

            if (!isFinal && Config.Instance.UseDefaultSoundPackageForFallback)
            {
                // not found, try default, I mean default codriver sound package
                return getSoundById(id, DefaultCoDriverSoundPackage, true);
            }
            return new AutoResampledCachedSound();
        }

        // need to be run in a non-UI thread
        public void Play()
        {
            // try to amplify the sound.
            var sound = this.AudioFiles[this.CurrentPlayIndex++].Sound;
            sound.PlaySpeed = this.CurrentPlaySpeed;
            sound.Amplification = this.CurrentPlayAmplification;
            sound.Tension = this.CurrentTension;
            this.PlaySound(sound, Config.Instance.UseSequentialMixerToHandleAudioConflict);
            Debug.WriteLine("Playing {0}", this.CurrentPlayIndex);
        }

        // need to be run in a non-UI thread
        public void PlayExample()
        {
            //load example audio file?
            this.initExampleAudio();
            this._exampleAudio.Amplification = this.CurrentPlayAmplification;
            this._exampleAudio.PlaySpeed = this.CurrentPlaySpeed;
            this.PlaySound(this._exampleAudio, true);
        }

        public void PlayMinusScript()
        {
            if (this.CurrentPlayIndex > 0)
            {
                for (int i = 0; i < this.AudioFiles.Count; i++)
                {
                    if (this.AudioFiles[i].Distance <= 0)
                    {   // play it!
                        // try to amplify the sound.
                        var sound = this.AudioFiles[this.CurrentPlayIndex++].Sound;
                        sound.PlaySpeed = this.CurrentPlaySpeed;
                        sound.Amplification = this.CurrentPlayAmplification;
                        sound.Tension = this.CurrentTension;
                        this.PlaySound(sound, true, true);  // play it as sequential system sound, followed by start stage sound
                        Debug.WriteLine("Playing {0}", this.CurrentPlayIndex);
                    } else {
                        break;
                    }
                }
            }
        }

        public void PlaySystem(string sound, bool isSequential = false, bool isSystem = false)
        {
            Debug.WriteLine("Playing system sound : {0}", sound);
            var audio = this.getSoundByKeyword(sound, this.CurrentCoDriverSoundPackagePath);
            audio.PlaySpeed = this.CurrentPlaySpeed;
            audio.Amplification = this.CurrentPlayAmplification;
            audio.Tension = this.CurrentTension;
            this.PlaySound(audio, isSequential, isSystem);
        }

        public void PlaySound(AutoResampledCachedSound sound, bool isSequential, bool isSystem = false)
        {
            if (!Config.Instance.UI_Mute)
            {
                this.Player.PlaybackRate = this.CurrentPlaySpeed;
                this.Player.PlaySound(sound, isSequential, isSystem);
            }
        }

        public void ReIndex(float distance)
        {
            var res = this.AudioFiles.BinarySearch(new AudioFile() { Distance = (int)distance }, Comparer<AudioFile>.Create((a, b) => a.Distance.CompareTo(b.Distance)));
            res = res > 0 ? res - 1 : ~res;
            this.CurrentPlayIndex = res;
            Debug.WriteLine("reindex to {0}", this.CurrentPlayIndex);
        }
    }
}
