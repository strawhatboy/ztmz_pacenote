using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Controls;

/// <summary>
/// Draws cached replay traces and a separate cursor visual. Playback only redraws the cursor.
/// </summary>
public sealed class ReplayOverviewControl : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _tracesVisual = new();
    private readonly DrawingVisual _cursorVisual = new();

    public static readonly DependencyProperty ThrottlePointsProperty = DependencyProperty.Register(
        nameof(ThrottlePoints),
        typeof(IReadOnlyList<ReplayPlotPoint>),
        typeof(ReplayOverviewControl),
        new FrameworkPropertyMetadata(null, OnTraceDataChanged));

    public static readonly DependencyProperty BrakePointsProperty = DependencyProperty.Register(
        nameof(BrakePoints),
        typeof(IReadOnlyList<ReplayPlotPoint>),
        typeof(ReplayOverviewControl),
        new FrameworkPropertyMetadata(null, OnTraceDataChanged));

    public static readonly DependencyProperty SpeedPointsProperty = DependencyProperty.Register(
        nameof(SpeedPoints),
        typeof(IReadOnlyList<ReplayPlotPoint>),
        typeof(ReplayOverviewControl),
        new FrameworkPropertyMetadata(null, OnTraceDataChanged));

    public static readonly DependencyProperty CursorDistanceProperty = DependencyProperty.Register(
        nameof(CursorDistance),
        typeof(float),
        typeof(ReplayOverviewControl),
        new FrameworkPropertyMetadata(0f, OnCursorChanged));

    public static readonly DependencyProperty MaximumDistanceProperty = DependencyProperty.Register(
        nameof(MaximumDistance),
        typeof(float),
        typeof(ReplayOverviewControl),
        new FrameworkPropertyMetadata(0f, OnTraceDataChanged));

    public IReadOnlyList<ReplayPlotPoint> ThrottlePoints
    {
        get => (IReadOnlyList<ReplayPlotPoint>)GetValue(ThrottlePointsProperty);
        set => SetValue(ThrottlePointsProperty, value);
    }

    public IReadOnlyList<ReplayPlotPoint> BrakePoints
    {
        get => (IReadOnlyList<ReplayPlotPoint>)GetValue(BrakePointsProperty);
        set => SetValue(BrakePointsProperty, value);
    }

    public IReadOnlyList<ReplayPlotPoint> SpeedPoints
    {
        get => (IReadOnlyList<ReplayPlotPoint>)GetValue(SpeedPointsProperty);
        set => SetValue(SpeedPointsProperty, value);
    }

    public float CursorDistance
    {
        get => (float)GetValue(CursorDistanceProperty);
        set => SetValue(CursorDistanceProperty, value);
    }

    public float MaximumDistance
    {
        get => (float)GetValue(MaximumDistanceProperty);
        set => SetValue(MaximumDistanceProperty, value);
    }

    public ReplayOverviewControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _visuals = new VisualCollection(this)
        {
            _tracesVisual,
            _cursorVisual
        };
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index)
    {
        return _visuals[index];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Math.Min(availableSize.Width, 640), 112);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RedrawTraces();
        RedrawCursor();
    }

    private static void OnTraceDataChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (ReplayOverviewControl)dependencyObject;
        control.RedrawTraces();
        control.RedrawCursor();
    }

    private static void OnCursorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((ReplayOverviewControl)dependencyObject).RedrawCursor();
    }

    private void RedrawTraces()
    {
        using var context = _tracesVisual.RenderOpen();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var split = Math.Round(ActualHeight * 0.56);
        var dividerPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)), 1);
        dividerPen.Freeze();
        context.DrawLine(dividerPen, new Point(0, split), new Point(ActualWidth, split));

        DrawInputTrace(context, ThrottlePoints, new SolidColorBrush(Color.FromRgb(46, 204, 113)), 0, split);
        DrawInputTrace(context, BrakePoints, new SolidColorBrush(Color.FromRgb(231, 76, 60)), 0, split);
        DrawSpeedTrace(context, SpeedPoints, new SolidColorBrush(Color.FromRgb(52, 152, 219)), split, ActualHeight - split);
    }

    private void RedrawCursor()
    {
        using var context = _cursorVisual.RenderOpen();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var position = ReplayPlotSampler.NormalizeDistance(CursorDistance, MaximumDistance);
        var x = Math.Round(position * ActualWidth);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 1.5);
        cursorPen.Freeze();
        context.DrawLine(cursorPen, new Point(x, 0), new Point(x, ActualHeight));
    }

    private void DrawInputTrace(DrawingContext context, IReadOnlyList<ReplayPlotPoint> points, Brush brush, double top, double height)
    {
        DrawTrace(context, points, brush, top, height, 0, 1);
    }

    private void DrawSpeedTrace(DrawingContext context, IReadOnlyList<ReplayPlotPoint> points, Brush brush, double top, double height)
    {
        if (points == null || points.Count == 0)
        {
            return;
        }

        var maximum = Math.Max(1, points.Max(point => point.Value));
        DrawTrace(context, points, brush, top, height, 0, maximum);
    }

    private void DrawTrace(
        DrawingContext context,
        IReadOnlyList<ReplayPlotPoint> points,
        Brush brush,
        double top,
        double height,
        double minimumValue,
        double maximumValue)
    {
        if (points == null || points.Count == 0 || MaximumDistance <= 0 || height <= 0)
        {
            return;
        }

        var range = Math.Max(double.Epsilon, maximumValue - minimumValue);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var started = false;
            foreach (var point in points)
            {
                var x = ReplayPlotSampler.NormalizeDistance(point.Distance, MaximumDistance) * ActualWidth;
                var normalizedValue = Math.Clamp((point.Value - minimumValue) / range, 0, 1);
                var y = top + (1 - normalizedValue) * height;
                var location = new Point(x, y);
                if (!started)
                {
                    geometryContext.BeginFigure(location, false, false);
                    started = true;
                }
                else
                {
                    geometryContext.LineTo(location, true, false);
                }
            }
        }

        geometry.Freeze();
        var pen = new Pen(brush, 1.25);
        pen.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }
}
