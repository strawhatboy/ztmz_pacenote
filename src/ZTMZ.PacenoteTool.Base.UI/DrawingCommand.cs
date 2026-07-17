using System.Collections.Generic;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Gradient information for drawing commands
/// </summary>
public class GradientInfo
{
    [JsonProperty("type")]
    public string Type { get; set; } // "linear" or "radial"
    
    [JsonProperty("colors")]
    public List<uint> Colors { get; set; } = new List<uint>();
    
    [JsonProperty("x0")]
    public float X0 { get; set; }
    
    [JsonProperty("y0")]
    public float Y0 { get; set; }
    
    [JsonProperty("x1")]
    public float X1 { get; set; }
    
    [JsonProperty("y1")]
    public float Y1 { get; set; }
    
    [JsonProperty("r0")]
    public float R0 { get; set; }
    
    [JsonProperty("r1")]
    public float R1 { get; set; }
}

/// <summary>
/// Base class for all drawing commands</summary>
public abstract class DrawingCommand
{
    [JsonProperty("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Clear the canvas
/// </summary>
public class ClearCommand : DrawingCommand
{
    public override string Type => "clear";
    
    [JsonProperty("color")]
    public uint? Color { get; set; }
}

/// <summary>
/// Draw a line
/// </summary>
public class DrawLineCommand : DrawingCommand
{
    public override string Type => "drawLine";
    
    [JsonProperty("x1")]
    public float X1 { get; set; }
    
    [JsonProperty("y1")]
    public float Y1 { get; set; }
    
    [JsonProperty("x2")]
    public float X2 { get; set; }
    
    [JsonProperty("y2")]
    public float Y2 { get; set; }
    
    [JsonProperty("thickness")]
    public float Thickness { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }

}

/// <summary>
/// Draw a rectangle outline
/// </summary>
public class DrawRectangleCommand : DrawingCommand
{
    public override string Type => "drawRectangle";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("width")]
    public float Width { get; set; }
    
    [JsonProperty("height")]
    public float Height { get; set; }
    
    [JsonProperty("thickness")]
    public float Thickness { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }
}

/// <summary>
/// Fill a rectangle
/// </summary>
public class FillRectangleCommand : DrawingCommand
{
    public override string Type => "fillRectangle";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("width")]
    public float Width { get; set; }
    
    [JsonProperty("height")]
    public float Height { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }
    
    [JsonProperty("gradient")]
    public GradientInfo Gradient { get; set; }
}

/// <summary>
/// Fill a rounded rectangle
/// </summary>
public class FillRoundedRectangleCommand : DrawingCommand
{
    public override string Type => "fillRoundedRectangle";

    [JsonProperty("x")]
    public float X { get; set; }

    [JsonProperty("y")]
    public float Y { get; set; }

    [JsonProperty("width")]
    public float Width { get; set; }

    [JsonProperty("height")]
    public float Height { get; set; }

    [JsonProperty("radius")]
    public float Radius { get; set; }

    [JsonProperty("color")]
    public uint Color { get; set; }

    [JsonProperty("gradient")]
    public GradientInfo Gradient { get; set; }
}

/// <summary>
/// Draw a circle outline
/// </summary>
public class DrawCircleCommand : DrawingCommand
{
    public override string Type => "drawCircle";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("radius")]
    public float Radius { get; set; }
    
    [JsonProperty("thickness")]
    public float Thickness { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }
}

/// <summary>
/// Fill a circle
/// </summary>
public class FillCircleCommand : DrawingCommand
{
    public override string Type => "fillCircle";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("radius")]
    public float Radius { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }

    [JsonProperty("gradient")]
    public GradientInfo Gradient { get; set; }
}

/// <summary>
/// Fill an ellipse
/// </summary>
public class FillEllipseCommand : DrawingCommand
{
    public override string Type => "fillEllipse";

    [JsonProperty("x")]
    public float X { get; set; }

    [JsonProperty("y")]
    public float Y { get; set; }

    [JsonProperty("radiusX")]
    public float RadiusX { get; set; }

    [JsonProperty("radiusY")]
    public float RadiusY { get; set; }

    [JsonProperty("color")]
    public uint Color { get; set; }

    [JsonProperty("gradient")]
    public GradientInfo Gradient { get; set; }
}

/// <summary>
/// Draw text
/// </summary>
public class DrawTextCommand : DrawingCommand
{
    public override string Type => "drawText";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("text")]
    public string Text { get; set; } = "";
    
    [JsonProperty("fontName")]
    public string FontName { get; set; } = "Arial";
    
    [JsonProperty("fontSize")]
    public float FontSize { get; set; } = 14;

    [JsonProperty("fontWeight")]
    public int FontWeight { get; set; } = 400;

    [JsonProperty("fontStyle")]
    public string FontStyle { get; set; } = "normal";

    [JsonProperty("textWidth")]
    public float TextWidth { get; set; }

    [JsonProperty("textHeight")]
    public float TextHeight { get; set; }

    [JsonProperty("baseline")]
    public float Baseline { get; set; }

    [JsonProperty("lineHeight")]
    public float LineHeight { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }
}

