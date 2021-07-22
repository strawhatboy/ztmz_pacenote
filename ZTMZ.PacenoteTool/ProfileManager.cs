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
        public AutoResampledCachedSound Sound { set; get; }
    }

    public class ProfileManager
    {
        public static string CODRIVER_FILENAME = "codriver.txt";
        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public string CurrentCoDriverName { set; get; }

        public string CurrentScriptPath { set; get; }
        public ScriptReader CurrentScriptReader { get; private set; }

        public string CurrentCoDriverSoundPackagePath { set; get; }

        public int CurrentPlayIndex { set; get; } = 0;

        public int CurrentPlayDeviceId { set; get; } = 0;

        private AudioFileReader _exampleAudio;
        private WaveOutEvent _exampleWaveOut;
        private Random _random = new Random();


        public IList<AudioFile> AudioFiles { set; get; } = new List<AudioFile>();

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
            this.CreateNewProfile("default");

            //load example audio file?
            this.initExampleAudio();

            //load supported audio types

            // var jsonContent = File.ReadAllText("supported_audio_types.json");
            // this.SupportedAudioTypes = JsonConvert.DeserializeObject<string[]>(jsonContent).ToList();
            this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;
        }

        private void initExampleAudio()
        {
            this._exampleAudio = new AudioFileReader("Alarm01.wav");
            //this._exampleAudio = new AudioFileReader("20210715_152916.m4a");
            this._exampleWaveOut = new WaveOutEvent();
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
            string filePath = string.Format("profiles/{0}/{1}.pacenote", this.CurrentProfile, itinerary);
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
            List<AudioFile> audioFiles = new List<AudioFile>();

            // clear lists
            this.Player?.Dispose();
            //foreach (var f in this.AudioFiles)
            //{
            //f.AudioFileReader.Dispose();
            //}
            this.AudioFiles.Clear();

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
            }

            if (playMode == 1 || playMode == 2)
            {
                // script mode now!
                var reader = ScriptReader.ReadFromFile(this.CurrentScriptPath);
                this.CurrentScriptReader = reader;

                AudioFile f = null;
                for (int i = 0; i < reader.PacenoteRecords.Count; i++)
                {
                    var record = reader.PacenoteRecords[i];
                    if (i > 0 &&
                        record.Distance - reader.PacenoteRecords[i - 1].Distance >=
                        Config.Instance.ScriptMode_MinDistanceForMerge)
                    {
                        // merge the two. means keep the f;
                    }
                    else
                    {
                        f = new AudioFile() {Distance = (int) record.Distance};
                    }

                    var sound = new AutoResampledCachedSound();
                    foreach (var note in record.Pacenotes)
                    {
                        sound.Append(this.getSoundByKeyword(note.Note));
                        foreach (var mod in note.Modifiers)
                        {
                            sound.Append(this.getSoundByKeyword(mod));
                        }
                    }

                    if (f.Sound == null)
                    {
                        f.Sound = sound;   
                    }
                    else
                    {
                        // merge
                        f.Sound.Append(sound);
                    }
                    audioFiles.Add(f);
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
            this.Player = new ZTMZAudioPlaybackEngine(this.CurrentPlayDeviceId);


            this.CurrentPlayIndex = 0;
        }

        private AutoResampledCachedSound getSoundByKeyword(string keyword)
        {
            var soundFilePath = string.Format("{0}/{1}", this.CurrentCoDriverSoundPackagePath, keyword);
            // load all files
            List<string> filePaths = new List<string>();
            foreach (var supportedFilter in this.SupportedAudioTypes)
            {
                filePaths.AddRange(Directory.GetFiles(soundFilePath, supportedFilter));
            }

            // random
            var randIndex = this._random.Next(0, filePaths.Count() - 1);
            return new AutoResampledCachedSound(filePaths[randIndex]);
        }

        // need to be run in a non-UI thread
        public void Play()
        {
            //var player = this.Players[this.CurrentPlayIndex++];
            //var waveout = new WaveOut();
            //waveout.DeviceNumber = deviceID;
            //waveout.Init(player);
            //waveout.Play();
            //player.Play();
            //while (player.PlaybackState == PlaybackState.Playing)
            //{
            //    Thread.Sleep(1000);
            //}
            this.Player.PlaySound(this.AudioFiles[this.CurrentPlayIndex++].Sound);
            Debug.WriteLine("Playing");
        }

        // need to be run in a non-UI thread
        public void PlayExample()
        {
            lock (obj)
            {
                this._exampleWaveOut.DeviceNumber = this.CurrentPlayDeviceId;
                this._exampleWaveOut.Init(this._exampleAudio);
                this._exampleWaveOut.Play();
                while (this._exampleWaveOut.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(1000);
                }

                this._exampleWaveOut.Dispose();
                this._exampleAudio.Dispose();
                this.initExampleAudio();
            }
        }
    }
}