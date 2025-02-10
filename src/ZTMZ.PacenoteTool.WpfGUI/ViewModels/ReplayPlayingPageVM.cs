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
    partial void OnShowBestReplayComparisonChanged(bool value){
        if (value) {
            
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
            this.PedalSections.First().Xi = rdp.time;
            this.PedalSections.First().Xj = rdp.time + this._replayDetailsPerTime.Last().time / 400;

            // update labels
            this.PedalLabel = $"{I18NLoader.Instance["dashboard.throttle"]}: {rdp.throttle * 100:0.0}%\t{I18NLoader.Instance["dashboard.brake"]}: {rdp.brake * 100:0.0}%\t{I18NLoader.Instance["dashboard.clutch"]}: {rdp.clutch * 100:0.0}%\t{I18NLoader.Instance["dashboard.handbrake"]}: {rdp.handbrake * 100:0.0}%";
            this.SpeedLabel = $"{I18NLoader.Instance["dashboard.speed"]}: {rdp.speed:0.0} km/h";
            this.RpmLabel = $"{I18NLoader.Instance["dashboard.rpm"]}: {rdp.rpm:0.0}";
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
            
            await Task.Delay(1000);
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
    private Margin _drawMargin = new Margin(10);

    [ObservableProperty]
    private List<ISeries> _pedalSeries = new();

    [ObservableProperty]
    private List<ISeries> _speedSeries = new();

    [ObservableProperty]
    private List<ISeries> _RpmSeries = new();

    [ObservableProperty]
    private List<ICartesianAxis> _pedalXAxis = new();

    [ObservableProperty]
    private ObservableCollection<RectangularSection> _pedalSections = new();

    private async Task setCharts() {
        await setPedals();
    }

    private async Task setPedals() {
        this.PedalSeries = new List<ISeries>();
        var throttle = new List<ObservablePoint>();
        var brake = new List<ObservablePoint>();
        var clutch = new List<ObservablePoint>();
        var handbrake = new List<ObservablePoint>();
        var speed = new List<ObservablePoint>();
        var rpm = new List<ObservablePoint>();
        for (int i = 0; i < this._replayDetailsPerTime.Count; i++) {
            throttle.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].throttle));
            brake.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].brake));
            clutch.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].clutch));
            handbrake.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].handbrake));
            speed.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].speed));
            rpm.Add(new ObservablePoint(this._replayDetailsPerTime[i].time, this._replayDetailsPerTime[i].rpm));
        }
        var throttleSeries = new LineSeries<ObservablePoint>(throttle) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.throttle", "en-us"),
        };
        var brakeSeries = new LineSeries<ObservablePoint>(brake) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.brake", "en-us"),
        };
        var clutchSeries = new LineSeries<ObservablePoint>(clutch) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.clutch", "en-us"),
        };
        var handbrakeSeries = new LineSeries<ObservablePoint>(handbrake) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Yellow) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.handbrake", "en-us"),
        };
        var speedSeries = new LineSeries<ObservablePoint>(speed) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.speed", "en-us"),
        };
        var rpmSeries = new LineSeries<ObservablePoint>(rpm) {
            GeometryFill = null,
            GeometryStroke = null,
            Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
            Fill = null,
            Name = I18NLoader.Instance.ResolveByKeyAndCulture("dashboard.rpm", "en-us"),
        };

        this.PedalSeries.Clear();
        this.SpeedSeries.Clear();
        this.RpmSeries.Clear();
        this.PedalSections.Clear();

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

        this.PedalXAxis = new List<ICartesianAxis> {
            new Axis{
                CrosshairLabelsBackground = SKColors.DarkOrange.AsLvcColor(),
                CrosshairLabelsPaint = new SolidColorPaint(SKColors.DarkRed),
                CrosshairPaint = new SolidColorPaint(SKColors.DarkOrange, 1),
                Labeler = value => value.ToString("N2"),
                Padding = new LiveChartsCore.Drawing.Padding(2),
            },
        };
        
        this.PedalSections.Add(
            new RectangularSection {
                Xi = 0,
                Xj = this._replayDetailsPerTime.Last().time / 400,
                Fill = new SolidColorPaint(new SKColor(255, 205, 210))
            });
    }
#endregion

}
