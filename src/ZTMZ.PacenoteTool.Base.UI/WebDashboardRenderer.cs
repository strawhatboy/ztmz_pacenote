using System;
using System.Collections.Generic;
using GameOverlay.Drawing;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Extension methods to capture drawing commands from Graphics object
/// This allows us to intercept drawing calls and convert them to commands for web clients
/// 
/// USAGE: After each gfx.DrawXXX() call in GameOverlayManager, also call capture.CaptureDrawXXX()
/// Or better: Create a wrapper that does both automatically
/// </summary>
public static class GraphicsCaptureExtensions
{
    /// <summary>
    /// Capture Clear command
    /// </summary>
    public static void CaptureClear(this DrawingCommandCapture capture, SolidBrush brush = null)
    {
        capture.Clear(brush);
    }
    
    /// <summary>
    /// Capture DrawLine command
    /// </summary>
    public static void CaptureDrawLine(this DrawingCommandCapture capture, SolidBrush brush, float startX, float startY, float endX, float endY, float stroke)
    {
        capture.DrawLine(startX, startY, endX, endY, brush, stroke);
    }
    
    /// <summary>
    /// Capture FillRectangle command
    /// </summary>
    public static void CaptureFillRectangle(this DrawingCommandCapture capture, SolidBrush brush, float left, float top, float right, float bottom)
    {
        capture.FillRectangle(brush, left, top, right, bottom);
    }
    
    /// <summary>
    /// Capture DrawCircle command
    /// </summary>
    public static void CaptureDrawCircle(this DrawingCommandCapture capture, SolidBrush brush, float x, float y, float radius, float stroke)
    {
        capture.DrawCircle(brush, x, y, radius, stroke);
    }
    
    /// <summary>
    /// Capture FillCircle command
    /// </summary>
    public static void CaptureFillCircle(this DrawingCommandCapture capture, SolidBrush brush, float x, float y, float radius)
    {
        capture.FillCircle(brush, x, y, radius);
    }
    
    /// <summary>
    /// Capture DrawText command
    /// </summary>
    public static void CaptureDrawText(this DrawingCommandCapture capture, Font font, SolidBrush brush, float x, float y, string text)
    {
        capture.DrawText(font, brush, x, y, text);
    }
    
    /// <summary>
    /// Get color from brush
    /// </summary>
    public static uint ToARGB(this SolidBrush brush)
    {
        if (brush == null) return 0;
        var c = brush.Color;
        return ((uint)(c.A * 255) << 24) |
               ((uint)(c.R * 255) << 16) |
               ((uint)(c.G * 255) << 8) |
               (uint)(c.B * 255);
    }
}
