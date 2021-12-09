using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Reflection;

namespace ZTMZ.PacenoteTool.Base
{
    public class Config
    {
        public const string CONFIG_FILE = "config.json";
        public const string USER_CONFIG_FILE = "userconfig.json";

        private Config()
        {

        }
        private static Config _instance;
        public IList<string> SupportedAudioTypes { set; get; }

        public float ScriptMode_MinDistanceForMerge { set; get; } = 29.7f;
        public float ScriptMode_PlaySecondsAdvanced { set; get; } = 4f;

        public bool UseSequentialMixerToHandleAudioConflict { set; get; } = true;

        public int UDPListenPort { set; get; } = 20777;

        public bool WarnIfPortMismatch { set; get; } = true;

        public int LoopbackCaptureSampleRate { set; get; } = 48000;
        public int LoopbackCaptureChannels { set; get; } = 2;

        public int PlaybackDeviceDesiredLatency { set; get; } = 175;

        public bool AutoCleanTempFiles { set; get; } = true;

        public int AutoScript_SamplesCountBeforeClip { set; get; } = 25;

        public int AutoScript_RecognizeThreshold { set; get; } = 5;
        public int AutoScript_RecognizePatience { set; get; } = 10;
        public bool AutoScript_HackGameWhenStart { set; get; } = false;

        public bool UseDefaultSoundPackageByDefault { set; get; } = true;

        public bool PreloadSounds { set; get; } = false;

        public string DirtGamePath { set; get; } = "";

        public string PythonPath { set; get; } = "Python38";

        public string SpeechRecogizerModelPath { set; get; } = "speech_model";


        // user config
        public int UI_SelectedProfile { set; get; } = 0;
        public int UI_SelectedPlaybackDevice { set; get; } = 0;
        public int UI_SelectedAudioPackage { set; get; } = 0;
        public double UI_PlaybackVolume { set; get; } = 0;
        public bool UI_ShowHud { set; get; } = true;
        public double UI_PlaybackAdjustSeconds { set; get; } = 0;


        public void Save(string path=USER_CONFIG_FILE)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Load()
        {
            Config config = null;
            Config userconfig = null;
            // 1. create config if not exist
            if (!File.Exists(CONFIG_FILE))
            {
                config = new Config();
                config.SupportedAudioTypes = new List<string>()
                {
                    "*.wav", "*.mp3", "*.aiff", "*.wma", "*.aac", "*.mp4", "*.m4a"
                };
                config.Save(CONFIG_FILE);
            } else
            {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(CONFIG_FILE));
            }

            if (!File.Exists(USER_CONFIG_FILE))
            {
                File.Copy(CONFIG_FILE, USER_CONFIG_FILE);
                userconfig = config;
            } else
            {
                userconfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText(USER_CONFIG_FILE));
            }

            // 2. merge userconfig with config

            return userconfig;
        }

        public static Config Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Config.Load();
                }

                return _instance;
            }
        }

        public void MergeFrom(Config config)
        {
            var properties = typeof(Config).GetProperties();
            foreach (var p in properties)
            {
                if (p.GetValue(this) != config)
                {

                }
            }
        }
    }


}