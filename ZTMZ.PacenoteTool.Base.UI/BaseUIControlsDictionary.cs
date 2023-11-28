using System;
using System.Windows;
using System.Windows.Markup;

namespace ZTMZ.PacenoteTool.Base.UI;

[Localizability(LocalizationCategory.Ignore)]
[Ambient]
[UsableDuringInitialization(true)]
public class BaseUIControlsDictionary : ResourceDictionary
{
    private const string DictionaryUri = "pack://application:,,,/ZTMZ.PacenoteTool.Base.UI;component/Res/BaseUi.xaml";

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseUIControlsDictionary"/> class.
    /// Default constructor defining <see cref="ResourceDictionary.Source"/> of the <c>WPF UI</c> controls dictionary.
    /// </summary>
    public BaseUIControlsDictionary()
    {
        Source = new Uri(DictionaryUri, UriKind.Absolute);
    }
}

