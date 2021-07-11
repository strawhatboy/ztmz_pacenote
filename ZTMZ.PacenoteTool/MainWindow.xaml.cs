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
            InitializeComponent();
            this._recordingConfig = new RecordingConfig()
            {
                ChannelCount = 2,
                SampleRate = 44100,
                Mp3BitRate = 96,
                UseLoopbackCapture = false,
            };
            this._hotKeyStartRecord = new HotKey(Key.F1, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording)
                {
                    this.Dispatcher.Invoke(() => { this.tb_time.Text = "start"; });
                
                    if (this._udpReceiver.GameState == GameState.Paused ||
                        this._udpReceiver.GameState == GameState.Racing)
                    {
                        this._recordingConfig.DestFilePath =
                            string.Format("{0}/{1}.mp3", this._trackFolder,
                                (int)this._udpReceiver.LastMessage.LapDistance);
                        this._recordingConfig.RecordingDate = DateTime.Now;
                        this._audioRecorder.Start(this._recordingConfig);
                        this._isRecordingInProgress = true;
                    }
                }
            });
            this._hotKeyStopRecord = new HotKey(Key.F2, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording)
                {
                    this.Dispatcher.Invoke(() => { this.tb_time.Text = "stop"; });
                    this._audioRecorder.Stop(false);
                    this._isRecordingInProgress = false;
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
                            // 2. get audio_recorder ready
                        }
                        else
                        {
                            // 1. load sounds
                            
                        }

                        break;
                }

                foreach (var profile in this._profileManager.GetAllProfiles())
                {
                    this.cb_profile.Items.Add(profile);
                }
            };
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
            }
        }

        private void Ck_replay_OnChecked(object sender, RoutedEventArgs e)
        {
            if (this._udpReceiver.GameState == GameState.Unknown)
            {
                if (this._toolState == ToolState.Recording)
                {
                    this._toolState = ToolState.Replaying;
                }
            }
            else
            {
                MessageBox.Show(this, "等当前赛事结束后才可以切换模式", "模式切换错误");
            }
        }

    }
}