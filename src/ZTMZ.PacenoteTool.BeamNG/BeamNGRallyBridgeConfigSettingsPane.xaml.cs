using System;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

[GameConfigPane(typeof(BeamNGRallyBridgeConfig))]
public partial class BeamNGRallyBridgeConfigSettingsPane : IGameConfigSettingsPane
{
    private readonly BeamNGRallyBridgeConfig _config;
    private readonly DispatcherTimer _statusRefreshTimer;
    private IGame? _game;
    private bool _isInitializing;

    public BeamNGRallyBridgeConfigSettingsPane(BeamNGRallyBridgeConfig config)
    {
        _config = config;
        _isInitializing = true;
        InitializeComponent();
        _statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusRefreshTimer.Tick += StatusRefreshTimer_OnTick;
        Loaded += SettingsPane_OnLoaded;
        Unloaded += SettingsPane_OnUnloaded;
        UserFolderPathTextBox.Text = _config.UserFolderPath;
        _isInitializing = false;
        RefreshBridgeStatus();
    }

    public override void InitializeWithGame(IGame game)
    {
        _game = game;
        RefreshBridgeStatus();
    }

    public override void RefreshFromConfiguration()
    {
        _isInitializing = true;
        UserFolderPathTextBox.Text = _config.UserFolderPath;
        _isInitializing = false;
        RefreshBridgeStatus();
    }

    private void UserFolderPathTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.UserFolderPath = UserFolderPathTextBox.Text.Trim();
        PersistConfiguration();
        RefreshBridgeStatus();
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
            PersistConfiguration();
            RefreshBridgeStatus();
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["game.beamng.bridge.install"],
                Content = string.Format(I18NLoader.Instance["game.beamng.bridge.installed"], installedPath),
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
        }
        catch (Exception exception)
        {
            RefreshBridgeStatus();
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

    private void SettingsPane_OnLoaded(object sender, RoutedEventArgs e)
    {
        _statusRefreshTimer.Start();
        RefreshBridgeStatus();
    }

    private void SettingsPane_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _statusRefreshTimer.Stop();
    }

    private void StatusRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshBridgeStatus();
    }

    private void RefreshBridgeStatus()
    {
        if (!BeamNGUserFolderLocator.IsValidUserFolder(_config.UserFolderPath))
        {
            BridgeStatusTextBlock.Text = I18NLoader.Instance["game.beamng.bridge.statusUserFolderRequired"];
            return;
        }

        var bridgeInstallation = BeamNGBridgeInstaller.GetInstallationInfo(_config);
        if (bridgeInstallation.State == BeamNGBridgeInstallationState.Missing)
        {
            BridgeStatusTextBlock.Text = I18NLoader.Instance["game.beamng.bridge.statusNotInstalled"];
            return;
        }

        if (!bridgeInstallation.IsCurrent)
        {
            BridgeStatusTextBlock.Text = string.Format(
                I18NLoader.Instance["game.beamng.bridge.statusUpdateRequired"],
                bridgeInstallation.InstalledVersion?.ToString() ?? I18NLoader.Instance["game.beamng.bridge.unknownVersion"],
                bridgeInstallation.BundledVersion?.ToString() ?? I18NLoader.Instance["game.beamng.bridge.unknownVersion"]);
            return;
        }

        if (_game?.IsInitialized == true && _game.GameDataReader is BeamNGGameDataReader reader)
        {
            if (!reader.IsBridgeListening)
            {
                BridgeStatusTextBlock.Text = string.Format(
                    I18NLoader.Instance["game.beamng.bridge.statusPortUnavailable"],
                    BeamNGRallyBridgeConfig.DefaultPort);
                return;
            }

            if (reader.IsBridgeConnected)
            {
                BridgeStatusTextBlock.Text = string.Format(
                    I18NLoader.Instance["game.beamng.bridge.statusConnected"],
                    BeamNGRallyBridgeConfig.DefaultPort);
                return;
            }

            BridgeStatusTextBlock.Text = string.Format(
                I18NLoader.Instance[reader.LastBridgePacketUtc is null
                    ? "game.beamng.bridge.statusWaiting"
                    : "game.beamng.bridge.statusNoRecentData"],
                BeamNGRallyBridgeConfig.DefaultPort);
            return;
        }

        BridgeStatusTextBlock.Text = I18NLoader.Instance["game.beamng.bridge.statusInstalled"];
    }
}
