using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class StartupSplashContent : UserControl
{
    public StartupSplashContent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
