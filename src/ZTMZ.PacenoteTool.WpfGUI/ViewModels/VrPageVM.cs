
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
using System.Diagnostics;
using VRGameOverlay.VROverlayWindow;
using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VrPageVM : ObservableObject {

    private VRGameOverlayManager _vrGameOverlayManager;

    public VrPageVM(VRGameOverlayManager vRGameOverlayManager)
    {
        _vrGameOverlayManager = vRGameOverlayManager;

        _collectionLock = new object();
        _vrWindowList = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(_vrWindowList, _collectionLock);

        // refresh window list
        RefreshvrWindowList();
    }

    [ObservableProperty]
    private bool _vrShowOverlay = Config.Instance.VrShowOverlay;

    partial void OnVrShowOverlayChanged(bool value)
    {
        Config.Instance.VrShowOverlay = value;
        Config.Instance.SaveUserConfig();
    }
    
    [ObservableProperty]
    private bool _vrUseZTMZHud = Config.Instance.VrUseZTMZHud;

    partial void OnVrUseZTMZHudChanged(bool value)
    {
        VrNotUseZTMZHud = !value;
        Config.Instance.VrUseZTMZHud = value;
        if (Config.Instance.VrUseZTMZHud) {
            Config.Instance.VrOverlayWindowName = Constants.HUD_WINDOW_NAME;
            _vrGameOverlayManager.UpdateOverlayWindow();
        }
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _vrNotUseZTMZHud = !Config.Instance.VrUseZTMZHud;

    [ObservableProperty]
    private ObservableCollection<object> _vrWindowList;

    private object _collectionLock;

    [RelayCommand]
    public void RefreshvrWindowList() 
    {
        _vrWindowList.Clear();
        Task.Run(() => {
            var windows = Win32Stuff.FindWindows();
            foreach (var wnd in windows)
            {
                string windowName = Win32Stuff.GetWindowText(wnd);
                if (!string.IsNullOrWhiteSpace(windowName))
                {
                    _vrWindowList.Add(new { Name = windowName, Handle = wnd });
                }
                if (windowName == Config.Instance.VrOverlayWindowName) {
                    VrSelectedWindow = _vrWindowList.Last();
                }
            }
        });
    }

    [RelayCommand]
    public void SavevrSettings() {
        Config.Instance.SaveUserConfig();
        _vrGameOverlayManager.UpdateOverlayWindow();
    }

    [ObservableProperty]
    private object _vrSelectedWindow;

    partial void OnVrSelectedWindowChanged(object value)
    {
        Config.Instance.VrOverlayWindowName = (string) value.GetType().GetProperty("Name").GetValue(value);
        Config.Instance.SaveUserConfig();
        // _vrGameOverlayManager.UpdateOverlayWindow();
    }

    [ObservableProperty]
    private float _vrOverlayPositionX = Config.Instance.VrOverlayPositionX;

    partial void OnVrOverlayPositionXChanged(float value)
    {
        Config.Instance.VrOverlayPositionX = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayPositionY = Config.Instance.VrOverlayPositionY;

    partial void OnVrOverlayPositionYChanged(float value)
    {
        Config.Instance.VrOverlayPositionY = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayPositionZ = Config.Instance.VrOverlayPositionZ;

    partial void OnVrOverlayPositionZChanged(float value)
    {
        Config.Instance.VrOverlayPositionZ = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayRotationX = Config.Instance.VrOverlayRotationX;

    partial void OnVrOverlayRotationXChanged(float value)
    {
        Config.Instance.VrOverlayRotationX = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayRotationY = Config.Instance.VrOverlayRotationY;

    partial void OnVrOverlayRotationYChanged(float value)
    {
        Config.Instance.VrOverlayRotationY = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayRotationZ = Config.Instance.VrOverlayRotationZ;

    partial void OnVrOverlayRotationZChanged(float value)
    {
        Config.Instance.VrOverlayRotationZ = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _vrOverlayScale = Config.Instance.VrOverlayScale;

    partial void OnVrOverlayScaleChanged(float value)
    {
        Config.Instance.VrOverlayScale = value;
        Config.Instance.SaveUserConfig();
    }

}
