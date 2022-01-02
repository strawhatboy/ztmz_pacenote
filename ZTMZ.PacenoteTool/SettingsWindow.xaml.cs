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
                this.pi_udpPort.ToolTip = string.Format(I18NLoader.Instance.CurrentDict["settings.tooltip.udpListenPort_Warning"].ToString(), check.Params[0]);
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
                if (this.btn_IsDarkTheme.IsChecked.HasValue)
                {
                    Config.Instance.IsDarkTheme = this.btn_IsDarkTheme.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                    var paletteHelper = new PaletteHelper();
                    var theme = paletteHelper.GetTheme();

                    theme.SetBaseTheme(Config.Instance.IsDarkTheme ? Theme.Dark : Theme.Light);
                    paletteHelper.SetTheme(theme);
                }
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
                if (this.tb_UDPListenPort.Value.HasValue)
                {
                    this.pb_udpPort.Visibility = Visibility.Visible;
                    Config.Instance.UDPListenPort = (int)this.tb_UDPListenPort.Value.Value;
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
                }
            };

            // playbackDelay
            this.tb_PlaybackDeviceDesiredLatency.Value = (uint)Config.Instance.PlaybackDeviceDesiredLatency;
            this.tb_PlaybackDeviceDesiredLatency.ValueChanged += (s, e) =>
            {
                if (this.tb_PlaybackDeviceDesiredLatency.Value.HasValue)
                {
                    Config.Instance.PlaybackDeviceDesiredLatency = (int)this.tb_PlaybackDeviceDesiredLatency.Value.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // fps
            this.tb_HudFPS.Value = (uint)Config.Instance.HudFPS;
            this.tb_HudFPS.LostFocus += (s, e) =>
            {
                if (this.tb_HudFPS.Value.HasValue)
                {
                    Config.Instance.HudFPS = (int)this.tb_HudFPS.Value.Value;
                    Config.Instance.SaveUserConfig();
                    this.HudFPSChanged?.Invoke();
                    this.pi_hudFPS.Visibility = Visibility.Visible;
                    this.tb_restartNeeded.Visibility = Visibility.Visible;
                }
            };
        }

        private void initVoicePackage()
        {
            // start/end
            this.btn_PlayStartAndEndSound.IsChecked = Config.Instance.PlayStartAndEndSound;
            this.btn_PlayStartAndEndSound.Click += (s, e) =>
            {
                if (this.btn_PlayStartAndEndSound.IsChecked.HasValue)
                {
                    Config.Instance.PlayStartAndEndSound = this.btn_PlayStartAndEndSound.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // Go !
            this.btn_PlayGoSound.IsChecked = Config.Instance.PlayGoSound;
            this.btn_PlayGoSound.Click += (s, e) =>
            {
                if (this.btn_PlayGoSound.IsChecked.HasValue)
                {
                    Config.Instance.PlayGoSound = this.btn_PlayGoSound.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // collision
            this.btn_PlayCollisionSound.IsChecked = Config.Instance.PlayCollisionSound;
            this.btn_PlayCollisionSound.Click += (s, e) =>
            {
                if (this.btn_PlayCollisionSound.IsChecked.HasValue)
                {
                    Config.Instance.PlayCollisionSound = this.btn_PlayCollisionSound.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // puncture
            this.btn_PlayWheelAbnormalSound.IsChecked = Config.Instance.PlayWheelAbnormalSound;
            this.btn_PlayWheelAbnormalSound.Click += (s, e) =>
            {
                if (this.btn_PlayWheelAbnormalSound.IsChecked.HasValue)
                {
                    Config.Instance.PlayWheelAbnormalSound = this.btn_PlayWheelAbnormalSound.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // default voice
            this.btn_UseDefaultSoundPackageByDefault.IsChecked = !Config.Instance.UseDefaultSoundPackageByDefault;
            this.btn_UseDefaultSoundPackageByDefault.Click += (s, e) =>
            {
                if (this.btn_UseDefaultSoundPackageByDefault.IsChecked.HasValue)
                {
                    Config.Instance.UseDefaultSoundPackageByDefault = !this.btn_UseDefaultSoundPackageByDefault.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // fallback default voice
            this.btn_UseDefaultSoundPackageForFallback.IsChecked = Config.Instance.UseDefaultSoundPackageForFallback;
            this.btn_UseDefaultSoundPackageForFallback.Click += (s, e) =>
            {
                if (this.btn_UseDefaultSoundPackageForFallback.IsChecked.HasValue)
                {
                    Config.Instance.UseDefaultSoundPackageForFallback = this.btn_UseDefaultSoundPackageForFallback.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };

            // preload
            this.btn_PreloadSounds.IsChecked = Config.Instance.PreloadSounds;
            this.btn_PreloadSounds.Click += (s, e) =>
            {
                if (this.btn_PreloadSounds.IsChecked.HasValue)
                {
                    Config.Instance.PreloadSounds = this.btn_PreloadSounds.IsChecked.Value;
                    Config.Instance.SaveUserConfig();
                }
            };
        }

        private void initMisc()
        {
            this.tb_UseSequentialMixerToHandleAudioConflict.IsChecked = Config.Instance.UseSequentialMixerToHandleAudioConflict;
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