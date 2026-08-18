using System;
using System.Threading.Tasks;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

public partial class DownloadFileDialog : ContentDialog
{
    private readonly TemporaryFileDownloadService _downloadService;

    public DownloadFileDialog(
        ContentDialogHost dialogHost,
        TemporaryFileDownloadService downloadService)
        : base(dialogHost)
    {
        InitializeComponent();
        _downloadService = downloadService;
    }

    public async Task<string> DownloadAsync(string url)
    {
        tb_file.Text = string.Format(
            I18NLoader.Instance["dialog.downloadFile.file"],
            "(1/1)",
            url);

        var progress = new Progress<FileDownloadProgress>(value =>
        {
            pb.IsIndeterminate = value.Percentage == null;
            if (value.Percentage is double percentage)
            {
                pb.Value = percentage;
            }

            var total = value.TotalBytes is long totalBytes
                ? $"{totalBytes / 1024d / 1024d:0.00} MB"
                : "? MB";
            tb_speed.Text = string.Format(
                "{0}, {1:0.00} KB/s, {2:0.00} MB / {3}",
                value.Percentage is double percent ? $"{percent:0}%" : "...",
                value.BytesPerSecond / 1024d,
                value.BytesReceived / 1024d / 1024d,
                total);
        });

        return await _downloadService.DownloadAsync(
            url,
            "ztmz-update",
            ".exe",
            progress);
    }

    public void CloseAfterDownload()
    {
        OnButtonClick(ContentDialogButton.Close);
    }
}
