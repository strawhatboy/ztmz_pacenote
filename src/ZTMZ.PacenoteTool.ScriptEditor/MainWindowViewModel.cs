using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public partial class MainWindowViewModel : ObservableObject
{
    private MonacoController _monacoController;
    private readonly string PACENOTE_FILTER = "路书文件(*.pacenote) | *.pacenote";
    private readonly string TITLE = "ZTMZ Club 路书脚本编辑工具v2.0";
    [ObservableProperty]
    private string _Title = "Pacenote Tool";

    [ObservableProperty]
    private string _relatedFile = string.Empty;

    [ObservableProperty]
    private bool _isSaved = false;

    public MainWindowViewModel()
    {
    }

    public void SetMonacoController(MonacoController monacoController)
    {
        this._monacoController = monacoController;
    }

    [RelayCommand]
    private async void Open()
    {
        await checkFileSaved();
        // open
        OpenFileDialog dialog = new OpenFileDialog();
        dialog.Filter = PACENOTE_FILTER;
        if (dialog.ShowDialog() == true)
        {
            this.RelatedFile = dialog.FileName;
            this.IsSaved = true;
            // this.updateTitle();
            this._monacoController.SetContentAsync(File.ReadAllText(this._relatedFile));
            // this.tryParsePacenote();
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrEmpty(this.RelatedFile))
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = PACENOTE_FILTER;
            if (dialog.ShowDialog() == true)
            {
                this.RelatedFile = dialog.FileName;
            }
            else
            {
                return;
            }
        }
        File.WriteAllText(this.RelatedFile, await this._monacoController.GetContentAsync());
        this.IsSaved = true;
        this.Title = TITLE + " - " + this.RelatedFile;
    }

    [RelayCommand]
    private async Task SaveAs() {
        SaveFileDialog dialog = new SaveFileDialog();
        dialog.Filter = PACENOTE_FILTER;
        if (dialog.ShowDialog() == true)
        {
            this.RelatedFile = dialog.FileName;
            File.WriteAllText(this.RelatedFile, await this._monacoController.GetContentAsync());
            this.IsSaved = true;
            this.Title = TITLE + " - " + this.RelatedFile;
        }
    }

    #region private methods
    private async Task checkFileSaved()
    {
        if (!this.IsSaved && !string.IsNullOrEmpty(this.RelatedFile))
        {
            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Title = "保存文件",
                Content = "当前文件尚未保存，是否先保存当前文件？",
                PrimaryButtonText = "听你的",
                // SecondaryButtonText = "Don't Save",
                CloseButtonText = "不保存"
            }.ShowDialogAsync();
            if (result == MessageBoxResult.Primary)
            {
                await Save();
            }
        }
    }
    #endregion
}

