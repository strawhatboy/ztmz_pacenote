using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base
{
    public class FileDownloader
    {
        private IEnumerable<string> files;
        public Dictionary<string, string> DownloadedFiles { get; private set; }
        Stopwatch sw = new Stopwatch();
        private int downloadingIndex = 0;
        private int downloadLength = 0;
        public bool isDownloading { set; get; }
        private bool _urlRedirected;

        public event Action<IDictionary<string, string>> DownloadComplete;

        public async Task<IDictionary<string, string>> DownloadFiles(IEnumerable<string> urls, IProgress<float> progress=null, CancellationToken token=default)
        {
            files = urls;
            downloadLength = urls.Count();
            downloadingIndex = 0;
            this.DownloadedFiles = new Dictionary<string, string>();
            while (downloadLength > 0)
            {
                isDownloading = true;
                var downloadedFile = await DownloadFile(urls.ElementAt(downloadingIndex), progress, token);
                downloadLength--;
                this.DownloadedFiles[urls.ElementAt(downloadingIndex)] = downloadedFile;
                downloadingIndex++;
            }

            isDownloading = false;
            this.DownloadComplete?.Invoke(this.DownloadedFiles);
            return this.DownloadedFiles;

        }// The event that will fire whenever the progress of the WebClient is changed

        public async Task<string> DownloadFile(string url, IProgress<float> progress=null, CancellationToken token=default)
        {
            // this.Title = string.Format(I18NLoader.Instance["dialog.downloadFile.title"],
                // String.Format("({0}/{1})", downloadingIndex, downloadLength));
            
            using (var webClient = new HttpClient())
            {
                // The variable that will be holding the url address (making sure it starts with http://)
                // Uri URL = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? new Uri(url) : new Uri("http://" + url);
                Uri URL = new Uri(url);
                if (_urlRedirected)
                {
                    // gitee's shit.
                    using (var res = await webClient.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead)) 
                    {
                        string redirUrl = res.Headers.GetValues("Location").FirstOrDefault();
                        if (redirUrl != null) {
                            URL = new Uri(redirUrl);
                        }
                    }
                }

                // Start the stopwatch which we will be using to calculate the download speed
                sw.Start();

                try
                {
                    var tmpFile = string.Format("{0}.tmp", Path.GetTempFileName());
                    this.DownloadedFiles[url] = tmpFile;
                    // Start downloading the file
                    using (var res = await webClient.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead)) {
                        var contentLength = res.Content.Headers.ContentLength;
                        using (var download = await res.Content.ReadAsStreamAsync(cancellationToken: token)) {
                            using (var fileStream = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None)) {
                                var buffer = new byte[81920];
                                var totalBytesRead = 0;
                                int bytesRead;
                                while ((bytesRead = await download.ReadAsync(buffer, 0, buffer.Length, token)) > 0) {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                                    totalBytesRead += bytesRead;
                                    if (progress != null) {
                                        progress.Report((float)totalBytesRead / contentLength.Value);
                                    }
                                }

                                if (totalBytesRead != contentLength) {
                                    throw new Exception("Downloaded file size mismatch.");
                                }

                                fileStream.Close();

                                if (progress != null) {
                                    progress.Report(1);
                                }

                                return tmpFile;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Wpf.Ui.Controls.MessageBox.(ex.Message);
                }

                sw.Stop();
            }
            return null;
        }

        private void ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            // this.tb_speed.Text = string.Format("{0}%, {1} kb/s, {2} MB / {3} MB",
            //     e.ProgressPercentage.ToString(),
            //     (e.BytesReceived / 1024d / sw.Elapsed.TotalSeconds).ToString("0.00"),
            //     (e.BytesReceived / 1024d / 1024d).ToString("0.00"),
            //     (e.TotalBytesToReceive / 1024d / 1024d).ToString("0.00"));

            // this.pb.Value = e.ProgressPercentage;
        }

        // The event that will trigger when the WebClient is completed
        private void Completed(object sender, AsyncCompletedEventArgs e)
        {
            // // Reset the stopwatch.
            // sw.Reset();

            // if (++downloadingIndex < downloadLength)
            // {
            //     // download next file.
            //     DownloadFile(files.ElementAt(downloadingIndex));
            // } else
            // {
            //     this.DownloadComplete?.Invoke(this.DownloadedFiles);
            //     this.OnButtonClick(ContentDialogButton.Close);
            // }
        }


    }
}
