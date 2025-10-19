using System;
using System.Collections.Generic;
using GameOverlay.Drawing;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// A proxy object that mimics GameOverlay.Drawing.Graphics interface for Lua scripts
/// but captures drawing commands instead of actually drawing
/// This allows us to record what dashboards are rendering without needing the real Graphics object
/// </summary>
public class GraphicsCommandProxy
{
    private readonly DrawingCommandCapture _capture;
    private readonly Dictionary<object, SolidBrush> _brushRegistry = new();
    private readonly Dictionary<object, Font> _fontRegistry = new();
    
    public int Width => _capture.Width;
    public int Height => _capture.Height;
    public float FPS => 60; // Web rendering target FPS
    
    public GraphicsCommandProxy(DrawingCommandCapture capture)
    {
        _capture = capture;
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
        var font = new ProxyFont { Name = fontName, Size = fontSize, Bold = bold, Italic = italic };
        _fontRegistry[font] = null;
        return font;
    }
    
    public object CreateCustomFont(string fontName, float fontSize, bool bold = false, bool italic = false)
    {
        return CreateFont(fontName, fontSize, bold, italic);
    }
    
    // ============================================================================
    // Geometry Creation
    // ============================================================================
    
    public object CreateGeometry()
    {
        return new ProxyGeometry();
    }
    
    // ============================================================================
    // Drawing Commands
    // ============================================================================
    
    public void ClearScene(object brush = null)
    {
        _capture.Clear(brush);
    }
    
    public void DrawLine(object brush, float x1, float y1, float x2, float y2, float thickness)
    {
        try
        {
            _capture.DrawLine(brush, x1, y1, x2, y2, thickness);
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
    
    public void FillRectangle(object brush, float left, float top, float right, float bottom)
    {
        _capture.FillRectangle(brush, left, top, right, bottom);
    }
    
    public void FillRectangle(object brush, object rectangle)
    {
        var rect = ExtractRectangle(rectangle);
        FillRectangle(brush, rect.Left, rect.Top, rect.Right, rect.Bottom);
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
        
        public void Dispose() { }  // No-op for proxy
    }
    
    public class ProxyFont
    {
        public string Name { get; set; }
        public float Size { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
    }
}
