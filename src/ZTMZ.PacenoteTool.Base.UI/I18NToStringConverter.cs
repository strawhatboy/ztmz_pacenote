using System;
using System.ComponentModel;
using System.Globalization;

namespace ZTMZ.PacenoteTool.Base.UI;

public class I18NToStringConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
        if (value is string s) {
            return I18NLoader.Instance[s];
        }
        return base.ConvertFrom(context, culture, value);
    }
}
