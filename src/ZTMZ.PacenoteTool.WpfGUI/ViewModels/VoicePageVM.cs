
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

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePageVM : ObservableObject {

    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;
    private readonly VoicePackagePageVM voicePackagePageVM;
    private readonly INavigationService navigationService;

    [ObservableProperty]
    private ObservableCollection<CoDriverPackageInfo> _voicePackages = new ObservableCollection<CoDriverPackageInfo>();

    private object _collectionLock = new();

    public VoicePageVM(Core.ZTMZPacenoteTool tool, 
    VoicePackagePageVM voicePackagePageVM,
    INavigationService navigationService) {
        this.tool = tool;
        this.voicePackagePageVM = voicePackagePageVM;
        this.navigationService = navigationService;

        BindingOperations.EnableCollectionSynchronization(VoicePackages, _collectionLock);

        // this.tool.onToolInitialized += () => {
            // update the view model
            VoicePackages.Clear();
            foreach (var pkg in tool.CoDriverPackages)
            {
                VoicePackages.Add(pkg.Info);
            }
        // };
    }

    [RelayCommand]
    private void NavigateForward(Type type)
    {
        _ = navigationService.NavigateWithHierarchy(type);
    }

    [RelayCommand]
    private void NavigateToVoicePackagePage(string voicePkgPath)
    {
        // TODO: how to pass the voice package path to the next page?
        // set the voice package path in the view model and then navigate to the next page
        voicePackagePageVM.VoicePackagePath = voicePkgPath;
        _ = navigationService.NavigateWithHierarchy(typeof(ZTMZ.PacenoteTool.WpfGUI.Views.VoicePackagePage));
    }

    [RelayCommand]
    private void NavigateBack()
    {
        _ = navigationService.GoBack();
    }
}
