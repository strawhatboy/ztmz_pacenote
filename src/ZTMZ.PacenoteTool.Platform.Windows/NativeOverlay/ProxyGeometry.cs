using System.Collections.Generic;
using GameOverlay.Drawing;
using SharpDX.Direct2D1;

namespace ZTMZ.PacenoteTool.UI.Windows;

/// <summary>
/// Proxy for Geometry that captures path commands instead of rendering
/// </summary>
public class ProxyGeometry
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    
    public List<(float X, float Y)> Points { get; } = new();
    public bool IsClosed { get; set; }
    public bool IsFilled { get; set; }
    
    private bool _figureStarted = false;
    private (float X, float Y)? _currentPoint = null;
    
    public void BeginFigure(object point, bool filled)
    {
        IsFilled = filled;
        _figureStarted = true;
        
        // Extract point coordinates
        var p = ExtractPoint(point);
        _currentPoint = p;
        Points.Add(p);
    }
    
    public void AddPoint(object point)
    {
        if (!_figureStarted)
        {
            throw new System.InvalidOperationException("BeginFigure must be called before AddPoint");
        }
        
        var p = ExtractPoint(point);
        Points.Add(p);
        _currentPoint = p;
    }
    
    public void AddLine(object point)
    {
        AddPoint(point);
    }
    
    public void AddCurve(object point, float radius, float rotationAngle = 0.0f)
    {
        // Default to Large arc, Clockwise sweep
        AddCurve(point, radius, ArcSize.Large, SweepDirection.Clockwise, rotationAngle);
    }
    
    public void AddCurve(object point, float radius, object arcSizeObj, object sweepDirectionObj = null, float rotationAngle = 0.0f)
    {
        try
        {
            var endPoint = ExtractPoint(point);
            
            if (_currentPoint == null)
            {
                Points.Add(endPoint);
                _currentPoint = endPoint;
                return;
            }
            
            var start = _currentPoint.Value;
            
            // Determine arc parameters
            ArcSize arcSize = ArcSize.Small;
            SweepDirection sweepDir = SweepDirection.Clockwise;
            
            // Handle both enum and integer values from Lua
            if (arcSizeObj is ArcSize a)
            {
                arcSize = a;
            }
            else if (arcSizeObj is int arcSizeInt)
            {
                // Lua passes integers: 0 = Small, 1 = Large
                arcSize = arcSizeInt == 0 ? ArcSize.Small : ArcSize.Large;
            }
            
            if (sweepDirectionObj is SweepDirection s)
            {
                sweepDir = s;
            }
            else if (sweepDirectionObj is int sweepInt)
            {
                // Lua passes integers: 0 = CounterClockwise, 1 = Clockwise
                sweepDir = sweepInt == 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise;
            }
            
            // Calculate arc points
            // This is a simplified arc approximation using the arc-to-bezier method
            const int segments = 20; // More segments for smoother curves
            
            // Calculate the center of the circle
            float dx = endPoint.X - start.X;
            float dy = endPoint.Y - start.Y;
            float distance = (float)System.Math.Sqrt(dx * dx + dy * dy);
            
            // If points are too close, just draw a line
            if (distance < 0.01f || radius < 0.01f)
            {
                Points.Add(endPoint);
                _currentPoint = endPoint;
                return;
            }
            
            // Midpoint between start and end
            float mx = (start.X + endPoint.X) / 2.0f;
            float my = (start.Y + endPoint.Y) / 2.0f;
            
            // Calculate the distance from midpoint to center
            float halfChord = distance / 2.0f;
            float h = (float)System.Math.Sqrt(System.Math.Max(0, radius * radius - halfChord * halfChord));
            
            // Perpendicular direction (rotated 90 degrees from chord)
            // In screen coordinates with Y-down, perpendicular is calculated as:
            float px = dy / distance;  // Swapped sign - this points "right" of chord direction
            float py = -dx / distance;
            
            // Determine center based on sweep direction and arc size
            float centerX, centerY;
            bool isLargeArc = (arcSize == ArcSize.Large);
            bool isClockwise = (sweepDir == SweepDirection.Clockwise);
            
            // In Direct2D with Y-down coordinates:
            // For Clockwise sweep: the arc curves to the RIGHT of the chord direction
            // For CounterClockwise: the arc curves to the LEFT of the chord direction
            // Large/Small determines which of the two possible arcs to use
            
            if (isClockwise)
            {
                // Clockwise: use negative perpendicular for small, positive for large
                if (isLargeArc)
                {
                    centerX = mx + h * px;
                    centerY = my + h * py;
                }
                else
                {
                    centerX = mx - h * px;
                    centerY = my - h * py;
                }
            }
            else
            {
                // CounterClockwise: use positive perpendicular for small, negative for large
                if (isLargeArc)
                {
                    centerX = mx - h * px;
                    centerY = my - h * py;
                }
                else
                {
                    centerX = mx + h * px;
                    centerY = my + h * py;
                }
            }
            
            // Calculate start and end angles
            float startAngle = (float)System.Math.Atan2(start.Y - centerY, start.X - centerX);
            float endAngle = (float)System.Math.Atan2(endPoint.Y - centerY, endPoint.X - centerX);
            
            // Determine sweep angle based on direction
            // In screen coordinates with Y-down:
            // - Clockwise means going in the direction of INCREASING angles (due to Y-flip)
            // - CounterClockwise means going in the direction of DECREASING angles
            float sweepAngle;
            if (isClockwise)
            {
                // Clockwise in screen coords = positive angle change
                sweepAngle = endAngle - startAngle;
                
                // Normalize to positive range [0, 2π)
                if (sweepAngle < 0)
                    sweepAngle += 2.0f * (float)System.Math.PI;
                if (sweepAngle >= 2.0f * (float)System.Math.PI)
                    sweepAngle -= 2.0f * (float)System.Math.PI;
            }
            else
            {
                // CounterClockwise in screen coords = negative angle change
                sweepAngle = endAngle - startAngle;
                
                // Normalize to negative range (-2π, 0]
                if (sweepAngle > 0)
                    sweepAngle -= 2.0f * (float)System.Math.PI;
                if (sweepAngle <= -2.0f * (float)System.Math.PI)
                    sweepAngle += 2.0f * (float)System.Math.PI;
            }
            
            // Generate arc points
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = startAngle + sweepAngle * t;
                float x = centerX + radius * (float)System.Math.Cos(angle);
                float y = centerY + radius * (float)System.Math.Sin(angle);
                Points.Add((x, y));
            }
            
            _currentPoint = endPoint;
        }
        catch (System.Exception ex)
        {
            _logger.Error(ex, $"AddCurve error: {ex.Message}");
            // Fallback to straight line
            var endPoint = ExtractPoint(point);
            Points.Add(endPoint);
            _currentPoint = endPoint;
        }
    }
    
    public void AddCurve(object point, float radius_x, float radius_y, float rotationAngle = 0.0f)
    {
        // For elliptical arcs, use average radius as approximation
        float avgRadius = (radius_x + radius_y) / 2.0f;
        AddCurve(point, avgRadius, ArcSize.Large, SweepDirection.Clockwise, rotationAngle);
    }
    
    /// <summary>
    /// Alias for AddCurve with ArcSize and SweepDirection parameters
    /// Used by Lua dashboards for compatibility with GameOverlay.Drawing.Geometry
    /// </summary>
    public void AddCurveWithArcSegmentArgs(object point, float radius, object arcSize, object sweepDirection = null, float rotationAngle = 0.0f)
    {
        AddCurve(point, radius, arcSize, sweepDirection, rotationAngle);
    }
    
    public void EndFigure()
    {
        _figureStarted = false;
    }
    
    public void Close()
    {
        IsClosed = true;
        _figureStarted = false;
    }
    
    public void Dispose()
    {
        // No-op for proxy
    }
    
    private (float X, float Y) ExtractPoint(object point)
    {
        if (point is Point p)
            return (p.X, p.Y);
        
        // Try dynamic property access for Lua tables
        try
        {
            dynamic dynPoint = point;
            return ((float)dynPoint.X, (float)dynPoint.Y);
        }
        catch
        {
            return (0, 0);
        }
    }
}

/// <summary>
/// Proxy for Point (used by MeasureString)
/// </summary>
public class ProxyPoint
{
    public float X { get; set; }
    public float Y { get; set; }
}
