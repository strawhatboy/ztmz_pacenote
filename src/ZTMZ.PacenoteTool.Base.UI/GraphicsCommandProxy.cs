using System;
using System.Collections.Generic;
using System.IO;
using GameOverlay.Drawing;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// A proxy object that mimics GameOverlay.Drawing.Graphics interface for Lua scripts
/// but captures drawing commands instead of actually drawing
/// This allows us to record what dashboards are rendering without needing the real Graphics object
/// </summary>
public class GraphicsCommandProxy : IDisposable
{
    private readonly DrawingCommandCapture _capture;
    private readonly Dictionary<object, SolidBrush> _brushRegistry = new();
    private readonly Dictionary<object, Font> _fontRegistry = new();
    private readonly WebDashboardTextMetrics _textMetrics;
    
    public int Width => _capture.Width;
    public int Height => _capture.Height;
    public float FPS { get; set; } = Config.WebDashboardFrameRateDefault;
    
    public GraphicsCommandProxy(DrawingCommandCapture capture, string customFontDirectory = null)
    {
        _capture = capture;
        try
        {
            _textMetrics = new WebDashboardTextMetrics(customFontDirectory);
            _capture.TextMetrics = _textMetrics;
        }
        catch
        {
            _textMetrics = null;
        }
    }
    
    // ============================================================================
    // Brush Creation (returns proxy brush objects)
    // ============================================================================
    
    public object CreateSolidBrush(int r, int g, int b, int a = 255)
    {
        // Create a fake brush object that stores color info
        var brush = new ProxyBrush { R = r, G = g, B = b, A = a };
        
        // For command capture, we just need the color
        _brushRegistry[brush] = null; // We'll convert to uint when needed
        
        return brush;
    }
    
    public object CreateLinearGradientBrush(params object[] colors)
    {
        var gradient = new ProxyLinearGradientBrush();
        foreach (var color in colors)
        {
            if (color is ProxyBrush brush)
            {
                gradient.Colors.Add(new ProxyColor { R = brush.R, G = brush.G, B = brush.B, A = brush.A });
            }
            else if (color is Color c)
            {
                // Handle GameOverlay.Drawing.Color
                gradient.Colors.Add(new ProxyColor 
                { 
                    R = (int)(c.R * 255), 
                    G = (int)(c.G * 255), 
                    B = (int)(c.B * 255), 
                    A = (int)(c.A * 255) 
                });
            }
        }
        return gradient;
    }
    
    public object CreateRadialGradientBrush(params object[] colors)
    {
        var gradient = new ProxyRadialGradientBrush();
        foreach (var color in colors)
        {
            if (color is ProxyBrush brush)
            {
                gradient.Colors.Add(new ProxyColor { R = brush.R, G = brush.G, B = brush.B, A = brush.A });
            }
            else if (color is Color c)
            {
                // Handle GameOverlay.Drawing.Color
                gradient.Colors.Add(new ProxyColor 
                { 
                    R = (int)(c.R * 255), 
                    G = (int)(c.G * 255), 
                    B = (int)(c.B * 255), 
                    A = (int)(c.A * 255) 
                });
            }
        }
        return gradient;
    }
    
    // ============================================================================
    // Font Creation
    // ============================================================================
    
    public object CreateFont(string fontName, float fontSize, bool bold = false, bool italic = false)
    {
        return CreateProxyFont(fontName, fontSize, bold, italic, false);
    }

    private object CreateProxyFont(string fontName, float fontSize, bool bold, bool italic, bool useCustomFont)
    {
        var font = new ProxyFont
        {
            Name = fontName,
            Size = fontSize,
            Bold = bold,
            Italic = italic,
            UseCustomFont = useCustomFont
        };
        _fontRegistry[font] = null;
        return font;
    }
    
    public object CreateCustomFont(string fontName, float fontSize, bool bold = false, bool italic = false)
    {
        return CreateProxyFont(fontName, fontSize, bold, italic, true);
    }
    
    // ============================================================================
    // Geometry Creation
    // ============================================================================
    
    public object CreateGeometry()
    {
        return new ProxyGeometry();
    }

    public ProxyImage CreateImage(string key, string path)
    {
        return new ProxyImage(key, path);
    }
    
    // ============================================================================
    // Drawing Commands
    // ============================================================================
    
    public void ClearScene(object brush = null)
    {
        _capture.Clear(brush);
    }

    /// <summary>
    /// Starts a web-only retained drawing layer. Lua dashboards call this only when
    /// DashboardScriptArguments.IsWebDashboard is true, so native overlays are unchanged.
    /// </summary>
    public void BeginStaticLayer()
    {
        _capture.BeginStaticLayer();
    }

    public void EndStaticLayer()
    {
        _capture.EndStaticLayer();
    }

    internal void BeginDashboardCapture(string dashboardId, bool staticLayerRefreshRequested)
    {
        _capture.BeginDashboardCapture(dashboardId, staticLayerRefreshRequested);
    }