/// <summary>
/// Draw text with background
/// </summary>
public class DrawTextWithBackgroundCommand : DrawingCommand
{
    public override string Type => "drawTextWithBackground";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("text")]
    public string Text { get; set; } = "";
    
    [JsonProperty("fontName")]
    public string FontName { get; set; } = "Arial";
    
    [JsonProperty("fontSize")]
    public float FontSize { get; set; } = 14;

    [JsonProperty("fontWeight")]
    public int FontWeight { get; set; } = 400;

    [JsonProperty("fontStyle")]
    public string FontStyle { get; set; } = "normal";

    [JsonProperty("textWidth")]
    public float TextWidth { get; set; }

    [JsonProperty("textHeight")]
    public float TextHeight { get; set; }

    [JsonProperty("baseline")]
    public float Baseline { get; set; }

    [JsonProperty("lineHeight")]
    public float LineHeight { get; set; }
    
    [JsonProperty("color")]
    public uint Color { get; set; }
    
    [JsonProperty("bgColor")]
    public uint BgColor { get; set; }
}

/// <summary>
/// Draw an image
/// </summary>
public class DrawImageCommand : DrawingCommand
{
    public override string Type => "drawImage";
    
    [JsonProperty("imageKey")]
    public string ImageKey { get; set; } = "";

    // Lets a client recover an image that was not preloaded, such as after a
    // dashboard is enabled while the web page is already connected.
    [JsonProperty("imageUrl", NullValueHandling = NullValueHandling.Ignore)]
    public string ImageUrl { get; set; }
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("width")]
    public float? Width { get; set; }
    
    [JsonProperty("height")]
    public float? Height { get; set; }
}

/// <summary>
/// Draw a region of an image
/// </summary>
public class DrawImageRegionCommand : DrawingCommand
{
    public override string Type => "drawImageRegion";
    
    [JsonProperty("imageKey")]
    public string ImageKey { get; set; } = "";

    [JsonProperty("imageUrl", NullValueHandling = NullValueHandling.Ignore)]
    public string ImageUrl { get; set; }
    
    [JsonProperty("srcX")]
    public float SrcX { get; set; }
    
    [JsonProperty("srcY")]
    public float SrcY { get; set; }
    
    [JsonProperty("srcWidth")]
    public float SrcWidth { get; set; }
    
    [JsonProperty("srcHeight")]
    public float SrcHeight { get; set; }
    
    [JsonProperty("destX")]
    public float DestX { get; set; }
    
    [JsonProperty("destY")]
    public float DestY { get; set; }
    
    [JsonProperty("destWidth")]
    public float DestWidth { get; set; }
    
    [JsonProperty("destHeight")]
    public float DestHeight { get; set; }
}

/// <summary>
/// Rotate transform
/// </summary>
public class RotateCommand : DrawingCommand
{
    public override string Type => "rotate";
    
    [JsonProperty("angle")]
    public float Angle { get; set; }
}

/// <summary>
/// Rotate at a specific point
/// </summary>
public class RotateAtCommand : DrawingCommand
{
    public override string Type => "rotateAt";
    
    [JsonProperty("angle")]
    public float Angle { get; set; }
    
    [JsonProperty("centerX")]
    public float CenterX { get; set; }
    
    [JsonProperty("centerY")]
    public float CenterY { get; set; }
}

/// <summary>
/// Translate transform
/// </summary>
public class TranslateCommand : DrawingCommand
{
    public override string Type => "translate";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
}

/// <summary>
/// Scale transform
/// </summary>
public class ScaleCommand : DrawingCommand
{
    public override string Type => "scale";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
}

/// <summary>
/// Reset transform
/// </summary>
public class ResetTransformCommand : DrawingCommand
{
    public override string Type => "resetTransform";
}

/// <summary>
/// Save graphics state
/// </summary>
public class SaveCommand : DrawingCommand
{
    public override string Type => "save";
}

/// <summary>
/// Restore graphics state
/// </summary>
public class RestoreCommand : DrawingCommand
{
    public override string Type => "restore";
}

/// <summary>
/// Set clipping region
/// </summary>
public class SetClipCommand : DrawingCommand
{
    public override string Type => "setClip";
    
    [JsonProperty("x")]
    public float X { get; set; }
    
    [JsonProperty("y")]
    public float Y { get; set; }
    
    [JsonProperty("width")]
    public float Width { get; set; }
    
    [JsonProperty("height")]
    public float Height { get; set; }
}

/// <summary>
/// Release clipping region
/// </summary>
public class ReleaseClipCommand : DrawingCommand
{
    public override string Type => "releaseClip";
}

/// <summary>
/// Fill a geometry path (polygon)
/// </summary>
public class FillGeometryCommand : DrawingCommand
{
    public override string Type => "fillGeometry";
    
    [JsonProperty("points")]
    public float[] Points { get; set; } = new float[0]; // [x1, y1, x2, y2, ...]
    
    [JsonProperty("color")]
    public uint Color { get; set; }

    [JsonProperty("gradient")]
    public GradientInfo Gradient { get; set; }
}

/// <summary>
/// Draw a geometry path outline (polygon)
/// </summary>
public class DrawGeometryCommand : DrawingCommand
{
    public override string Type => "drawGeometry";
    
    [JsonProperty("points")]
    public float[] Points { get; set; } = new float[0]; // [x1, y1, x2, y2, ...]
    
    [JsonProperty("color")]
    public uint Color { get; set; }
    
    [JsonProperty("thickness")]
    public float Thickness { get; set; }
}
