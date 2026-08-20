using System;
using System.Collections.Generic;
using System.IO;
using GameOverlay.Drawing.CustomFont;
using SharpDX.DirectWrite;

namespace ZTMZ.PacenoteTool.UI.Windows;

internal readonly record struct WebDashboardTextLayout(
    float Width,
    float Height,
    float Baseline,
    float LineHeight);

/// <summary>
/// Measures proxy text with the same DirectWrite layout engine used by the native overlay.
/// </summary>
internal sealed class WebDashboardTextMetrics : IDisposable
{
    private readonly Factory _fontFactory = new();
    private readonly Dictionary<FontKey, TextFormat> _formats = new();
    private readonly CustomFontLoader _customFontLoader;
    private readonly FontCollection _customFontCollection;
    private bool _disposed;

    public WebDashboardTextMetrics(string customFontDirectory)
    {
        if (string.IsNullOrWhiteSpace(customFontDirectory)
            || !Directory.Exists(customFontDirectory)
            || Directory.GetFiles(customFontDirectory, "*.ttf").Length == 0)
        {
            return;
        }

        _customFontLoader = new CustomFontLoader(_fontFactory, customFontDirectory);
        _customFontCollection = new FontCollection(
            _fontFactory,
            _customFontLoader,
            _customFontLoader.Key);
    }

    public WebDashboardTextLayout Measure(
        string fontName,
        float fontSize,
        bool bold,
        bool italic,
        bool useCustomFont,
        string text,
        float layoutWidth,
        float layoutHeight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new WebDashboardTextLayout(0, 0, fontSize * 0.8f, fontSize * 1.2f);
        }

        if (_disposed)
        {
            return CreateFallback(text, fontSize);
        }

        try
        {
            var textFormat = GetOrCreateTextFormat(
                fontName,
                fontSize,
                bold,
                italic,
                useCustomFont);
            using var layout = new TextLayout(
                _fontFactory,
                text,
                textFormat,
                Math.Max(1, layoutWidth),
                Math.Max(1, layoutHeight));
            var metrics = layout.Metrics;
            var lineMetrics = layout.GetLineMetrics();
            var baseline = lineMetrics.Length > 0 ? lineMetrics[0].Baseline : fontSize * 0.8f;
            var lineHeight = lineMetrics.Length > 0 ? lineMetrics[0].Height : fontSize * 1.2f;

            return new WebDashboardTextLayout(
                metrics.Width,
                metrics.Height,
                baseline,
                lineHeight);
        }
        catch
        {
            return CreateFallback(text, fontSize);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var textFormat in _formats.Values)
        {
            textFormat.Dispose();
        }

        _formats.Clear();
        _customFontCollection?.Dispose();
        _customFontLoader?.Dispose();
        _fontFactory.Dispose();
        _disposed = true;
    }

    private TextFormat GetOrCreateTextFormat(
        string fontName,
        float fontSize,
        bool bold,
        bool italic,
        bool useCustomFont)
    {
        var key = new FontKey(fontName, fontSize, bold, italic, useCustomFont);
        if (_formats.TryGetValue(key, out var textFormat))
        {
            return textFormat;
        }

        var fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
        var fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
        textFormat = useCustomFont && _customFontCollection is not null
            ? new TextFormat(
                _fontFactory,
                fontName,
                _customFontCollection,
                fontWeight,
                fontStyle,
                FontStretch.Normal,
                fontSize)
            : new TextFormat(_fontFactory, fontName, fontWeight, fontStyle, fontSize);
        textFormat.WordWrapping = WordWrapping.NoWrap;
        _formats.Add(key, textFormat);
        return textFormat;
    }

    private static WebDashboardTextLayout CreateFallback(string text, float fontSize)
    {
        var lines = text.Split('\n');
        var maximumWidth = 0f;
        foreach (var line in lines)
        {
            maximumWidth = Math.Max(maximumWidth, line.Length * fontSize * 0.6f);
        }

        return new WebDashboardTextLayout(
            maximumWidth,
            lines.Length * fontSize * 1.2f,
            fontSize * 0.8f,
            fontSize * 1.2f);
    }

    private readonly record struct FontKey(
        string Name,
        float Size,
        bool Bold,
        bool Italic,
        bool UseCustomFont);
}
