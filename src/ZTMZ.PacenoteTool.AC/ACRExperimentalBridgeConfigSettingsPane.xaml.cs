using System;
using System.Windows;
using System.Windows.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI.Game;

namespace ZTMZ.PacenoteTool.AC;

[GameConfigPane(typeof(ACRExperimentalBridgeConfig))]
public partial class ACRExperimentalBridgeConfigSettingsPane : IGameConfigSettingsPane
{
    private readonly ACRExperimentalBridgeConfig _config;
    private readonly DispatcherTimer _statusRefreshTimer;
    private IGame? _game;
    private bool _isRefreshing;

    public ACRExperimentalBridgeConfigSettingsPane(ACRExperimentalBridgeConfig config)
    {
        _config = config;
        InitializeComponent();
        _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusRefreshTimer.Tick += (_, _) => RefreshBridgeStatus();
        Loaded += (_, _) => _statusRefreshTimer.Start();
        Unloaded += (_, _) => _statusRefreshTimer.Stop();
        RefreshFromConfiguration();
    }

    public override void InitializeWithGame(IGame game)
    {
        _game = game;
        RefreshBridgeStatus();
    }

    public override void RefreshFromConfiguration()
    {
        _isRefreshing = true;
        EnableControlsCheckBox.IsChecked = _config.ControlsEnabled;
        _isRefreshing = false;
        RefreshBridgeStatus();
    }

    private async void EnableControlsCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["game.acr.experimental.warningTitle"],
            Content = I18NLoader.Instance["game.acr.experimental.enableWarning"],
            PrimaryButtonText = I18NLoader.Instance["game.acr.experimental.confirm"],
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
        }.ShowDialogAsync();
        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            _isRefreshing = true;
            EnableControlsCheckBox.IsChecked = false;
            _isRefreshing = false;
            return;
        }

        _config.ControlsEnabled = true;
        PersistConfiguration();
        RefreshBridgeStatus();
        if (_game is not null)
        {
            GameRuntimeNotifications.NotifyLocalBridgeControlsEnabled(_game);
        }
    }

    private void EnableControlsCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        _config.ControlsEnabled = false;
        PersistConfiguration();
        RefreshBridgeStatus();
    }

    private async void InjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_config.ControlsEnabled)
        {
            return;
        }

        var confirmation = await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["game.acr.experimental.warningTitle"],
            Content = I18NLoader.Instance["game.acr.experimental.injectWarning"],
            PrimaryButtonText = I18NLoader.Instance["game.acr.experimental.inject"],
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
        }.ShowDialogAsync();
        if (confirmation != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        InjectButton.IsEnabled = false;
        try
        {
            var result = await ACRExperimentalBridgeController.InjectAsync();
            BridgeStatusTextBlock.Text = result.Message;
            if (result.Succeeded && _game is not null)
            {
                GameRuntimeNotifications.NotifyLocalBridgeInjected(_game);
            }
        }
        finally
        {
            RefreshBridgeStatus();
        }
    }

    private void RefreshBridgeStatus()
    {
        if (!_config.ControlsEnabled)
        {
            BridgeStatusTextBlock.Text = I18NLoader.Instance["game.acr.experimental.disabled"];
            InjectButton.IsEnabled = false;
            return;
        }

        var status = ACRExperimentalBridgeController.Inspect();
        BridgeStatusTextBlock.Text = ACRExperimentalBridgeController.FormatStatus(status);
        InjectButton.IsEnabled = status.BundleAvailable && status.GameRunning && !status.AlreadyInjected;
    }

    private void PersistConfiguration()
    {
        if (_game is not null)
        {
            Config.Instance.SaveGameConfig(_game);
        }
    }
}
