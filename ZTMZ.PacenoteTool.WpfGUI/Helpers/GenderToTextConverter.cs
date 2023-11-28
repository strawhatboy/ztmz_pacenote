
using System.Globalization;
using System.Windows.Data;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Helpers;

internal class GenderToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string) {
            var str = ((string)value).ToUpper();
            if (str == "M") {
                return I18NLoader.Instance["misc.gender_male"];
            } else if (str == "F") {
                return I18NLoader.Instance["misc.gender_female"];
            }
        }

        return I18NLoader.Instance["misc.gender_unknown"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string) {
            var str = ((string)value);
            if (str == I18NLoader.Instance["misc.gender_male"]) {
                return "M";
            } else if (str == I18NLoader.Instance["misc.gender_female"]) {
                return "F";
            }
        }

        return "U";
    }
}

