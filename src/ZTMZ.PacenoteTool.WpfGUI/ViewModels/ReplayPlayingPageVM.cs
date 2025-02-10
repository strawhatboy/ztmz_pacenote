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
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using LiveChartsCore.Kernel.Sketches;
using System.Threading.Tasks;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

// public class BindableAxisSection: LiveChartsCore

public partial class ReplayPlayingPageVM : ObservableObject, INavigationAware
{
    private Replay _replay = null;
    private Replay _bestReplay = null;
    private string tmpAudioPath = "";
    private List<ReplayDetailsPerTime> _replayDetailsPerTime = null;
    private List<ReplayDetailsPerTime> _bestReplayDetailsPerTime = null;
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

    [ObservableProperty]
    private bool _scrubbingEnabled;


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

    [ObservableProperty]
    private bool _hasBestReplay;

    [ObservableProperty]
    private bool _showBestReplayComparison;
    private void setVisibilityOfBestReplaySeries(List<ISeries> series, bool visible) {
        series.ForEach(a => {
            if (a.Name.Contains(I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.best", "en-us"))) {
                a.IsVisible = visible ? true : false;
            }
        });
    }
    partial void OnShowBestReplayComparisonChanged(bool value){
        if (value) {
            // set visibility of best replay
            // last half of pedal series
            setVisibilityOfBestReplaySeries(this.PedalSeries, true);
            setVisibilityOfBestReplaySeries(this.SpeedSeries, true);
            setVisibilityOfBestReplaySeries(this.RpmSeries, true);
            setVisibilityOfBestReplaySeries(this.GSeries, true);
            setVisibilityOfBestReplaySeries(this.GearSeries, true);
            setVisibilityOfBestReplaySeries(this.BrakeTempSeries, true);
            setVisibilityOfBestReplaySeries(this.SuspensionSeries, true);
            setVisibilityOfBestReplaySeries(this.SuspensionSpeedSeries, true);
        } else {
            // set visibility of best replay
            // last half of pedal series
            setVisibilityOfBestReplaySeries(this.PedalSeries, false);
            setVisibilityOfBestReplaySeries(this.SpeedSeries, false);
            setVisibilityOfBestReplaySeries(this.RpmSeries, false);
            setVisibilityOfBestReplaySeries(this.GSeries, false);
            setVisibilityOfBestReplaySeries(this.GearSeries, false);
            setVisibilityOfBestReplaySeries(this.BrakeTempSeries, false);
            setVisibilityOfBestReplaySeries(this.SuspensionSeries, false);
            setVisibilityOfBestReplaySeries(this.SuspensionSpeedSeries, false);
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
            
            // this.PedalSections.Clear();
            // this performance is so bad!
            // this.PedalSections.First().Xi = rdp.time;
            // this.PedalSections.First().Xj = rdp.time;

            // update labels
            this.PedalLabel = $"{I18NLoader.Instance["dashboard.throttle"]}: {rdp.throttle * 100:00.0}% | {I18NLoader.Instance["dashboard.brake"]}: {rdp.brake * 100:00.0}% | {I18NLoader.Instance["dashboard.clutch"]}: {rdp.clutch * 100:00.0}% | {I18NLoader.Instance["dashboard.handbrake"]}: {rdp.handbrake * 100:00.0}%";
            this.SpeedLabel = $"{I18NLoader.Instance["dashboard.speed"]}: {rdp.speed:0.0} km/h";
            this.RpmLabel = $"{I18NLoader.Instance["dashboard.rpm"]}: {rdp.rpm:0.0}";
            this.GLabel = $"{I18NLoader.Instance["dashboard.g_lat"]}: {rdp.g_lat:0.0} | {I18NLoader.Instance["dashboard.g_long"]}: {rdp.g_long:0.0}";
            this.GearLabel = $"{I18NLoader.Instance["dashboard.gear"]}: {rdp.gear}";
            this.BrakeTempLabel = $"{I18NLoader.Instance["dashboard.brake_temp_front_left"]}: {rdp.brake_temp_front_left:0.0} | {I18NLoader.Instance["dashboard.brake_temp_front_right"]}: {rdp.brake_temp_front_right:0.0} | {I18NLoader.Instance["dashboard.brake_temp_rear_left"]}: {rdp.brake_temp_rear_left:0.0} | {I18NLoader.Instance["dashboard.brake_temp_rear_right"]}: {rdp.brake_temp_rear_right:0.0}";
            this.SuspensionLabel = $"{I18NLoader.Instance["dashboard.suspension_front_left"]}: {rdp.suspension_front_left:0.0} | {I18NLoader.Instance["dashboard.suspension_front_right"]}: {rdp.suspension_front_right:0.0} | {I18NLoader.Instance["dashboard.suspension_rear_left"]}: {rdp.suspension_rear_left:0.0} | {I18NLoader.Instance["dashboard.suspension_rear_right"]}: {rdp.suspension_rear_right:0.0}";
            this.SuspensionSpeedLabel = $"{I18NLoader.Instance["dashboard.suspension_speed_front_left"]}: {rdp.suspension_speed_front_left:0.0} | {I18NLoader.Instance["dashboard.suspension_speed_front_right"]}: {rdp.suspension_speed_front_right:0.0} | {I18NLoader.Instance["dashboard.suspension_speed_rear_left"]}: {rdp.suspension_speed_rear_left:0.0} | {I18NLoader.Instance["dashboard.suspension_speed_rear_right"]}: {rdp.suspension_speed_rear_right:0.0}";
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
        // load replay with ReplayId
        this._replay = await ReplayManager.Instance.getReplay(this.ReplayId);
        this._bestReplay = await ReplayManager.Instance.GetBestReplay(_tool.CurrentGame, _replay.track, _replay.car_class, _replay.car);
        if (this._bestReplay != null && this._bestReplay.id != this._replay.id) {
            // got the best replay
            this.HasBestReplay = true;
            this._bestReplayDetailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(this._bestReplay.id);
        }

        this._replayDetailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(this.ReplayId);
        await setCharts();
        this.Track = this._replay.track;
        this.Car = this._replay.car;
        this.CarClass = this._replay.car_class;

        if (!string.IsNullOrEmpty(this._replay.video_path) && File.Exists(this._replay.video_path))
        {
            this.ScrubbingEnabled = true;
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
            // ScrubbingEnabled is only for video, when playing audio, it should be false, or the audio will not be played.
            this.ScrubbingEnabled = false;
            _logger.Warn("Video not found, will create an empty audio file.");
            var duration = (double)(this._replayDetailsPerTime.Last().timestamp - this._replayDetailsPerTime.First(a => a.timestamp > 0).timestamp) / 10000000 + this._replayDetailsPerTime.First(a => a.timestamp > 0).time;
            // create an empty audio file wth ffmpeg
            tmpAudioPath = Path.Combine(Path.GetTempPath(), "empty.mp3");
            GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
            await FFMpegArguments
                .FromFileInput("anullsrc=channel_layout=stereo:sample_rate=44100", false, options => options.WithCustomArgument($"-f lavfi -t {duration}"))
                .OutputToFile(tmpAudioPath, true)
                .ProcessAsynchronously();

            _logger.Info("Created empty audio: " + tmpAudioPath);

            this.VideoPath = tmpAudioPath;
            this.IsPaused = false;
            this._replay.video_begin_timestamp = this._replayDetailsPerTime.First(a => a.timestamp > 0).timestamp;
            
            Application.Current.Dispatcher.Invoke(() => {
                try {
                    this.mediaElement.Play();
                } catch (Exception ex) {
                    _logger.Error($"Error when playing tmp audio: {ex}");
                }
            });
        }
        this._timer.Start();
    }

    public void OnNavigatedFrom()
    {
        // throw new NotImplementedException();
        this.mediaElement.Stop();
        this._timer.Stop();
        this.VideoPath = "";
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

#region Charts
    [ObservableProperty]
    private string _pedalLabel = "";

    [ObservableProperty]
    private string _speedLabel = "";

    [ObservableProperty]
    private string _rpmLabel = "";

    [ObservableProperty]
    private string _gLabel = "";

    [ObservableProperty]
    private string _gearLabel = "";

    [ObservableProperty]
    private string _brakeTempLabel = "";

    [ObservableProperty]
    private string _suspensionLabel = "";

    [ObservableProperty]
    private string _suspensionSpeedLabel = "";

    [ObservableProperty]
    private Margin _drawMargin = new Margin(10);

    [ObservableProperty]
    private List<ISeries> _pedalSeries = new();

    [ObservableProperty]
    private List<ISeries> _speedSeries = new();

    [ObservableProperty]
    private List<ISeries> _RpmSeries = new();

    [ObservableProperty]
    private List<ISeries> _gSeries = new();

    [ObservableProperty]
    private List<ISeries> _gearSeries = new();

    [ObservableProperty]
    private List<ISeries> _brakeTempSeries = new();

    [ObservableProperty]
    private List<ISeries> _suspensionSeries = new();

    [ObservableProperty]
    private List<ISeries> _suspensionSpeedSeries = new();

    [ObservableProperty]
    private List<ICartesianAxis> _pedalXAxis = new();

    [ObservableProperty]
    private ObservableCollection<RectangularSection> _pedalSections = new();

    private async Task setCharts() {
        await setPedals();
        await setBestCharts();
    }

    private LineSeries<ObservablePoint> getLineSeries(List<ObservablePoint> points, SKColor color, string name, LiveChartsCore.Painting.Paint? stroke = null) {
        return new LineSeries<ObservablePoint>(points) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = stroke ?? new SolidColorPaint(color) { StrokeThickness = 1 },
            Fill = null,
            Name = name
        };
    }

    
    private StepLineSeries<ObservablePoint> getStepLineSeries(List<ObservablePoint> points, SKColor color, string name, LiveChartsCore.Painting.Paint? stroke = null) {
        return new StepLineSeries<ObservablePoint>(points) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = stroke ?? new SolidColorPaint(color) { StrokeThickness = 1 },
            Fill = null,
            Name = name
        };
    }

    private async Task setPedals() {
        this.PedalSeries = new List<ISeries>();
        var throttle = new List<ObservablePoint>();
        var brake = new List<ObservablePoint>();
        var clutch = new List<ObservablePoint>();
        var handbrake = new List<ObservablePoint>();
        var speed = new List<ObservablePoint>();
        var rpm = new List<ObservablePoint>();
        var g_lat = new List<ObservablePoint>();
        var g_long = new List<ObservablePoint>();
        var gear = new List<ObservablePoint>();
        var brake_temp_front_left = new List<ObservablePoint>();
        var brake_temp_front_right = new List<ObservablePoint>();
        var brake_temp_rear_left = new List<ObservablePoint>();
        var brake_temp_rear_right = new List<ObservablePoint>();
        var suspension_front_left = new List<ObservablePoint>();
        var suspension_front_right = new List<ObservablePoint>();
        var suspension_rear_left = new List<ObservablePoint>();
        var suspension_rear_right = new List<ObservablePoint>();
        var suspension_speed_front_left = new List<ObservablePoint>();
        var suspension_speed_front_right = new List<ObservablePoint>();
        var suspension_speed_rear_left = new List<ObservablePoint>();
        var suspension_speed_rear_right = new List<ObservablePoint>();
        for (int i = 0; i < this._replayDetailsPerTime.Count; i++) {
            throttle.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].throttle));
            brake.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake));
            clutch.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].clutch));
            handbrake.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].handbrake));
            speed.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].speed));
            rpm.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].rpm));
            g_lat.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].g_lat));
            g_long.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].g_long));
            gear.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].gear));
            brake_temp_front_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake_temp_front_left));
            brake_temp_front_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake_temp_front_right));
            brake_temp_rear_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake_temp_rear_left));
            brake_temp_rear_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake_temp_rear_right));
            suspension_front_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_front_left));
            suspension_front_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_front_right));
            suspension_rear_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_rear_left));
            suspension_rear_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_rear_right));
            suspension_speed_front_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_speed_front_left));
            suspension_speed_front_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_speed_front_right));
            suspension_speed_rear_left.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_speed_rear_left));
            suspension_speed_rear_right.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].suspension_speed_rear_right));
        }
        var current_str = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.current", "en-us");
        var throttleSeries = getLineSeries(throttle, SKColors.Green, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.throttle", "en-us")}");
        var brakeSeries = getLineSeries(brake, SKColors.Red, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake", "en-us")}");
        var clutchSeries = getLineSeries(clutch, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.clutch", "en-us")}");
        var handbrakeSeries = getLineSeries(handbrake, SKColors.Orange, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.handbrake", "en-us")}");
        var speedSeries = getLineSeries(speed, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.speed", "en-us")}");
        var rpmSeries = getLineSeries(rpm, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.rpm", "en-us")}");
        var g_latSeries = getLineSeries(g_lat, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.g_lat", "en-us")}");
        var g_longSeries = getLineSeries(g_long, SKColors.Red, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.g_long", "en-us")}");
        var gearSeries = getStepLineSeries(gear, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.gear", "en-us")}");
        var brake_temp_front_leftSeries = getLineSeries(brake_temp_front_left, SKColors.Green, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_front_left", "en-us")}");
        var brake_temp_front_rightSeries = getLineSeries(brake_temp_front_right, SKColors.Red, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_front_right", "en-us")}");
        var brake_temp_rear_leftSeries = getLineSeries(brake_temp_rear_left, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_rear_left", "en-us")}");
        var brake_temp_rear_rightSeries = getLineSeries(brake_temp_rear_right, SKColors.Orange, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_rear_right", "en-us")}");
        var suspension_front_leftSeries = getLineSeries(suspension_front_left, SKColors.Green, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_front_left", "en-us")}");
        var suspension_front_rightSeries = getLineSeries(suspension_front_right, SKColors.Red, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_front_right", "en-us")}");
        var suspension_rear_leftSeries = getLineSeries(suspension_rear_left, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_rear_left", "en-us")}");
        var suspension_rear_rightSeries = getLineSeries(suspension_rear_right, SKColors.Orange, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_rear_right", "en-us")}");
        var suspension_speed_front_leftSeries = getLineSeries(suspension_speed_front_left, SKColors.Green, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_front_left", "en-us")}");
        var suspension_speed_front_rightSeries = getLineSeries(suspension_speed_front_right, SKColors.Red, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_front_right", "en-us")}");
        var suspension_speed_rear_leftSeries = getLineSeries(suspension_speed_rear_left, SKColors.Blue, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_rear_left", "en-us")}");
        var suspension_speed_rear_rightSeries = getLineSeries(suspension_speed_rear_right, SKColors.Orange, $"{current_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_rear_right", "en-us")}");

        this.PedalSeries.Clear();
        this.SpeedSeries.Clear();
        this.RpmSeries.Clear();
        this.PedalSections.Clear();
        this.GSeries.Clear();
        this.GearSeries.Clear();
        this.BrakeTempSeries.Clear();
        this.SuspensionSeries.Clear();
        this.SuspensionSpeedSeries.Clear();

        if (Config.Instance.ReplayPlayPedalsMode == 1) {
            this.PedalSeries.Add(throttleSeries);
        } else if (Config.Instance.ReplayPlayPedalsMode == 2) {
            this.PedalSeries.Add(brakeSeries);
        } else if (Config.Instance.ReplayPlayPedalsMode == 3) {
            this.PedalSeries.Add(clutchSeries);
        } else if (Config.Instance.ReplayPlayPedalsMode == 4) {
            this.PedalSeries.Add(handbrakeSeries);
        } else {
            this.PedalSeries.Add(throttleSeries);
            this.PedalSeries.Add(brakeSeries);
            this.PedalSeries.Add(clutchSeries);
            this.PedalSeries.Add(handbrakeSeries);
        }
        this.SpeedSeries.Add(speedSeries);
        this.RpmSeries.Add(rpmSeries);
        this.GSeries.Add(g_latSeries);
        this.GSeries.Add(g_longSeries);
        this.GearSeries.Add(gearSeries);
        this.BrakeTempSeries.Add(brake_temp_front_leftSeries);
        this.BrakeTempSeries.Add(brake_temp_front_rightSeries);
        this.BrakeTempSeries.Add(brake_temp_rear_leftSeries);
        this.BrakeTempSeries.Add(brake_temp_rear_rightSeries);
        this.SuspensionSeries.Add(suspension_front_leftSeries);
        this.SuspensionSeries.Add(suspension_front_rightSeries);
        this.SuspensionSeries.Add(suspension_rear_leftSeries);
        this.SuspensionSeries.Add(suspension_rear_rightSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_front_leftSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_front_rightSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_rear_leftSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_rear_rightSeries);

        this.PedalXAxis = new List<ICartesianAxis> {
            new Axis{
                CrosshairLabelsBackground = SKColors.DarkOrange.AsLvcColor(),
                CrosshairLabelsPaint = new SolidColorPaint(SKColors.DarkRed),
                CrosshairPaint = new SolidColorPaint(SKColors.DarkOrange, 1),
                Labeler = value => value.ToString("N2"),
                Padding = new LiveChartsCore.Drawing.Padding(2),
            },
        };
        
        // this.PedalSections.Add(
        //     new RectangularSection {
        //         Xi = 0,
        //         Xj = 0,
        //         // Fill = new SolidColorPaint(new SKColor(255, 205, 210)),
        //         Stroke = new SolidColorPaint(new SKColor(255, 205, 210)) { StrokeThickness = 1 },
        //     });
    }

    private void setVisibilityOfLineSeries(LineSeries<ObservablePoint> series, bool visible) {
        series.IsVisible = visible ? true : false;
    }
    private void setVisibilityOfStepLineSeries(StepLineSeries<ObservablePoint> series, bool visible) {
        series.IsVisible = visible ? true : false;
    }

    private async Task setBestCharts() {
        if (this._bestReplayDetailsPerTime == null || this._bestReplayDetailsPerTime.Count == 0) {
            return;
        }

        var throttle = new List<ObservablePoint>();
        var brake = new List<ObservablePoint>();
        var clutch = new List<ObservablePoint>();
        var handbrake = new List<ObservablePoint>();
        var speed = new List<ObservablePoint>();
        var rpm = new List<ObservablePoint>();
        var g_lat = new List<ObservablePoint>();
        var g_long = new List<ObservablePoint>();
        var gear = new List<ObservablePoint>();
        var brake_temp_front_left = new List<ObservablePoint>();
        var brake_temp_front_right = new List<ObservablePoint>();
        var brake_temp_rear_left = new List<ObservablePoint>();
        var brake_temp_rear_right = new List<ObservablePoint>();
        var suspension_front_left = new List<ObservablePoint>();
        var suspension_front_right = new List<ObservablePoint>();
        var suspension_rear_left = new List<ObservablePoint>();
        var suspension_rear_right = new List<ObservablePoint>();
        var suspension_speed_front_left = new List<ObservablePoint>();
        var suspension_speed_front_right = new List<ObservablePoint>();
        var suspension_speed_rear_left = new List<ObservablePoint>();
        var suspension_speed_rear_right = new List<ObservablePoint>();
        for (int i = 0; i < this._bestReplayDetailsPerTime.Count; i++) {
            throttle.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].throttle));
            brake.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].brake));
            clutch.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].clutch));
            handbrake.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].handbrake));
            speed.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].speed));
            rpm.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].rpm));
            g_lat.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].g_lat));
            g_long.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].g_long));
            gear.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].gear));
            brake_temp_front_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].brake_temp_front_left));
            brake_temp_front_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].brake_temp_front_right));
            brake_temp_rear_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].brake_temp_rear_left));
            brake_temp_rear_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].brake_temp_rear_right));
            suspension_front_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_front_left));
            suspension_front_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_front_right));
            suspension_rear_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_rear_left));
            suspension_rear_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_rear_right));
            suspension_speed_front_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_speed_front_left));
            suspension_speed_front_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_speed_front_right));
            suspension_speed_rear_left.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_speed_rear_left));
            suspension_speed_rear_right.Add(new ObservablePoint(this._bestReplayDetailsPerTime[i].time, this._bestReplayDetailsPerTime[i].suspension_speed_rear_right));
        }
        var best_str = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.best", "en-us");
        var dashed_effect = new DashEffect(new float[] { 2, 2 }, 0);
        var throttleSeries = getLineSeries(throttle, SKColors.Green, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.throttle", "en-us")}", new SolidColorPaint(SKColors.Green) { StrokeThickness = 1, PathEffect = dashed_effect });
        var brakeSeries = getLineSeries(brake, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake", "en-us")}", new SolidColorPaint(SKColors.Red) { StrokeThickness = 1, PathEffect = dashed_effect });
        var clutchSeries = getLineSeries(clutch, SKColors.Blue, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.clutch", "en-us")}", new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1, PathEffect = dashed_effect });
        var handbrakeSeries = getLineSeries(handbrake, SKColors.Orange, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.handbrake", "en-us")}", new SolidColorPaint(SKColors.Orange) { StrokeThickness = 1, PathEffect = dashed_effect });
        var speedSeries = getLineSeries(speed, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.speed", "en-us")}");
        var rpmSeries = getLineSeries(rpm, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.rpm", "en-us")}");
        var g_latSeries = getLineSeries(g_lat, SKColors.Blue, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.g_lat", "en-us")}", new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1, PathEffect = dashed_effect });
        var g_longSeries = getLineSeries(g_long, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.g_long", "en-us")}", new SolidColorPaint(SKColors.Red) { StrokeThickness = 1, PathEffect = dashed_effect });
        var gearSeries = getStepLineSeries(gear, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.gear", "en-us")}");
        var brake_temp_front_leftSeries = getLineSeries(brake_temp_front_left, SKColors.Green, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_front_left", "en-us")}", new SolidColorPaint(SKColors.Green) { StrokeThickness = 1, PathEffect = dashed_effect });
        var brake_temp_front_rightSeries = getLineSeries(brake_temp_front_right, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_front_right", "en-us")}", new SolidColorPaint(SKColors.Red) { StrokeThickness = 1, PathEffect = dashed_effect });
        var brake_temp_rear_leftSeries = getLineSeries(brake_temp_rear_left, SKColors.Blue, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_rear_left", "en-us")}", new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1, PathEffect = dashed_effect });
        var brake_temp_rear_rightSeries = getLineSeries(brake_temp_rear_right, SKColors.Orange, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake_temp_rear_right", "en-us")}", new SolidColorPaint(SKColors.Orange) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_front_leftSeries = getLineSeries(suspension_front_left, SKColors.Green, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_front_left", "en-us")}", new SolidColorPaint(SKColors.Green) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_front_rightSeries = getLineSeries(suspension_front_right, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_front_right", "en-us")}", new SolidColorPaint(SKColors.Red) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_rear_leftSeries = getLineSeries(suspension_rear_left, SKColors.Blue, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_rear_left", "en-us")}", new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_rear_rightSeries = getLineSeries(suspension_rear_right, SKColors.Orange, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_rear_right", "en-us")}", new SolidColorPaint(SKColors.Orange) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_speed_front_leftSeries = getLineSeries(suspension_speed_front_left, SKColors.Green, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_front_left", "en-us")}", new SolidColorPaint(SKColors.Green) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_speed_front_rightSeries = getLineSeries(suspension_speed_front_right, SKColors.Red, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_front_right", "en-us")}", new SolidColorPaint(SKColors.Red) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_speed_rear_leftSeries = getLineSeries(suspension_speed_rear_left, SKColors.Blue, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_rear_left", "en-us")}", new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1, PathEffect = dashed_effect });
        var suspension_speed_rear_rightSeries = getLineSeries(suspension_speed_rear_right, SKColors.Orange, $"{best_str}-{I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.suspension_speed_rear_right", "en-us")}", new SolidColorPaint(SKColors.Orange) { StrokeThickness = 1, PathEffect = dashed_effect });

        // setVisibilityOfLineSeries(throttleSeries, true);
        // setVisibilityOfLineSeries(brakeSeries, true);
        // setVisibilityOfLineSeries(clutchSeries, true);
        // setVisibilityOfLineSeries(handbrakeSeries, true);
        // setVisibilityOfLineSeries(speedSeries, true);
        // setVisibilityOfLineSeries(rpmSeries, true);
        // setVisibilityOfLineSeries(g_latSeries, true);
        // setVisibilityOfLineSeries(g_longSeries, true);
        // setVisibilityOfStepLineSeries(gearSeries, true);
        // setVisibilityOfLineSeries(brake_temp_front_leftSeries, true);
        // setVisibilityOfLineSeries(brake_temp_front_rightSeries, true);
        // setVisibilityOfLineSeries(brake_temp_rear_leftSeries, true);
        // setVisibilityOfLineSeries(brake_temp_rear_rightSeries, true);
        // setVisibilityOfLineSeries(suspension_front_leftSeries, true);
        // setVisibilityOfLineSeries(suspension_front_rightSeries, true);
        // setVisibilityOfLineSeries(suspension_rear_leftSeries, true);
        // setVisibilityOfLineSeries(suspension_rear_rightSeries, true);
        // setVisibilityOfLineSeries(suspension_speed_front_leftSeries, true);
        // setVisibilityOfLineSeries(suspension_speed_front_rightSeries, true);
        // setVisibilityOfLineSeries(suspension_speed_rear_leftSeries, true);
        // setVisibilityOfLineSeries(suspension_speed_rear_rightSeries, true);

        this.PedalSeries.Add(throttleSeries);
        this.PedalSeries.Add(brakeSeries);
        this.PedalSeries.Add(clutchSeries);
        this.PedalSeries.Add(handbrakeSeries);
        this.SpeedSeries.Add(speedSeries);
        this.RpmSeries.Add(rpmSeries);
        this.GSeries.Add(g_latSeries);
        this.GSeries.Add(g_longSeries);
        this.GearSeries.Add(gearSeries);
        this.BrakeTempSeries.Add(brake_temp_front_leftSeries);
        this.BrakeTempSeries.Add(brake_temp_front_rightSeries);
        this.BrakeTempSeries.Add(brake_temp_rear_leftSeries);
        this.BrakeTempSeries.Add(brake_temp_rear_rightSeries);
        this.SuspensionSeries.Add(suspension_front_leftSeries);
        this.SuspensionSeries.Add(suspension_front_rightSeries);
        this.SuspensionSeries.Add(suspension_rear_leftSeries);
        this.SuspensionSeries.Add(suspension_rear_rightSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_front_leftSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_front_rightSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_rear_leftSeries);
        this.SuspensionSpeedSeries.Add(suspension_speed_rear_rightSeries);
    }
#endregion

}
