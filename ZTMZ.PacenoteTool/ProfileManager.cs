using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;

namespace ZTMZ.PacenoteTool
{
    public class AudioFile
    {
        public string Extension { set; get; }
        public string FileName { set; get; }
        public string FilePath { set; get; }
        public int Distance { set; get; }
        public byte[] Content { set; get; }
    }

    public class ProfileManager
    {
        public static string CODRIVER_FILENAME = "codriver.txt";
        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public string CurrentCoDriverName { set; get; }

        public int CurrentPlayIndex { set; get; } = 0;
        
        public IEnumerable<AudioFile> AudioFiles { set; get; }

        public IEnumerable<SoundPlayer> Players { set; get; }

        public AudioFile CurrentAudioFile => this.CurrentPlayIndex < this.AudioFiles.Count() ? this.AudioFiles.ElementAt(this.CurrentPlayIndex) : null;

        public SoundPlayer CurrentSoundPlayer => this.CurrentPlayIndex < this.Players.Count()
            ? this.Players.ElementAt(this.CurrentPlayIndex)
            : null;

        public ProfileManager()
        {
            Directory.CreateDirectory(string.Format("profiles"));
            this.CreateNewProfile("default");
        }

        public List<string> GetAllProfiles()
        {
            return Directory.GetDirectories("profiles/").ToList();
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(string.Format("profiles/{0}", profileName));
            this.CurrentProfile = profileName;
        }

        public string StartRecording(string itinerary)
        {
            string filesPath = this.GetRecordingsFolder(itinerary);
            this.CurrentItineraryPath = filesPath;
            return filesPath;
        }

        public string GetRecordingsFolder(string itinerary)
        {
            string filesPath = string.Format("profiles/{0}/{1}", this.CurrentProfile, itinerary);
            Directory.CreateDirectory(filesPath);
            return filesPath;
        }

        public void StopRecording(string codriver)
        {
            // save the codriver name to config
            File.WriteAllText(Path.Join(this.CurrentItineraryPath, CODRIVER_FILENAME), codriver);
        }

        public void StartReplaying(string itinerary)
        {
            // load codriver name
            this.CurrentItineraryPath = this.GetRecordingsFolder(itinerary);
            var codriver = File.ReadLines(Path.Join(this.CurrentItineraryPath, CODRIVER_FILENAME)).FirstOrDefault();
            this.CurrentCoDriverName = codriver;

            // load all files
            var filePaths = Directory.GetFiles(this.CurrentItineraryPath, "*.mp3").AsEnumerable();
            // also allow wav files
            filePaths = filePaths.Concat(Directory.GetFiles(this.CurrentItineraryPath, "*.wav"));

            // get files
            var audioFiles = from path in filePaths
                select
                    new AudioFile()
                    {
                        FileName = Path.GetFileName(path),
                        FilePath = path,
                        Distance = int.Parse(Path.GetFileNameWithoutExtension(path)),
                        Extension = Path.GetExtension(path),
                        Content = File.ReadAllBytes(path)
                    };

            var sortedAudioFiles = from audioFile in audioFiles 
                orderby audioFile.Distance ascending 
                select audioFile;

            this.AudioFiles = sortedAudioFiles;

            this.Players = from audiofile in sortedAudioFiles
                select new SoundPlayer(new MemoryStream(audiofile.Content));

            this.CurrentPlayIndex = 0;
        }

        public void Play()
        {
            var player = this.Players.ElementAt(this.CurrentPlayIndex++);
            player.Play();
        }
    }
}