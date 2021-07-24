using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using NAudio.Wave;
using OnlyR.Core.Models;
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
        private IEnumerable<RecordingDeviceInfo> _recordingDevices;
        private int _selectReplayDeviceID = 0;
        private int _selectReplayMode = 0;
        private bool _firstSoundPlayed = false;

        public MainWindow()
        {
            this._recordingConfig = new RecordingConfig()
            {
                ChannelCount = 2,
                SampleRate = 8000,
                Mp3BitRate = 48,
                UseLoopbackCapture = false,
            };
            InitializeComponent();
            this._hotKeyStartRecord = new HotKey(Key.F1, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording && !this._isRecordingInProgress)
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
                            this.tb_isRecording.Text = "√ √ √";
                            this.tb_isRecording.Foreground = new SolidColorBrush(Colors.Red);
                        });
                    }
                }
            });
            this._hotKeyStopRecord = new HotKey(Key.F4, KeyModifier.None, key =>
            {
                if (this._toolState == ToolState.Recording && this._isRecordingInProgress)
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

                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    // play in threads.
                    // play sound (maybe state not changed and audio files not loaded.)
                    if (this._profileManager.CurrentAudioFile != null)
                    {
                        if (this._selectReplayMode == 0)
                        {
                            if (msg.LapDistance >= this._profileManager.CurrentAudioFile.Distance)
                            {
                                // play it
                                this._profileManager.Play();
                            }
                        }
                        else
                        {
                            // script mode
                            if (this._profileManager.CurrentScriptReader != null)
                            {
                                if (this._profileManager.CurrentScriptReader.IsDynamic && msg.LapDistance +
                                    msg.Speed / 3.6f * Config.Instance.ScriptMode_PlaySecondsAdvanced >=
                                    this._profileManager.CurrentAudioFile.Distance)
                                {
                                    // play before in <PlaySecondsAdvanced> seconds.
                                    this._profileManager.Play();
                                }

                                if (!this._profileManager.CurrentScriptReader.IsDynamic &&
                                    msg.LapDistance >= this._profileManager.CurrentAudioFile.Distance)
                                {
                                    // not dynamic, play it
                                    this._profileManager.Play();
                                }
                            }
                        }
                    }
                };
                worker.RunWorkerAsync();
            };

            this._udpReceiver.onGameStateChanged += state =>
            {
                this.Dispatcher.Invoke(() => { this.tb_gamestate.Text = state.ToString(); });
                switch (state)
                {
                    case GameState.Unknown:
                        // enable profile switch
                        this.Dispatcher.Invoke(() =>
                        {
                            this.cb_profile.IsEnabled = true;
                            this.cb_replay_device.IsEnabled = true;
                            this.cb_codrivers.IsEnabled = true;
                            
                            // enable mode change.
                            this.ck_record.IsEnabled = true;
                            this.ck_replay.IsEnabled = true;
                            this.cb_replay_mode.IsEnabled = true;
                        });
                        break;
                    case GameState.RaceEnd:
                        // end recording, unload trace loaded?
                        if (this._toolState == ToolState.Recording)
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                var codriver = PromptDialog.Dialog.Prompt(
                                    "录制完成，是哪位小可爱/大佬在录制路书呢？",
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
                        this.Dispatcher.Invoke(() =>
                        {
                            this.tb_currentTrack.Text = this._trackName;
                            // disable profile switch, replay device selection
                            this.cb_profile.IsEnabled = false;
                            this.cb_replay_device.IsEnabled = false;

                            this.ck_record.IsEnabled = false;
                            this.ck_replay.IsEnabled = false;
                            this.cb_codrivers.IsEnabled = false;
                            this.cb_replay_mode.IsEnabled = false;
                        });
                        if (this._toolState == ToolState.Recording)
                        {
                            // 1. create folder
                            this._trackFolder = this._profileManager.StartRecording(this._trackName);
                            // 2. get audio_recorder ready (audio_recorder already ready...)
                        }
                        else
                        {
                            var worker = new BackgroundWorker();
                            worker.DoWork += (sender, e) =>
                            {
                                // 1. load sounds
                                this._profileManager.StartReplaying(this._trackName, this._selectReplayMode);
                                this.Dispatcher.Invoke(() =>
                                {
                                    this.tb_codriver.Text = this._profileManager.CurrentCoDriverName;
                                    if (this._selectReplayMode != 0 &&
                                        this._profileManager.CurrentScriptReader != null)
                                    {
                                        this.chb_isDynamicPlay.IsChecked = this._profileManager.CurrentScriptReader.IsDynamic;
                                    }
                                });
                                var firstSound = this._profileManager.AudioFiles.FirstOrDefault();
                                if (firstSound != null &&
                                    firstSound.Distance < 0) // && this._firstSoundPlayed == false)
                                {
                                    // this._firstSoundPlayed = true;
                                    // play the RaceBegin sound, just when counting down from 5 to 0.
                                    // play in threads.
                                    this._profileManager.Play();
                                }
                                else
                                {
                                    //TODO: cannot find any sound for this track. try to use 'default profile' ?
                                }
                            };
                            worker.RunWorkerAsync();
                        }

                        break;
                }
            };

            foreach (var profile in this._profileManager.GetAllProfiles())
            {
                this.cb_profile.Items.Add(profile);
            }

            foreach (var codriver in this._profileManager.GetAllCodrivers())
            {
                this.cb_codrivers.Items.Add(codriver);
            }

            this.cb_profile.SelectedIndex = 0;
            this.cb_codrivers.SelectedIndex = 0;

            this._recordingDevices = AudioRecorder.GetRecordingDeviceList();
            foreach (var rDevice in this._recordingDevices)
            {
                this.cb_recording_device.Items.Add(rDevice.Name);
            }

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                WaveOutCapabilities WOC = WaveOut.GetCapabilities(i);
                this.cb_replay_device.Items.Add(WOC.ProductName);
            }

            this.cb_replay_device.SelectedIndex = 0;

            // if there's no recording device, would throw exception...
            if (this._recordingDevices.Count() > 0)
                this.cb_recording_device.SelectedIndex = 0;


            // check the file
            var preCheck = new PrerequisitesCheck();
            if (!preCheck.Check())
            {
                // not pass
                var result =
                    MessageBox.Show("你的Dirt Rally 2.0 的配置文件中的UDP端口未正确打开，如果没有打开，工具将无法正常工作，点击“是”自动修改配置文件，点击“否”退出程序自行修改",
                        "配置错误", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    preCheck.Write();
                }
                else
                {
                    // Goodbye.
                    System.Windows.Application.Current.Shutdown();
                }
            }
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
                this.ck_replay.IsChecked = true;
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
                this.ck_record.IsChecked = true;
            }
        }

        private void Tb_recordQuality_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (this.tb_recordQuality.SelectedIndex)
            {
                case 0:
                    // low
                    this._recordingConfig.SampleRate = 8000;
                    this._recordingConfig.Mp3BitRate = 48;
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

        private void cb_recording_device_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._recordingConfig.RecordingDevice = (from x in this._recordingDevices
                where x.Name == this.cb_recording_device.SelectedItem.ToString()
                select x.Id).First();
        }


        private void tb_currentTrack_view_Click(object sender, RoutedEventArgs e)
        {
            if (this._profileManager.CurrentItineraryPath != null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe",
                    System.IO.Path.GetFullPath(this._profileManager.CurrentItineraryPath)));
            }
        }

        private void cb_profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentProfile = this.cb_profile.SelectedItem.ToString().Split('/')[1];
        }

        private void cb_replay_device_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentPlayDeviceId = this.cb_replay_device.SelectedIndex;
        }

        private void btn_play_example_Click(object sender, RoutedEventArgs e)
        {
            var bgw = new BackgroundWorker();
            bgw.DoWork += (arg, e) => { this._profileManager.PlayExample(); };
            bgw.RunWorkerAsync();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(@"本工具由ZTMZ Club制作，仅供学习交流，请勿用作商业用途。
如果在使用过程中遇到问题，可以在QQ群 (207790761) 中询问。
本工具已在gitee开源 (https://gitee.com/ztmz/ztmz_pacenote)，欢迎前往交流。
本工具借鉴（抄袭√）和参考了部分开源软件，在此表示感谢：
CrewChiefV4 (https://gitlab.com/mr_belowski/CrewChiefV4)
dr2_logger (https://github.com/ErlerPhilipp/dr2_logger)
以及使用的第三方Nuget包：
Newtonsoft.Json (https://www.newtonsoft.com/json)
NAudio (https://github.com/naudio/NAudio)
PromptDialog (https://github.com/manuelcanepa/wpf-prompt-dialog)
AvalonEdit (http://avalonedit.net/)

最后再次感谢ZTMZ Club组委会和群里大佬们的帮助与支持。
", "关于本工具 v2.0 Beta Patch 2");
        }

        private void btn_currentTrack_script_Click(object sender, RoutedEventArgs e)
        {
            if (this._profileManager.CurrentItineraryPath != null)
            {
                Process.Start(new ProcessStartInfo("ZTMZ.PacenoteTool.ScriptEditor.exe",
                    string.Format("\"{0}\"", System.IO.Path.GetFullPath(this._profileManager.CurrentScriptPath))));
            }
        }

        private void cb_replay_mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._selectReplayMode = this.cb_replay_mode.SelectedIndex;
        }

        private void cb_codrivers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentCoDriverSoundPackagePath = this.cb_codrivers.SelectedItem.ToString();
        }
    }
}