using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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

    public UpdateService(IContentDialogService contentDialogService) {
        _contentDialogService = contentDialogService;
    }

    const string updateURL = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate.json";

    const string betaUpdateURL = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate_beta.json";


    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

    public UpdateFile UpdateFile { private set; get; }

    public async Task<UpdateFile> CheckUpdate() 
    {
        var updateFile = await CheckUpdate(updateURL);
        if (updateFile == null && Config.Instance.OptInBetaPlan || ToolUtils.GetToolVersion() == ToolVersion.TEST) 
        {
            // when there's no new stable version and user opt in beta plan, 
            // or it is beta version running now, we will check beta update
            updateFile = await CheckUpdate(betaUpdateURL);
        }

        return updateFile;
    }

    public async Task<UpdateFile> CheckUpdate(string url)
    {
        using (HttpClient w = new HttpClient())
        {
            var json = await w.GetStringAsync(url);
            if (json == null)
            {
                return null;
            }

            var versionFile = JsonConvert.DeserializeObject<UpdateFile>(json);
            this.UpdateFile = versionFile;
            // compare version
            var newVersion = new Version(versionFile.version);
            var skippedVersion = new Version(Config.Instance.SkippedVersion);
            var minVersionSupported = new Version(versionFile.minVersionSupported);
            var myVersion = new Version(CurrentVersion);

            if (newVersion.Equals(skippedVersion))
            {
                // this version is set to be skipped.
                return null;
            }

            if (myVersion.CompareTo(minVersionSupported) < 0)
            {
                return null;
            }

            if (myVersion.CompareTo(newVersion) < 0)
            {
                // need update
                // show new update dialog
                NewUpdateDialog nud = new NewUpdateDialog(_contentDialogService.GetContentPresenter(), versionFile.version, CurrentVersion, versionFile.changelog);

                ContentDialogResult dres = await nud.ShowAsync();
                if (dres == ContentDialogResult.Primary)
                {
                    // update
                    await this.Update(versionFile);
                    return versionFile;
                } else {
                }
            }

            return null;
        }
    }

    public async Task Update(UpdateFile f)
    {
        DownloadFileDialog dfd = new DownloadFileDialog(_contentDialogService.GetContentPresenter(), f);
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

