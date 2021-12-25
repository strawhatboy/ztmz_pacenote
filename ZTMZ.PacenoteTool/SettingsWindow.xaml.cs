using System.Windows;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            initGeneral();
            initVoicePackage();
            initMisc();
        }

        private void initGeneral()
        {
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
            this.cb_language.SelectionChanged += Cb_language_SelectionChanged;

            // port
            this.tb_UDPListenPort.Value = (uint)Config.Instance.UDPListenPort;

            // playbackDelay
            this.tb_PlaybackDeviceDesiredLatency.Value = (uint)Config.Instance.PlaybackDeviceDesiredLatency;

            // fps
            this.tb_HudFPS.Value = (uint)Config.Instance.HudFPS;
        }

        private void Cb_language_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var c = I18NLoader.Instance.cultures[this.cb_language.SelectedIndex];
            Config.Instance.Language = c;
            I18NLoader.Instance.SetCulture(c);
        }

        private void initVoicePackage()
        {
            // start/end
            this.btn_PlayStartAndEndSound.IsChecked = Config.Instance.PlayStartAndEndSound;

            // collision
            this.btn_PlayCollisionSound.IsChecked = Config.Instance.PlayCollisionSound;

            // default voice
            this.btn_UseDefaultSoundPackageByDefault.IsChecked = !Config.Instance.UseDefaultSoundPackageByDefault;

            // preload
            this.btn_PreloadSounds.IsChecked = Config.Instance.PreloadSounds;
        }

        private void initMisc()
        {
            this.tb_UseSequentialMixerToHandleAudioConflict.IsChecked = Config.Instance.UseSequentialMixerToHandleAudioConflict;
        }
    }
}