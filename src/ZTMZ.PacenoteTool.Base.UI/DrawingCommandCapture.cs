using System;
using System.Collections.Generic;
using GameOverlay.Drawing;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Wrapper around GameOverlay.Drawing.Graphics that captures all drawing commands
/// for serialization and streaming to web clients
/// </summary>
public class DrawingCommandCapture
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly List<DrawingCommand> _commands = new();
    private readonly Dictionary<object, ImageRegistration> _imageKeyMap = new();
    
    public int Width { get; set; }
    public int Height { get; set; }
    
    /// <summary>
    /// Register an image object with its key for later lookup
    /// </summary>
    public void RegisterImage(string key, object image, string imageUrl = null)
    {
        if (image != null && !_imageKeyMap.ContainsKey(image))
        {
            _imageKeyMap[image] = new ImageRegistration(key, imageUrl);
        }
    }

    public void ClearRegisteredImages()
    {
        _imageKeyMap.Clear();
    }
    
    /// <summary>
    /// Get the web resource details for an image object.
    /// </summary>
    private ImageRegistration GetImageRegistration(object image)
    {
        if (image == null) return null;
        return _imageKeyMap.TryGetValue(image, out var registration) ? registration : null;
    }

    private sealed class ImageRegistration
    {
        public ImageRegistration(string key, string imageUrl)
        {
            Key = key;
            ImageUrl = imageUrl;
        }

        public string Key { get; }
        public string ImageUrl { get; }
    }
    
    /// <summary>
    /// Get the count of captured commands without clearing
    /// </summary>
    public int CommandCount => _commands.Count;
    
    /// <summary>
    /// Get all captured commands and clear the list
    /// </summary>
    public List<DrawingCommand> GetCommands()
    {
        var commands = new List<DrawingCommand>(_commands);
        _commands.Clear();
        return commands;
    }
    
    /// <summary>
    /// Clear all captured commands without returning them
    /// </summary>
    public void ClearCommands()
    {
        _commands.Clear();
    }
    
    /// <summary>
    /// Get color from brush
    /// </summary>
    private uint GetBrushColor(SolidBrush brush)
    {
        if (brush == null) return 0;
        
        // Extract ARGB from brush color
        var c = brush.Color;
        uint argb = ((uint)(c.A * 255) << 24) | 
                    ((uint)(c.R * 255) << 16) | 
                    ((uint)(c.G * 255) << 8) | 
                    (uint)(c.B * 255);
        return argb;
    }

    /// <summary>
    /// Get color from brush object (handles ProxyBrush via duck typing)
    /// </summary>
    private uint GetBrushColor(object brush)
    {
        if (brush == null) return 0;
        
        if (brush is SolidBrush solidBrush)
            return GetBrushColor(solidBrush);
        
        // Handle ProxyBrush via dynamic (duck typing)
        try
        {
            dynamic proxy = brush;
            int r = proxy.R;
            int g = proxy.G;
            int b = proxy.B;
            int a = proxy.A;
            return (uint)(a << 24 | r << 16 | g << 8 | b);
        }
        catch
        {
            return 0; // Transparent fallback
        }
    }
    
    /// <summary>
    /// Get gradient info from brush (if it's a gradient brush)
    /// </summary>
    private GradientInfo GetGradientInfo(object brush)
    {
        if (brush == null) return null;

        if (brush is LinearGradientBrush linearGradient)
        {
            var info = new GradientInfo
            {
                Type = "linear",
                X0 = linearGradient.Start.X,
                Y0 = linearGradient.Start.Y,
                X1 = linearGradient.End.X,
                Y1 = linearGradient.End.Y
            };

            AddGradientColors(info, linearGradient.GradientColors);
            return info.Colors.Count > 0 ? info : null;
        }

        if (brush is RadialGradientBrush radialGradient)
        {
            var center = radialGradient.Center;
            var originOffset = radialGradient.GradientOriginOffset;
            var info = new GradientInfo
            {
                Type = "radial",
                X0 = center.X + originOffset.X,
                Y0 = center.Y + originOffset.Y,
                R0 = 0,
                X1 = center.X,
                Y1 = center.Y,
                R1 = Math.Max(radialGradient.RadiusX, radialGradient.RadiusY)
            };

            AddGradientColors(info, radialGradient.GradientColors);
            return info.Colors.Count > 0 ? info : null;
        }
        
        var typeName = brush.GetType().Name;

        if (typeName == "ProxyLinearGradientBrush")
        {
            try
            {
                dynamic gradient = brush;
                var info = new GradientInfo
                {
                    Type = "linear",
                    X0 = gradient.StartX,
                    Y0 = gradient.StartY,
                    X1 = gradient.EndX,
                    Y1 = gradient.EndY
                };

                foreach (var color in gradient.Colors)
                {
                    dynamic c = color;
                    uint argb = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
                    info.Colors.Add(argb);
                }

                return info.Colors.Count > 0 ? info : null;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error getting proxy linear gradient info: {ex.Message}");
                return null;
            }
        }

        if (typeName == "ProxyRadialGradientBrush")
        {
            try
            {
                dynamic gradient = brush;
                var info = new GradientInfo
                {
                    Type = "radial",
                    X0 = gradient.CenterX,
                    Y0 = gradient.CenterY,
                    R0 = 0,
                    X1 = gradient.CenterX,
                    Y1 = gradient.CenterY,
                    R1 = Math.Max(gradient.RadiusX, gradient.RadiusY)
                };

                foreach (var color in gradient.Colors)
                {
                    dynamic c = color;
                    uint argb = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
                    info.Colors.Add(argb);
                }

                return info.Colors.Count > 0 ? info : null;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error getting proxy radial gradient info: {ex.Message}");
                return null;
            }
        }

        if (typeName.StartsWith("Proxy", StringComparison.Ordinal))
        {
            return null;
        }
        
        return null;
    }

    private static void AddGradientColors(GradientInfo gradient, Color[] colors)
    {
        if (colors == null)
        {
            return;
        }

        foreach (var color in colors)
        {
            gradient.Colors.Add(unchecked((uint)color.ToARGB()));
        }
    }
    
    /// <summary>
    /// Get font info
    /// </summary>
    private (string name, float size) GetFontInfo(Font font)
    {
        if (font == null) return ("Arial", 14);
        return (font.FontFamilyName, font.FontSize);
    }

    /// <summary>
    /// Get font info from object (handles ProxyFont via duck typing)
    /// </summary>
    private (string name, float size) GetFontInfo(object font)
    {
        if (font == null) return ("Arial", 14);
        
        if (font is Font realFont)
            return GetFontInfo(realFont);
        
        // Handle ProxyFont via dynamic (duck typing)
        try
        {
            dynamic proxy = font;
            string name = proxy.Name;
            float size = proxy.Size;
            return (name, size);
        }
        catch
        {
            return ("Arial", 14); // Fallback
        }
    }
    
    // ======================================================================
    // Drawing Commands
    // ======================================================================
    
    public void Clear(object brush = null)
    {
        _commands.Add(new ClearCommand
        {
            Color = brush != null ? GetBrushColor(brush) : null
        });
    }
    
    public void DrawLine(float startX, float startY, float endX, float endY, object brush, float stroke)
    {
        _commands.Add(new DrawLineCommand
        {
            X1 = startX,
            Y1 = startY,
            X2 = endX,
            Y2 = endY,
            Thickness = stroke,
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawRectangle(object brush, float left, float top, float right, float bottom, float stroke)
    {
        _commands.Add(new DrawRectangleCommand
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top,
            Thickness = stroke,
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawRectangle(object brush, Rectangle rect, float stroke)
    {
        DrawRectangle(brush, rect.Left, rect.Top, rect.Right, rect.Bottom, stroke);
    }
    
    public void FillRectangle(object brush, float left, float top, float right, float bottom)
    {
        var gradientInfo = GetGradientInfo(brush);
        var color = GetBrushColor(brush);
        
        _commands.Add(new FillRectangleCommand
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top,
            Color = color,
            Gradient = gradientInfo
        });
    }
    
    public void FillRectangle(object brush, Rectangle rect)
    {
        FillRectangle(brush, rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public void FillRoundedRectangle(object brush, float left, float top, float right, float bottom, float radius)
    {
        _commands.Add(new FillRoundedRectangleCommand
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top,
            Radius = radius,
            Color = GetBrushColor(brush),
            Gradient = GetGradientInfo(brush)
        });
    }
    
    public void DrawCircle(object brush, float x, float y, float radius, float stroke)
    {
        _commands.Add(new DrawCircleCommand
        {
            X = x,
            Y = y,
            Radius = radius,
            Thickness = stroke,
            Color = GetBrushColor(brush)
        });
    }
    
    public void FillCircle(object brush, float x, float y, float radius)
    {
        _commands.Add(new FillCircleCommand
        {
            X = x,
            Y = y,
            Radius = radius,
            Color = GetBrushColor(brush),
            Gradient = GetGradientInfo(brush)
        });
    }

    public void FillEllipse(object brush, float x, float y, float radiusX, float radiusY)
    {
        _commands.Add(new FillEllipseCommand
        {
            X = x,
            Y = y,
            RadiusX = radiusX,
            RadiusY = radiusY,
            Color = GetBrushColor(brush),
            Gradient = GetGradientInfo(brush)
        });
    }
    
    public void DrawText(object font, object brush, float x, float y, string text)
    {
        var fontInfo = GetFontInfo(font);
        _commands.Add(new DrawTextCommand
        {
            X = x,
            Y = y,
            Text = text ?? "",
            FontName = fontInfo.name,
            FontSize = fontInfo.size,
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawText(object font, float fontSize, object brush, float x, float y, string text)
    {
        var fontInfo = GetFontInfo(font);
        _commands.Add(new DrawTextCommand
        {
            X = x,
            Y = y,
            Text = text ?? "",
            FontName = fontInfo.name,
            FontSize = fontSize, // Use the provided fontSize instead of font's default
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawTextWithBackground(object font, object brush, object background, float x, float y, string text)
    {
        var fontInfo = GetFontInfo(font);
        _commands.Add(new DrawTextWithBackgroundCommand
        {
            X = x,
            Y = y,
            Text = text ?? "",
            FontName = fontInfo.name,
            FontSize = fontInfo.size,
            Color = GetBrushColor(brush),
            BgColor = GetBrushColor(background)
        });
    }
    
    public void DrawTextWithBackground(object font, float fontSize, object brush, object background, float x, float y, string text)
    {
        var fontInfo = GetFontInfo(font);
        _commands.Add(new DrawTextWithBackgroundCommand
        {
            X = x,
            Y = y,
            Text = text ?? "",
            FontName = fontInfo.name,
            FontSize = fontSize, // Use the provided fontSize instead of font's default
            Color = GetBrushColor(brush),
            BgColor = GetBrushColor(background)
        });
    }
    
    public void DrawImage(object image, float left, float top, float right, float bottom)
    {
        var imageRegistration = GetImageRegistration(image);
        if (imageRegistration != null)
        {
            _commands.Add(new DrawImageCommand
            {
                ImageKey = imageRegistration.Key,
                ImageUrl = imageRegistration.ImageUrl,
                X = left,
                Y = top,
                Width = right - left,
                Height = bottom - top
            });
        }
        else
        {
            _logger.Warn($"DrawImage: Image not registered in image key map");
        }
    }
    
    public void DrawImage(object image, float x, float y)
    {
        var imageRegistration = GetImageRegistration(image);
        if (imageRegistration != null)
        {
            _commands.Add(new DrawImageCommand
            {
                ImageKey = imageRegistration.Key,
                ImageUrl = imageRegistration.ImageUrl,
                X = x,
                Y = y
            });
        }
        else
        {
            _logger.Warn($"DrawImage: Image not registered in image key map");
        }
    }
    
    // ======================================================================
    // Transformations
    // ======================================================================
    
    public void RotateAt(float x, float y, float angle)
    {
        _commands.Add(new RotateAtCommand
        {
            CenterX = x,
            CenterY = y,
            Angle = angle
        });
    }
    
    public void Rotate(float angle)
    {
        _commands.Add(new RotateCommand { Angle = angle });
    }
    
    public void Translate(float x, float y)
    {
        _commands.Add(new TranslateCommand { X = x, Y = y });
    }
    
    public void Scale(float x, float y)
    {
        _commands.Add(new ScaleCommand { X = x, Y = y });
    }
    
    public void ResetTransform()
    {
        _commands.Add(new ResetTransformCommand());
    }
    
    // ======================================================================
    // State Management
    // ======================================================================
    
    private int _stateDepth = 0;
    
    public void Save()
    {
        _stateDepth++;
        _commands.Add(new SaveCommand());
    }
    
    public void Restore()
    {
        if (_stateDepth > 0)
        {
            _stateDepth--;
            _commands.Add(new RestoreCommand());
        }
    }
    
    public void SetClip(float x, float y, float width, float height)
    {
        _commands.Add(new SetClipCommand
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        });
    }
    
    public void ReleaseClip()
    {
        _commands.Add(new ReleaseClipCommand());
    }
    
    // ======================================================================
    // Geometry Drawing
    // ======================================================================
    
    public void FillGeometry(object geometry, object brush)
    {
        if (geometry is ProxyGeometry geo && geo.Points.Count > 0)
        {
            // Flatten points to array: [x1, y1, x2, y2, ...]
            var points = new float[geo.Points.Count * 2];
            for (int i = 0; i < geo.Points.Count; i++)
            {
                points[i * 2] = geo.Points[i].X;
                points[i * 2 + 1] = geo.Points[i].Y;
            }
            
            _commands.Add(new FillGeometryCommand
            {
                Points = points,
                Color = GetBrushColor(brush),
                Gradient = GetGradientInfo(brush)
            });
        }
    }
    
    public void DrawGeometry(object geometry, object brush, float thickness)
    {
        if (geometry is ProxyGeometry geo && geo.Points.Count > 0)
        {
            // Flatten points to array: [x1, y1, x2, y2, ...]
            var points = new float[geo.Points.Count * 2];
            for (int i = 0; i < geo.Points.Count; i++)
            {
                points[i * 2] = geo.Points[i].X;
                points[i * 2 + 1] = geo.Points[i].Y;
            }
            
            _commands.Add(new DrawGeometryCommand
            {
                Points = points,
                Color = GetBrushColor(brush),
                Thickness = thickness
            });
        }
    }
    
    // ======================================================================
    // Text Measurement (doesn't create a command, just returns size)
    // ======================================================================
    
    public object MeasureString(object font, float fontSize, string text)
    {
        // For web client, we need to approximate text size
        // We'll use a simple heuristic: average character width * length
        // Real measurement would require the browser's font metrics
        
        if (string.IsNullOrEmpty(text)) return new ProxyPoint { X = 0, Y = 0 };
        
        // Rough approximation: fontSize * 0.6 for average character width
        // fontSize * 1.2 for line height
        float charWidth = fontSize * 0.6f;
        float lineHeight = fontSize * 1.2f;
        
        // Handle multi-line text
        var lines = text.Split('\n');
        float maxWidth = 0;
        foreach (var line in lines)
        {
            float lineWidth = line.Length * charWidth;
            if (lineWidth > maxWidth) maxWidth = lineWidth;
        }
        
        float totalHeight = lines.Length * lineHeight;
        
        return new ProxyPoint { X = maxWidth, Y = totalHeight };
    }
    
    public object MeasureString(object font, string text)
    {
        var fontInfo = GetFontInfo(font);
        return MeasureString(font, fontInfo.size, text);
    }
}
