
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZTMZ.PacenoteTool.WpfGUI.Helpers;

/// <summary>
/// Converts a percentage to a color, linearly
/// 0% = Red
/// 100% = Green
/// </summary>
internal class PercentageToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not float percentage)
            return null;

        if (percentage < 0)
            percentage = 0;
        if (percentage > 1)
            percentage = 1;

        var red = percentage < 0.5 ? (byte)255 : (byte)(256 - (percentage - 0.5) * 512);
        var green = percentage > 0.5 ? (byte)255 : (byte)(percentage * 512);

        return new SolidColorBrush(Color.FromArgb(255, red, green, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
