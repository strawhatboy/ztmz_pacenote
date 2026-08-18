using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.WpfGUI.Views;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;


public class UpdateService {

    private readonly IContentDialogService _contentDialogService;
    private readonly ApplicationUpdateQueryService _updateQuery;
    private readonly TemporaryFileDownloadService _updateDownload;

    public UpdateService(
        IContentDialogService contentDialogService,
        ApplicationUpdateQueryService updateQuery,
        TemporaryFileDownloadService updateDownload) {
        _contentDialogService = contentDialogService;
        _updateQuery = updateQuery;
        _updateDownload = updateDownload;
    }

    string updateURL = Config.Instance.UpdateDefinitionUrl;

    string betaUpdateURL = Config.Instance.UpdateDefinitionUrl_Beta;

    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

    public UpdateFile? UpdateFile { private set; get; }

    public async Task<UpdateFile?> CheckUpdate()
    {
        var updateFile = await CheckForUpdate();
        if (updateFile == null)
        {
            return null;
        }

        return await ShowUpdateDialogAsync(updateFile) ? updateFile : null;
    }

    public async Task<UpdateFile?> CheckForUpdate()
    {
        var skippedVersion = Version.TryParse(Config.Instance.SkippedVersion, out var skipped)
            ? skipped
            : null;
        var definition = await _updateQuery.FindUpdateAsync(
            new ApplicationUpdateQueryOptions(
                updateURL,
                betaUpdateURL,
                new Version(CurrentVersion),
                skippedVersion,
                Config.Instance.OptInBetaPlan || ToolUtils.GetToolVersion() == ToolVersion.TEST));
        var updateFile = definition == null ? null : UpdateFile.FromDefinition(definition);
        UpdateFile = updateFile;
        return updateFile;
    }

    public async Task<bool> ShowUpdateDialogAsync(UpdateFile updateFile)
    {
        var changelogUrl = updateFile.ResolveChangelogUrl(Config.Instance.Language);
        var dialog = new NewUpdateDialog(
            _contentDialogService.GetDialogHostEx()!,
            updateFile.version,
            CurrentVersion,
            changelogUrl);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        await Update(updateFile);
        return true;
    }

    public async Task Update(UpdateFile f)
    {
        var dialog = new DownloadFileDialog(
            _contentDialogService.GetDialogHostEx()!,
            _updateDownload);
        var dialogTask = dialog.ShowAsync();
        try
        {
            var downloadedFile = await dialog.DownloadAsync(f.url);
            dialog.CloseAfterDownload();
            await dialogTask;

            Process.Start(new ProcessStartInfo(downloadedFile));
            System.Windows.Application.Current.Shutdown();
        }
        catch
        {
            dialog.CloseAfterDownload();
            await dialogTask;
            throw;
        }
    }
}
