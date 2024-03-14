
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePackagePageVM(ZTMZPacenoteTool tool) : ObservableObject {
    
    [ObservableProperty]
    private string _voicePackagePath = "";

    partial void OnVoicePackagePathChanged(string value)
    {
        // update the view model
        var coDriverPkg = tool.CoDriverPackages.FirstOrDefault(p => p.Info.Path == value);
        if (coDriverPkg != null)
        {
            HeaderContent = coDriverPkg.Info.DisplayText;
        }
    }

    [ObservableProperty]
    private string _headerContent;
}
