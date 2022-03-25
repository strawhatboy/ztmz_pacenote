using MaterialDesignThemes.Wpf;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xceed.Wpf.Toolkit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Dialog;

namespace ZTMZ.PacenoteTool
{
    public partial class SettingsWindow : Window
    {
        public event Action PortChanged;
        public event Action HudFPSChanged;
        public bool CanClose { set; get; } = false;
        public SettingsWindow()
        {
            InitializeComponent();

            initGeneral();
            initVoicePackage();
            initPlayback();
            initHud();
        }

        private void checkUDP()
        {
            var check = new PrerequisitesCheck().Check();
            if (check.Code == PrerequisitesCheckResultCode.PORT_NOT_MATCH)
            {
                this.pi_udpPort.Visibility = Visibility.Visible;
                this.pi_udpPort.ToolTip = string.Format(I18NLoader.Instance["settings.tooltip.udpListenPort_Warning"], check.Params[0]);
            } else if (check.Code == PrerequisitesCheckResultCode.OK)
            {
                this.pi_udpPort.Visibility = Visibility.Hidden;
            }
        }

        private void initGeneral()
        {
            // theme

            initBoolSetting(this.btn_IsDarkTheme, "IsDarkTheme", false, () =>
            {
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();
            
                theme.SetBaseTheme(Config.Instance.IsDarkTheme ? Theme.Dark : Theme.Light);
                paletteHelper.SetTheme(theme);
            });

            // language
            foreach (var c in I18NLoader.Instance.culturesFullname)
            {
                this.cb_language.Items.Add(c);
            }
            var cindex = I18NLoader.Instance.cultures.FindIndex(a => a.Equals(Config.Instance.Language));
            if (cindex == -1)
            {
                this.cb_language.SelectedIndex = 0;
            } else
            {
                this.cb_language.SelectedIndex = cindex;
            }
            this.cb_language.SelectionChanged += (s, e) =>
            {
                var c = I18NLoader.Instance.cultures[this.cb_language.SelectedIndex];
                Config.Instance.Language = c;
                Config.Instance.SaveUserConfig();
                I18NLoader.Instance.SetCulture(c);
            };

            // port
            this.tb_UDPListenPort.Value = (uint)Config.Instance.UDPListenPort;
            checkUDP();
            this.tb_UDPListenPort.LostFocus += (s, e) =>
            {
                this.pb_udpPort.Visibility = Visibility.Visible;
                Config.Instance.UDPListenPort = (int)this.tb_UDPListenPort.Value.Value;
                // open the port mismatch dialog next time.
                Config.Instance.WarnIfPortMismatch = true;
                Config.Instance.SaveUserConfig();
                checkUDP();
                this.PortChanged?.Invoke();
                this.tb_UDPListenPort.IsEnabled = false;
                var bgw = new BackgroundWorker();
                bgw.DoWork += (s, e) =>
                {
                    Thread.Sleep(6000);
                    this.Dispatcher.Invoke(() =>
                    {
                        this.tb_UDPListenPort.IsEnabled = true;
                        this.pb_udpPort.Visibility = Visibility.Collapsed;
                    });
                };
                bgw.RunWorkerAsync();
            };

            // playbackDelay
            initUIntSetting(tb_PlaybackDeviceDesiredLatency, "PlaybackDeviceDesiredLatency");

        }

        private void initHud()
        {
            // fps
            this.tb_HudFPS.Value = (uint)Config.Instance.HudFPS;
            this.tb_HudFPS.LostFocus += (s, e) =>
            {
                Config.Instance.HudFPS = (int)this.tb_HudFPS.Value.Value;
                Config.Instance.SaveUserConfig();
                this.HudFPSChanged?.Invoke();
                this.pi_hudFPS.Visibility = Visibility.Visible;
                this.tb_restartNeeded.Visibility = Visibility.Visible;
            };
            
            // telemetry
            initBoolSetting(btn_hudShowTelemetry, "HudShowTelemetry");
            initSliderSetting(sl_hudSizePercentage, "HudSizePercentage");
            initSliderSetting(sl_hudPaddingH, "HudPaddingH");
            initSliderSetting(sl_hudPaddingV, "HudPaddingV");
            initSliderSetting(sl_hudElementSpacing, "HudElementSpacing");
            initSliderSetting(sl_hudSectorThicknessRatio, "HudSectorThicknessRatio");
            initSliderSetting(sl_hudBackgroundOpacity, "HudBackgroundOpacity");
            initBoolSetting(btn_hudShowDebugTelemetry, "HudShowDebugTelemetry");
            
            // telemetry elements
            initBoolSetting(btn_hudTelemetryShowGBall, "HudTelemetryShowGBall");
            initBoolSetting(btn_hudTelemetryShowSpdSector, "HudTelemetryShowSpdSector");
            initBoolSetting(btn_hudTelemetryShowRPMSector, "HudTelemetryShowRPMSector");
            initBoolSetting(btn_hudTelemetryShowPedals, "HudTelemetryShowPedals");
            initBoolSetting(btn_hudTelemetryShowGear, "HudTelemetryShowGear");
            initBoolSetting(btn_hudTelemetryShowSteering, "HudTelemetryShowSteering");
            initUIntSetting(tb_hudTelemetrySteeringDegree, "HudTelemetrySteeringDegree");
            initBoolSetting(btn_hudTelemetryShowSuspensionBars, "HudTelemetryShowSuspensionBars");
            
        }

