using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Reflection;
using System.Globalization;
using ZTMZ.PacenoteTool.Base.Game;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base
{
    public enum AudioProcessType
    {
        None = 0,
        CutHeadAndTail = 1,
        MixTailAndHead = 2,
    }

    public class Config
    {
        private object _lock = new();
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public static string CONFIG_FILE = AppLevelVariables.Instance.GetPath("config.json");
        public static string USER_CONFIG_FILE = AppLevelVariables.Instance.GetPath("userconfig.json");

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

        [Obsolete("Use udp port in each game config respectively", false)]
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

        private bool _UseDefaultSoundPackageByDefault = true;
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
        private bool _UI_Mute = false;
        public bool UI_Mute
        {
            set { this._UI_Mute = value; this._userconfig["UI_Mute"] = value; }
            get => this._UI_Mute;
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

        private float _UI_PlaybackSpeed = 1.0f;
        public float UI_PlaybackSpeed
        {
            set { this._UI_PlaybackSpeed = value; this._userconfig["UI_PlaybackSpeed"] = value; }
            get => this._UI_PlaybackSpeed;
        }
        private int _UI_SelectedGame = 1;
        public int UI_SelectedGame
        {
            set { this._UI_SelectedGame = value; this._userconfig["UI_SelectedGame"] = value; }
            get => this._UI_SelectedGame;
        }

        private bool _UseDynamicPlaybackSpeed = true;
        public bool UseDynamicPlaybackSpeed
        {
            set { this._UseDynamicPlaybackSpeed = value; this._userconfig["UseDynamicPlaybackSpeed"] = value; }
            get => this._UseDynamicPlaybackSpeed;
        }

        private float _DynamicPlaybackMaxSpeed = 1.2f;
        public float DynamicPlaybackMaxSpeed
        {
            set { this._DynamicPlaybackMaxSpeed = value; this._userconfig["DynamicPlaybackMaxSpeed"] = value; }
            get => this._DynamicPlaybackMaxSpeed;
        }

        private bool _UseTempoInsteadOfRate = true;
        public bool UseTempoInsteadOfRate
        {
            set { this._UseTempoInsteadOfRate = value; this._userconfig["UseTempoInsteadOfRate"] = value; }
            get => this._UseTempoInsteadOfRate;
        }
        

        private bool _PlayStartAndEndSound = false;
        public bool PlayStartAndEndSound
        {
            set { this._PlayStartAndEndSound = value; this._userconfig["PlayStartAndEndSound"] = value; }
            get => this._PlayStartAndEndSound;
        }

        private bool _PlayGoSound = true;
        public bool PlayGoSound
        {
            set { this._PlayGoSound = value; this._userconfig["PlayGoSound"] = value; }
            get => this._PlayGoSound;
        }

        private bool _PlayCollisionSound = false;
        public bool PlayCollisionSound
        {
            set { this._PlayCollisionSound = value; this._userconfig["PlayCollisionSound"] = value; }
            get => this._PlayCollisionSound;
        }

        private bool _PlayWheelAbnormalSound = false;
        public bool PlayWheelAbnormalSound
        {
            set { this._PlayWheelAbnormalSound = value; this._userconfig["PlayWheelAbnormalSound"] = value; }
            get => this._PlayWheelAbnormalSound;
        }

        private bool _UseDynamicVolume = true;

        public bool UseDynamicVolume
        {
            set { this._UseDynamicVolume = value; this._userconfig["UseDynamicVolume"] = value; }
            get => this._UseDynamicVolume;
        }

        private int _DynamicVolumePerturbationFrequency = 3;
        public int DynamicVolumePerturbationFrequency
        {
            set { this._DynamicVolumePerturbationFrequency = value; this._userconfig["DynamicVolumePerturbationFrequency"] = value; }
            get => this._DynamicVolumePerturbationFrequency;
        }

        private float _DynamicVolumePerturbationAmplitude = 0.75f;
        public float DynamicVolumePerturbationAmplitude
        {
            set { this._DynamicVolumePerturbationAmplitude = value; this._userconfig["DynamicVolumePerturbationAmplitude"] = value; }
            get => this._DynamicVolumePerturbationAmplitude;
        }

        private float _CollisionSpeedChangeThreshold_Slight = 160f;
        public float CollisionSpeedChangeThreshold_Slight
        {
            set { this._CollisionSpeedChangeThreshold_Slight = value; this._userconfig["CollisionSpeedChangeThreshold_Slight"] = value; }
            get => this._CollisionSpeedChangeThreshold_Slight;
        }

        private float _CollisionSpeedChangeThreshold_Medium = 280f;
        public float CollisionSpeedChangeThreshold_Medium
        {
            set { this._CollisionSpeedChangeThreshold_Medium = value; this._userconfig["CollisionSpeedChangeThreshold_Medium"] = value; }
            get => this._CollisionSpeedChangeThreshold_Medium;
        }

        private float _CollisionSpeedChangeThreshold_Severe = 400f;    // 400 m/s^2 = 40g，安全气囊弹出时的加速度
        public float CollisionSpeedChangeThreshold_Severe
        {
            set { this._CollisionSpeedChangeThreshold_Severe = value; this._userconfig["CollisionSpeedChangeThreshold_Severe"] = value; }
            get => this._CollisionSpeedChangeThreshold_Severe;
        }


        private int _WheelAbnormalFramesReportThreshold = 120;
        public int WheelAbnormalFramesReportThreshold
        {
            set { this._WheelAbnormalFramesReportThreshold = value; this._userconfig["WheelAbnormalFramesReportThreshold"] = value; }
            get => this._WheelAbnormalFramesReportThreshold;
        }

        private int _WheelAbnormalPercentageReportThreshold = 40;
        public int WheelAbnormalPercentageReportThreshold
        {
            set { this._WheelAbnormalPercentageReportThreshold = value; this._userconfig["WheelAbnormalPercentageReportThreshold"] = value; }
            get => this._WheelAbnormalPercentageReportThreshold;
        }

        private bool _UseReversePacenote = false;
        public bool UseReversePacenote
        {
            set { this._UseReversePacenote = value; this._userconfig["UseReversePacenote"] = value; }
            get => this._UseReversePacenote;
        }

        private int _ProcessWatcherInterval = 1000;
        public int ProcessWatcherInterval
        {
            set { this._ProcessWatcherInterval = value; this._userconfig["ProcessWatcherInterval"] = value; }
            get => this._ProcessWatcherInterval;
        }

        private bool _CloseWindowToMinimize = false;
        public bool CloseWindowToMinimize
        {
            set { this._CloseWindowToMinimize = value; this._userconfig["CloseWindowToMinimize"] = value; }
            get => this._CloseWindowToMinimize;
        }

        private bool _ShowClosePrompt = true;
        public bool ShowClosePrompt
        {
            set { this._ShowClosePrompt = value; this._userconfig["ShowClosePrompt"] = value; }
            get => this._ShowClosePrompt;
        }

        #region HUD

        private bool _HudLockFPS = true;
        public bool HudLockFPS
        {
            set { this._HudLockFPS = value; this._userconfig["HudLockFPS"] = value; }
            get => this._HudLockFPS;
        }

        private int _HudFPS = 60;
        public int HudFPS
        {
            set { this._HudFPS = value; this._userconfig["HudFPS"] = value; }
            get => this._HudFPS;
        }

        private bool _HudTopMost = false;
        public bool HudTopMost
        {
            set { this._HudTopMost = value; this._userconfig["HudTopMost"] = value; }
            get => this._HudTopMost;
        }
        
        private bool _HudChromaKeyMode = false;
        public bool HudChromaKeyMode
        {
            set { this._HudChromaKeyMode = value; this._userconfig["HudChromaKeyMode"] = value; }
            get => this._HudChromaKeyMode;
        }
        
        private bool _HudShowTelemetry = true;
        public bool HudShowTelemetry
        {
            set { this._HudShowTelemetry = value; this._userconfig["HudShowTelemetry"] = value; }
            get => this._HudShowTelemetry;
        }
        
        private bool _HudShowDebugTelemetry = false;
        public bool HudShowDebugTelemetry
        {
            set { this._HudShowDebugTelemetry = value; this._userconfig["HudShowDebugTelemetry"] = value; }
            get => this._HudShowDebugTelemetry;
        }
        
        // height percentage of the game window (0~0.3)
        private float _HudSizePercentage = 0.12f;
        public float HudSizePercentage
        {
            set { this._HudSizePercentage = value; this._userconfig["HudSizePercentage"] = value; }
            get => this._HudSizePercentage;   
        }
        
        // Horizontal Padding (according to the height) (0~0.5)
        private float _HudPaddingH = 0.2f;
        public float HudPaddingH
        {
            set { this._HudPaddingH = value; this._userconfig["HudPaddingH"] = value; }
            get => this._HudPaddingH;   
        }
        
        // Vertical Padding (according to the height) (0~0.3)
        private float _HudPaddingV = 0.1f;
        public float HudPaddingV
        {
            set { this._HudPaddingV = value; this._userconfig["HudPaddingV"] = value; }
            get => this._HudPaddingV;   
        }
        
        // Spacing between elements (according to the height) (0~0.3)
        private float _HudElementSpacing = 0.2f;
        public float HudElementSpacing
        {
            set { this._HudElementSpacing = value; this._userconfig["HudElementSpacing"] = value; }
            get => this._HudElementSpacing;   
        }
        
        private float _HudSectorThicknessRatio = 0.25f;
        public float HudSectorThicknessRatio
        {
            set { this._HudSectorThicknessRatio = value; this._userconfig["HudSectorThicknessRatio"] = value; }
            get => this._HudSectorThicknessRatio;   
        }

        private float _HudBackgroundOpacity = 0.5f;
        public float HudBackgroundOpacity
        {
            set { this._HudBackgroundOpacity = value; this._userconfig["HudBackgroundOpacity"] = value; }
            get => this._HudBackgroundOpacity;   
        }

        private float _HudWHRatio = 6f;
        public float HudWHRatio
        {
            set { this._HudWHRatio = value; this._userconfig["HudWHRatio"] = value; }
            get => this._HudWHRatio;   
        }
        
        
        
        private bool _HudTelemetryShowGBall = true;
        public bool HudTelemetryShowGBall
        {
            set { this._HudTelemetryShowGBall = value; this._userconfig["HudTelemetryShowGBall"] = value; }
            get => this._HudTelemetryShowGBall;
        }
        private bool _HudTelemetryShowSpdSector = true;
        public bool HudTelemetryShowSpdSector
        {
            set { this._HudTelemetryShowSpdSector = value; this._userconfig["HudTelemetryShowSpdSector"] = value; }
            get => this._HudTelemetryShowSpdSector;
        }
        private bool _HudTelemetryShowPedals = true;
        public bool HudTelemetryShowPedals
        {
            set { this._HudTelemetryShowPedals = value; this._userconfig["HudTelemetryShowPedals"] = value; }
            get => this._HudTelemetryShowPedals;
        }
        private bool _HudTelemetryShowGear = true;
        public bool HudTelemetryShowGear
        {
            set { this._HudTelemetryShowGear = value; this._userconfig["HudTelemetryShowGear"] = value; }
            get => this._HudTelemetryShowGear;
        }
        private bool _HudTelemetryShowSteering = true;
        public bool HudTelemetryShowSteering
        {
            set { this._HudTelemetryShowSteering = value; this._userconfig["HudTelemetryShowSteering"] = value; }
            get => this._HudTelemetryShowSteering;
        }
        private int _HudTelemetrySteeringDegree = 540;
        public int HudTelemetrySteeringDegree
        {
            set { this._HudTelemetrySteeringDegree = value; this._userconfig["HudTelemetrySteeringDegree"] = value; }
            get => this._HudTelemetrySteeringDegree;
        }
        private bool _HudTelemetryShowRPMSector = true;
        public bool HudTelemetryShowRPMSector
        {
            set { this._HudTelemetryShowRPMSector = value; this._userconfig["HudTelemetryShowRPMSector"] = value; }
            get => this._HudTelemetryShowRPMSector;
        }
        private bool _HudTelemetryShowSuspensionBars = true;
        public bool HudTelemetryShowSuspensionBars
        {
            set { this._HudTelemetryShowSuspensionBars = value; this._userconfig["HudTelemetryShowSuspensionBars"] = value; }
            get => this._HudTelemetryShowSuspensionBars;
        }

        private float _HudShiftGreenZone = 0.75f;
        public float HudShiftGreenZone
        {
            set { this._HudShiftGreenZone = value; this._userconfig["HudShiftGreenZone"] = value; }
            get => this._HudShiftGreenZone;
        }

        private float _HudShiftYellowZone = 0.85f;
        public float HudShiftYellowZone
        {
            set { this._HudShiftYellowZone = value; this._userconfig["HudShiftYellowZone"] = value; }
            get => this._HudShiftYellowZone;
        }

        private float _HudShiftRedZone = 0.95f;
        public float HudShiftRedZone
        {
            set { this._HudShiftRedZone = value; this._userconfig["HudShiftRedZone"] = value; }
            get => this._HudShiftRedZone;
        }

        private int _UI_SelectedHudIndex = 0;
        public int UI_SelectedHudIndex
        {
            set { this._UI_SelectedHudIndex = value; this._userconfig["UI_SelectedHudIndex"] = value; }
            get => this._UI_SelectedHudIndex;
        }

        #endregion

        #region VrOverlay

        private bool _VrUseZTMZHud = true;
        public bool VrUseZTMZHud
        {
            set { this._VrUseZTMZHud = value; this._userconfig["VrUseZTMZHud"] = value; }
            get => this._VrUseZTMZHud;
        }

        private bool _VrShowOverlay = true;
        public bool VrShowOverlay
        {
            set { this._VrShowOverlay = value; this._userconfig["VrShowOverlay"] = value; }
            get => this._VrShowOverlay;
        }

        private string _VrOverlayWindowName = Constants.HUD_WINDOW_NAME;
        public string VrOverlayWindowName
        {
            set { this._VrOverlayWindowName = value; this._userconfig["VrOverlayWindowName"] = value; }
            get => this._VrOverlayWindowName;
        }

        public float _VrOverlayPositionX = 0.0f;
        public float VrOverlayPositionX
        {
            set { this._VrOverlayPositionX = value; this._userconfig["VrOverlayPositionX"] = value; }
            get => this._VrOverlayPositionX;
        }

        public float _VrOverlayPositionY = 0.0f;
        public float VrOverlayPositionY
        {
            set { this._VrOverlayPositionY = value; this._userconfig["VrOverlayPositionY"] = value; }
            get => this._VrOverlayPositionY;
        }

        public float _VrOverlayPositionZ = 1.0f;
        public float VrOverlayPositionZ
        {
            set { this._VrOverlayPositionZ = value; this._userconfig["VrOverlayPositionZ"] = value; }
            get => this._VrOverlayPositionZ;
        }

        public float _VrOverlayRotationX = 0.0f;
        public float VrOverlayRotationX
        {
            set { this._VrOverlayRotationX = value; this._userconfig["VrOverlayRotationX"] = value; }
            get => this._VrOverlayRotationX;
        }

        public float _VrOverlayRotationY = 0.0f;
        public float VrOverlayRotationY
        {
            set { this._VrOverlayRotationY = value; this._userconfig["VrOverlayRotationY"] = value; }
            get => this._VrOverlayRotationY;
        }

        public float _VrOverlayRotationZ = 0.0f;
        public float VrOverlayRotationZ
        {
            set { this._VrOverlayRotationZ = value; this._userconfig["VrOverlayRotationZ"] = value; }
            get => this._VrOverlayRotationZ;
        }

        public float _VrOverlayScale = 0.0f;
        public float VrOverlayScale
        {
            set { this._VrOverlayScale = value; this._userconfig["VrOverlayScale"] = value; }
            get => this._VrOverlayScale;
        }

        #endregion

        private string _Language = CultureInfo.CurrentCulture.Name.ToLower();
        public string Language
        {
            set { this._Language = value; this._userconfig["Language"] = value; }
            get => this._Language;
        }

        private string _SkippedVersion = "1.0.0.0";
        public string SkippedVersion
        {
            set { this._SkippedVersion = value; this._userconfig["SkippedVersion"] = value; }
            get => this._SkippedVersion;
        }

        // user defined dark/light theme
        private bool _IsDarkTheme = false;
        public bool IsDarkTheme
        {
            set { this._IsDarkTheme = value; this._userconfig["IsDarkTheme"] = value; }
            get => this._IsDarkTheme;
        }

        // Windows 10/11 system theme, dark/light
        private bool _UseSystemTheme = true;
        public bool UseSystemTheme
        {
            set { this._UseSystemTheme = value; this._userconfig["UseSystemTheme"] = value; }
            get => this._UseSystemTheme;
        }

        // Windows 10/11 system accent color
        private bool _UseSystemAccentColor = true;
        public bool UseSystemAccentColor
        {
            set { this._UseSystemAccentColor = value; this._userconfig["UseSystemAccentColor"] = value; }
            get => this._UseSystemAccentColor;
        }

        // custom accent color
        private int _AccentColorR = 222;
        public int AccentColorR 
        {
            set { this._AccentColorR = value; this._userconfig["AccentColorR"] = value; }
            get => this._AccentColorR;
        }

        private int _AccentColorG = 156;
        public int AccentColorG 
        {
            set { this._AccentColorG = value; this._userconfig["AccentColorG"] = value; }
            get => this._AccentColorG;
        }

        private int _AccentColorB = 18;
        public int AccentColorB 
        {
            set { this._AccentColorB = value; this._userconfig["AccentColorB"] = value; }
            get => this._AccentColorB;
        }

        private bool _EnableOnlineAnalytics = true;

        [Obsolete("Won't enable online analystics any more")]
        public bool EnableOnlineAnalytics
        {
            set { this._EnableOnlineAnalytics = value; this._userconfig["EnableOnlineAnalytics"] = value; }
            get => this._EnableOnlineAnalytics;
        } 
        private bool _IsOnlineAnalyticsSet = false;

        [Obsolete("Won't enable online analystics any more")]
        public bool IsOnlineAnalyticsSet
        {
            set { this._IsOnlineAnalyticsSet = value; this._userconfig["IsOnlineAnalyticsSet"] = value; }
            get => this._IsOnlineAnalyticsSet;
        } 

        
        private bool _OptInBetaPlan = false;

        public bool OptInBetaPlan
        {
            set { this._OptInBetaPlan = value; this._userconfig["OptInBetaPlan"] = value; }
            get => this._OptInBetaPlan;
        } 

        private int _LogLevel = 4;

        public int LogLevel
        {
            set { this._LogLevel = value; this._userconfig["LogLevel"] = value; }
            get => this._LogLevel;
        } 


        private bool _UseDefaultSoundPackageForFallback = true;
        public bool UseDefaultSoundPackageForFallback
        {
            set { this._UseDefaultSoundPackageForFallback = value; this._userconfig["UseDefaultSoundPackageForFallback"] = value; }
            get => this._UseDefaultSoundPackageForFallback;
        }

        private string _AdditionalCoDriverPackagesSearchPath = "";
        public string AdditionalCoDriverPackagesSearchPath
        {
            set { this._AdditionalCoDriverPackagesSearchPath = value; this._userconfig["AdditionalCoDriverPackagesSearchPath"] = value; }
            get => this._AdditionalCoDriverPackagesSearchPath;
        }

        private string _AdditionalPacenotesDefinitionSearchPath = "";
        public string AdditionalPacenotesDefinitionSearchPath
        {
            set { this._AdditionalPacenotesDefinitionSearchPath = value; this._userconfig["AdditionalPacenotesDefinitionSearchPath"] = value; }
            get => this._AdditionalPacenotesDefinitionSearchPath;
        }

        private bool _RemoveIntoAndOnto = false;
        public bool RemoveIntoAndOnto
        {
            set { this._RemoveIntoAndOnto = value; this._userconfig["RemoveIntoAndOnto"] = value; }
            get => this._RemoveIntoAndOnto;
        }

        private string _ExamplePacenoteString = "3_left,over_crest,dont_cut,into,4_right,tightens,over_narrow_bridge,80";
        public string ExamplePacenoteString
        {
            set { this._ExamplePacenoteString = value; this._userconfig["ExamplePacenoteString"] = value; }
            get => this._ExamplePacenoteString;
        }

        // 5e-1f ~ 1e-5f, determine how compact between the audios
        private float _FactorToRemoveSpaceFromAudioFiles = 1e-5f;
        public float FactorToRemoveSpaceFromAudioFiles
        {
            set { this._FactorToRemoveSpaceFromAudioFiles = value; this._userconfig["FactorToRemoveSpaceFromAudioFiles"] = value; }
            get => this._FactorToRemoveSpaceFromAudioFiles;
        }

        private int _IntercomEffect = 0;
        public int IntercomEffect
        {
            set { this._IntercomEffect = value; this._userconfig["IntercomEffect"] = value; }
            get => this._IntercomEffect;
        }

        // connect the distance call audio to next pacenote, to improve the rhythm, e.g. "3_left [maybe pause] into,4_right"
        private bool _ConnectCloseDistanceCallToNextPacenote = true;
        public bool ConnectCloseDistanceCallToNextPacenote
        {
            set { this._ConnectCloseDistanceCallToNextPacenote = value; this._userconfig["ConnectDistanceCallToNextPacenote"] = value; }
            get => this._ConnectCloseDistanceCallToNextPacenote;
        }

        // if the distance call is numeric, connect it to its previous call. e.g. "3_left/80"
        private bool _ConnectNumericDistanceCallToPreviousPacenote = true;
        public bool ConnectNumericDistanceCallToPreviousPacenote
        {
            set { this._ConnectNumericDistanceCallToPreviousPacenote = value; this._userconfig["ConnectNumericDistanceCallToPreviousPacenote"] = value; }
            get => this._ConnectNumericDistanceCallToPreviousPacenote;
        }

        private int _AudioProcessType = 2;
        public int AudioProcessType
        {
            set { this._AudioProcessType = value; this._userconfig["AudioProcessType"] = value; }
            get => this._AudioProcessType;
        }

        private bool _StartWithWindows = false;
        public bool StartWithWindows
        {
            set { this._StartWithWindows = value; this._userconfig["StartWithWindows"] = value; }
            get => this._StartWithWindows;
        }

        private bool _CheckUpdateWhenStartup = true;
        public bool CheckUpdateWhenStartup
        {
            set { this._CheckUpdateWhenStartup = value; this._userconfig["CheckUpdateWhenStartup"] = value; }
            get => this._CheckUpdateWhenStartup;
        }

        private string _UpdateDefinitionUrl = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate.json";
        public string UpdateDefinitionUrl
        {
            set { this._UpdateDefinitionUrl = value; this._userconfig["UpdateDefinitionUrl"] = value; }
            get => this._UpdateDefinitionUrl;
        }

        private string _UpdateDefinitionUrl_Beta = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate_beta.json";
        public string UpdateDefinitionUrl_Beta
        {
            set { this._UpdateDefinitionUrl_Beta = value; this._userconfig["UpdateDefinitionUrl_Beta"] = value; }
            get => this._UpdateDefinitionUrl_Beta;
        }

        private string _UpdateDefinitionUrl_AudioPkg = "https://gitee.com/ztmz/ztmz_audio_pkgs/raw/master/repo.json";
        public string UpdateDefinitionUrl_AudioPkg
        {
            set { this._UpdateDefinitionUrl_AudioPkg = value; this._userconfig["UpdateDefinitionUrl_AudioPkg"] = value; }
            get => this._UpdateDefinitionUrl_AudioPkg;
        }

        private bool _AutoInitializeGame = true;
        public bool AutoInitializeGame
        {
            set { this._AutoInitializeGame = value; this._userconfig["AutoInitializeGame"] = value; }
            get => this._AutoInitializeGame;
        }

#region Replay
        // for a race, split the replay to ReplaySaveGranularity checkpoints, with the race time stored.
        private int _ReplaySaveGranularity = 256;
        public int ReplaySaveGranularity
        {
            set { this._ReplaySaveGranularity = value; this._userconfig["ReplaySaveGranularity"] = value; }
            get => this._ReplaySaveGranularity;
        }

        // save the replay every ReplaySaveInterval milliseconds
        private int _ReplaySaveInterval = 200;
        public int ReplaySaveInterval
        {
            set { this._ReplaySaveInterval = value; this._userconfig["ReplaySaveInterval"] = value; }
            get => this._ReplaySaveInterval;
        }

        // save the replay without interval, save every frame
        private bool _ReplaySaveWithoutInterval = false;
        public bool ReplaySaveWithoutInterval
        {
            set { this._ReplaySaveWithoutInterval = value; this._userconfig["ReplaySaveWithoutInterval"] = value; }
            get => this._ReplaySaveWithoutInterval;
        }
        
        // turn on to save the replay
        private bool _ReplaySave = true;
        public bool ReplaySave
        {
            set { this._ReplaySave = value; this._userconfig["ReplaySave"] = value; }
            get => this._ReplaySave;
        }

        /// <summary>
        /// Preferred filter for best replay, used for delta calculation during racing.
        /// 0: stage name, 1: stage name+car class, 2: stage name+car name
        /// For online and offline replays, and for leaderboard.
        /// </summary>
        private int _ReplayPreferredFilter = 1;
        public int ReplayPreferredFilter
        {
            set { this._ReplayPreferredFilter = value; this._userconfig["ReplayPreferredFilter"] = value; }
            get => this._ReplayPreferredFilter;
        }

        /// <summary>
        /// Stored replay count limit per stagename + carname, for offline replay.
        /// </summary>
        private int _ReplayStoredCountLimit = 2;
        public int ReplayStoredCountLimit
        {
            set { this._ReplayStoredCountLimit = value; this._userconfig["ReplayStoredCountLimit"] = value; }
            get => this._ReplayStoredCountLimit;
        }

        private bool _OnlineRivalsEnabled = false;
        public bool OnlineRivalsEnabled
        {
            set { this._OnlineRivalsEnabled = value; this._userconfig["OnlineRivalsEnabled"] = value; }
            get => this._OnlineRivalsEnabled;
        }

        /// <summary>
        /// Names of rivals, on racenet
        /// Prior to a race, players can download rivals' replay data, which is then utilized to display a real-time ghost cursor on the progress bar during their own run. This feature also enables dynamic delta calculations, showcasing the time difference between the player’s current performance and their rivals’ recorded runs.
        /// </summary>
        private string _RivalsWatchList = "";
        public string RivalsWatchList
        {
            set { this._RivalsWatchList = value; this._userconfig["RivalsWatchList"] = value; }
            get => this._RivalsWatchList;
        }

        /// <summary>
        /// Whether to store the replay video with OBS Studio
        /// </summary>
        private bool _ReplayOBSSave = false;
        public bool ReplayOBSSave
        {
            set { this._ReplayOBSSave = value; this._userconfig["ReplayOBSSave"] = value; }
            get => this._ReplayOBSSave;
        }

        private string _ReplayOBSWebsocketUrl = "ws://localhost:4455";
        public string ReplayOBSWebsocketUrl
        {
            set { this._ReplayOBSWebsocketUrl = value; this._userconfig["ReplayOBSWebsocketUrl"] = value; }
            get => this._ReplayOBSWebsocketUrl;
        }

        private string _ReplayOBSWebsocketPassword = "";
        public string ReplayOBSWebsocketPassword
        {
            set { this._ReplayOBSWebsocketPassword = value; this._userconfig["ReplayOBSWebsocketPassword"] = value; }
            get => this._ReplayOBSWebsocketPassword;
        }

        private int _ReplayOBSWebsocketTimeout = 1000;
        public int ReplayOBSWebsocketTimeout
        {
            set { this._ReplayOBSWebsocketTimeout = value; this._userconfig["ReplayOBSWebsocketTimeout"] = value; }
            get => this._ReplayOBSWebsocketTimeout;
        }

        /// <summary>
        /// Whether to delete the related video when deleting the replay due to the replay count limit.
        /// </summary>
        private bool _ReplayDeleteRelatedVideo = true;
        public bool ReplayDeleteRelatedVideo
        {
            set { this._ReplayDeleteRelatedVideo = value; this._userconfig["ReplayDeleteRelatedVideo"] = value; }
            get => this._ReplayDeleteRelatedVideo;
        }

        private string _ReplayFFmpegPath = "";
        public string ReplayFFmpegPath
        {
            set { this._ReplayFFmpegPath = value; this._userconfig["ReplayFFmpegPath"] = value; }
            get => this._ReplayFFmpegPath;
        }

        /// <summary>
        /// 0: all, 1: throttle, 2: brake, 3: clutch, 4: handbrake
        /// </summary>
        private int _ReplayPlayPedalsMode = 0;
        public int ReplayPlayPedalsMode
        {
            set { this._ReplayPlayPedalsMode = value; this._userconfig["ReplayPlayPedalsMode"] = value; }
            get => this._ReplayPlayPedalsMode;
        }

        private bool _replayShowDiagrams = false;
        public bool ReplayShowDiagrams
        {
            set { this._replayShowDiagrams = value; this._userconfig["ReplayShowDiagrams"] = value; }
            get => this._replayShowDiagrams;
        }

        private bool _replayCleanUpAbnormalVideo = true;
        public bool ReplayCleanUpAbnormalVideo
        {
            set { this._replayCleanUpAbnormalVideo = value; this._userconfig["ReplayCleanUpAbnormalVideo"] = value; }
            get => this._replayCleanUpAbnormalVideo;
        }

#endregion

        public void Save(string path)
        {
            lock(_lock) {
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
        }

        public void SaveUserConfig()
        {
            var content = System.Text.Json.JsonSerializer.Serialize(this._userconfig);
            lock(_lock) {
                File.WriteAllText(USER_CONFIG_FILE, content);
            }
        }

        public void SaveGameConfig(IGame game)
        {
            var gameConfigPath = AppLevelVariables.Instance.GetPath(Path.Join(Constants.PATH_GAMES, string.Format("{0}.json", game.Name)));
            var configContent = JsonConvert.SerializeObject(game.GameConfigurations, Formatting.Indented);
            lock(_lock) {
                File.WriteAllText(gameConfigPath, configContent);
            }
        }

        public Dictionary<string, IGameConfig> LoadGameConfig(IGame game)
        {
            var gameConfigPath = AppLevelVariables.Instance.GetPath(Path.Join(Constants.PATH_GAMES, string.Format("{0}.json", game.Name)));
            if (!File.Exists(gameConfigPath)) 
            {
                SaveGameConfig(game);
                return game.GameConfigurations;
            }

            // Get All GameConfig
            List<Type> gameConfigTypes = new();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                gameConfigTypes.AddRange(asm.GetTypes().Where(t => typeof(IGameConfig).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract));
            }

            var content = File.ReadAllText(gameConfigPath);
            
            var gameConfig = new Dictionary<string, IGameConfig>();
            var jObj = JObject.Parse(content);
            foreach (var kv in jObj) 
            {
                var configType = gameConfigTypes.FirstOrDefault(t => kv.Key.Equals(t.GetCustomAttribute<GameConfigAttribute>().name));
                if (configType != null) 
                {
                    // has this config
                    var config = JsonConvert.DeserializeObject(kv.Value.ToString(), configType);
                    if (config != null)
                    {
                        gameConfig.Add(kv.Key, (IGameConfig)config);
                    }
                }
            }

            // in case of missing configs, use default values.
            foreach (var kv in game.GameConfigurations)
            {
                if (!gameConfig.ContainsKey(kv.Key))
                {
                    gameConfig.Add(kv.Key, kv.Value);
                }

                if (kv.Value is CommonGameConfigs commonGameConfigs)
                {
                    if (gameConfig[kv.Key] is CommonGameConfigs commonGameConfigs2)
                    {
                        // do not override the existing values
                        commonGameConfigs2.Merge(commonGameConfigs, isOverride: false);
                    }
                }
            }

            _logger.Debug($"Loaded game config for {game.Name}, content: {content}");
            return gameConfig;
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
                    "*.wav", "*.mp3", "*.aiff", "*.wma", "*.aac", "*.mp4", "*.m4a", "*.ogg"
                };
                config.Save(CONFIG_FILE);
            }
            else
            {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(CONFIG_FILE));
            }

            userconfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText(CONFIG_FILE));
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
                if (p.CanWrite) 
                { 
                    p.SetValue(this, p.GetValue(defaultConfig));
                }
            }
        }
    }


}
