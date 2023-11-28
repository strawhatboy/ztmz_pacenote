
using System.Windows;

namespace ZTMZ.PacenoteTool.Base.UI;

public static class I18NHelper {
    public static void ApplyI18NToApplication() {
        
        var CurrentDict = new ResourceDictionary();
        
        foreach (var key in I18NLoader.Instance.CurrentCulture.Keys)
        {
            CurrentDict.Add(key, I18NLoader.Instance.CurrentCulture[key]);
        }
        Application.Current.Resources.MergedDictionaries.Add(CurrentDict);
    }
}