    internal void EndDashboardCapture()
    {
        _capture.EndDashboardCapture();
    }
    
    public void DrawLine(object brush, float x1, float y1, float x2, float y2, float thickness)
    {
        try
        {
            _capture.DrawLine(x1, y1, x2, y2, brush, thickness);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DrawLine error: {ex.Message}");
            throw;
        }
    }
    
    public void DrawLine(object brush, object point1, object point2, float thickness)
    {
        try
        {
            // Handle Point objects
            var p1 = ExtractPoint(point1);
            var p2 = ExtractPoint(point2);
            DrawLine(brush, p1.X, p1.Y, p2.X, p2.Y, thickness);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DrawLine(points) error: {ex.Message}");
            throw;
        }
    }
    
    public void DrawRectangle(object brush, float left, float top, float right, float bottom, float stroke)
    {
        _capture.DrawRectangle(brush, left, top, right, bottom, stroke);
    }
    
    public void DrawRectangle(object brush, object rectangle, float stroke)
    {
        var rect = ExtractRectangle(rectangle);
        DrawRectangle(brush, rect.Left, rect.Top, rect.Right, rect.Bottom, stroke);
    }

    public void DrawRoundedRectangle(object brush, float left, float top, float right, float bottom, float radius, float stroke)
    {
        _capture.DrawRoundedRectangle(brush, left, top, right, bottom, radius, stroke);
    }
    
    public void FillRectangle(object brush, float left, float top, float right, float bottom)
    {
        _capture.FillRectangle(brush, left, top, right, bottom);
    }
    
