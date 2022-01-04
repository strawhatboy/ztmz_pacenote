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
using ZTMZ.PacenoteTool.Base;
using AutoUpdaterDotNET;
using System.Globalization;
using MaterialDesignThemes.Wpf;
using System.Threading;
using ZTMZ.PacenoteTool.Dialog;

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
        private ProfileManager _profileManager;
        private AudioRecorder _audioRecorder;
        private GameOverlayManager _gameOverlayManager;
        private DR2Helper _dr2Helper;
        private string _trackName;
        private string _trackFolder;
        private AutoRecorder _autoRecorder;
        private bool _isRecordingInProgress = false;
        private bool _isPureAudioRecording = true;
        private ScriptEditor.MainWindow _scriptWindow;
        private string _version = "v2.5.1";

        private SettingsWindow _settingsWindow;


        private RecordingConfig _recordingConfig = new RecordingConfig()
        {
            ChannelCount = 2,
            SampleRate = 22050,
            Mp3BitRate = 128,
            UseLoopbackCapture = false,
        };

        private IEnumerable<RecordingDeviceInfo> _recordingDevices;
        private int _selectReplayDeviceID = 0;
        private int _selectReplayMode = 0;
        private bool _firstSoundPlayed = false;
        private double _scriptTiming = 0;
        private int _playpointAdjust = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // show page for new version
            // AutoUpdater.OpenDownloadPage = true; 
            AutoUpdater.ShowSkipButton = false;
            //AutoUpdater.ReportErrors = true;
            AutoUpdater.Start("https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate.xml");


            // put initialization to window_loaded to accelerate window loading.
            this._profileManager = new();
            this._audioRecorder = new();
            this._gameOverlayManager = new();
            this._dr2Helper = new();
            this._autoRecorder = new();

            this.initHotKeys();
            //this.initializeI18N();
            this.initializeUDPReceiver();
            this.initializeComboBoxes();
            this.checkPrerequisite();
            this.checkIfDevVersion();
            this.initializeGameOverlay();
            this.initializeAutoRecorder();
            this.applyUserConfig();
            this.initializeTheme();
        }

        private void initHotKeys()
        {
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
                                (int)this._udpReceiver.LastMessage.LapDistance);
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
            }, false);
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
            }, false);
        }

        private void registerHotKeys()
        {
            this._hotKeyStartRecord?.Register();
            this._hotKeyStopRecord?.Register();
        }

        private void unregisterHotKeys()
        {
            this._hotKeyStartRecord?.Unregister();
            this._hotKeyStopRecord?.Unregister();
        }

        private void initializeUDPReceiver()
        {
            this._udpReceiver = new UDPReceiver();
            this._udpReceiver.onCollisionDetected += () =>
            {
                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    this._profileManager.PlaySystem(ProfileManager.SYSTEM_COLLISION);
                };
                worker.RunWorkerAsync();
            };
            this._udpReceiver.onWheelAbnormalDetected += wheelIndex =>
            {
                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    this._profileManager.PlaySystem(ProfileManager.SYSTEM_PUNCTURE[wheelIndex]);
                };
                worker.RunWorkerAsync();
            };
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

                    this.tb_wp_fl.Text = msg.SpeedFrontLeft.ToString("0.0");
                    this.tb_wp_fr.Text = msg.SpeedFrontRight.ToString("0.0");
                    this.tb_wp_rl.Text = msg.SpeedRearLeft.ToString("0.0");
                    this.tb_wp_rr.Text = msg.SpeedRearRight.ToString("0.0");
                });

                if (this._toolState == ToolState.Recording && !this._isPureAudioRecording)
                {
                    this._autoRecorder.Distance = (int)msg.LapDistance;
                    return;
                }

                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {

                    // play in threads.
                    // play sound (maybe state not changed and audio files not loaded.)
                    if (this._profileManager.CurrentAudioFile != null)
                    {
                        if (this._selectReplayMode == 0)
                        {
                            if (msg.LapDistance +
                                msg.Speed / 3.6f * (0 - this._scriptTiming) >=
                                this._profileManager.CurrentAudioFile.Distance + this._playpointAdjust)
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
                                    msg.Speed / 3.6f * (Config.Instance.ScriptMode_PlaySecondsAdvanced -
                                                        this._scriptTiming) >=
                                    this._profileManager.CurrentAudioFile.Distance + this._playpointAdjust)
                                {
                                    // play before in <PlaySecondsAdvanced> seconds.
                                    this._profileManager.Play();
                                }

                                if (!this._profileManager.CurrentScriptReader.IsDynamic &&
                                    msg.LapDistance +
                                    msg.Speed / 3.6f * (0 - this._scriptTiming) >=
                                    this._profileManager.CurrentAudioFile.Distance + this._playpointAdjust)
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

            this._udpReceiver.onGameStateChanged += this.gamestateChangedHandler;
        }

        private void gamestateChangedHandler(GameState lastState, GameState state)
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
                    if (this._toolState == ToolState.Recording && this._isPureAudioRecording)
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

                    // end recording of autoscript
                    if (this._toolState == ToolState.Recording && !this._isPureAudioRecording)
                    {

                    }
                    if (this._toolState == ToolState.Replaying && lastState == GameState.Racing && Config.Instance.PlayStartAndEndSound)
                    {
                        // play end sound
                        this._profileManager.PlaySystem(ProfileManager.SYSTEM_END_STAGE);
                    }

                    break;
                case GameState.RaceBegin:
                    // load trace, use lastmsg tracklength & startZ
                    // this._udpReceiver.LastMessage.TrackLength
                    this._udpReceiver.ResetWheelStatus();
                    this._trackName = this._dr2Helper.GetItinerary(
                        this._udpReceiver.LastMessage.TrackLength.ToString("f2", CultureInfo.InvariantCulture),
                        this._udpReceiver.LastMessage.StartZ
                    );
                    this.Dispatcher.Invoke(() =>
                    {
                        this.tb_currentTrack.Text = this._trackName;
                        this.tb_currentTrack.ToolTip = this._trackName;
                        this._gameOverlayManager.TrackName = this._trackName;
                        // disable profile switch, replay device selection
                        this.cb_profile.IsEnabled = false;
                        this.cb_replay_device.IsEnabled = false;

                        //this.ck_record.IsEnabled = false;
                        //this.ck_replay.IsEnabled = false;
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
                                this._gameOverlayManager.ScriptAuthor = this._profileManager.CurrentCoDriverName;
                                if (this._selectReplayMode != 0 &&
                                    this._profileManager.CurrentScriptReader != null)
                                {
                                    this.chb_isDynamicPlay.IsChecked =
                                        this._profileManager.CurrentScriptReader.IsDynamic;
                                    //this.sl_scriptTiming.IsEnabled =
                                    //    this._profileManager.CurrentScriptReader.IsDynamic;
                                    this.tb_scriptAuthor.Text = this._profileManager.CurrentScriptReader.Author;
                                    this._gameOverlayManager.ScriptAuthor = this._profileManager.CurrentScriptReader.Author;
                                }
                                else
                                {
                                    this.chb_isDynamicPlay.IsChecked = false;
                                    //this.sl_scriptTiming.IsEnabled = false;
                                    this.tb_scriptAuthor.Text = "???";
                                    this._gameOverlayManager.ScriptAuthor = "???";
                                }

                                // switch replay mode tab
                                switch (this._selectReplayMode)
                                {
                                    case 0:
                                        this.tab_playMode.SelectedIndex = 0;
                                        break;
                                    case 1:
                                        this.tab_playMode.SelectedIndex = 1;
                                        break;
                                    case 2:
                                        this.tab_playMode.SelectedIndex = this._profileManager.AudioPacenoteCount >
                                                                          this._profileManager.ScriptPacenoteCount
                                            ? 0
                                            : 1;
                                        break;
                                    default:
                                        this.tab_playMode.SelectedIndex = 0;
                                        break;
                                }
                                if (this.tab_playMode.SelectedIndex == 0)
                                {
                                    this._gameOverlayManager.PacenoteType = "纯语音";
                                } else
                                {
                                    this._gameOverlayManager.PacenoteType = "脚本";
                                }
                            });

                            if (lastState != GameState.Paused && Config.Instance.PlayStartAndEndSound)
                            {
                                // play start sound
                                this._profileManager.PlaySystem(ProfileManager.SYSTEM_START_STAGE);
                            }
                        };
                        worker.RunWorkerAsync();
                    }

                    break;
                case GameState.Racing:
                    if (lastState == GameState.RaceBegin && Config.Instance.PlayGoSound)
                    {
                        // just go !
                        this._profileManager.PlaySystem(ProfileManager.SYSTEM_GO);
                    }
                    break;
            }
        }

        private void initializeComboBoxes()
        {
            foreach (var profile in this._profileManager.GetAllProfiles())
            {
                this.cb_profile.Items.Add(profile);
            }

            foreach (var codriver in this._profileManager.GetAllCodrivers())
            {
                if (!Config.Instance.UseDefaultSoundPackageByDefault && codriver == ProfileManager.DEFAULT_CODRIVER)
                {
                    continue;
                }
                this.cb_codrivers.Items.Add(codriver);
            }
            if (this.cb_codrivers.Items.Count == 1)
            {
                // only 'default' presents
                //this.cb_codrivers.SelectedIndex = 0;
            }

            //this.cb_profile.SelectedIndex = 0;

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

            //this.cb_replay_device.SelectedIndex = 0;


            // if there's no recording device, would throw exception...
            if (this._recordingDevices.Count() > 0)
                this.cb_recording_device.SelectedIndex = 0;
        }

        private void checkPrerequisite()
        {
            // check the file
            var preCheck = new PrerequisitesCheck();
            var checkResult = preCheck.Check();
            switch (checkResult.Code)
            {
                case PrerequisitesCheckResultCode.PORT_NOT_OPEN:
                    // not pass
                    //var result =
                    //MessageBox.Show(
                    //    "你的Dirt Rally 2.0 的配置文件中的UDP端口未正确打开，如果没有打开，工具将无法正常工作，点击“是”自动修改配置文件，点击“否”退出程序自行修改",
                    //    "配置错误", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    var pnoDialog = new PortNotOpenDialog();
                    var resPNO = pnoDialog.ShowDialog();
                    if (resPNO.HasValue && resPNO.Value)
                    {
                        preCheck.Write();
                    }
                    else
                    {
                        // Goodbye.
                        System.Windows.Application.Current.Shutdown();
                    }

                    break;
                case PrerequisitesCheckResultCode.PORT_NOT_MATCH:
                    var pmDialog = new PortMismatchDialog(checkResult.Params[0].ToString(), checkResult.Params[1].ToString());
                    var resPM = pmDialog.ShowDialog();
                    if (resPM.HasValue && !resPM.Value)
                    {
                        // force fix
                        preCheck.Write();
                        Config.Instance.UDPListenPort = 20777;
                        Config.Instance.SaveUserConfig();
                    }
                    //MessageBox.Show(String.Format("你的Dirt Rally 2.0 的配置文件中的UDP端口 {0} 和本工具中的UDP端口 {1} 配置不同，可能会导致地图读取失败（也可能是使用了simhub转发）",
                    //    checkResult.Params[0], checkResult.Params[1]),
                    //    "配置警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }

        private void checkIfDevVersion()
        {
            if (System.IO.Directory.Exists(Config.Instance.PythonPath) &&
                System.IO.Directory.Exists(Config.Instance.SpeechRecogizerModelPath))
            {
                // dev mode
                this.Title = string.Format("{0}{1}",
                    I18NLoader.Instance.CurrentDict["application.title_dev"].ToString(),
                    _version);
                this.cb_record_mode.SelectedIndex = 1;  // auto script mode
                if (Config.Instance.AutoScript_HackGameWhenStart)
                {
                    while (!System.IO.File.Exists(System.IO.Path.Join(Config.Instance.DirtGamePath, "dirtrally2.exe")))
                    {
                        var res = PromptDialog.Dialog.Prompt(
                                    "当前为开发版本，需要定位游戏所在位置便于分离路书声音",
                                    "尘埃拉力赛2.0游戏目录",
                                    "");
                        if (res != null)
                        {
                            Config.Instance.DirtGamePath = res.ToString();
                        }
                    }
                    Config.Instance.SaveUserConfig();
                    GameHacker.HackDLLs(Config.Instance.DirtGamePath);
                }
            }
            else
            {
                this.Title = string.Format("{0}{1}",
                    I18NLoader.Instance.CurrentDict["application.title"].ToString(),
                    _version);
                // disable auto script mode
                this.cb_record_mode.Items.RemoveAt(this.cb_record_mode.Items.Count - 1);
            }
        }

        private void initializeGameOverlay()
        {
            if (Config.Instance.UI_ShowHud) { 
                this._gameOverlayManager.StartLoop();
            }
        }

        private void initializeAutoRecorder()
        {

            this._autoRecorder.Initialized += (deviceName) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("自动脚本录制模式已启动！");
                    this.g_autoScriptListeningDevice.Visibility = Visibility.Hidden;
                    this.tb_autoScriptListeningDevice.Text = deviceName;
                    this.tb_autoScriptListeningDevice.ToolTip = deviceName;
                    this.tab_playMode.SelectedIndex = 1;
                });
            };
            this._autoRecorder.PieceRecognized += t =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    // MessageBox.Show("Recognized: " + t.Item2);
                    this._scriptWindow?.AppendLine(t.Item2);
                });
            };
            this._autoRecorder.Uninitialized += () =>
            {
                // enable
                this.Dispatcher.Invoke(() =>
                {
                    this.g_autoScriptListeningDevice.Visibility = Visibility.Visible;
                    this.ck_record.IsEnabled = true;
                    this.ck_replay.IsEnabled = true;
                    this.tb_autoScriptListeningDevice.Text = "";
                    this.tb_autoScriptListeningDevice.ToolTip = null;
                });
            };
        }

        private void applyUserConfig()
        {
            if (Config.Instance.UI_SelectedProfile < this.cb_profile.Items.Count)
            {
                this.cb_profile.SelectedIndex = Config.Instance.UI_SelectedProfile;
            }

            if (Config.Instance.UI_SelectedAudioPackage < this.cb_codrivers.Items.Count)
            {
                this.cb_codrivers.SelectedIndex = Config.Instance.UI_SelectedAudioPackage;
            }

            if (Config.Instance.UI_SelectedPlaybackDevice < this.cb_replay_device.Items.Count)
            {
                this.cb_replay_device.SelectedIndex = Config.Instance.UI_SelectedPlaybackDevice;
            }

            this.chk_Hud.IsChecked = Config.Instance.UI_ShowHud;
            this.sl_scriptTiming.Value = Config.Instance.UI_PlaybackAdjustSeconds;
            this.s_volume.Value = Config.Instance.UI_PlaybackVolume;
        }

        private void initializeTheme()
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(Config.Instance.IsDarkTheme ? Theme.Dark : Theme.Light);
            paletteHelper.SetTheme(theme);
        }


        private void Ck_record_OnChecked(object sender, RoutedEventArgs e)
        {
            if (this._udpReceiver.GameState == GameState.Unknown)
            {
                if (this._toolState == ToolState.Replaying)
                {
                    this._toolState = ToolState.Recording;
                    if (!this._isPureAudioRecording)
                    {
                        BackgroundWorker bgw = new BackgroundWorker();
                        bgw.DoWork += (o, args) =>
                        {
                            try
                            {
                                this._autoRecorder.Initialize();
                            }
                            catch (Exception e)
                            {
                                this._autoRecorder.Uninitialize();
                                this.Dispatcher.Invoke(() =>
                                MessageBox.Show(string.Format("自动脚本录制模式启动 大失败！{0}{1}", System.Environment.NewLine, e.ToString()),
                                    "大失败",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error));
                            }
                        };
                        bgw.RunWorkerAsync();
                    } else
                    {
                        this.tab_playMode.SelectedIndex = 0;
                        this.registerHotKeys();
                    }
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
                    //this._autoRecorder.StopSoundCapture();
                    if (!this._isPureAudioRecording)
                    {
                        //disable controls
                        this.ck_record.IsEnabled = false;
                        this.ck_replay.IsEnabled = false;
                        this._autoRecorder.Uninitialize();
                    } else
                    {
                        this.unregisterHotKeys();
                    }
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
                    this._recordingConfig.SampleRate = 11025;
                    this._recordingConfig.Mp3BitRate = 48;
                    break;
                case 1:
                    this._recordingConfig.SampleRate = 22050;
                    this._recordingConfig.Mp3BitRate = 128;
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    System.IO.Path.GetFullPath(this._profileManager.CurrentItineraryPath)));
            }
        }

        private void cb_profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentProfile = this.cb_profile.SelectedItem.ToString().Split('/')[1];
            Config.Instance.UI_SelectedProfile = this.cb_profile.SelectedIndex;
            Config.Instance.SaveUserConfig();
        }

        private void cb_replay_device_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentPlayDeviceId = this.cb_replay_device.SelectedIndex;
            Config.Instance.UI_SelectedPlaybackDevice = this.cb_replay_device.SelectedIndex;
            Config.Instance.SaveUserConfig();
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
WindowsAPICodePack-Shell (https://github.com/aybe/Windows-API-Code-Pack-1.1)
AutoUpdater.NET (https://github.com/ravibpatel/AutoUpdater.NET)

最后再次感谢ZTMZ Club组委会和群里大佬们的帮助与支持。
", "关于本工具 v2.4", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btn_currentTrack_script_Click(object sender, RoutedEventArgs e)
        {
            //if (this._profileManager.CurrentItineraryPath != null)
            {
                // System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ZTMZ.PacenoteTool.ScriptEditor.exe",
                //    string.Format("\"{0}\"", System.IO.Path.GetFullPath(this._profileManager.CurrentScriptPath))));
                ScriptEditor.App.InitHighlightComponent();
                _scriptWindow = new ScriptEditor.MainWindow();
                _scriptWindow.Show();
                _scriptWindow.HandleFileOpen(System.IO.Path.GetFullPath(this._profileManager.CurrentScriptPath));
            }
        }

        private void cb_replay_mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._selectReplayMode = this.cb_replay_mode.SelectedIndex;
        }

        private void cb_codrivers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._profileManager.CurrentCoDriverSoundPackagePath = this.cb_codrivers.SelectedItem.ToString();
            this._gameOverlayManager.AudioPackage = this._profileManager.CurrentCoDriverSoundPackagePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            Config.Instance.UI_SelectedAudioPackage = this.cb_codrivers.SelectedIndex;
            Config.Instance.SaveUserConfig();
        }

        private void S_volume_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this._profileManager != null)
            {
                var value = (int)e.NewValue;
                this._profileManager.CurrentPlayAmplification = value;
                if (this.tb_volume != null)
                {
                    this.tb_volume.Text = value > 0 ? $"+{value}" : $"{value}";
                }
                Config.Instance.UI_PlaybackVolume = e.NewValue;
                Config.Instance.SaveUserConfig();
            }
        }

        private void Btn_startScriptEditor_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ZTMZ.PacenoteTool.ScriptEditor.exe"));
        }

        private void Btn_startAudioCompressor_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ZTMZ.PacenoteTool.AudioBatchProcessor.exe"));
        }

        private void Sl_scriptTiming_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var value = this.sl_scriptTiming.Value.ToString("0.0");
            this._scriptTiming = this.sl_scriptTiming.Value;
            this.tb_scriptTiming.Text = (this.sl_scriptTiming.Value > 0 ? $"+{value}" : $"{value}") + "s";
            Config.Instance.UI_PlaybackAdjustSeconds = e.NewValue;
            Config.Instance.SaveUserConfig();
        }

        //private void S_playpointAdjust_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        //{
        //    var value = (int)this.s_playpointAdjust.Value;
        //    this._playpointAdjust = value;
        //    this.tb_playpointAdjust.Text = (value > 0 ? $"+{value}" : $"{value}") + "米";
        //}

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            e.Handled = true;
        }

        private void cb_record_mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this._isPureAudioRecording = this.cb_record_mode.SelectedIndex == 0;
        }

        private void tb_soundSettings_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", "Shell32.dll, Control_RunDLL \"C:\\Windows\\System32\\mmsys.cpl"));
        }

        private void chk_Hud_Click(object sender, RoutedEventArgs e)
        {
            if (this.chk_Hud.IsChecked.HasValue)
            {
                if (this.chk_Hud.IsChecked.Value)
                {
                    //this.chk_Hud.IsChecked = false;
                    this._gameOverlayManager.StartLoop();
                } else
                {
                    this._gameOverlayManager.StopLoop();
                }

                Config.Instance.UI_ShowHud = this.chk_Hud.IsChecked.Value;
                Config.Instance.SaveUserConfig();
                this.chk_Hud.IsEnabled = false;
                this.pb_Hud.Visibility = Visibility.Visible;
                var bgw = new BackgroundWorker();
                bgw.DoWork += (s, e) =>
                {
                    Thread.Sleep(3000);
                    this.Dispatcher.Invoke(() =>
                    {
                        this.chk_Hud.IsEnabled = true;
                        this.pb_Hud.Visibility = Visibility.Collapsed;
                    });
                };
                bgw.RunWorkerAsync();
            }
        }

        private void Btn_Settings_OnClick(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.PortChanged += () =>
                {
                    BackgroundWorker bgw = new BackgroundWorker();
                    bgw.DoWork += (s, e) =>
                    {
                        this._udpReceiver.StopListening();
                        Thread.Sleep(2800);
                        this._udpReceiver.StartListening();
                    };
                    bgw.RunWorkerAsync();
                };
                //_settingsWindow.HudFPSChanged += () =>
                //{
                //    this._gameOverlayManager.StopLoop();
                //    this._gameOverlayManager.StartLoop();
                //};
            }
            _settingsWindow.Show();
            _settingsWindow.Focus();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.CanClose = true;
            }
            // close all windows
            foreach (Window win in Application.Current.Windows)
            {
                if (win != this)
                {
                    win.Close();
                }
            }
        }
    }
}