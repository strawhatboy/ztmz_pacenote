using System;
using System.Collections.Generic;
using System.Linq;
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

    private abstract class CaptureControlCommand : DrawingCommand
    {
    }

    private sealed class DashboardScopeCommand : CaptureControlCommand
    {
        public DashboardScopeCommand(string dashboardId, bool staticLayerRefreshRequested, bool isBegin)
        {
            DashboardId = dashboardId;
            StaticLayerRefreshRequested = staticLayerRefreshRequested;
            IsBegin = isBegin;
        }

        public override string Type => "__dashboardScope";

        public string DashboardId { get; }

        public bool StaticLayerRefreshRequested { get; }

        public bool IsBegin { get; }
    }

    private sealed class StaticLayerBoundaryCommand : CaptureControlCommand
    {
        public StaticLayerBoundaryCommand(bool isBegin)
        {
            IsBegin = isBegin;
        }

        public override string Type => "__staticLayer";

        public bool IsBegin { get; }
    }

    private sealed class CapturedLayerBuilder
    {
        public CapturedLayerBuilder(string id, WebDashboardDrawLayerKind kind, bool staticContentProvided)
        {
            Id = id;
            Kind = kind;
            StaticContentProvided = staticContentProvided;
        }

        public string Id { get; }

        public WebDashboardDrawLayerKind Kind { get; }

        public List<DrawingCommand> Commands { get; } = new();

        public bool StaticContentProvided { get; set; }
    }

    private readonly record struct FontInfo(
        string Name,
        float Size,
        int Weight,
        string Style,
        WebDashboardTextLayout Layout);

    private readonly record struct TextLayoutBounds(float Width, float Height);
    
    public int Width { get; set; }
    public int Height { get; set; }
    internal WebDashboardTextMetrics TextMetrics { get; set; }
    
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
    public int CommandCount => _commands.Count(command => command is not CaptureControlCommand);
    
    /// <summary>
    /// Get all captured commands and clear the list
    /// </summary>
    public List<DrawingCommand> GetCommands()
    {
        var commands = _commands
            .Where(command => command is not CaptureControlCommand)
            .ToList();
        _commands.Clear();
        return commands;
    }

    /// <summary>
    /// Marks the command stream emitted by one dashboard. These markers are not exposed to
    /// legacy clients and let the web client retain each dashboard's original z-order.
    /// </summary>
    public void BeginDashboardCapture(string dashboardId, bool staticLayerRefreshRequested)
    {
        _commands.Add(new DashboardScopeCommand(dashboardId, staticLayerRefreshRequested, true));
    }

    public void EndDashboardCapture()
    {
        _commands.Add(new DashboardScopeCommand(string.Empty, false, false));
    }

    /// <summary>
    /// Starts a dashboard-authored static layer. The surrounding dashboard scope decides
    /// whether an empty static layer means "unchanged" or "clear the cached layer".
    /// </summary>
    public void BeginStaticLayer()
    {
        _commands.Add(new StaticLayerBoundaryCommand(true));
    }

    public void EndStaticLayer()
    {
        _commands.Add(new StaticLayerBoundaryCommand(false));
    }

    /// <summary>
    /// Returns a layered frame when a dashboard has opted in with BeginStaticLayer/EndStaticLayer.
    /// The flattened command list remains available for legacy fallback rendering.
    /// </summary>
    public WebDashboardDrawFrame GetWebDashboardDrawFrame()
    {
        var legacyCommands = _commands
            .Where(command => command is not CaptureControlCommand)
            .ToList();
        var layers = new List<CapturedLayerBuilder>();
        var currentDashboardId = "scene";
        var currentKind = WebDashboardDrawLayerKind.Static;
        var currentSegment = 0;
        var staticLayerRefreshRequested = true;
        var hasExplicitStaticLayers = false;

        CapturedLayerBuilder GetCurrentLayer(bool ensureLayer)
        {
            var id = $"{currentDashboardId}:{currentKind.ToString().ToLowerInvariant()}:{currentSegment}";
            var existing = layers.LastOrDefault(layer => layer.Id == id);
            if (existing != null || !ensureLayer)
            {
                return existing;
            }

            var staticContentProvided = currentKind == WebDashboardDrawLayerKind.Static
                && staticLayerRefreshRequested;
            var created = new CapturedLayerBuilder(id, currentKind, staticContentProvided);
            layers.Add(created);
            return created;
        }

        foreach (var command in _commands)
        {
            switch (command)
            {
                case DashboardScopeCommand scope when scope.IsBegin:
                    currentDashboardId = scope.DashboardId;
                    currentKind = WebDashboardDrawLayerKind.Dynamic;
                    currentSegment = 0;
                    staticLayerRefreshRequested = scope.StaticLayerRefreshRequested;
                    break;
                case DashboardScopeCommand:
                    currentDashboardId = "scene";
                    currentKind = WebDashboardDrawLayerKind.Static;
                    currentSegment = 0;
                    staticLayerRefreshRequested = true;
                    break;
                case StaticLayerBoundaryCommand boundary when boundary.IsBegin:
                    currentKind = WebDashboardDrawLayerKind.Static;
                    currentSegment++;
                    hasExplicitStaticLayers = true;
                    GetCurrentLayer(true);
                    break;
                case StaticLayerBoundaryCommand:
                    currentKind = WebDashboardDrawLayerKind.Dynamic;
                    currentSegment++;
                    break;
                case CaptureControlCommand:
                    break;
                default:
                    var layer = GetCurrentLayer(true);
                    layer.Commands.Add(command);
                    if (layer.Kind == WebDashboardDrawLayerKind.Static)
                    {
                        // An opt-in dashboard that still redraws its static content every
                        // frame remains correct; the server can compare and retain it.
                        layer.StaticContentProvided = true;
                    }
                    break;
            }
        }

        _commands.Clear();
        var frameLayers = layers
            .Select(layer => new WebDashboardDrawLayer(
                layer.Id,
                layer.Kind,
                layer.Commands,
                layer.StaticContentProvided))
            .ToList();
        return new WebDashboardDrawFrame(frameLayers, legacyCommands, hasExplicitStaticLayers);
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
    
    private FontInfo GetFontInfo(
        object font,
        float fontSize,
        string text,
        float? layoutWidth = null,
        float? layoutHeight = null)
    {
        var width = layoutWidth ?? Width;
        var height = layoutHeight ?? Height;

        if (font is GraphicsCommandProxy.ProxyFont proxyFont)
        {
            var layout = TextMetrics?.Measure(
                proxyFont.Name,
                fontSize,
                proxyFont.Bold,
                proxyFont.Italic,
                proxyFont.UseCustomFont,
                text,
                width,
                height) ?? CreateFallbackLayout(text, fontSize);
            return new FontInfo(
                proxyFont.Name,
                fontSize,
                proxyFont.Bold ? 700 : 400,
                proxyFont.Italic ? "italic" : "normal",
                layout);
        }

        if (font is Font realFont)
        {
            return CreateFallbackFontInfo(
                realFont.FontFamilyName,
                fontSize,
                realFont.Bold,
                realFont.Italic,
                text);
        }

        try
        {
            dynamic proxy = font;
            return CreateFallbackFontInfo(
                (string)proxy.Name,
                fontSize,
                (bool)proxy.Bold,
                (bool)proxy.Italic,
                text);
        }
        catch
        {
            return CreateFallbackFontInfo("Arial", fontSize, false, false, text);
        }
    }

    private static FontInfo CreateFallbackFontInfo(
        string name,
        float fontSize,
        bool bold,
        bool italic,
        string text)
    {
        return new FontInfo(
            name,
            fontSize,
            bold ? 700 : 400,
            italic ? "italic" : "normal",
            CreateFallbackLayout(text, fontSize));
    }

    private static WebDashboardTextLayout CreateFallbackLayout(string text, float fontSize)
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

    private static float GetFontSize(object font)
    {
        if (font is GraphicsCommandProxy.ProxyFont proxyFont)
        {
            return proxyFont.Size;
        }

        if (font is Font realFont)
        {
            return realFont.FontSize;
        }

        try
        {
            dynamic proxy = font;
            return (float)proxy.Size;
        }
        catch
        {
            return 14;
        }
    }

    private TextLayoutBounds GetDrawingLayoutBounds(float fontSize, float x, float y)
    {
        var clippedWidth = x < 0 ? Width + x : Width - x;
        var clippedHeight = y < 0 ? Height + y : Height - y;

        if (clippedWidth <= fontSize)
        {
            clippedWidth = Width;
        }

        if (clippedHeight <= fontSize)
        {
            clippedHeight = Height;
        }

        return new TextLayoutBounds(clippedWidth, clippedHeight);
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

    public void DrawRoundedRectangle(object brush, float left, float top, float right, float bottom, float radius, float stroke)
    {
        _commands.Add(new DrawRoundedRectangleCommand
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top,
            Radius = radius,
            Thickness = stroke,
            Color = GetBrushColor(brush)
        });
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
        var normalizedText = text ?? "";
        var fontSize = GetFontSize(font);
        var bounds = GetDrawingLayoutBounds(fontSize, x, y);
        var fontInfo = GetFontInfo(font, fontSize, normalizedText, bounds.Width, bounds.Height);
        _commands.Add(new DrawTextCommand
        {
            X = x,
            Y = y,
            Text = normalizedText,
            FontName = fontInfo.Name,
            FontSize = fontInfo.Size,
            FontWeight = fontInfo.Weight,
            FontStyle = fontInfo.Style,
            TextWidth = fontInfo.Layout.Width,
            TextHeight = fontInfo.Layout.Height,
            Baseline = fontInfo.Layout.Baseline,
            LineHeight = fontInfo.Layout.LineHeight,
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawText(object font, float fontSize, object brush, float x, float y, string text)
    {
        var normalizedText = text ?? "";
        var bounds = GetDrawingLayoutBounds(fontSize, x, y);
        var fontInfo = GetFontInfo(font, fontSize, normalizedText, bounds.Width, bounds.Height);
        _commands.Add(new DrawTextCommand
        {
            X = x,
            Y = y,
            Text = normalizedText,
            FontName = fontInfo.Name,
            FontSize = fontInfo.Size,
            FontWeight = fontInfo.Weight,
            FontStyle = fontInfo.Style,
            TextWidth = fontInfo.Layout.Width,
            TextHeight = fontInfo.Layout.Height,
            Baseline = fontInfo.Layout.Baseline,
            LineHeight = fontInfo.Layout.LineHeight,
            Color = GetBrushColor(brush)
        });
    }
    
    public void DrawTextWithBackground(object font, object brush, object background, float x, float y, string text)
    {
        var normalizedText = text ?? "";
        var fontSize = GetFontSize(font);
        var bounds = GetDrawingLayoutBounds(fontSize, x, y);
        var fontInfo = GetFontInfo(font, fontSize, normalizedText, bounds.Width, bounds.Height);
        _commands.Add(new DrawTextWithBackgroundCommand
        {
            X = x,
            Y = y,
            Text = normalizedText,
            FontName = fontInfo.Name,
            FontSize = fontInfo.Size,
            FontWeight = fontInfo.Weight,
            FontStyle = fontInfo.Style,
            TextWidth = fontInfo.Layout.Width,
            TextHeight = fontInfo.Layout.Height,
            Baseline = fontInfo.Layout.Baseline,
            LineHeight = fontInfo.Layout.LineHeight,
            Color = GetBrushColor(brush),
            BgColor = GetBrushColor(background)
        });
    }
    
    public void DrawTextWithBackground(object font, float fontSize, object brush, object background, float x, float y, string text)
    {
        var normalizedText = text ?? "";
        var bounds = GetDrawingLayoutBounds(fontSize, x, y);
        var fontInfo = GetFontInfo(font, fontSize, normalizedText, bounds.Width, bounds.Height);
        _commands.Add(new DrawTextWithBackgroundCommand
        {
            X = x,
            Y = y,
            Text = normalizedText,
            FontName = fontInfo.Name,
            FontSize = fontInfo.Size,
            FontWeight = fontInfo.Weight,
            FontStyle = fontInfo.Style,
            TextWidth = fontInfo.Layout.Width,
            TextHeight = fontInfo.Layout.Height,
            Baseline = fontInfo.Layout.Baseline,
            LineHeight = fontInfo.Layout.LineHeight,
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
        if (string.IsNullOrEmpty(text))
        {
            return new ProxyPoint { X = 0, Y = 0 };
        }

        var fontInfo = GetFontInfo(font, fontSize, text);
        return new ProxyPoint { X = fontInfo.Layout.Width, Y = fontInfo.Layout.Height };
    }
    
    public object MeasureString(object font, string text)
    {
        return MeasureString(font, GetFontSize(font), text);
    }
}
