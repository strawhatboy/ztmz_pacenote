using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.Models;

namespace ZTMZ.PacenoteTool.WpfGUI.Views
{
    /// <summary>
    /// Interaction logic for DownloadFileDialog.xaml
    /// </summary>
    public partial class DownloadFileDialog : ContentDialog
    {
        private IEnumerable<string> files;
        public Dictionary<string, string> DownloadedFiles { get; private set; }
        Stopwatch sw = new Stopwatch();
        private int downloadingIndex = 0;
        private int downloadLength = 0;
        public bool isDownloading { set; get; }
        private bool _urlRedirected;

        public event Action<IDictionary<string, string>> DownloadComplete;
        public DownloadFileDialog(ContentPresenter contentPresenter, UpdateFile f) : base(contentPresenter)
        {
            InitializeComponent();
            _urlRedirected = f.urlRedirected;
        }

        public void DownloadFiles(IEnumerable<string> urls)
        {
            files = urls;
            downloadLength = urls.Count();
            downloadingIndex = 0;
            if (downloadLength > 0)
            {
                this.DownloadedFiles = new Dictionary<string, string>();
                isDownloading = true;
                DownloadFile(urls.ElementAt(downloadingIndex));
            }

        }// The event that will fire whenever the progress of the WebClient is changed

        public void DownloadFile(string url)
        {
            this.tb_title.Text = string.Format(I18NLoader.Instance["dialog.downloadFile.title"],
                String.Format("({0}/{1})", downloadingIndex, downloadLength));
            this.tb_file.Text = string.Format(I18NLoader.Instance["dialog.downloadFile.file"],
                url);
            using (var webClient = new WebClient())
            {
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(Completed);
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(ProgressChanged);

                // The variable that will be holding the url address (making sure it starts with http://)
                // Uri URL = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? new Uri(url) : new Uri("http://" + url);
                Uri URL = new Uri(url);
                if (_urlRedirected)
                {
                    // gitee's shit.
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(URL);
                    request.AllowAutoRedirect = false;
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    string redirUrl = response.Headers["Location"];
                    response.Close();
                    URL = new Uri(redirUrl);
                }

                // Start the stopwatch which we will be using to calculate the download speed
                sw.Start();

                try
                {
                    var tmpExeFile = string.Format("{0}.exe", Path.GetTempFileName());
                    this.DownloadedFiles[url] = tmpExeFile;
                    // Start downloading the file
                    webClient.DownloadFileAsync(URL, tmpExeFile);
                }
                catch (Exception ex)
                {
                    // Wpf.Ui.Controls.MessageBox.(ex.Message);
                }
            }
        }

        private void ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.tb_speed.Text = string.Format("{0}%, {1} kb/s, {2} MB / {3} MB",
                e.ProgressPercentage.ToString(),
                (e.BytesReceived / 1024d / sw.Elapsed.TotalSeconds).ToString("0.00"),
                (e.BytesReceived / 1024d / 1024d).ToString("0.00"),
                (e.TotalBytesToReceive / 1024d / 1024d).ToString("0.00"));

            this.pb.Value = e.ProgressPercentage;
        }

        // The event that will trigger when the WebClient is completed
        private void Completed(object sender, AsyncCompletedEventArgs e)
        {
            // Reset the stopwatch.
            sw.Reset();

            if (++downloadingIndex < downloadLength)
            {
                // download next file.
                DownloadFile(files.ElementAt(downloadingIndex));
            } else
            {
                this.DownloadComplete?.Invoke(this.DownloadedFiles);
                this.OnButtonClick(ContentDialogButton.Close);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
