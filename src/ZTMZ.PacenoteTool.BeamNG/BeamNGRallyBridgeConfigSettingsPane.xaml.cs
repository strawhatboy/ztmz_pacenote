using System;
using Microsoft.Win32;
using System.Windows;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

[GameConfigPane(typeof(BeamNGRallyBridgeConfig))]
public partial class BeamNGRallyBridgeConfigSettingsPane : IGameConfigSettingsPane
{
    private readonly BeamNGRallyBridgeConfig _config;
    private IGame? _game;
    private bool _isInitializing;

    public BeamNGRallyBridgeConfigSettingsPane(BeamNGRallyBridgeConfig config)
    {
        _config = config;
        _isInitializing = true;
        InitializeComponent();
        UserFolderPathTextBox.Text = _config.UserFolderPath;
        ExternalPacenotesToggle.IsChecked = _config.EnableExternalPacenotes;
        _isInitializing = false;
    }

    public override void InitializeWithGame(IGame game)
    {
        _game = game;
    }

    private void UserFolderPathTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.UserFolderPath = UserFolderPathTextBox.Text.Trim();
        PersistConfiguration();
    }

    private void ExternalPacenotesToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.EnableExternalPacenotes = ExternalPacenotesToggle.IsChecked == true;
        PersistConfiguration();
    }

    private void BrowseUserFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var initialDirectory = BeamNGUserFolderLocator.IsValidUserFolder(_config.UserFolderPath)
            ? _config.UserFolderPath
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = initialDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true || dialog.FolderNames.Length == 0)
        {
            return;
        }

        UserFolderPathTextBox.Text = dialog.FolderNames[0];
    }

    private async void InstallBridgeButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistConfiguration();
        try
        {
            var installedPath = BeamNGBridgeInstaller.Install(_config);
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["game.beamng.bridge.install"],
                Content = string.Format(I18NLoader.Instance["game.beamng.bridge.installed"], installedPath),
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
        }
        catch (Exception exception)
        {
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["game.beamng.bridge.installFailed"],
                Content = exception.Message,
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
        }
    }

    private void PersistConfiguration()
    {
        if (_game is null)
        {
            return;
        }

        Config.Instance.SaveGameConfig(_game);
        RestartNeeded?.Invoke();
    }
}
