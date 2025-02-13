// This is the page for each voice package, including the list of tokens
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
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.Base.Script;
using System.Windows.Threading;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePackagePageVM : ObservableObject, INavigationAware
{
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ZTMZPacenoteTool tool;

    [ObservableProperty]
    private string _voicePackagePath = "";

    [ObservableProperty]
    private CoDriverPackage _codriverPackage;

    partial void OnVoicePackagePathChanged(string value)
    {
        CodriverPackage = tool.CoDriverPackages.FirstOrDefault(p => p.Info.Path == VoicePackagePath);
        HeaderContent = CodriverPackage.Info.DisplayText;
    }

    [ObservableProperty]
    private IList<object> _dataContent = new ObservableCollection<object>(); // data content for the DataGrid

    private object _collectionLock = new object();

    [ObservableProperty]
    private string _headerContent;

    [RelayCommand]
    private void Listen(string filePath)
    {
        tool.PlaySound(filePath);
    }
    private string bool2str(bool b)
    {
        return b ? "✅" : "❌";
    }

    public VoicePackagePageVM(ZTMZPacenoteTool tool)
    {
        this.tool = tool;
        BindingOperations.EnableCollectionSynchronization(DataContent, _collectionLock);
    }

    public async void OnNavigatedTo()
    {
        DataContent.Clear();
        // update the view model
        if (CodriverPackage == null)
        {
            return;
        }

        // Task.Run(() => {

        // update the view
        foreach (var pacenoteDef in Base.Script.ScriptResource.Instance.Pacenotes)
        {
            if (CodriverPackage.id2tokensPath.TryGetValue(pacenoteDef.id, out var tokensPath))
            {
                if (tokensPath.Count > 0)
                {
                    var item = new
                    {
                        id = pacenoteDef.id,
                        Token = Base.Script.ScriptResource.Instance.FilenameDict[pacenoteDef.id].First(),
                        TokenDescription = pacenoteDef.description,
                        IsAvailable = bool2str(true),
                        Type = Base.Script.ScriptResource.Instance.TypeDict[pacenoteDef.type].name,
                        FilesCount = tokensPath.Count,
                        Files = tokensPath.Select((s, index) =>
                        new { Index = index + 1, FilePath = s }),
                    };
                    DataContent.Add(item);
                }
            }
            else
            {
                // missing token in the voice package
                var item = new
                {
                    id = pacenoteDef.id,
                    Token = Base.Script.ScriptResource.Instance.FilenameDict[pacenoteDef.id].First(),
                    TokenDescription = pacenoteDef.description,
                    IsAvailable = bool2str(false),
                    Type = Base.Script.ScriptResource.Instance.TypeDict[pacenoteDef.type].name,
                    FilesCount = 0,
                    Files = new List<object>()
                };
                DataContent.Add(item);
            }
        }
    }

    public async void OnNavigatedFrom()
    {
        // throw new NotImplementedException();
    }
}