        private void initVoicePackage()
        {
            // start/end
            initBoolSetting(btn_PlayStartAndEndSound, "PlayStartAndEndSound");

            // Go !
            initBoolSetting(btn_PlayGoSound, "PlayGoSound");

            // collision
            initBoolSetting(btn_PlayCollisionSound, "PlayCollisionSound");
            
            // collision thresholds
            initUIntSetting(tb_Collision_Slight, "CollisionSpeedChangeThreshold_Slight");
            initUIntSetting(tb_Collision_Medium, "CollisionSpeedChangeThreshold_Medium");
            initUIntSetting(tb_Collision_Severe, "CollisionSpeedChangeThreshold_Severe");

            // puncture
            initBoolSetting(btn_PlayWheelAbnormalSound, "PlayWheelAbnormalSound");

            // default voice
            initBoolSetting(btn_UseDefaultSoundPackageByDefault, "UseDefaultSoundPackageByDefault", true, () =>
            {
                pi_defaultSoundPackage.Visibility = Visibility.Visible;
                tb_restartNeeded.Visibility = Visibility.Visible;
            });

            // fallback default voice
            initBoolSetting(btn_UseDefaultSoundPackageForFallback, "UseDefaultSoundPackageForFallback");

            // preload
            initBoolSetting(btn_PreloadSounds, "PreloadSounds");
            
        }

        private void initPlayback()
        {
            initBoolSetting(tb_UseSequentialMixerToHandleAudioConflict, "UseSequentialMixerToHandleAudioConflict");

            // dynamic playback spd
            initBoolSetting(btn_UseDynamicPlaybackSpeed, "UseDynamicPlaybackSpeed");
            
            // use tempo
            initBoolSetting(btn_UseTempoInsteadOfRate, "UseTempoInsteadOfRate");

            // max dynamic playback spd
            initSliderSetting(sl_dynamicPlaybackMaxSpeed, "DynamicPlaybackMaxSpeed");

            // dynamic volume
            initBoolSetting(btn_useDynamicVolume, "UseDynamicVolume");
            initUIntSetting(tb_dynamicVolumePerturbationFrequency, "DynamicVolumePerturbationFrequency");
            initSliderSetting(sl_dynamicVolumePerturbationAmplitude, "DynamicVolumePerturbationAmplitude");
        }

        private void initMisc()
        {
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!CanClose)
            {
                this.Hide();
                e.Cancel = true;
            }
        }

        private void btn_reset_Click(object sender, RoutedEventArgs e)
        {
            var res = new ResetConfigDialog().ShowDialog();
            if (res.HasValue && res.Value)
            {
                Config.Instance.ResetToDefault();
                Config.Instance.SaveUserConfig();
            }
        }

        private void initBoolSetting(ToggleButton btn, string configName, bool isReverse = false, Action clicked = null)
        {
            var configProperty = typeof(Config).GetProperty(configName);
            if (configProperty.PropertyType != typeof(bool))
            {
                return;
            }
            btn.IsChecked = (bool)configProperty.GetValue(Config.Instance);
            btn.Click += (s, e) =>
            {
                configProperty.SetValue(Config.Instance, isReverse ? !btn.IsChecked.Value : btn.IsChecked.Value);
                Config.Instance.SaveUserConfig();
                clicked?.Invoke();
            };
        }

        private void initUIntSetting(UIntegerUpDown tb, string configName)
        {
            var configProperty = typeof(Config).GetProperty(configName);
            if (configProperty.PropertyType != typeof(int))
            {
                return;
            }
            tb.Value = Convert.ToUInt32(configProperty.GetValue(Config.Instance));
            tb.ValueChanged += (s, e) =>
            {
                if (tb.Value.HasValue)
                {
                    configProperty.SetValue(Config.Instance, (int)tb.Value.Value);
                    Config.Instance.SaveUserConfig();
                }
            };
        }

        private void initSliderSetting(Slider sl, string configName)
        {
            var configProperty = typeof(Config).GetProperty(configName);
            if (configProperty.PropertyType != typeof(float))
            {
                return;
            }
            sl.Value = Convert.ToSingle(configProperty.GetValue(Config.Instance));
            sl.ValueChanged += (s, e) =>
            {
                configProperty.SetValue(Config.Instance, (float)sl.Value);
                Config.Instance.SaveUserConfig();
            };
        }
    }
}