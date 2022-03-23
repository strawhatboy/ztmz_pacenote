using MaterialDesignThemes.Wpf;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
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
            initMisc();
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
            this.btn_IsDarkTheme.IsChecked = Config.Instance.IsDarkTheme;
            this.btn_IsDarkTheme.Click += (s, e) =>
            {
                Config.Instance.IsDarkTheme = this.btn_IsDarkTheme.IsChecked.Value;
                Config.Instance.SaveUserConfig();
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();

                theme.SetBaseTheme(Config.Instance.IsDarkTheme ? Theme.Dark : Theme.Light);
                paletteHelper.SetTheme(theme);
            };

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
            this.tb_PlaybackDeviceDesiredLatency.Value = (uint)Config.Instance.PlaybackDeviceDesiredLatency;
            this.tb_PlaybackDeviceDesiredLatency.ValueChanged += (s, e) =>
            {
                Config.Instance.PlaybackDeviceDesiredLatency = (int)this.tb_PlaybackDeviceDesiredLatency.Value.Value;
                Config.Instance.SaveUserConfig();
            };

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
        }

        private void initVoicePackage()
        {
            // start/end
            this.btn_PlayStartAndEndSound.IsChecked = Config.Instance.PlayStartAndEndSound;
            this.btn_PlayStartAndEndSound.Click += (s, e) =>
            {
                Config.Instance.PlayStartAndEndSound = this.btn_PlayStartAndEndSound.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            // Go !
            this.btn_PlayGoSound.IsChecked = Config.Instance.PlayGoSound;
            this.btn_PlayGoSound.Click += (s, e) =>
            {
                Config.Instance.PlayGoSound = this.btn_PlayGoSound.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            // collision
            this.btn_PlayCollisionSound.IsChecked = Config.Instance.PlayCollisionSound;
            this.btn_PlayCollisionSound.Click += (s, e) =>
            {
                Config.Instance.PlayCollisionSound = this.btn_PlayCollisionSound.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };
            
            // collision thresholds
            this.tb_Collision_Slight.Value = (uint)Config.Instance.CollisionSpeedChangeThreshold_Slight;
            this.tb_Collision_Slight.ValueChanged += (s, e) =>
            {
                Config.Instance.CollisionSpeedChangeThreshold_Slight = (int)this.tb_Collision_Slight.Value.Value;
                Config.Instance.SaveUserConfig();
            };
            this.tb_Collision_Medium.Value = (uint)Config.Instance.CollisionSpeedChangeThreshold_Medium;
            this.tb_Collision_Medium.ValueChanged += (s, e) =>
            {
                Config.Instance.CollisionSpeedChangeThreshold_Medium = (int)this.tb_Collision_Medium.Value.Value;
                Config.Instance.SaveUserConfig();
            };
            this.tb_Collision_Severe.Value = (uint)Config.Instance.CollisionSpeedChangeThreshold_Severe;
            this.tb_Collision_Severe.ValueChanged += (s, e) =>
            {
                Config.Instance.CollisionSpeedChangeThreshold_Severe = (int)this.tb_Collision_Severe.Value.Value;
                Config.Instance.SaveUserConfig();
            };

            // puncture
            this.btn_PlayWheelAbnormalSound.IsChecked = Config.Instance.PlayWheelAbnormalSound;
            this.btn_PlayWheelAbnormalSound.Click += (s, e) =>
            {
                Config.Instance.PlayWheelAbnormalSound = this.btn_PlayWheelAbnormalSound.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            // default voice
            this.btn_UseDefaultSoundPackageByDefault.IsChecked = !Config.Instance.UseDefaultSoundPackageByDefault;
            this.btn_UseDefaultSoundPackageByDefault.Click += (s, e) =>
            {
                Config.Instance.UseDefaultSoundPackageByDefault = !this.btn_UseDefaultSoundPackageByDefault.IsChecked.Value;
                Config.Instance.SaveUserConfig();
                this.pi_defaultSoundPackage.Visibility = Visibility.Visible;
                this.tb_restartNeeded.Visibility = Visibility.Visible;
            };

            // fallback default voice
            this.btn_UseDefaultSoundPackageForFallback.IsChecked = Config.Instance.UseDefaultSoundPackageForFallback;
            this.btn_UseDefaultSoundPackageForFallback.Click += (s, e) =>
            {
                Config.Instance.UseDefaultSoundPackageForFallback = this.btn_UseDefaultSoundPackageForFallback.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            // preload
            this.btn_PreloadSounds.IsChecked = Config.Instance.PreloadSounds;
            this.btn_PreloadSounds.Click += (s, e) =>
            {
                Config.Instance.PreloadSounds = this.btn_PreloadSounds.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };
            
            // dynamic playback spd
            this.btn_UseDynamicPlaybackSpeed.IsChecked = Config.Instance.UseDynamicPlaybackSpeed;
            this.btn_UseDynamicPlaybackSpeed.Click += (s, e) =>
            {
                Config.Instance.UseDynamicPlaybackSpeed = this.btn_UseDynamicPlaybackSpeed.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };
            
            // use tempo
            this.btn_UseTempoInsteadOfRate.IsChecked = Config.Instance.UseTempoInsteadOfRate;
            this.btn_UseTempoInsteadOfRate.Click += (s, e) =>
            {
                Config.Instance.UseTempoInsteadOfRate = this.btn_UseTempoInsteadOfRate.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            // max dynamic playback spd
            this.sl_dynamicPlaybackMaxSpeed.Value = Config.Instance.DynamicPlaybackMaxSpeed;
            this.sl_dynamicPlaybackMaxSpeed.ValueChanged += (s, e) =>
            {
                Config.Instance.DynamicPlaybackMaxSpeed = (float)this.sl_dynamicPlaybackMaxSpeed.Value;
                Config.Instance.SaveUserConfig();
            };
            
            // dynamic volume
            this.btn_useDynamicVolume.IsChecked = Config.Instance.UseDynamicVolume;
            this.btn_useDynamicVolume.Click += (s, e) =>
            {
                Config.Instance.UseDynamicVolume = this.btn_useDynamicVolume.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };

            this.tb_dynamicVolumePerturbationFrequency.Value = (uint)Config.Instance.DynamicVolumePerturbationFrequency;
            this.tb_dynamicVolumePerturbationFrequency.ValueChanged += (s, e) =>
            {
                Config.Instance.DynamicVolumePerturbationFrequency = (int)this.tb_dynamicVolumePerturbationFrequency.Value.Value;
                Config.Instance.SaveUserConfig();
            };

            this.sl_dynamicVolumePerturbationAmplitude.Value = Config.Instance.DynamicVolumePerturbationAmplitude;
            this.sl_dynamicVolumePerturbationAmplitude.ValueChanged += (s, e) =>
            {
                Config.Instance.DynamicVolumePerturbationAmplitude = (float)this.sl_dynamicVolumePerturbationAmplitude.Value;
                Config.Instance.SaveUserConfig();
            };
        }

        private void initMisc()
        {
            this.tb_UseSequentialMixerToHandleAudioConflict.IsChecked = Config.Instance.UseSequentialMixerToHandleAudioConflict;
            this.tb_UseSequentialMixerToHandleAudioConflict.Click += (s, e) =>
            {
                Config.Instance.UseSequentialMixerToHandleAudioConflict = this.tb_UseSequentialMixerToHandleAudioConflict.IsChecked.Value;
                Config.Instance.SaveUserConfig();
            };
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
    }
}