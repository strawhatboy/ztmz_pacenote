using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Services;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPageVM : ObservableObject, INavigationAware
{
    private readonly INavigationService navigationService;

    public ReplayPageVM(INavigationService navigationService)
    {
        this.navigationService = navigationService;
    }


    [RelayCommand]
    private void NavigateForward(Type type)
    {
        _ = navigationService.NavigateWithHierarchy(type);
    }
    
    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }
}
