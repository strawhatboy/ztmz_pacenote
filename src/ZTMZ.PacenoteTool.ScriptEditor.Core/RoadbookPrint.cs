using System.Globalization;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed record RoadbookPrintOptions(
    double PaperWidthMillimeters,
    double PaperHeightMillimeters,
    bool Landscape,
    double HorizontalMarginMillimeters,
    double VerticalMarginMillimeters,
    string PageRanges = "",
    int Copies = 1,
    string PrinterName = "",
    bool ShowPageNumbers = true,
    RoadbookPageNumberStyle PageNumberStyle = RoadbookPageNumberStyle.CurrentAndTotal,
    RoadbookPageNumberPosition PageNumberPosition = RoadbookPageNumberPosition.BottomCenter);

public enum RoadbookPageNumberStyle
{
    CurrentAndTotal,
    Current
}

public enum RoadbookPageNumberPosition
{
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum RoadbookPrintResult
{
    Succeeded,
    PrinterUnavailable,
    OtherError
}

public static class RoadbookPrintCssBuilder
{
    public static string BuildPageStyle(RoadbookPrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var paperWidth = options.Landscape
            ? options.PaperHeightMillimeters
            : options.PaperWidthMillimeters;
        var paperHeight = options.Landscape
            ? options.PaperWidthMillimeters
            : options.PaperHeightMillimeters;
        var horizontalMargin = options.HorizontalMarginMillimeters
            .ToString("0.###", CultureInfo.InvariantCulture);
        var verticalMargin = options.VerticalMarginMillimeters
            .ToString("0.###", CultureInfo.InvariantCulture);
        var paperWidthText = paperWidth.ToString("0.###", CultureInfo.InvariantCulture);
        var paperHeightText = paperHeight.ToString("0.###", CultureInfo.InvariantCulture);
        var pageRule = $"size: {paperWidthText}mm {paperHeightText}mm; " +
                       $"margin: {verticalMargin}mm {horizontalMargin}mm;";

        if (!options.ShowPageNumbers)
        {
            return $"@page {{ {pageRule} }}";
        }

        var marginBox = options.PageNumberPosition switch
        {
            RoadbookPageNumberPosition.BottomLeft => "@bottom-left",
            RoadbookPageNumberPosition.BottomRight => "@bottom-right",
            _ => "@bottom-center"
        };
        var content = options.PageNumberStyle == RoadbookPageNumberStyle.Current
            ? "counter(page)"
            : "counter(page) ' / ' counter(pages)";
        return $"@page {{ {pageRule} {marginBox} {{ content: {content}; color: #5f6368; font-family: sans-serif; font-size: 9pt; }} }}";
    }
}
