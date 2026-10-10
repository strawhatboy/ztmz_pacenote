namespace ZTMZ.PacenoteTool.KT;

/// <summary>Independent sound-detail settings, as selected by WRC7's playback code.</summary>
internal sealed record Wrc7PacenoteModes(bool? Direction, bool? Hairpin, bool? Hazard)
{
    public static readonly Wrc7PacenoteModes Unknown = new(null, null, null);
    public bool? ForCategory(string category) => category switch
    {
        "HAIRPIN" => Hairpin,
        "DANGER" => Hazard,
        _ => Direction
    };
}
