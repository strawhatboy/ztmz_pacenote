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

        private Dictionary<string, dynamic> _userconfig = new();
        public IList<string> SupportedAudioTypes { set; get; }

        private float _ScriptMode_MinDistanceForMerge = 30f;
        public float ScriptMode_MinDistanceForMerge
        {
            set { this._ScriptMode_MinDistanceForMerge = value; this._userconfig["ScriptMode_MinDistanceForMerge"] = value; }
            get => this._ScriptMode_MinDistanceForMerge;
        }

        private float _ScriptMode_PlaySecondsAdvanced = 4f;
        public float ScriptMode_PlaySecondsAdvanced
        {
            set { this._ScriptMode_PlaySecondsAdvanced = value; this._userconfig["ScriptMode_PlaySecondsAdvanced"] = value; }
            get => this._ScriptMode_PlaySecondsAdvanced;
        }

        private bool _UseSequentialMixerToHandleAudioConflict = true;
        public bool UseSequentialMixerToHandleAudioConflict
        {
            set { this._UseSequentialMixerToHandleAudioConflict = value; this._userconfig["UseSequentialMixerToHandleAudioConflict"] = value; }
            get => this._UseSequentialMixerToHandleAudioConflict;
        }

        private int _UDPListenPort = 20777;
        public int UDPListenPort
        {
            set { this._UDPListenPort = value; this._userconfig["UDPListenPort"] = value; }
            get => this._UDPListenPort;
        }

        private bool _WarnIfPortMismatch = true;
        public bool WarnIfPortMismatch
        {
            set { this._WarnIfPortMismatch = value; this._userconfig["WarnIfPortMismatch"] = value; }
            get => this._WarnIfPortMismatch;
        }

        private int _LoopbackCaptureSampleRate = 48000;
        public int LoopbackCaptureSampleRate
        {
            set { this._LoopbackCaptureSampleRate = value; this._userconfig["LoopbackCaptureSampleRate"] = value; }
            get => this._LoopbackCaptureSampleRate;
        }

        private int _LoopbackCaptureChannels = 2;
        public int LoopbackCaptureChannels
        {
            set { this._LoopbackCaptureChannels = value; this._userconfig["LoopbackCaptureChannels"] = value; }
            get => this._LoopbackCaptureChannels;
        }

        private int _PlaybackDeviceDesiredLatency = 175;
        public int PlaybackDeviceDesiredLatency
        {
            set { this._PlaybackDeviceDesiredLatency = value; this._userconfig["PlaybackDeviceDesiredLatency"] = value; }
            get => this._PlaybackDeviceDesiredLatency;
        }

        private bool _AutoCleanTempFiles = true;
        public bool AutoCleanTempFiles
        {
            set { this._AutoCleanTempFiles = value; this._userconfig["AutoCleanTempFiles"] = value; }
            get => this._AutoCleanTempFiles;
        }

        private int _AutoScript_SamplesCountBeforeClip = 25;
        public int AutoScript_SamplesCountBeforeClip
        {
            set { this._AutoScript_SamplesCountBeforeClip = value; this._userconfig["AutoScript_SamplesCountBeforeClip"] = value; }
            get => this._AutoScript_SamplesCountBeforeClip;
        }

        private int _AutoScript_RecognizeThreshold = 5;
        public int AutoScript_RecognizeThreshold
        {
            set { this._AutoScript_RecognizeThreshold = value; this._userconfig["AutoScript_RecognizeThreshold"] = value; }
            get => this._AutoScript_RecognizeThreshold;
        }

        private int _AutoScript_RecognizePatience = 10;
        public int AutoScript_RecognizePatience
        {
            set { this._AutoScript_RecognizePatience = value; this._userconfig["AutoScript_RecognizePatience"] = value; }
            get => this._AutoScript_RecognizePatience;
        }

        private bool _AutoScript_HackGameWhenStart = false;
        public bool AutoScript_HackGameWhenStart
        {
            set { this._AutoScript_HackGameWhenStart = value; this._userconfig["AutoScript_HackGameWhenStart"] = value; }
            get => this._AutoScript_HackGameWhenStart;
        }

        private bool _UseDefaultSoundPackageByDefault = false;
        public bool UseDefaultSoundPackageByDefault
        {
            set { this._UseDefaultSoundPackageByDefault = value; this._userconfig["UseDefaultSoundPackageByDefault"] = value; }
            get => this._UseDefaultSoundPackageByDefault;
        }

        private bool _PreloadSounds = false;
        public bool PreloadSounds
        {
            set { this._PreloadSounds = value; this._userconfig["PreloadSounds"] = value; }
            get => this._PreloadSounds;
        }

        private string _DirtGamePath = "";
        public string DirtGamePath
        {
            set { this._DirtGamePath = value; this._userconfig["DirtGamePath"] = value; }
            get => this._DirtGamePath;
        }

        private string _PythonPath = "Python38";
        public string PythonPath
        {
            set { this._PythonPath = value; this._userconfig["PythonPath"] = value; }
            get => this._PythonPath;
        }

        private string _SpeechRecogizerModelPath = "speech_model";
        public string SpeechRecogizerModelPath
        {
            set { this._SpeechRecogizerModelPath = value; this._userconfig["SpeechRecogizerModelPath"] = value; }
            get => this._SpeechRecogizerModelPath;
        }


        // user config
        private int _UI_SelectedProfile = 0;
        public int UI_SelectedProfile
        {
            set { this._UI_SelectedProfile = value; this._userconfig["UI_SelectedProfile"] = value; }
            get => this._UI_SelectedProfile;
        }

        private int _UI_SelectedPlaybackDevice = 0;
        public int UI_SelectedPlaybackDevice
        {
            set { this._UI_SelectedPlaybackDevice = value; this._userconfig["UI_SelectedPlaybackDevice"] = value; }
            get => this._UI_SelectedPlaybackDevice;
        }

        private int _UI_SelectedAudioPackage = 0;
        public int UI_SelectedAudioPackage
        {
            set { this._UI_SelectedAudioPackage = value; this._userconfig["UI_SelectedAudioPackage"] = value; }
            get => this._UI_SelectedAudioPackage;
        }

        private double _UI_PlaybackVolume = 0;
        public double UI_PlaybackVolume
        {
            set { this._UI_PlaybackVolume = value; this._userconfig["UI_PlaybackVolume"] = value; }
            get => this._UI_PlaybackVolume;
        }

        private bool _UI_ShowHud = true;
        public bool UI_ShowHud
        {
            set { this._UI_ShowHud = value; this._userconfig["UI_ShowHud"] = value; }
            get => this._UI_ShowHud;
        }

        private double _UI_PlaybackAdjustSeconds = 0;
        public double UI_PlaybackAdjustSeconds
        {
            set { this._UI_PlaybackAdjustSeconds = value; this._userconfig["UI_PlaybackAdjustSeconds"] = value; }
            get => this._UI_PlaybackAdjustSeconds;
        }

        private bool _PlayStartAndEndSound = true;
        public bool PlayStartAndEndSound
        {
            set { this._PlayStartAndEndSound = value; this._userconfig["PlayStartAndEndSound"] = value; }
            get => this._PlayStartAndEndSound;
        }

        private bool _PlayCollisionSound = true;
        public bool PlayCollisionSound
        {
            set { this._PlayCollisionSound = value; this._userconfig["PlayCollisionSound"] = value; }
            get => this._PlayCollisionSound;
        }
        
        
        private int _HudFPS = 30;
        public int HudFPS
        {
            set { this._HudFPS = value; this._userconfig["HudFPS"] = value; }
            get => this._HudFPS;
        }


        public void Save(string path = USER_CONFIG_FILE)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void SaveUserConfig()
        {
            var content = System.Text.Json.JsonSerializer.Serialize(this._userconfig);
            File.WriteAllText(USER_CONFIG_FILE, content);
        }

        public static Config Load(bool returnDefault = false)
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
            }
            else
            {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(CONFIG_FILE));
            }

            userconfig = config;
            if (!File.Exists(USER_CONFIG_FILE))
            {
                //File.Copy(CONFIG_FILE, USER_CONFIG_FILE);
                File.WriteAllText(USER_CONFIG_FILE, "{ }");
            }
            else
            {
                var rawUserConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, dynamic>>(File.ReadAllText(USER_CONFIG_FILE));
                userconfig.MergeFromUserConfig(rawUserConfig);
            }

            // 2. merge userconfig with config

            return returnDefault ? config : userconfig;
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

        public void MergeFromUserConfig(Dictionary<string, dynamic> userConfig)
        {
            this._userconfig = userConfig;
            var properties = typeof(Config).GetProperties();
            foreach (var p in properties)
            {
                if (userConfig.ContainsKey(p.Name))
                { //  && p.GetValue(this) != userConfig[p.Name]
                    if (userConfig[p.Name].GetType() == typeof(System.Text.Json.JsonElement))
                    {
                        var element = (System.Text.Json.JsonElement)userConfig[p.Name];
                        p.SetValue(this, System.Text.Json.JsonSerializer.Deserialize(element.GetRawText(), p.PropertyType));
                    }
                }
            }
        }

        public void ResetToDefault()
        {
            var defaultConfig = Load(true);
            var properties = typeof(Config).GetProperties();
            foreach (var p in properties)
            {
                p.SetValue(this, p.GetValue(defaultConfig));
            }
        }
    }


}