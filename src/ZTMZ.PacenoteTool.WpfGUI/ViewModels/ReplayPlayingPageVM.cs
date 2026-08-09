using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FFMpegCore;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public enum ReplayAnalysisMetric
{
    Inputs,
    Speed,
    Engine,
    Gear,
    GForce,
    BrakeTemperatures,
    SuspensionTravel,
    SuspensionVelocity,
    Position
}

public sealed class ReplayAnalysisMetricOption
{
    public ReplayAnalysisMetricOption(ReplayAnalysisMetric metric, string label)
    {
        Metric = metric;
        Label = label;
    }

    public ReplayAnalysisMetric Metric { get; }
    public string Label { get; }
}

public partial class ReplayPlayingPageVM : ObservableObject, INavigationAware
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool;
    private readonly Dictionary<ReplayAnalysisMetric, ReplayAnalysisPlotData> _analysisCache = new();

    private Replay? _replay;
    private Replay? _bestReplay;
    private List<ReplayDetailsPerTime>? _replayDetailsPerTime;
    private List<ReplayDetailsPerTime>? _bestReplayDetailsPerTime;
    private ReplayTimelineLookup? _replayTimelineLookup;
    private ReplayTimelineLookup? _bestReplayTimelineLookup;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _overviewCancellation;
    private string _temporaryAudioPath = string.Empty;
    private bool _hasTimestamp;
    private bool _isApplyingReplayLocked;
    private bool _isResetting;
    private int _analysisGeneration;

    public MediaElement? mediaElement;

    [ObservableProperty]
    private string _headerContent = string.Empty;

    [ObservableProperty]
    private int _replayId = -1;

    partial void OnReplayIdChanged(int value)
    {
        HeaderContent = $"Replay: {value}";
    }

    [ObservableProperty]
    private string _videoPath = string.Empty;

    [ObservableProperty]
    private double _playPosition;

    partial void OnPlayPositionChanged(double value)
    {
        UpdatePlaybackTelemetry(value);
    }

    [ObservableProperty]
    private double _videoLength;

    [ObservableProperty]
    private double _volume = 1;

    partial void OnVolumeChanged(double value)
    {
        if (mediaElement != null)
        {
            mediaElement.Volume = value;
        }
    }

    [ObservableProperty]
    private double _playSpeed = 1;

    partial void OnPlaySpeedChanged(double value)
    {
        if (mediaElement != null)
        {
            mediaElement.SpeedRatio = value;
        }
    }

    [ObservableProperty]
    private bool _isPaused = true;

    [ObservableProperty]
    private bool _isPausedBecauseOfSliding;

    [ObservableProperty]
    private bool _scrubbingEnabled;

    [ObservableProperty]
    private string _track = string.Empty;

    [ObservableProperty]
    private string _car = string.Empty;

    [ObservableProperty]
    private string _carClass = string.Empty;

    [ObservableProperty]
    private float _finishTime;

    [ObservableProperty]
    private double _lapTime;

    [ObservableProperty]
    private float _lapDistance;

    [ObservableProperty]
    private float _gotoLapDistance;

    partial void OnGotoLapDistanceChanged(float value)
    {
        if (_isResetting || _replayTimelineLookup == null || !IsPaused)
        {
            return;
        }

        var targetPosition = GetPlaybackPositionForDistance(value);
        SeekTo(targetPosition, false);
    }

    [ObservableProperty]
    private float _currentSpeed;

    [ObservableProperty]
    private float _currentGear;

    [ObservableProperty]
    private float _currentThrottlePercent;

    [ObservableProperty]
    private float _currentBrakePercent;

    [ObservableProperty]
    private IReadOnlyList<ReplayPlotPoint> _overviewThrottlePoints = Array.Empty<ReplayPlotPoint>();

    [ObservableProperty]
    private IReadOnlyList<ReplayPlotPoint> _overviewBrakePoints = Array.Empty<ReplayPlotPoint>();

    [ObservableProperty]
    private IReadOnlyList<ReplayPlotPoint> _overviewSpeedPoints = Array.Empty<ReplayPlotPoint>();

    [ObservableProperty]
    private float _overviewCursorDistance;

    [ObservableProperty]
    private float _overviewMaximumDistance;

    [ObservableProperty]
    private bool _replayLocked;

    partial void OnReplayLockedChanged(bool value)
    {
        if (_isApplyingReplayLocked || _replay == null)
        {
            return;
        }

        _ = SetReplayLockedFromUiAsync(value);
    }

    [ObservableProperty]
    private bool _hasBestReplay;

    [ObservableProperty]
    private bool _showBestReplayComparison;

    partial void OnShowBestReplayComparisonChanged(bool value)
    {
        if (SelectedViewIndex == 1)
        {
            _ = EnsureAnalysisChartAsync();
        }
    }

    [ObservableProperty]
    private int _selectedViewIndex = Config.Instance.ReplayShowDiagrams ? 1 : 0;

    partial void OnSelectedViewIndexChanged(int value)
    {
        if (_isResetting)
        {
            return;
        }

        var showAnalysis = value == 1;
        Config.Instance.ReplayShowDiagrams = showAnalysis;
        Config.Instance.SaveUserConfig();
        if (showAnalysis)
        {
            _ = EnsureAnalysisChartAsync();
        }
        else
        {
            ClearAnalysisChart();
        }
    }

    public ObservableCollection<ReplayAnalysisMetricOption> AnalysisMetrics { get; } = new();

    [ObservableProperty]
    private ReplayAnalysisMetricOption? _selectedAnalysisMetric;

    partial void OnSelectedAnalysisMetricChanged(ReplayAnalysisMetricOption? value)
    {
        if (value != null && SelectedViewIndex == 1)
        {
            _ = EnsureAnalysisChartAsync();
        }
    }

    [ObservableProperty]
    private IReadOnlyList<ReplayAnalysisSeries> _analysisSeries = Array.Empty<ReplayAnalysisSeries>();

    [ObservableProperty]
    private bool _analysisUsesInputScale;

    public ReplayPlayingPageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool)
    {
        _tool = tool;
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += TimerTicks;

        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.Inputs, I18NLoader.Instance["replay.analysisInputs"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.Speed, I18NLoader.Instance["replay.analysisSpeed"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.Engine, I18NLoader.Instance["replay.analysisEngine"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.Gear, I18NLoader.Instance["replay.analysisGear"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.GForce, I18NLoader.Instance["replay.analysisGForce"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.BrakeTemperatures, I18NLoader.Instance["replay.analysisBrakeTemperatures"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.SuspensionTravel, I18NLoader.Instance["replay.analysisSuspensionTravel"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.SuspensionVelocity, I18NLoader.Instance["replay.analysisSuspensionVelocity"]));
        AnalysisMetrics.Add(new ReplayAnalysisMetricOption(ReplayAnalysisMetric.Position, I18NLoader.Instance["replay.analysisPosition"]));
        SelectedAnalysisMetric = AnalysisMetrics.First();
    }

    public async Task OnNavigatedToAsync()
    {
        ResetPlaybackState();

        _replay = await ReplayManager.Instance.getReplay(ReplayId);
        if (_replay == null || !_replay.available)
        {
            HeaderContent = $"Replay: {ReplayId}";
            return;
        }

        _replayDetailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(ReplayId);
        if (_replayDetailsPerTime.Count == 0)
        {
            return;
        }

        _replayTimelineLookup = ReplayTimelineLookup.From(_replayDetailsPerTime);
        await LoadBestReplayAsync();
        InitializeReplayMetadata();
        await PrepareOverviewDataAsync();

        if (SelectedViewIndex == 1)
        {
            await EnsureAnalysisChartAsync();
        }

        await StartMediaAsync();
        _timer.Start();
    }

    public Task OnNavigatedFromAsync()
    {
        ResetPlaybackState();
        return Task.CompletedTask;
    }

    private async Task LoadBestReplayAsync()
    {
        if (_replay == null)
        {
            return;
        }

        _bestReplay = await ReplayManager.Instance.GetBestReplay(_tool.CurrentGame, _replay.track, _replay.car_class, _replay.car);
        if (_bestReplay == null || !_bestReplay.available || _bestReplay.id == _replay.id)
        {
            return;
        }

        _bestReplayDetailsPerTime = await ReplayManager.Instance.getReplayDetailsPerTime(_bestReplay.id);
        if (_bestReplayDetailsPerTime.Count == 0)
        {
            return;
        }

        _bestReplayTimelineLookup = ReplayTimelineLookup.From(_bestReplayDetailsPerTime);
        HasBestReplay = true;
    }

    private void InitializeReplayMetadata()
    {
        if (_replay == null || _replayDetailsPerTime == null)
        {
            return;
        }

        Track = _replay.track;
        Car = _replay.car;
        CarClass = _replay.car_class;
        FinishTime = _replay.finish_time;
        HeaderContent = $"{Track} - {Car}";

        _isApplyingReplayLocked = true;
        ReplayLocked = _replay.locked;
        _isApplyingReplayLocked = false;

        var firstTimestamped = _replayDetailsPerTime.FirstOrDefault(detail => detail.timestamp > 0);
        _hasTimestamp = firstTimestamped != null;
        if (_hasTimestamp && _replay.video_begin_timestamp == 0)
        {
            _replay.video_begin_timestamp = firstTimestamped!.timestamp;
        }
    }

    private async Task PrepareOverviewDataAsync()
    {
        if (_replayTimelineLookup == null)
        {
            return;
        }

        _overviewCancellation?.Cancel();
        _overviewCancellation?.Dispose();
        _overviewCancellation = new CancellationTokenSource();
        var cancellationToken = _overviewCancellation.Token;
        var details = _replayTimelineLookup.OrderedByDistance.ToList();
        var maximumDistance = Math.Max(_replay?.track_length ?? 0, details.LastOrDefault()?.distance ?? 0);

        ReplayOverviewData overview;
        try
        {
            overview = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ReplayOverviewData(
                    ReplayPlotSampler.DownsampleByDistance(details, detail => detail.throttle),
                    ReplayPlotSampler.DownsampleByDistance(details, detail => detail.brake),
                    ReplayPlotSampler.DownsampleByDistance(details, detail => detail.speed),
                    maximumDistance);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        OverviewThrottlePoints = overview.Throttle;
        OverviewBrakePoints = overview.Brake;
        OverviewSpeedPoints = overview.Speed;
        OverviewMaximumDistance = overview.MaximumDistance;
    }

    private async Task StartMediaAsync()
    {
        if (_replay == null || _replayDetailsPerTime == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_replay.video_path) && File.Exists(_replay.video_path))
        {
            ScrubbingEnabled = true;
            VideoPath = _replay.video_path;
        }
        else
        {
            ScrubbingEnabled = false;
            var duration = GetReplayDuration();
            _temporaryAudioPath = Path.Combine(Path.GetTempPath(), $"ztmz-replay-empty-{Guid.NewGuid():N}.mp3");
            GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
            await FFMpegArguments
                .FromFileInput("anullsrc=channel_layout=stereo:sample_rate=44100", false, options => options.WithCustomArgument($"-f lavfi -t {duration}"))
                .OutputToFile(_temporaryAudioPath, true)
                .ProcessAsynchronously();
            VideoPath = _temporaryAudioPath;
        }

        if (mediaElement != null)
        {
            mediaElement.SpeedRatio = PlaySpeed;
            mediaElement.Volume = Volume;
            mediaElement.Play();
            IsPaused = false;
        }
    }

    private double GetReplayDuration()
    {
        if (_replayDetailsPerTime == null || _replayDetailsPerTime.Count == 0)
        {
            return 0;
        }

        if (_hasTimestamp)
        {
            var first = _replayDetailsPerTime.First(detail => detail.timestamp > 0);
            var last = _replayDetailsPerTime.Last();
            return Math.Max(0, (last.timestamp - first.timestamp) / 10_000_000d + first.time);
        }

        return Math.Max(0, _replayDetailsPerTime.Last().time - _replayDetailsPerTime.First().time);
    }

    private void TimerTicks(object? sender, EventArgs args)
    {
        if (mediaElement == null || IsPaused)
        {
            return;
        }

        try
        {
            if (mediaElement.NaturalDuration.HasTimeSpan && VideoLength <= 0)
            {
                VideoLength = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
            }

            if (mediaElement.HasAudio || mediaElement.HasVideo)
            {
                PlayPosition = mediaElement.Position.TotalSeconds;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update replay position.");
        }
    }

    private void UpdatePlaybackTelemetry(double position)
    {
        if (_replayTimelineLookup == null || _replay == null)
        {
            return;
        }

        var replayTime = (float)Math.Max(0, position);
        ReplayDetailsPerTime? detail;
        if (_hasTimestamp)
        {
            var timestamp = (long)(replayTime * 10_000_000) + _replay.video_begin_timestamp;
            detail = _replayTimelineLookup.GetByTimestamp(timestamp);
        }
        else
        {
            detail = _replayTimelineLookup.GetByTime(replayTime);
        }

        if (detail == null)
        {
            return;
        }

        LapTime = detail.time;
        LapDistance = detail.distance;
        CurrentSpeed = detail.speed;
        CurrentGear = detail.gear;
        CurrentThrottlePercent = Math.Clamp(detail.throttle * 100, 0, 100);
        CurrentBrakePercent = Math.Clamp(detail.brake * 100, 0, 100);
        OverviewCursorDistance = detail.distance;
    }

    private double GetPlaybackPositionForDistance(float distance)
    {
        if (_replayTimelineLookup == null || _replay == null)
        {
            return 0;
        }

        if (_hasTimestamp)
        {
            var timestamp = _replayTimelineLookup.GetTimestampByDistance(distance);
            return Math.Max(0, (timestamp - _replay.video_begin_timestamp) / 10_000_000d);
        }

        return Math.Max(0, _replayTimelineLookup.GetTimeByDistance(distance));
    }

    [RelayCommand]
    private Task SlideStarted()
    {
        _timer.Stop();
        if (mediaElement != null && !IsPaused)
        {
            mediaElement.Pause();
            IsPausedBecauseOfSliding = true;
        }
        else
        {
            IsPausedBecauseOfSliding = false;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SlideEnded(double endPosition)
    {
        SeekTo(endPosition, IsPausedBecauseOfSliding);
        IsPausedBecauseOfSliding = false;
        _timer.Start();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SpaceKey()
    {
        if (mediaElement == null)
        {
            return Task.CompletedTask;
        }

        if (IsPaused)
        {
            mediaElement.Position = TimeSpan.FromSeconds(PlayPosition);
            mediaElement.Play();
            IsPaused = false;
        }
        else
        {
            mediaElement.Pause();
            IsPaused = true;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task LeftKey()
    {
        SeekTo(Math.Max(0, PlayPosition - 5), false);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RightKey()
    {
        SeekTo(Math.Min(VideoLength, PlayPosition + 5), false);
        return Task.CompletedTask;
    }

    private void SeekTo(double position, bool resume)
    {
        var target = Math.Max(0, position);
        if (VideoLength > 0)
        {
            target = Math.Min(target, VideoLength);
        }

        if (mediaElement != null)
        {
            mediaElement.Position = TimeSpan.FromSeconds(target);
            if (resume)
            {
                mediaElement.Play();
                IsPaused = false;
            }
            else if (IsPaused)
            {
                mediaElement.Play();
                mediaElement.Pause();
            }
        }

        PlayPosition = target;
    }

    private async Task SetReplayLockedFromUiAsync(bool value)
    {
        if (_replay == null)
        {
            return;
        }

        try
        {
            await ReplayManager.Instance.SetReplayLockedAsync(_replay.id, value);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update replay lock state.");
            _isApplyingReplayLocked = true;
            ReplayLocked = !value;
            _isApplyingReplayLocked = false;
        }
    }

    private async Task EnsureAnalysisChartAsync()
    {
        if (SelectedViewIndex != 1 || SelectedAnalysisMetric == null || _replayTimelineLookup == null)
        {
            return;
        }

        var metric = SelectedAnalysisMetric.Metric;
        if (!_analysisCache.TryGetValue(metric, out var plotData))
        {
            _analysisCancellation?.Cancel();
            _analysisCancellation?.Dispose();
            _analysisCancellation = new CancellationTokenSource();
            var cancellationToken = _analysisCancellation.Token;
            var generation = ++_analysisGeneration;
            var traces = GetAnalysisTraces(metric).ToList();
            var currentDetails = _replayTimelineLookup.OrderedByDistance.ToList();
            var bestDetails = _bestReplayTimelineLookup?.OrderedByDistance.ToList();

            try
            {
                plotData = await Task.Run(
                    () => CreateAnalysisPlotData(traces, currentDetails, bestDetails, cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested || generation != _analysisGeneration || SelectedViewIndex != 1 || SelectedAnalysisMetric?.Metric != metric)
            {
                return;
            }

            _analysisCache[metric] = plotData;
        }

        ApplyAnalysisChart(plotData);
    }

    private static ReplayAnalysisPlotData CreateAnalysisPlotData(
        IReadOnlyList<ReplayAnalysisTrace> traces,
        IReadOnlyList<ReplayDetailsPerTime> currentDetails,
        IReadOnlyList<ReplayDetailsPerTime>? bestDetails,
        CancellationToken cancellationToken)
    {
        var plots = new List<ReplayAnalysisTraceData>(traces.Count);
        foreach (var trace in traces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReplayPlotSampler.DownsampleByDistance(currentDetails, trace.ValueSelector);
            var best = bestDetails == null
                ? Array.Empty<ReplayPlotPoint>()
                : ReplayPlotSampler.DownsampleByDistance(bestDetails, trace.ValueSelector);
            plots.Add(new ReplayAnalysisTraceData(trace, current, best));
        }

        return new ReplayAnalysisPlotData(plots);
    }

    private void ApplyAnalysisChart(ReplayAnalysisPlotData plotData)
    {
        if (SelectedAnalysisMetric == null)
        {
            return;
        }

        AnalysisSeries = plotData.Traces
            .Where(traceData => traceData.Current.Count > 0)
            .Select(traceData => new ReplayAnalysisSeries(
                traceData.Trace.Label,
                traceData.Trace.Color,
                traceData.Trace.IsStep,
                traceData.Current,
                traceData.Best,
                I18NLoader.Instance["dashboard.current"],
                I18NLoader.Instance["dashboard.best"]))
            .ToArray();
        AnalysisUsesInputScale = SelectedAnalysisMetric.Metric == ReplayAnalysisMetric.Inputs;
    }

    private IEnumerable<ReplayAnalysisTrace> GetAnalysisTraces(ReplayAnalysisMetric metric)
    {
        var green = Colors.LimeGreen;
        var red = Colors.OrangeRed;
        var blue = Colors.DeepSkyBlue;
        var orange = Colors.Orange;
        var purple = Colors.MediumPurple;

        switch (metric)
        {
            case ReplayAnalysisMetric.Inputs:
                var inputs = new List<ReplayAnalysisTrace>
                {
                    new(detail => detail.throttle, green, I18NLoader.Instance["dashboard.throttle"]),
                    new(detail => detail.brake, red, I18NLoader.Instance["dashboard.brake"]),
                    new(detail => detail.clutch, blue, I18NLoader.Instance["dashboard.clutch"]),
                    new(detail => detail.handbrake, orange, I18NLoader.Instance["dashboard.handbrake"]),
                    new(detail => detail.steering, purple, I18NLoader.Instance["dashboard.steering"])
                };
                return FilterConfiguredInputs(inputs);
            case ReplayAnalysisMetric.Speed:
                return new[] { new ReplayAnalysisTrace(detail => detail.speed, blue, I18NLoader.Instance["dashboard.speed"]) };
            case ReplayAnalysisMetric.Engine:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.rpm, red, I18NLoader.Instance["dashboard.rpm"]),
                    new ReplayAnalysisTrace(detail => detail.max_rpm, Color.FromRgb(130, 130, 130), I18NLoader.Instance["replay.maximumRpm"])
                };
            case ReplayAnalysisMetric.Gear:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.gear, blue, I18NLoader.Instance["dashboard.gear"], true),
                    new ReplayAnalysisTrace(detail => detail.max_gears, Color.FromRgb(130, 130, 130), I18NLoader.Instance["replay.maximumGear"], true)
                };
            case ReplayAnalysisMetric.GForce:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.g_lat, blue, I18NLoader.Instance["dashboard.g_lat"]),
                    new ReplayAnalysisTrace(detail => detail.g_long, red, I18NLoader.Instance["dashboard.g_long"])
                };
            case ReplayAnalysisMetric.BrakeTemperatures:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.brake_temp_front_left, green, I18NLoader.Instance["dashboard.brake_temp_front_left"]),
                    new ReplayAnalysisTrace(detail => detail.brake_temp_front_right, red, I18NLoader.Instance["dashboard.brake_temp_front_right"]),
                    new ReplayAnalysisTrace(detail => detail.brake_temp_rear_left, blue, I18NLoader.Instance["dashboard.brake_temp_rear_left"]),
                    new ReplayAnalysisTrace(detail => detail.brake_temp_rear_right, orange, I18NLoader.Instance["dashboard.brake_temp_rear_right"])
                };
            case ReplayAnalysisMetric.SuspensionTravel:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.suspension_front_left, green, I18NLoader.Instance["dashboard.suspension_front_left"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_front_right, red, I18NLoader.Instance["dashboard.suspension_front_right"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_rear_left, blue, I18NLoader.Instance["dashboard.suspension_rear_left"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_rear_right, orange, I18NLoader.Instance["dashboard.suspension_rear_right"])
                };
            case ReplayAnalysisMetric.SuspensionVelocity:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.suspension_speed_front_left, green, I18NLoader.Instance["dashboard.suspension_speed_front_left"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_speed_front_right, red, I18NLoader.Instance["dashboard.suspension_speed_front_right"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_speed_rear_left, blue, I18NLoader.Instance["dashboard.suspension_speed_rear_left"]),
                    new ReplayAnalysisTrace(detail => detail.suspension_speed_rear_right, orange, I18NLoader.Instance["dashboard.suspension_speed_rear_right"])
                };
            case ReplayAnalysisMetric.Position:
                return new[]
                {
                    new ReplayAnalysisTrace(detail => detail.pos_x, red, I18NLoader.Instance["replay.positionX"]),
                    new ReplayAnalysisTrace(detail => detail.pos_y, green, I18NLoader.Instance["replay.positionY"]),
                    new ReplayAnalysisTrace(detail => detail.pos_z, blue, I18NLoader.Instance["replay.positionZ"])
                };
            default:
                return Array.Empty<ReplayAnalysisTrace>();
        }
    }

    private static IEnumerable<ReplayAnalysisTrace> FilterConfiguredInputs(IReadOnlyList<ReplayAnalysisTrace> inputs)
    {
        return Config.Instance.ReplayPlayPedalsMode switch
        {
            1 => new[] { inputs[0] },
            2 => new[] { inputs[1] },
            3 => new[] { inputs[2] },
            4 => new[] { inputs[3] },
            _ => inputs
        };
    }

    private void ClearAnalysisChart()
    {
        AnalysisSeries = Array.Empty<ReplayAnalysisSeries>();
        AnalysisUsesInputScale = false;
    }

    private void ResetPlaybackState()
    {
        _isResetting = true;
        _timer.Stop();
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
        _overviewCancellation?.Cancel();
        _overviewCancellation?.Dispose();
        _overviewCancellation = null;
        _analysisGeneration++;
        _analysisCache.Clear();

        try
        {
            mediaElement?.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to stop replay media element during reset.");
        }

        VideoPath = string.Empty;
        if (!string.IsNullOrEmpty(_temporaryAudioPath) && File.Exists(_temporaryAudioPath))
        {
            try
            {
                File.Delete(_temporaryAudioPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete temporary replay audio: {0}", _temporaryAudioPath);
            }
        }

        _temporaryAudioPath = string.Empty;
        _replay = null;
        _bestReplay = null;
        _replayDetailsPerTime = null;
        _bestReplayDetailsPerTime = null;
        _replayTimelineLookup = null;
        _bestReplayTimelineLookup = null;
        _hasTimestamp = false;
        HasBestReplay = false;
        ShowBestReplayComparison = false;
        ScrubbingEnabled = false;
        IsPaused = true;
        IsPausedBecauseOfSliding = false;
        PlayPosition = 0;
        VideoLength = 0;
        LapTime = 0;
        LapDistance = 0;
        GotoLapDistance = 0;
        Track = string.Empty;
        Car = string.Empty;
        CarClass = string.Empty;
        FinishTime = 0;
        CurrentSpeed = 0;
        CurrentGear = 0;
        CurrentThrottlePercent = 0;
        CurrentBrakePercent = 0;
        OverviewThrottlePoints = Array.Empty<ReplayPlotPoint>();
        OverviewBrakePoints = Array.Empty<ReplayPlotPoint>();
        OverviewSpeedPoints = Array.Empty<ReplayPlotPoint>();
        OverviewCursorDistance = 0;
        OverviewMaximumDistance = 0;
        ClearAnalysisChart();
        _isResetting = false;
    }

    private sealed record ReplayAnalysisTrace(
        Func<ReplayDetailsPerTime, float> ValueSelector,
        Color Color,
        string Label,
        bool IsStep = false);

    private sealed record ReplayAnalysisTraceData(
        ReplayAnalysisTrace Trace,
        IReadOnlyList<ReplayPlotPoint> Current,
        IReadOnlyList<ReplayPlotPoint> Best);

    private sealed record ReplayAnalysisPlotData(IReadOnlyList<ReplayAnalysisTraceData> Traces);

    private sealed record ReplayOverviewData(
        IReadOnlyList<ReplayPlotPoint> Throttle,
        IReadOnlyList<ReplayPlotPoint> Brake,
        IReadOnlyList<ReplayPlotPoint> Speed,
        float MaximumDistance);
}

public sealed record ReplayAnalysisSeries(
    string Label,
    Color Color,
    bool IsStep,
    IReadOnlyList<ReplayPlotPoint> Current,
    IReadOnlyList<ReplayPlotPoint> Best,
    string CurrentLabel,
    string BestLabel);
