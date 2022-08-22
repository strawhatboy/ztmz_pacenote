using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Dialog;

namespace ZTMZ.PacenoteTool
{
    public class UpdateFile
    {
        public string version { set; get; }
        public string url { set; get; }
        public bool urlRedirected { set; get; } = false;
        public string changelog { set; get; }
        public string minVersionSupported { set; get; }
    }
    public class UpdateManager
    {
        const string updateURL = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate.json";

        const string betaUpdateURL = "https://gitee.com/ztmz/ztmz_pacenote/raw/master/autoupdate_beta.json";


        public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public UpdateFile UpdateFile { private set; get; }
        public UpdateManager()
        {

        }

        public UpdateFile CheckUpdate() 
        {
            var updateFile = CheckUpdate(updateURL);
            if (updateFile == null && Config.Instance.OptInBetaPlan || ToolUtils.GetToolVersion() == ToolVersion.TEST) 
            {
                // when there's no new stable version and user opt in beta plan, 
                // or it is beta version running now, we will check beta update
                updateFile = CheckUpdate(betaUpdateURL);
            }

            return updateFile;
        }

        public UpdateFile CheckUpdate(string url)
        {
            using (WebClient w = new WebClient())
            {
                var json = w.DownloadString(url);
                if (json == null)
                {
                    return null;
                }

                var versionFile = JsonConvert.DeserializeObject<UpdateFile>(json);
                this.UpdateFile = versionFile;
                // compare version
                var newVersion = new Version(versionFile.version);
                var minVersionSupported = new Version(versionFile.minVersionSupported);
                var myVersion = new Version(CurrentVersion);

                if (myVersion.CompareTo(minVersionSupported) < 0)
                {
                    return null;
                }

                if (myVersion.CompareTo(newVersion) < 0)
                {
                    // need update
                    // show new update dialog
                    NewUpdateDialog nud = new NewUpdateDialog(versionFile.version, CurrentVersion, versionFile.changelog);
                    GoogleAnalyticsHelper.Instance.TrackPageView("Dialog - Update", "update");
                    var dres = nud.ShowDialog();
                    if (dres.HasValue && dres.Value)
                    {
                        // update
                        this.Update(versionFile);
                        GoogleAnalyticsHelper.Instance.TrackDialogEventConfirmed("update");
                        return versionFile;
                    } else {
                        GoogleAnalyticsHelper.Instance.TrackDialogEventCancelled("update");
                    }
                }

                return null;
            }
        }

        public void Update(UpdateFile f)
        {
            DownloadFileDialog dfd = new DownloadFileDialog(f);
            GoogleAnalyticsHelper.Instance.TrackPageView("Dialog - Download", "download");
            dfd.DownloadComplete += Dfd_DownloadComplete;
            dfd.DownloadFiles(new List<string> { f.url });
            dfd.ShowDialog();
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
}
