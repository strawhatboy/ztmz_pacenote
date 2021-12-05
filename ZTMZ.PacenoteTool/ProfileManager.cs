using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool
{
    public class AudioFile
    {
        //public string Extension { set; get; }
        //public string FileName { set; get; }
        //public string FilePath { set; get; }
        public int Distance { set; get; }

        //public AudioFileReader AudioFileReader { set; get; }
        //public byte[] Content { set; get; }
        public AutoResampledCachedSound Sound { set; get; } = null;
    }

    public class CoDriverPackage
    {
        public Dictionary<string, List<string>> tokensPath { set; get; } = new Dictionary<string, List<string>>();

        public Dictionary<string, List<AutoResampledCachedSound>> tokens { set; get; } = new Dictionary<string, List<AutoResampledCachedSound>>();
    }

    public class ProfileManager
    {
        public static string DEFAULT_PROFILE = "default";
        public static string DEFAULT_CODRIVER = "codrivers/default";
        public static string CODRIVER_FILENAME = "codriver.txt";
        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public string CurrentCoDriverName { set; get; }

        public string CurrentScriptPath { set; get; }
        public ScriptReader CurrentScriptReader { get; private set; }

        public string CurrentCoDriverSoundPackagePath { set; get; }

        public int CurrentPlayIndex { set; get; } = 0;

        private Dictionary<string, AutoResampledCachedSound> soundCache = new();

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
            Directory.CreateDirectory(string.Format("profiles"));
            Directory.CreateDirectory(string.Format("codrivers"));
            this.CreateNewProfile(DEFAULT_PROFILE);

            //load example audio file?
            this.initExampleAudio();

            //load supported audio types

            // var jsonContent = File.ReadAllText("supported_audio_types.json");
            // this.SupportedAudioTypes = JsonConvert.DeserializeObject<string[]>(jsonContent).ToList();
            this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;

            // load all codriver sounds
            this.initCodriverSounds();
        }

        private void initExampleAudio()
        {
            this._exampleAudio = new AutoResampledCachedSound("Alarm01.wav");
            //this._exampleAudio = new AudioFileReader("20210715_152916.m4a");
            // this._exampleWaveOut = new WaveOutEvent();
        }

        private void initCodriverSounds()
        {
            foreach (var codriverPath in this.GetAllCodrivers())
            {
                this.CoDriverPackages[codriverPath] = new CoDriverPackage();
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
            if (!this.soundCache.ContainsKey(path))
            {
                this.soundCache[path] = new AutoResampledCachedSound(path);
            }
            return this.soundCache[path];
        }

        private void clearSoundCache()
        {
            this.soundCache.Clear();
        }

        public List<string> GetAllProfiles()
        {
            return Directory.GetDirectories("profiles/").ToList();
        }

        public List<string> GetAllCodrivers()
        {
            return Directory.GetDirectories("codrivers/").ToList();
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(string.Format("profiles/{0}", profileName));
            this.CurrentProfile = profileName;
        }

        public string StartRecording(string itinerary)
        {
            string filesPath = this.GetRecordingsFolder(itinerary);
            this.CurrentScriptPath = this.GetScriptFile(itinerary);
            this.CurrentItineraryPath = filesPath;
            return filesPath;
        }

        public string GetRecordingsFolder(string itinerary)
        {
            string filesPath = string.Format("profiles/{0}/{1}", this.CurrentProfile, itinerary);
            Directory.CreateDirectory(filesPath);
            return filesPath;
        }
        public string GetScriptFile(string itinerary)
        {
            return this.GetScriptFile(itinerary, this.CurrentProfile);
        }

        public string GetScriptFile(string itinerary, string profile)
        {
            string filePath = string.Format("profiles/{0}/{1}.pacenote", profile, itinerary);
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
            File.WriteAllText(Path.Join(this.CurrentItineraryPath, CODRIVER_FILENAME), codriver);
        }

        public void StartReplaying(string itinerary, int playMode = 0)
        {
            this.CurrentItineraryPath = this.GetRecordingsFolder(itinerary);
            this.CurrentScriptPath = this.GetScriptFile(itinerary);
            // also need to check flag from config file.
            if (string.IsNullOrEmpty(File.ReadAllText(this.CurrentScriptPath).Trim()))
            {
                // fallback to default profile
                this.CurrentScriptPath = this.GetScriptFile(itinerary, DEFAULT_PROFILE);
            }
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
                var codirverfilepath = Path.Join(this.CurrentItineraryPath, CODRIVER_FILENAME);
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
                var reader = ScriptReader.ReadFromFile(this.CurrentScriptPath);
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
                return tokens[this._random.Next(0, tokens.Count)];
            }
            if (!Config.Instance.PreloadSounds && package.tokensPath.ContainsKey(keyword))
            {
                var tokens = package.tokensPath[keyword];
                return this.getSoundFromCache(tokens[this._random.Next(0, tokens.Count)]);
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

            if (!isFinal)
            {
                // not found, try default 
                return getSoundByKeyword(keyword, DEFAULT_CODRIVER, true);
            }

            return new AutoResampledCachedSound();
        }

        // need to be run in a non-UI thread
        public void Play()
        {
            // try to amplify the sound.
            var sound = this.AudioFiles[this.CurrentPlayIndex++].Sound;
            sound.Amplification = this.CurrentPlayAmplification;
            this.Player.PlaySound(sound);
            Debug.WriteLine("Playing");
        }

        // need to be run in a non-UI thread
        public void PlayExample()
        {
            this._exampleAudio.Amplification = this.CurrentPlayAmplification;
            this.Player.PlaySound(this._exampleAudio);
        }

        public void ReIndex(float distance)
        {
            var res = this.AudioFiles.BinarySearch(new AudioFile() { Distance = (int)distance }, Comparer<AudioFile>.Create((a, b) => a.Distance.CompareTo(b.Distance)));
            res = res > 0 ? res - 1 : ~res;
            this.CurrentPlayIndex = res;
        }
    }
}