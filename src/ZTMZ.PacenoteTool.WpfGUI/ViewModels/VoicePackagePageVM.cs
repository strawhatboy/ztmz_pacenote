// This is the page for each voice package, including the list of tokens
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

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
        HeaderContent = CodriverPackage?.Info.DisplayText ?? string.Empty;
    }

    [ObservableProperty]
    private ObservableCollection<object> _dataContent = new();

    [ObservableProperty]
    private string _headerContent;

    [ObservableProperty]
    private bool _isLoading;

    private int _contentLoadVersion;

    [RelayCommand]
    private void Listen(string filePath)
    {
        tool.PlaySound(filePath);
    }
    private static string bool2str(bool b)
    {
        return b ? "✅" : "❌";
    }

    public VoicePackagePageVM(ZTMZPacenoteTool tool)
    {
        this.tool = tool;
    }

    public async Task OnNavigatedToAsync()
    {
        var loadVersion = Interlocked.Increment(ref _contentLoadVersion);
        var codriverPackage = CodriverPackage;
        var dataContent = new ObservableCollection<object>();
        DataContent = dataContent;
        IsLoading = codriverPackage != null;

        if (codriverPackage == null)
        {
            return;
        }

        try
        {
            // Let the new page paint before any package work starts.
            await Dispatcher.Yield(DispatcherPriority.Background);

            var items = await Task.Run(() => BuildDataContent(codriverPackage));
            if (loadVersion == Volatile.Read(ref _contentLoadVersion))
            {
                await AddItemsInBatchesAsync(dataContent, items, loadVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load co-driver package details for {0}", codriverPackage.Info.Path);
            if (loadVersion == Volatile.Read(ref _contentLoadVersion))
            {
                dataContent.Clear();
            }
        }
        finally
        {
            if (loadVersion == Volatile.Read(ref _contentLoadVersion))
            {
                IsLoading = false;
            }
        }
    }

    public Task OnNavigatedFromAsync()
    {
        Interlocked.Increment(ref _contentLoadVersion);
        IsLoading = false;
        return Task.CompletedTask;
    }

    private async Task AddItemsInBatchesAsync(
        ObservableCollection<object> dataContent,
        IReadOnlyList<object> items,
        int loadVersion)
    {
        const int batchSize = 16;

        for (var offset = 0; offset < items.Count; offset += batchSize)
        {
            if (loadVersion != Volatile.Read(ref _contentLoadVersion))
            {
                return;
            }

            var end = Math.Min(offset + batchSize, items.Count);
            for (var index = offset; index < end; index++)
            {
                dataContent.Add(items[index]);
            }

            // Queue the next batch behind rendering and input processing.
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private static List<object> BuildDataContent(CoDriverPackage codriverPackage)
    {
        var scriptResource = Base.Script.ScriptResource.Instance;
        var dataContent = new List<object>();

        foreach (var pacenoteDef in scriptResource.Pacenotes)
        {
            var isAvailable = codriverPackage.id2tokensPath.TryGetValue(pacenoteDef.id, out var tokensPath)
                && tokensPath.Count > 0;
            var files = isAvailable
                ? tokensPath.Select((path, index) => new { Index = index + 1, FilePath = path }).Cast<object>().ToList()
                : new List<object>();
            dataContent.Add(new
            {
                id = pacenoteDef.id,
                Token = scriptResource.FilenameDict[pacenoteDef.id].First(),
                TokenDescription = pacenoteDef.description,
                IsAvailable = bool2str(isAvailable),
                Type = scriptResource.TypeDict[pacenoteDef.type].name,
                FilesCount = files.Count,
                Files = files
            });
        }

        return dataContent;
    }
}
