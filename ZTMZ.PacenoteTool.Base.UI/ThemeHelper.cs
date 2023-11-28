using System.Windows.Media;

namespace ZTMZ.PacenoteTool.Base.UI;

public static class ThemeHelper {

    public static Color GetAccentColor() {
        return Color.FromRgb((byte)Config.Instance.AccentColorR, (byte)Config.Instance.AccentColorG, (byte)Config.Instance.AccentColorB);
    }
}
