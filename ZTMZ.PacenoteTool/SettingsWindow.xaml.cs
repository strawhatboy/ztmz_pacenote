using MaterialDesignThemes.Wpf;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.WindowsAPICodePack.Dialogs;
using Xceed.Wpf.Toolkit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Dialog;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.Dialog;

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

        public void SetGame(IGame game)
        {
            tb_CurrentGame.Text = game.Name;
            sp_CurrentGame.Children.Clear();
            foreach (var kv in game.GameConfigurations)
            {
                IGameConfigSettingsPane pane = kv.Value.UI;
                sp_CurrentGame.Children.Add(pane);
                sp_CurrentGame.Children.Add(new Separator());
                img_CurrentGame.Source = game.Image;
                pane.InitializeWithGame(game);
                pane.RestartNeeded = () => this.tb_restartNeeded.Visibility = Visibility.Visible;
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

            // log level
            this.cb_logLevel.SelectedIndex = (int)Config.Instance.LogLevel;
            this.cb_logLevel.SelectionChanged += (s, e) => 
            {
                Config.Instance.LogLevel = this.cb_logLevel.SelectedIndex;
                Config.Instance.SaveUserConfig();
                NLogManager.setLogLevel(Config.Instance.LogLevel);
            };
            this.btn_viewLogs.Click += (s, e) =>
            {
                var logPath = AppLevelVariables.Instance.GetPath("logs");
                if (!Directory.Exists(logPath)) 
                {
                    Directory.CreateDirectory(logPath);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    AppLevelVariables.Instance.GetPath("logs")));
            };


            initBoolSetting(btn_enableGoogleAnalytics, "EnableGoogleAnalytics");
            initBoolSetting(btn_warnIfPortMismatch, "WarnIfPortMismatch");
            initBoolSetting(btn_optInBetaPlan, "OptInBetaPlan");
        }

        private void initHud()
        {
            // fps
            this.tb_HudFPS.Value = (uint)Config.Instance.HudFPS;
            this.tb_HudFPS.ValueChanged += (s, e) =>
            {
                Config.Instance.HudFPS = (int)this.tb_HudFPS.Value.Value;
                Config.Instance.SaveUserConfig();
                this.HudFPSChanged?.Invoke();
                this.pi_hudFPS.Visibility = Visibility.Visible;
                this.tb_restartNeeded.Visibility = Visibility.Visible;
            };

            initBoolSetting(btn_hudChromaKeyMode, "HudChromaKeyMode", false, () =>
            {
                // need restart
                this.tb_restartNeeded.Visibility = Visibility.Visible;
                pi_hudChromaKeyMode.Visibility = Visibility.Visible;
            });
            
            initBoolSetting(btn_hudTopMost, "HudTopMost", false, () =>
            {
                // need restart
                this.tb_restartNeeded.Visibility = Visibility.Visible;
                pi_hudTopMost.Visibility = Visibility.Visible;
            });

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
            // additional voice package
            initFolderSelectionSetting(txtBox_additionalCoDriverPackagesSearchPath, btn_additionalCoDriverPackagesSearchPath, "AdditionalCoDriverPackagesSearchPath");

            // additional pacenote definition
            initFolderSelectionSetting(txtBox_additionalPacenotesDefinitionSearchPath, btn_additionalPacenotesDefinitionSearchPath, "AdditionalPacenotesDefinitionSearchPath");
            
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

        private void initFolderSelectionSetting(TextBox txtBox, Button btn, string configKey)
        {
            var configProperty = typeof(Config).GetProperty(configKey);
            if (configProperty.PropertyType != typeof(string))
            {
                return;
            }
            txtBox.Text = (string)configProperty.GetValue(Config.Instance);
            txtBox.TextChanged += (o, e) =>
            {
                // if (Directory.Exists(txtBox.Text))
                // {
                    configProperty.SetValue(Config.Instance, txtBox.Text);
                    Config.Instance.SaveUserConfig();
                // }
            };
            btn.Click += (o, e) =>
            {
                // show directory selection dialog
                var dialog = new CommonOpenFileDialog();
                dialog.IsFolderPicker = true;
                CommonFileDialogResult result = dialog.ShowDialog();
                if (result == CommonFileDialogResult.Ok)
                {
                    txtBox.Text = dialog.FileName;
                }
            };
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
            // playbackDelay
            initUIntSetting(tb_PlaybackDeviceDesiredLatency, "PlaybackDeviceDesiredLatency");
        }

        private void initMisc()
        {
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!CanClose)
            {
                if (tb_restartNeeded.Visibility == Visibility.Visible) 
                {
                    BaseDialog.Show("", "settings.restartNeeded", null,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                }
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
            if (isReverse) {
                btn.IsChecked = !btn.IsChecked;
            }
            btn.Click += (s, e) =>
            {
                var value = isReverse ? !btn.IsChecked.Value : btn.IsChecked.Value;
                configProperty.SetValue(Config.Instance, value);
                GoogleAnalyticsHelper.Instance.TrackConfigToggleEvent(configName, value ? "on" : "off");
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
