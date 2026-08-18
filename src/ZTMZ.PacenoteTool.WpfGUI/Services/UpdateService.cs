using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

    public UpdateService(
        IContentDialogService contentDialogService,
        ApplicationUpdateQueryService updateQuery) {
        _contentDialogService = contentDialogService;
        _updateQuery = updateQuery;
    }

    string updateURL = Config.Instance.UpdateDefinitionUrl;

    string betaUpdateURL = Config.Instance.UpdateDefinitionUrl_Beta;

    string codriverPkgURL = Config.Instance.UpdateDefinitionUrl_AudioPkg;


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

    public async Task<List<CodriverPackageUpdateFile>> CheckCodriverPackagesUpdate(List<CoDriverPackageInfo> packages) {
        using (HttpClient w = new HttpClient()) {
            var json = await w.GetStringAsync(codriverPkgURL);
            if (json == null) {
                return null;
            }

            var updates = JsonConvert.DeserializeObject<List<CodriverPackageUpdateFile>>(json);
            if (updates == null || updates.Count == 0) {
                return null;
            }

            List<CodriverPackageUpdateFile> needUpdate = new List<CodriverPackageUpdateFile>();
            
            // compare versions
            foreach (var update in updates) {
                var updateId = update.Id;
                var pkg = packages.FirstOrDefault(p => p.id == updateId);    // a little bit slow, but it's ok
                if (pkg != null) {
                    var newVersion = new Version(update.version);
                    var myVersion = new Version(pkg.version);
                    if (myVersion.CompareTo(newVersion) < 0) {
                        // append to need update list   
                        update.needUpdate = true;
                        update.Path = pkg.Path; // keep the original path for deletion
                        update.originalVersion = pkg.version;
                    }
                    needUpdate.Add(update);
                } else {
                    update.needDownload = true;
                    needUpdate.Add(update);
                }
            }

            return needUpdate;
        }
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
        DownloadFileDialog dfd = new DownloadFileDialog(_contentDialogService.GetDialogHostEx()!, f);
        dfd.DownloadComplete += Dfd_DownloadComplete;
        dfd.DownloadFiles(new List<string> { f.url });
        await dfd.ShowAsync();
    }

    private void Dfd_DownloadComplete(IDictionary<string, string> obj)
    {
        // try to close application and update
        if (obj != null && obj.Count > 0)
        {
            Process.Start(new ProcessStartInfo(String.Format("{0}", obj.First().Value)));
            System.Windows.Application.Current.Shutdown();
        }
    }
}
