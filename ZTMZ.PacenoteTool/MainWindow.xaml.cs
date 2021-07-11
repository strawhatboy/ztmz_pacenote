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
        public MainWindow()
        {
            InitializeComponent();
            this._hotKeyStartRecord = new HotKey(Key.F1, KeyModifier.None, key =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.tb_time.Text = "start";
                });
            });
            this._hotKeyStopRecord = new HotKey(Key.F2, KeyModifier.None, key =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.tb_time.Text = "stop";
                });
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
                this.Dispatcher.Invoke(() =>
                {
                    this.tb_gamestate.Text = state.ToString();
                });
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
                        break;
                }
            };
        }
    }
}