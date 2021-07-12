using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OnlyR.Core.Recorder;

namespace ZTMZ.PacenoteTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HotKey _hotKeyStartRecord;
        private HotKey _hotKeyStopRecord;
        private UDPReceiver _udpReceiver;
        private ToolState _toolState = ToolState.Replaying;
        private ProfileManager _profileManager = new();
        private AudioRecorder _audioRecorder = new();
        private DR2Helper _dr2Helper = new();
        private string _trackName;
        private string _trackFolder;
        private bool _isRecordingInProgress = false;
        private RecordingConfig _recordingConfig;

        public MainWindow()
        {
            this._recordingConfig = new RecordingConfig()
            {
                ChannelCount = 2,
                SampleRate = 22050,
                Mp3BitRate = 64,
                UseLoopbackCapture = false,
            };
            InitializeComponent();
            this._hotKeyStartRecord = new HotKey(Key.F1, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording)
                {
                    //this.Dispatcher.Invoke(() => { this.tb_time.Text = "start"; });

                    if (this._udpReceiver.GameState == GameState.Paused ||
                        this._udpReceiver.GameState == GameState.Racing)
                    {
                        this._recordingConfig.DestFilePath =
                            string.Format("{0}/{1}.mp3", this._trackFolder,
                                (int) this._udpReceiver.LastMessage.LapDistance);
                        this._recordingConfig.RecordingDate = DateTime.Now;
                        this._audioRecorder.Start(this._recordingConfig);
                        this._isRecordingInProgress = true;
                        this.Dispatcher.Invoke(() =>
                        {
                            this.tb_isRecording.Text = "√";
                            this.tb_isRecording.Foreground = new SolidColorBrush(Colors.Red);
                        });
                    }
                }
            });
            this._hotKeyStopRecord = new HotKey(Key.F2, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording)
                {
                    //this.Dispatcher.Invoke(() => { this.tb_time.Text = "stop"; });
                    this._audioRecorder.Stop(false);
                    this._isRecordingInProgress = false;
                    this.Dispatcher.Invoke(() =>
                    {
                        this.tb_isRecording.Text = "×";
                        this.tb_isRecording.Foreground = new SolidColorBrush(Colors.Black);
                    });
                }
            });
            this._udpReceiver = new UDPReceiver();
            this._udpReceiver.onNewMessage += msg =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.tb_time.Text = msg.Time.ToString("0.0");
                    this.tb_distance.Text = msg.LapDistance.ToString("0.0");
                    this.tb_speed.Text = msg.Speed.ToString("0.0");
                    this.tb_laptime.Text = msg.LapTime.ToString("0.0");
                    this.tb_tracklength.Text = msg.TrackLength.ToString("0.0");
                    this.tb_progress.Text = msg.CompletionRate.ToString("0.00");

                    this.tb_position_z.Text = msg.StartZ.ToString("0.0");
                });

                if (this._toolState != ToolState.Replaying) return;

                // play sound (maybe state not changed and audio files not loaded.)
                if (this._profileManager.CurrentAudioFile != null &&
                    msg.LapDistance >= this._profileManager.CurrentAudioFile.Distance)
                {
                    // play it
                    this._profileManager.Play();
                }
            };

            this._udpReceiver.onGameStateChanged += state =>
            {
                this.Dispatcher.Invoke(() => { this.tb_gamestate.Text = state.ToString(); });
                switch (state)
                {
                    case GameState.Unknown:
                        // end recording, unload trace loaded?
                        break;
                    case GameState.RaceEnd:
                        // end recording, unload trace loaded?
                        if (this._toolState == ToolState.Recording)
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                var codriver = PromptDialog.Dialog.Prompt(
                                    "录制完成，是哪位小可爱、大佬在录制路书呢？",
                                    "领航员信息",
                                    "未知").ToString();
                                this._profileManager.StopRecording(codriver);
                            });
                        }
                        break;
                    case GameState.RaceBegin:
                        // load trace, use lastmsg tracklength & startZ
                        // this._udpReceiver.LastMessage.TrackLength
                        this._trackName = this._dr2Helper.GetItinerary(
                            this._udpReceiver.LastMessage.TrackLength.ToString("f2"),
                            this._udpReceiver.LastMessage.StartZ
                        );
                        if (this._toolState == ToolState.Recording)
                        {
                            // 1. create folder
                            this._trackFolder = this._profileManager.StartRecording(this._trackName);
                            // 2. get audio_recorder ready (audio_recorder already ready...)
                        }
                        else
                        {
                            // 1. load sounds
                            this._profileManager.StartReplaying(this._trackName);
                            var firstSound = this._profileManager.AudioFiles.FirstOrDefault();
                            if (firstSound != null && firstSound.Distance < 0)
                            {
                                // play the RaceBegin sound, just when counting down from 5 to 0.
                                this._profileManager.Play();
                            }
                            else
                            {
                                //TODO: cannot find any sound for this track. try to use 'default profile' ?
                            }
                        }

                        break;
                }
            };

            foreach (var profile in this._profileManager.GetAllProfiles())
            {
                this.cb_profile.Items.Add(profile);
            }

            this.cb_profile.SelectedIndex = 0;
        }

        private void Ck_record_OnChecked(object sender, RoutedEventArgs e)
        {
            if (this._udpReceiver.GameState == GameState.Unknown)
            {
                if (this._toolState == ToolState.Replaying)
                {
                    this._toolState = ToolState.Recording;
                }
            }
            else
            {
                MessageBox.Show(this, "等当前赛事结束后才可以切换模式", "模式切换错误");
                e.Handled = true;
            }
        }

        private void Ck_replay_OnChecked(object sender, RoutedEventArgs e)
        {
            if (this._udpReceiver.GameState == GameState.Unknown)
            {
                if (this._toolState == ToolState.Recording)
                {
                    this._toolState = ToolState.Replaying;
                    this.tb_isRecording.Text = "不可用";
                }
            }
            else
            {
                MessageBox.Show(this, "等当前赛事结束后才可以切换模式", "模式切换错误");
                e.Handled = true;
            }
        }

        private void Tb_recordQuality_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (this.tb_recordQuality.SelectedIndex)
            {
                case 0:
                    // low
                    this._recordingConfig.SampleRate = 11025;
                    this._recordingConfig.Mp3BitRate = 64;
                    break;
                case 1:
                    this._recordingConfig.SampleRate = 22050;
                    this._recordingConfig.Mp3BitRate = 96;
                    break;
                case 2:
                    // very huge file...
                    this._recordingConfig.SampleRate = 44100;
                    this._recordingConfig.Mp3BitRate = 320;
                    break;
            }
        }
    }
}