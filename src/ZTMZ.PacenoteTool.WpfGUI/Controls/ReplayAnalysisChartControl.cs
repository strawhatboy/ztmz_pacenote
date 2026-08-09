using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Controls;

/// <summary>Renders replay analysis traces without a charting-library dependency.</summary>
public sealed class ReplayAnalysisChartControl : FrameworkElement
{
    private const double LeftMargin = 54;
    private const double RightMargin = 14;
    private const double BottomMargin = 28;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _chartVisual = new();

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IReadOnlyList<ReplayAnalysisSeries>),
        typeof(ReplayAnalysisChartControl),
        new FrameworkPropertyMetadata(null, OnChartDataChanged));

    public static readonly DependencyProperty ShowBestReplayProperty = DependencyProperty.Register(
        nameof(ShowBestReplay),
        typeof(bool),
        typeof(ReplayAnalysisChartControl),
        new FrameworkPropertyMetadata(false, OnChartDataChanged));

    public static readonly DependencyProperty UsesInputScaleProperty = DependencyProperty.Register(
        nameof(UsesInputScale),
        typeof(bool),
        typeof(ReplayAnalysisChartControl),
        new FrameworkPropertyMetadata(false, OnChartDataChanged));

    public IReadOnlyList<ReplayAnalysisSeries> Series
    {
        get => (IReadOnlyList<ReplayAnalysisSeries>)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public bool ShowBestReplay
    {
        get => (bool)GetValue(ShowBestReplayProperty);
        set => SetValue(ShowBestReplayProperty, value);
    }

    public bool UsesInputScale
    {
        get => (bool)GetValue(UsesInputScaleProperty);
        set => SetValue(UsesInputScaleProperty, value);
    }

    public ReplayAnalysisChartControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _visuals = new VisualCollection(this) { _chartVisual };
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Math.Min(availableSize.Width, 960), Math.Max(260, availableSize.Height));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }

    private static void OnChartDataChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((ReplayAnalysisChartControl)dependencyObject).Redraw();
    }

    private void Redraw()
    {
        using var context = _chartVisual.RenderOpen();
        if (ActualWidth <= LeftMargin + RightMargin || ActualHeight <= BottomMargin)
        {
            return;
        }

        var series = Series ?? Array.Empty<ReplayAnalysisSeries>();
        var legendHeight = DrawLegend(context, series);
        var plot = new Rect(
            LeftMargin,
            legendHeight + 12,
            Math.Max(1, ActualWidth - LeftMargin - RightMargin),
            Math.Max(1, ActualHeight - legendHeight - BottomMargin - 12));

        var maximumDistance = GetMaximumDistance(series);
        var valueRange = GetValueRange(series);
        DrawGrid(context, plot, maximumDistance, valueRange.Minimum, valueRange.Maximum);

        foreach (var trace in series)
        {
            DrawTrace(context, plot, trace.Current, trace.Color, trace.IsStep, false, maximumDistance, valueRange.Minimum, valueRange.Maximum);
            if (ShowBestReplay)
            {
                DrawTrace(context, plot, trace.Best, trace.Color, trace.IsStep, true, maximumDistance, valueRange.Minimum, valueRange.Maximum);
            }
        }
    }

    private double DrawLegend(DrawingContext context, IReadOnlyList<ReplayAnalysisSeries> series)
    {
        if (series.Count == 0)
        {
            return 0;
        }

        const double lineHeight = 20;
        var x = LeftMargin;
        var y = 2d;
        foreach (var trace in series)
        {
            DrawLegendEntry(context, trace.CurrentLabel + " - " + trace.Label, trace.Color, false, ref x, ref y, lineHeight);
            if (ShowBestReplay && trace.Best.Count > 0)
            {
                DrawLegendEntry(context, trace.BestLabel + " - " + trace.Label, trace.Color, true, ref x, ref y, lineHeight);
            }
        }

        return y + lineHeight;
    }

    private void DrawLegendEntry(DrawingContext context, string label, Color color, bool dashed, ref double x, ref double y, double lineHeight)
    {
        var text = CreateText(label, Brushes.Gray, 12);
        var requiredWidth = 22 + text.Width + 18;
        if (x + requiredWidth > ActualWidth && x > LeftMargin)
        {
            x = LeftMargin;
            y += lineHeight;
        }

        var pen = CreatePen(color, dashed);
        context.DrawLine(pen, new Point(x, y + lineHeight / 2), new Point(x + 16, y + lineHeight / 2));
        context.DrawText(text, new Point(x + 21, y + 2));
        x += requiredWidth;
    }

    private void DrawGrid(DrawingContext context, Rect plot, double maximumDistance, double minimumValue, double maximumValue)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
        gridBrush.Freeze();
        var gridPen = new Pen(gridBrush, 1);
        gridPen.Freeze();
        var axisPen = new Pen(Brushes.Gray, 1);
        axisPen.Freeze();

        context.DrawRectangle(null, axisPen, plot);
        for (var index = 0; index <= 4; index++)
        {
            var fraction = index / 4d;
            var y = plot.Bottom - plot.Height * fraction;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = minimumValue + (maximumValue - minimumValue) * fraction;
            var label = CreateText(value.ToString("0.##", CultureInfo.CurrentCulture), Brushes.Gray, 11);
            context.DrawText(label, new Point(Math.Max(0, plot.Left - label.Width - 7), y - label.Height / 2));

            if (index < 4)
            {
                var x = plot.Left + plot.Width * fraction;
                context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var distance = maximumDistance * fraction;
                var distanceLabel = CreateText(distance.ToString("0", CultureInfo.CurrentCulture) + " m", Brushes.Gray, 11);
                context.DrawText(distanceLabel, new Point(x, plot.Bottom + 5));
            }
        }
    }

    private double GetMaximumDistance(IReadOnlyList<ReplayAnalysisSeries> series)
    {
        var maximum = series
            .SelectMany(trace => ShowBestPoints(trace))
            .Where(point => float.IsFinite(point.Distance))
            .Select(point => (double)point.Distance)
            .DefaultIfEmpty(1d)
            .Max();
        return Math.Max(1d, maximum);

        IEnumerable<ReplayPlotPoint> ShowBestPoints(ReplayAnalysisSeries trace)
        {
            return ShowBestReplay ? trace.Current.Concat(trace.Best) : trace.Current;
        }
    }

    private (double Minimum, double Maximum) GetValueRange(IReadOnlyList<ReplayAnalysisSeries> series)
    {
        if (UsesInputScale)
        {
            return (-1d, 1d);
        }

        var values = series
            .SelectMany(trace => ShowBestReplay ? trace.Current.Concat(trace.Best) : trace.Current)
            .Where(point => float.IsFinite(point.Value))
            .Select(point => (double)point.Value)
            .ToList();
        if (values.Count == 0)
        {
            return (0d, 1d);
        }

        var minimum = values.Min();
        var maximum = values.Max();
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            var padding = Math.Max(1d, Math.Abs(maximum) * 0.1d);
            return (minimum - padding, maximum + padding);
        }

        var rangePadding = (maximum - minimum) * 0.05d;
        return (minimum - rangePadding, maximum + rangePadding);
    }

    private static void DrawTrace(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<ReplayPlotPoint> points,
        Color color,
        bool isStep,
        bool dashed,
        double maximumDistance,
        double minimumValue,
        double maximumValue)
    {
        if (points == null || points.Count == 0)
        {
            return;
        }

        var valueRange = Math.Max(double.Epsilon, maximumValue - minimumValue);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var started = false;
            var previous = new Point();
            foreach (var point in points)
            {
                if (!float.IsFinite(point.Distance) || !float.IsFinite(point.Value))
                {
                    continue;
                }

                var x = plot.Left + Math.Clamp(point.Distance / maximumDistance, 0, 1) * plot.Width;
                var y = plot.Bottom - Math.Clamp((point.Value - minimumValue) / valueRange, 0, 1) * plot.Height;
                var current = new Point(x, y);
                if (!started)
                {
                    geometryContext.BeginFigure(current, false, false);
                    started = true;
                }
                else if (isStep)
                {
                    geometryContext.LineTo(new Point(current.X, previous.Y), true, false);
                    geometryContext.LineTo(current, true, false);
                }
                else
                {
                    geometryContext.LineTo(current, true, false);
                }

                previous = current;
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, CreatePen(color, dashed), geometry);
    }

    private static Pen CreatePen(Color color, bool dashed)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, 1.25);
        if (dashed)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        pen.Freeze();
        return pen;
    }

    private FormattedText CreateText(string text, Brush brush, double size)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
