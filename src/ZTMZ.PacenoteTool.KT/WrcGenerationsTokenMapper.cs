using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.KT;

public static class WrcGenerationsTokenMapper
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["_and"]="and", ["_into"]="into", ["_for"]="into",
        ["ACUTE_HAIRPIN_LEFT"]="left_acute", ["ACUTE_HAIRPIN_RIGHT"]="right_acute",
        // PN_DonutCounter -> frame 93 -> XL_Turns_L_TurnAround: counterclockwise.
        ["DONUT"]="reverse_donut",
        // WRCG's LR/RL chicanes name the first turn; ZTMZ names the entry side.
        // Explicit LEFTENTRY/RIGHTENTRY already describe that side: do not invert them.
        ["CHICANE_LEFT"]="right_entry_chicane", ["CHICANE_LEFTENTRY"]="left_entry_chicane",
        ["CHICANE_RIGHT"]="left_entry_chicane", ["CHICANE_RIGHTENTRY"]="right_entry_chicane",
        ["LEFT_RIGHT"]="twisty_left", ["RIGHT_LEFT"]="twisty_right",
        ["FAST_KINKS"]="fast/kinks", ["SLOW_KINKS"]="slow/kinks",
        ["BUMP"]="bumps", ["bumpy"]="bumps", ["caution2"]="caution", ["danger2"]="double_caution",
        ["def_dont_cut"]="important/dont_cut", ["downhill2"]="downhill", ["uphill2"]="uphill",
        // A linking instruction between corners, not an instruction to take the inside line.
        ["hard_brake"]="hard/brake", ["jump2"]="jump", ["keep_into"]="hold/into",
        ["narrows2"]="narrows", ["off_camber"]="bad_camber", ["open"]="opens",
        ["over_bump"]="over_bumps", ["over_rails"]="over/rails", ["slippy"]="slippery",
        ["very_long"]="verylong", ["very_narrow"]="very/narrow", ["widens2"]="widens",
        ["UNDER_BRIDGE"]="under_bridge", ["after_junction"]="after/junction",
        ["around_bale"]="around_bales", ["around_rock"]="around/rocks", ["around_panels"]="around_sign",
        ["junction_left"]="junction/left", ["junction_right"]="junction/right",
        ["go_right"]="right", ["go_left"]="left", ["medium"]="half_long",
        ["hay_in"]="bales/inside", ["hay_out"]="bales/outside",
        ["rock_in"]="rocks_inside", ["rock_out"]="rocks_outside",
        ["tree_in"]="tree_inside", ["tree_out"]="tree_outside",
        ["fence_in"]="fence_inside", ["fence_out"]="fence_outside",
        ["wall_in"]="wall_inside", ["wall_out"]="wall_outside",
        ["house_in"]="house/inside", ["house_out"]="house/outside", ["sign_in"]="sign/inside",
        ["WATER"]="ford"
    };

    // Only return tokens that actually exist in the loaded official/custom vocabulary.
    public static bool TryMap(string atom, IReadOnlySet<string> vocabulary, out string[] tokens)
    {
        string value;
        var turn = Regex.Match(atom, @"^(LEFT|RIGHT)_([1-6])(_SHORT)?$", RegexOptions.CultureInvariant);
        var alternating = Regex.Match(atom, @"^(LEFT_RIGHT|RIGHT_LEFT)_([1-6])$", RegexOptions.CultureInvariant);
        if (turn.Success)
            value = turn.Groups[2].Value + "_" + turn.Groups[1].Value.ToLowerInvariant() + (turn.Groups[3].Success ? "/short" : "");
        else if (alternating.Success)
        {
            // These use PN_Twisty_L/R, not two independently graded corners.
            value = (alternating.Groups[1].Value == "LEFT_RIGHT" ? "twisty_left/" : "twisty_right/") + alternating.Groups[2].Value;
        }
        else if (Regex.IsMatch(atom, @"^m\d+$")) value = atom[1..];
        else if (Regex.IsMatch(atom, @"^tightens_[1-7]$")) value = "tightens_to_" + atom[^1];
        else value = Aliases.GetValueOrDefault(atom, atom.ToLowerInvariant());
        tokens = value.Split('/');
        if (tokens.All(vocabulary.Contains)) return true;
        tokens = [];
        return false;
    }
}
