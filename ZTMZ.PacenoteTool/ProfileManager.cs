using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool
{
    

    public class ProfileManager
    {

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

        public Dictionary<string, CoDriverPackage> CoDriverPackages { set; get; } = new Dictionary<string, CoDriverPackage>();


        //public IEnumerable<Mp3FileReader> Players { set; get; }
        //public IList<MediaPlayer> Players { set; get; } = new List<MediaPlayer>();
        //public IList<SoundPlayer> Players { set; get; } = new List<SoundPlayer>();
        //public IList<WaveOutEvent> Players { set; get; } = new List<WaveOutEvent>();

        public ZTMZAudioPlaybackEngine Player { set; get; }


        public AudioFile CurrentAudioFile => this.AudioFiles != null && this.CurrentPlayIndex < this.AudioFiles.Count()
            ? this.AudioFiles[this.CurrentPlayIndex]
            : null;
        public AudioFile NextAudioFile => this.AudioFiles != null && this.CurrentPlayIndex+1 < this.AudioFiles.Count()
            ? this.AudioFiles[this.CurrentPlayIndex+1]
            : null;

        //public SoundPlayer CurrentSoundPlayer => this.Players != null && this.CurrentPlayIndex < this.Players.Count()
        //    ? this.Players[this.CurrentPlayIndex]
        //    : null;        
        //public WaveOutEvent CurrentSoundPlayer => this.Players != null && this.CurrentPlayIndex < this.Players.Count()
        //    ? this.Players[this.CurrentPlayIndex]
        //    : null;

        private object obj = new();

        public IList<string> SupportedAudioTypes { set; get; } = new List<string>();

        public ProfileManager()
        {
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(string.Format("profiles")));
            Directory.CreateDirectory(AppLevelVariables.Instance.GetPath(string.Format("codrivers")));
            this.CreateNewProfile(Constants.DEFAULT_PROFILE);


            //load supported audio types

            // var jsonContent = File.ReadAllText("supported_audio_types.json");
            // this.SupportedAudioTypes = JsonConvert.DeserializeObject<string[]>(jsonContent).ToList();
            this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;

            // load all codriver sounds
            this.initCodriverSounds();
        }

        private void initExampleAudio()
        {
            this._exampleAudio = new AutoResampledCachedSound();
            var parts = Config.Instance.ExamplePacenoteString.Split(new char[]{',', '/'}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var s = this.getSoundByKeyword(part, this.CurrentCoDriverSoundPackagePath);
                this._exampleAudio.Append(s);
            }
            //this._exampleAudio = new AudioFileReader("20210715_152916.m4a");
            // this._exampleWaveOut = new WaveOutEvent();
        }

        private void initCodriverSounds()
        {
            foreach (var codriverPath in this.GetAllCodrivers())
            {
                this.CoDriverPackages[codriverPath] = new CoDriverPackage();
                
                // try load info
                var infoFilePath = Path.Join(codriverPath, Constants.CODRIVER_PACKAGE_INFO_FILENAME);
                if (File.Exists(infoFilePath))
                {
                    try
                    {
                        this.CoDriverPackages[codriverPath].Info = 
                            JsonConvert.DeserializeObject<CoDriverPackageInfo>(File.ReadAllText(infoFilePath));
                        this.CoDriverPackages[codriverPath].Info.Path = codriverPath;
                    }
                    catch
                    {
                        // boom
                    }
                }
                else
                {
                    this.CoDriverPackages[codriverPath].Info = new CoDriverPackageInfo()
                    {
                        name = codriverPath,
                        Path = codriverPath,
                        version = "0.0.0",
                    };
                }

                List<string> filePaths = new List<string>();
                // try file directly

                foreach (var supportedFilter in this.SupportedAudioTypes)
                {
                    filePaths.AddRange(Directory.GetFiles(codriverPath, supportedFilter));
                }

                foreach (var f in filePaths)
                {
                    if (Config.Instance.PreloadSounds)
                    {
                        this.CoDriverPackages[codriverPath].tokens[Path.GetFileNameWithoutExtension(f)] =
                            new List<AutoResampledCachedSound>() { new AutoResampledCachedSound(f) };
                    }
                    else
                    {
                        this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileNameWithoutExtension(f)] =
                            new List<string>() { f };
                    }
                }

                // not found, try folders
                var soundFilePaths = Directory.GetDirectories(codriverPath);
                foreach (var soundFilePath in soundFilePaths)
                {
                    filePaths.Clear();
                    //var soundFilePath = string.Format("{0}/{1}", codriverPath, keyword);
                    if (Directory.Exists(soundFilePath))
                    {
                        if (Config.Instance.PreloadSounds)
                        {
                            this.CoDriverPackages[codriverPath].tokens[Path.GetFileName(soundFilePath)] = new List<AutoResampledCachedSound>();
                        }
                        else
                        {
                            this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileName(soundFilePath)] = new List<string>();
                        }
                        // load all files
                        foreach (var supportedFilter in this.SupportedAudioTypes)
                        {
                            filePaths.AddRange(Directory.GetFiles(soundFilePath, supportedFilter));
                        }

                        foreach (var filePath in filePaths)
                        {
                            if (Config.Instance.PreloadSounds)
                            {
                                this.CoDriverPackages[codriverPath].tokens[Path.GetFileName(soundFilePath)].Add(new AutoResampledCachedSound(filePath));
                            }
                            else
                            {
                                this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileName(soundFilePath)].Add(filePath);
                            }
                        }
                    }
                }
            }
        }

        private AutoResampledCachedSound getSoundFromCache(string path)
        {
            this.soundCache.AddOrUpdate(path, new AutoResampledCachedSound(path), (key, oldValue) => oldValue);
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
            var dirs = Directory.GetDirectories(AppLevelVariables.Instance.GetPath("codrivers\\")).ToList();
            if (Directory.Exists(Config.Instance.AdditionalCoDriverPackagesSearchPath))
            {
                dirs.AddRange(Directory.GetDirectories(Config.Instance.AdditionalCoDriverPackagesSearchPath));
            }

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

        public void StartReplaying(IGame game, string itinerary, int playMode = 0)
        {
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
                foreach (var supportedFilter in this.SupportedAudioTypes)
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


            this.CurrentPlayIndex = 0;
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
            if (ScriptResource.ALIAS_CONSTRUCTED.ContainsKey(keyword))
            {
                keyword = ScriptResource.ALIAS_CONSTRUCTED[keyword].Item2;
            }

            var package = this.CoDriverPackages[codriverPackage];
            if (Config.Instance.PreloadSounds && package.tokens.ContainsKey(keyword))
            {
                var tokens = package.tokens[keyword];
                
                if (Config.Instance.AudioProcessType == (int)AudioProcessType.CutHeadAndTail) 
                {
                    return new AutoResampledCachedSound(tokens[this._random.Next(0, tokens.Count)].CutHeadAndTail(Config.Instance.FactorToRemoveSpaceFromAudioFiles));
                }
                return tokens[this._random.Next(0, tokens.Count)];
            }
            if (!Config.Instance.PreloadSounds && package.tokensPath.ContainsKey(keyword))
            {
                var tokens = package.tokensPath[keyword];
                if (tokens.Count > 0) 
                {
                    return this.getSoundFromCache(tokens[this._random.Next(0, tokens.Count)]);
                } else {
                    // No sound file in the folder
                    return new AutoResampledCachedSound();
                }
            }

            // not found, try fallback keyword
            if (ScriptResource.FALLBACK.ContainsKey(keyword))
            {
                var fallbacks = ScriptResource.FALLBACK[keyword].Split('>', StringSplitOptions.RemoveEmptyEntries);
                if (fallbacks.Length > 1)
                {
                    AutoResampledCachedSound sound = new AutoResampledCachedSound();
                    foreach (var fallback in fallbacks)
                    {
                        sound.Append(getSoundByKeyword(fallback, codriverPackage));
                    }
                    return sound;
                } else if (fallbacks.Length == 1)
                {
                    return getSoundByKeyword(fallbacks[0], codriverPackage);
                }
            }

            if (!isFinal && Config.Instance.UseDefaultSoundPackageForFallback)
            {
                // not found, try default 
                return getSoundByKeyword(keyword, AppLevelVariables.Instance.GetPath(Constants.DEFAULT_CODRIVER), true);
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
            Debug.WriteLine("Playing");
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

        public void PlaySystem(string sound)
        {
            Debug.WriteLine("Playing system sound : {0}", sound);
            var audio = this.getSoundByKeyword(sound, this.CurrentCoDriverSoundPackagePath);
            audio.PlaySpeed = this.CurrentPlaySpeed;
            audio.Amplification = this.CurrentPlayAmplification;
            audio.Tension = this.CurrentTension;
            this.PlaySound(audio, false);
        }

        private void PlaySound(AutoResampledCachedSound sound, bool isSequential) {
            if (!Config.Instance.UI_Mute) {
                this.Player.PlaybackRate = this.CurrentPlaySpeed;
                this.Player.PlaySound(sound, isSequential);
            }
        }

        public void ReIndex(float distance)
        {
            var res = this.AudioFiles.BinarySearch(new AudioFile() { Distance = (int)distance }, Comparer<AudioFile>.Create((a, b) => a.Distance.CompareTo(b.Distance)));
            res = res > 0 ? res - 1 : ~res;
            this.CurrentPlayIndex = res;
        }
    }
}
