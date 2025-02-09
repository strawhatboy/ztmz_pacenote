using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using SharpDX.Win32;
using FFMpegCore;
using System.Linq;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPlayingPageVM : ObservableObject, INavigationAware
{
    private Replay _replay = null;
    private string tmpAudioPath = "";
    private List<ReplayDetailsPerTime> _replayDetailsPerTime = null;
    private ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool = null;

    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    private DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Send);  // highest priority

    /// <summary>
    /// this is the video player on the UI.
    /// </summary>
    public MediaElement mediaElement = null;

    [ObservableProperty]
    private string _headerContent = "";

    [ObservableProperty]
    private int _replayId = -1;
    partial void OnReplayIdChanged(int value) {
        this.HeaderContent = $"Replay: {value}";
    }
    [ObservableProperty]
    private string _videoPath = "";

    [ObservableProperty]
    private double _playPosition;

    partial void OnPlayPositionChanged(double newValue){
        // calculate the distance
        UpdateLapTimeAndLapDistance(newValue);
    }
    [ObservableProperty]
    private double _videoLength;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isPausedBecauseOfSliding;


#region right panel
    [ObservableProperty]
    private string _track;

    [ObservableProperty]
    private string _car;

    [ObservableProperty]
    private string _carClass;
    [ObservableProperty]
    private double _lapTime;

    [ObservableProperty]
    private float _lapDistance;

    [ObservableProperty]
    private float _gotoLapDistance;
    partial void OnGotoLapDistanceChanged(float value){
        // calculate the time
        if (IsPaused) {
            Dispatcher.CurrentDispatcher.Invoke(() => {
                this._timer.Stop();
            });
            var timestamp = ReplayManager.getTimeStampWithDistance(this._replayDetailsPerTime, value);
            var tgtPosition = (double)(timestamp - this._replay.video_begin_timestamp) / 10000000 - this.PlayPositionOffset;
            this.PlayPosition = tgtPosition < 0 ? 0 : tgtPosition;
            Dispatcher.CurrentDispatcher.Invoke(() => {
                this._timer.Start();
            });
        }
    }

    /// <summary>
    /// this is the offset of the play position. put values in to change the video/data offset
    /// </summary>
    [ObservableProperty]
    private double _playPositionOffset = 0.0; 
    partial void OnPlayPositionOffsetChanged(double newValue){
        // calculate the distance
        UpdateLapTimeAndLapDistance(this.PlayPosition);
    }
#endregion

    private void UpdateLapTimeAndLapDistance(double newValue) {
        if (this._replayDetailsPerTime != null && this._replayDetailsPerTime.Count > 0) {
            var playTime = newValue + this.PlayPositionOffset;
            // convert playTime to ticks and plus this._replay.video_begin_timestamp is the key to get the replay details per time
            var key = (long)(playTime * 10000000) + this._replay.video_begin_timestamp;
            var rdp = ReplayManager.getReplayDetailsPerTimeWithTimeStamp(this._replayDetailsPerTime, key);
            this.LapTime = rdp.time;    // the laptime
            this.LapDistance = rdp.distance;
        }
    }

    public ReplayPlayingPageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool) {
        this._tool = tool;
        // triggered every 10 ms
        this._timer.Interval = new TimeSpan(0, 0, 0, 0, 1);
        this._timer.Tick += this.Timer_Ticks;
    }

    private void Timer_Ticks(object? sender, EventArgs args) {
        if (this.mediaElement == null) {
            return;
        }

        try {
            if (!IsPaused) {
                this.VideoLength = this.mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                this.PlayPosition = this.mediaElement.Position.TotalSeconds;
            }
        } catch (Exception ex) {
            _logger.Error(ex);
        }
    }

    public async void OnNavigatedTo()
    {
        this._timer.Start();
        // load replay with ReplayId
        this._replay = await ReplayManager.Instance.getReplay(this.ReplayId);
        this._replayDetailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(this.ReplayId);

        this.Track = this._replay.track;
        this.Car = this._replay.car;
        this.CarClass = this._replay.car_class;

        if (!string.IsNullOrEmpty(this._replay.video_path) && File.Exists(this._replay.video_path))
        {
            _logger.Info("Playing video: " + this._replay.video_path);
            // load video
            this.VideoPath = this._replay.video_path;
            this.IsPaused = false;
            Application.Current.Dispatcher.Invoke(() => {
                this.mediaElement.Play();
            });
        }
        else
        {
            _logger.Warn("Video not found, will create an empty audio file.");
            var duration = (double)(this._replayDetailsPerTime.Last().timestamp - this._replayDetailsPerTime.First(a => a.timestamp > 0).timestamp) / 10000000 + this._replayDetailsPerTime.First(a => a.timestamp > 0).time;
            // create an empty audio file wth ffmpeg
            tmpAudioPath = Path.Combine(Path.GetTempPath(), "empty.wav");
            GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
            await FFMpegArguments
                .FromFileInput("anullsrc=channel_layout=mono:sample_rate=48000", false, options => options.WithCustomArgument($"-f lavfi -t {duration}"))
                .OutputToFile(tmpAudioPath, true)
                .ProcessAsynchronously();

            this.VideoPath = tmpAudioPath;
            this.IsPaused = false;
            this._replay.video_begin_timestamp = this._replayDetailsPerTime.First(a => a.timestamp > 0).timestamp;
            
            Application.Current.Dispatcher.Invoke(() => {
                this.mediaElement.Play();
            });
        }
    }

    public void OnNavigatedFrom()
    {
        // throw new NotImplementedException();
        this.mediaElement.Stop();
        this._timer.Stop();
        if (!string.IsNullOrEmpty(this.tmpAudioPath) && File.Exists(this.tmpAudioPath)) {
            File.Delete(this.tmpAudioPath);
        }
    }

    [RelayCommand]
    private async void SlideStarted() {
        Application.Current.Dispatcher.Invoke(() => {
            // always stop the timer.
            this._timer.Stop();
        });
        // pause the playing and wait for the slide end
        if (!this.IsPaused) {
            _logger.Trace("Slide started, pausing the video.");
            Application.Current.Dispatcher.Invoke(() => {
                _logger.Trace("Pausing the video and timer.");
                this.mediaElement.Pause();
            });
            this.IsPausedBecauseOfSliding = true;
        } else {
            this.IsPausedBecauseOfSliding = false;
        }
    }

    [RelayCommand]
    private async void SlideEnded(double endPosition) {
        Application.Current.Dispatcher.Invoke(() => {
            // always change the position. no matter paused or not
            this.mediaElement.Position = TimeSpan.FromSeconds(endPosition);
            this._timer.Start();
        });
        if (this.IsPausedBecauseOfSliding) {
            _logger.Trace("Slide ended, resuming the video.");
            Application.Current.Dispatcher.Invoke(() => {
                _logger.Trace("Resuming the video and timer.");
                this.mediaElement.Play();
            });
        } else {
            
            Application.Current.Dispatcher.Invoke(() => {
                // quick play and pause to locate
                this.mediaElement.Play();
                this.mediaElement.Pause();
            });
        }
    }

    [RelayCommand]
    private async void SpaceKey() {
        if (this.IsPaused) {
            Application.Current.Dispatcher.Invoke(() => {
                this.mediaElement.Position = TimeSpan.FromSeconds(this.PlayPosition);
                this.mediaElement.Play();
            });
            this.IsPaused = false;
        } else {
            // space key pressed. should pause the video
            Application.Current.Dispatcher.Invoke(() => {
                this.mediaElement.Pause();
            });
            this.IsPaused = true;
        }
    }
}
