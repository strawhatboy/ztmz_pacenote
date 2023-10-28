
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class GeneralPageVM : ObservableObject {
    [ObservableProperty]
    private SolidColorBrush _accentColor = new SolidColorBrush(ThemeHelper.GetAccentColor());

    [ObservableProperty]
    private int _accentColorR = Config.Instance.AccentColorR;
    [ObservableProperty]
    private int _accentColorG = Config.Instance.AccentColorG;
    [ObservableProperty]
    private int _accentColorB = Config.Instance.AccentColorB;

    [ObservableProperty]
    private int _themeSelection = Config.Instance.UseSystemTheme ? 2 : (Config.Instance.IsDarkTheme ? 1 : 0);

    [ObservableProperty]
    private IList<object> _languages = new ObservableCollection<object>();

    [ObservableProperty]
    private int _languageSelection = 0;

    [ObservableProperty]
    private Visibility _accentColorPickerVisibility = Config.Instance.UseSystemTheme ? Visibility.Collapsed : Visibility.Visible;

    partial void OnAccentColorRChanged(int value)
    {
        Config.Instance.AccentColorR = value;
        updateAccentColor();
    }
    partial void OnAccentColorGChanged(int value)
    {
        Config.Instance.AccentColorG = value;
        updateAccentColor();
    }
    partial void OnAccentColorBChanged(int value)
    {
        Config.Instance.AccentColorB = value;
        updateAccentColor();
    }

    private Wpf.Ui.Appearance.ApplicationTheme CurrentTheme = Config.Instance.UseSystemTheme ? Wpf.Ui.Appearance.ApplicationTheme.Unknown : 
        (Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);

    private void updateAccentColor() {
        Config.Instance.SaveUserConfig();
        AccentColor.Color = ThemeHelper.GetAccentColor();
        if (!Config.Instance.UseSystemTheme) {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(), 
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
                    WindowBackdropType.Mica,
                    false);
        }
    }

    public GeneralPageVM() {
        foreach (var c in I18NLoader.Instance.culturesFullname)
        {
            Languages.Add(c);
        }
        var cindex = I18NLoader.Instance.cultures.FindIndex(a => a.Equals(Config.Instance.Language));
        if (cindex == -1)
        {
            LanguageSelection = 0;
        } else
        {
            LanguageSelection = cindex;
        }
    }

    [RelayCommand]
    private void ThemeSelectionChanged() {
        Config.Instance.IsDarkTheme = ThemeSelection == 1;
        Config.Instance.SaveUserConfig();
        var theme = selectionToTheme(ThemeSelection);
        if (CurrentTheme == theme)
            return;
        if (ThemeSelection <= 1) {

            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(), 
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme,
                WindowBackdropType.Mica,
                false);
            CurrentTheme = theme;
            Config.Instance.UseSystemTheme = false;
            Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
            AccentColorPickerVisibility = Visibility.Visible;
        } else {
            Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(Application.Current.MainWindow);
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            CurrentTheme = Wpf.Ui.Appearance.ApplicationTheme.Unknown;
            Config.Instance.UseSystemTheme = true;
            AccentColorPickerVisibility = Visibility.Collapsed;
        }
    }

    private Wpf.Ui.Appearance.ApplicationTheme selectionToTheme(int selection) {
        if (selection == 1) {
            return Wpf.Ui.Appearance.ApplicationTheme.Dark;
        } else if (selection == 0) {
            return Wpf.Ui.Appearance.ApplicationTheme.Light;
        } else {
            return Wpf.Ui.Appearance.ApplicationTheme.Unknown;
        }
    }

    partial void OnLanguageSelectionChanged(int value)
    {
        var c = I18NLoader.Instance.cultures[value];
        Config.Instance.Language = c;
        Config.Instance.SaveUserConfig();
        I18NLoader.Instance.SetCulture(c);
        I18NHelper.ApplyI18NToApplication();
    }
}