    public void FillRectangle(object brush, object rectangle)
    {
        var rect = ExtractRectangle(rectangle);
        FillRectangle(brush, rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public void FillRoundedRectangle(object brush, float left, float top, float right, float bottom, float radius)
    {
        _capture.FillRoundedRectangle(brush, left, top, right, bottom, radius);
    }

    public void FillEllipse(object brush, float x, float y, float radiusX, float radiusY)
    {
        _capture.FillEllipse(brush, x, y, radiusX, radiusY);
    }
    
    public void DrawCircle(object brush, float x, float y, float radius, float stroke)
    {
        _capture.DrawCircle(brush, x, y, radius, stroke);
    }
    
    public void FillCircle(object brush, float x, float y, float radius)
    {
        _capture.FillCircle(brush, x, y, radius);
    }
    
    public void DrawText(object font, object brush, float x, float y, string text)
    {
        _capture.DrawText(font, brush, x, y, text);
    }
    
    public void DrawText(object font, float fontSize, object brush, float x, float y, string text)
    {
        _capture.DrawText(font, fontSize, brush, x, y, text);
    }
    
    public void DrawTextWithBackground(object font, object brush, object background, float x, float y, string text)
    {
        _capture.DrawTextWithBackground(font, brush, background, x, y, text);
    }
    
    public void DrawTextWithBackground(object font, float fontSize, object brush, object background, float x, float y, string text)
    {
        _capture.DrawTextWithBackground(font, fontSize, brush, background, x, y, text);
    }
    
    // ============================================================================
    // Image Drawing
    // ============================================================================
    
    public void DrawImage(object image, float left, float top, float right, float bottom)
    {
        _capture.DrawImage(image, left, top, right, bottom);
    }
    
    public void DrawImage(object image, float x, float y)
    {
        _capture.DrawImage(image, x, y);
    }
    
    public void DrawImage(object image, object rectangle)
    {
        var rect = ExtractRectangle(rectangle);
        _capture.DrawImage(image, rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
    
    // ============================================================================
    // Transformations
    // ============================================================================
    
    public void RotateAt(float x, float y, float angle)
    {
        _capture.RotateAt(x, y, angle);
    }
    
    public void Rotate(float angle)
    {
        _capture.Rotate(angle);
    }
    
    public void Translate(float x, float y)
    {
        _capture.Translate(x, y);
    }
    
    public void Scale(float x, float y)
    {
        _capture.Scale(x, y);
    }
    
    public void ResetTransform()
    {
        _capture.ResetTransform();
    }
    
    // ============================================================================
    // State Management  
    // ============================================================================
    
    public void Save()
    {
        _capture.Save();
    }
    
    public void Restore()
    {
        _capture.Restore();
    }
    
    public void SetClip(float x, float y, float width, float height)
    {
        _capture.SetClip(x, y, width, height);
    }
    
    public void ReleaseClip()
    {
        _capture.ReleaseClip();
    }

    public void ClipRegionStart(float left, float top, float right, float bottom)
    {
        _capture.SetClip(left, top, right - left, bottom - top);
    }

    public void ClipRegionEnd()
    {
        _capture.ReleaseClip();
    }
    
    // ============================================================================
    // Geometry Drawing
    // ============================================================================
    
    public void FillGeometry(object geometry, object brush)
    {
        _capture.FillGeometry(geometry, brush);
    }
    
    public void DrawGeometry(object geometry, object brush, float thickness)
    {
        _capture.DrawGeometry(geometry, brush, thickness);
    }
    
    // ============================================================================
    // Helper Methods
    // ============================================================================
    
    // No conversion needed - DrawingCommandCapture accepts object and handles ProxyBrush/ProxyFont via duck typing
    
    private (float X, float Y) ExtractPoint(object point)
    {
        // Handle different point representations from Lua
        if (point is GameOverlay.Drawing.Point p)
            return (p.X, p.Y);
        
        // Try dynamic property access for Lua tables
        dynamic dynPoint = point;
        try
        {
            return (dynPoint.X, dynPoint.Y);
        }
        catch
        {
            return (0, 0);
        }
    }
    
    private (float Left, float Top, float Right, float Bottom) ExtractRectangle(object rectangle)
    {
        if (rectangle is GameOverlay.Drawing.Rectangle r)
            return (r.Left, r.Top, r.Right, r.Bottom);
        
        dynamic dynRect = rectangle;
        try
        {
            return (dynRect.Left, dynRect.Top, dynRect.Right, dynRect.Bottom);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }
    
    // ============================================================================
    // Text Measurement
    // ============================================================================
    
    public object MeasureString(object font, float fontSize, string text)
    {
        return _capture.MeasureString(font, fontSize, text);
    }
    
    public object MeasureString(object font, string text)
    {
        return _capture.MeasureString(font, text);
    }
    
    // ============================================================================
    // Centered Text Drawing (convenience method that uses MeasureString)
    // ============================================================================
    
    public void drawTextWithBackgroundCentered(object font, float fontSize, object brush, object background, float x, float y, string text)
    {
        // Measure text size
        var textSize = MeasureString(font, fontSize, text);
        
        // Extract size from ProxyPoint
        float width = 0, height = 0;
        if (textSize is ProxyPoint p)
        {
            width = p.X;
            height = p.Y;
        }
        else
        {
            // Try dynamic access
            try
            {
                dynamic dynSize = textSize;
                width = dynSize.X;
                height = dynSize.Y;
            }
            catch { }
        }
        
        // Draw text centered at (x, y)
        _capture.DrawTextWithBackground(font, fontSize, brush, background, 
            x - width * 0.5f, y - height * 0.5f, text);
    }
    
    // ============================================================================
    // Proxy Objects
    // ============================================================================
    
    public class ProxyBrush
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public int A { get; set; }

        public Color Color
        {
            get => new Color(R, G, B, A);
            set
            {
                R = (int)Math.Round(value.R * 255);
                G = (int)Math.Round(value.G * 255);
                B = (int)Math.Round(value.B * 255);
                A = (int)Math.Round(value.A * 255);
            }
        }

        public void Dispose() { }
    }
    
    public class ProxyColor
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public int A { get; set; }
    }
    
    public class ProxyLinearGradientBrush
    {
        public List<ProxyColor> Colors { get; set; } = new List<ProxyColor>();
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float EndX { get; set; }
        public float EndY { get; set; }
        
        public void SetRange(float startX, float startY, float endX, float endY)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
        }
        
        public void Dispose() { }  // No-op for proxy
    }
    
    public class ProxyRadialGradientBrush
    {
        public List<ProxyColor> Colors { get; set; } = new List<ProxyColor>();
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float RadiusX { get; set; }
        public float RadiusY { get; set; }
        
        public void SetRange(float centerX, float centerY, float radiusX, float radiusY)
        {
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        public void SetCenter(float centerX, float centerY)
        {
            CenterX = centerX;
            CenterY = centerY;
        }

        public void SetRadius(float radiusX, float radiusY)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
        }
        
        public void Dispose() { }  // No-op for proxy
    }
    
    public class ProxyFont
    {
        public string Name { get; set; }
        public float Size { get; set; }
        public float FontSize => Size;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        internal bool UseCustomFont { get; set; }

        public void Dispose() { }
    }

    public void Dispose()
    {
        _textMetrics?.Dispose();
        _capture.TextMetrics = null;
    }

    public class ProxyImage
    {
        public string Key { get; }
        public string Path { get; }
        public float Width { get; }
        public float Height { get; }

        public ProxyImage(string key, string path)
        {
            Key = key;
            Path = path;
            (Width, Height) = ReadDimensions(path);
        }

        public void Dispose() { }

        private static (float Width, float Height) ReadDimensions(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[26];
                stream.ReadExactly(header);

                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                {
                    return (
                        (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19],
                        (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
                }

                if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
                {
                    return (header[6] | (header[7] << 8), header[8] | (header[9] << 8));
                }

                if (header[0] == 'B' && header[1] == 'M')
                {
                    return (
                        header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24),
                        header[22] | (header[23] << 8) | (header[24] << 16) | (header[25] << 24));
                }
            }
            catch (Exception)
            {
                // Image dimensions are optional for command capture.
            }

            return (0, 0);
        }
    }
}
