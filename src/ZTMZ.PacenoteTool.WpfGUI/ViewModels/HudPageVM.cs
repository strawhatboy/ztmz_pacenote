
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.UI.Windows.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ZTMZ.PacenoteTool.UI.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HudPageVM : ObservableObject {

    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public int OverlayBackendIndex
    {
        get => (int)OverlayBackendSettings.Requested;
        set => _ = SelectOverlayBackendAsync(value);
    }

    private async Task SelectOverlayBackendAsync(int value)
    {
        try { await OverlayBackendSettings.SelectAsync(value); }
        catch (Exception error) { _logger.Error(error, "Could not change overlay display backend."); }
        finally { RefreshOverlayBackendSettings(); }
    }

    public string OverlayBackendStatusKey => _overlaySession.BackendDecision.StatusKey;
    public bool ShowOverlayTopMost => OverlayBackendSettings.Requested == OverlayDisplayBackend.SeparateWindow;

    private readonly NativeOverlaySessionController _overlaySession;

    private void RefreshOverlayBackendSettings()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(OverlayBackendIndex));
            OnPropertyChanged(nameof(ShowOverlayTopMost));
            OnPropertyChanged(nameof(OverlayBackendStatusKey));
        }));
    }

    [ObservableProperty]
    private float _hudFPS = Config.Instance.HudFPS;

    partial void OnHudFPSChanged(float value)
    {
        Config.Instance.HudFPS = Convert.ToInt32(value);
        _gameOverlayManager.SetFPS(Config.Instance.HudFPS);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _hudTopMost = Config.Instance.HudTopMost;

    partial void OnHudTopMostChanged(bool value)
    {
        Config.Instance.HudTopMost = value;
        _gameOverlayManager.SetTopMost(value);
        Config.Instance.SaveUserConfig();
    }
    
    [ObservableProperty]
    private bool _hudLockFPS = Config.Instance.HudLockFPS;

    partial void OnHudLockFPSChanged(bool value)
    {
        Config.Instance.HudLockFPS = value;
        if (value) {
            _gameOverlayManager.SetFPS(Config.Instance.HudFPS);
        } else {
            _gameOverlayManager.SetFPS(0);
        }
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _webDashboardEnabled = Config.Instance.WebDashboardEnabled;

    partial void OnWebDashboardEnabledChanged(bool value)
    {
        if (_isApplyingRuntimeControls)
        {
            return;
        }

        if (!_runtimeControls.SetWebDashboardEnabled(value))
        {
            ApplyRuntimeControls();
        }
    }

    [ObservableProperty]
    private bool _webDashboardBrowserLuaEnabled = Config.Instance.WebDashboardBrowserLuaEnabled;

    partial void OnWebDashboardBrowserLuaEnabledChanged(bool value)
    {
        Config.Instance.WebDashboardBrowserLuaEnabled = value;
        _gameOverlayManager.SetWebDashboardBrowserLuaEnabled(value);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _webDashboardPort = Config.Instance.WebDashboardPort;

    partial void OnWebDashboardPortChanged(int value)
    {
        Config.Instance.WebDashboardPort = value;
        if (WebDashboardEnabled)
        {
            _gameOverlayManager.ConfigureWebDashboard(true, value, WebDashboardFrameRate);
        }
        Config.Instance.SaveUserConfig();
        RefreshWebDashboardStatus();
    }

    [ObservableProperty]
    private int _webDashboardFrameRate = Config.Instance.WebDashboardFrameRate;

    public IReadOnlyList<string> WebDashboardCanvasResolutionOptions { get; } =
        global::ZTMZ.PacenoteTool.Base.WebDashboardCanvasResolution.Supported;

    [ObservableProperty]
    private string _webDashboardCanvasResolution = Config.Instance.WebDashboardCanvasResolution;

    partial void OnWebDashboardCanvasResolutionChanged(string value)
    {
        Config.Instance.WebDashboardCanvasResolution = value;
        var configuredResolution = Config.Instance.WebDashboardCanvasResolution;
        if (!string.Equals(configuredResolution, value, StringComparison.Ordinal))
        {
            WebDashboardCanvasResolution = configuredResolution;
            return;
        }

        if (WebDashboardEnabled)
        {
            _gameOverlayManager.ConfigureWebDashboard(true, WebDashboardPort, WebDashboardFrameRate);
        }
        Config.Instance.SaveUserConfig();
    }

    partial void OnWebDashboardFrameRateChanged(int value)
    {
        Config.Instance.WebDashboardFrameRate = value;
        var configuredFrameRate = Config.Instance.WebDashboardFrameRate;
        if (configuredFrameRate != value)
        {
            WebDashboardFrameRate = configuredFrameRate;
            return;
        }

        if (WebDashboardEnabled)
        {
            _gameOverlayManager.ConfigureWebDashboard(true, WebDashboardPort, configuredFrameRate);
        }
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _isWebDashboardRunning;

    [ObservableProperty]
    private string _webDashboardStatus = string.Empty;

    [ObservableProperty]
    private string _webDashboardUrl = string.Empty;

    [ObservableProperty]
    private string _webDashboardAddressesTooltip = string.Empty;

    [ObservableProperty]
    private Brush _webDashboardStatusBrush = Brushes.Gray;

    [RelayCommand(CanExecute = nameof(CanOpenWebDashboard))]
    private void OpenWebDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WebDashboardUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open WebDashboard URL: {0}", WebDashboardUrl);
        }
    }

    private bool CanOpenWebDashboard()
    {
        return IsWebDashboardRunning && Uri.TryCreate(WebDashboardUrl, UriKind.Absolute, out _);
    }

    private void RefreshWebDashboardStatus()
    {
        if (!WebDashboardEnabled)
        {
            IsWebDashboardRunning = false;
            WebDashboardStatus = I18NLoader.Instance["settings.tooltip.webDashboardStatusStopped"];
            WebDashboardStatusBrush = Brushes.Gray;
            UpdateWebDashboardUrls();
            OpenWebDashboardCommand.NotifyCanExecuteChanged();
            return;
        }

        var status = _gameOverlayManager.WebDashboardStatus;
        IsWebDashboardRunning = status.IsRunning;
        WebDashboardStatusBrush = status.State switch
        {
            WebDashboardServerState.Running => Brushes.ForestGreen,
            WebDashboardServerState.PortUnavailable or WebDashboardServerState.Error => Brushes.IndianRed,
            _ => Brushes.Gray
        };
        WebDashboardStatus = status.State switch
        {
            WebDashboardServerState.Running => I18NLoader.Instance["settings.tooltip.webDashboardStatusRunning"],
            WebDashboardServerState.PortUnavailable => I18NLoader.Instance["settings.tooltip.webDashboardStatusPortUnavailable"],
            WebDashboardServerState.Error => string.Format(
                I18NLoader.Instance["settings.tooltip.webDashboardStatusError"],
                status.ErrorMessage),
            _ => I18NLoader.Instance["settings.tooltip.webDashboardStatusStopped"]
        };
        UpdateWebDashboardUrls(
            status.Port == WebDashboardPort && !string.IsNullOrWhiteSpace(status.Url)
                ? status.Url
                : string.Empty);
        OpenWebDashboardCommand.NotifyCanExecuteChanged();
    }

    private void UpdateWebDashboardUrls(string preferredUrl = "")
    {
        var urls = WebDashboardServer.GetDashboardUrls(WebDashboardPort);
        WebDashboardUrl = !string.IsNullOrWhiteSpace(preferredUrl)
            ? preferredUrl
            : urls.FirstOrDefault() ?? string.Empty;
        WebDashboardAddressesTooltip = string.Join(
            Environment.NewLine,
            I18NLoader.Instance["settings.tooltip.webDashboardAvailableAddresses"],
            string.Join(Environment.NewLine, urls));
    }

    private void OnRuntimeControlsStateChanged(object? sender, EventArgs eventArgs)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplyRuntimeControls();
            return;
        }

        _ = dispatcher.BeginInvoke(ApplyRuntimeControls);
    }

    private void ApplyRuntimeControls()
    {
        var snapshot = _runtimeControls.GetSnapshot();
        _isApplyingRuntimeControls = true;
        try
        {
            WebDashboardEnabled = snapshot.IsWebDashboardEnabled;
        }
        finally
        {
            _isApplyingRuntimeControls = false;
        }

        RefreshWebDashboardStatus();
    }

    [ObservableProperty]
    private ObservableCollection<object> _dashboardItems = new();

    [RelayCommand]
    private void ShowInSeparateWindow() {
        _previewController.Show();
    }
    
    private GameOverlayManager _gameOverlayManager;
    private readonly WpfNativeOverlayPreviewController _previewController;

    private RuntimeControlsService _runtimeControls;

    private bool _isApplyingRuntimeControls;

    [ObservableProperty]
    private bool _isHudInitializing = false;

    public HudPageVM(
        GameOverlayManager gameOverlayManager,
        NativeOverlaySessionController overlaySession,
        RuntimeControlsService runtimeControls,
        WpfNativeOverlayPreviewController previewController) {
        _gameOverlayManager = gameOverlayManager;
        _overlaySession = overlaySession;
        _overlaySession.Changed += (_, _) => RefreshOverlayBackendSettings();
        _runtimeControls = runtimeControls;
        _previewController = previewController;
        _runtimeControls.StateChanged += OnRuntimeControlsStateChanged;
        _gameOverlayManager.OnGameOverlayInitializingStateChanged += (isInitializing) => {
            IsHudInitializing = isInitializing;
        };
        ApplyRuntimeControls();
        RefreshWebDashboardStatus();

        // maybe we should create a custom control for this, but it's working for now
        foreach (var dashboard in _gameOverlayManager.Dashboards) {
            var cardExpander = new CardExpander();
            cardExpander.Padding = new Thickness(6);
            Grid grid = new Grid();
            // 1st column for the preview image.
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            // preview image
            Wpf.Ui.Controls.Image previewImage = new Wpf.Ui.Controls.Image();
            previewImage.Source = LoadPreviewImage(dashboard);
            previewImage.Height = 50;
            previewImage.Width = 100;
            previewImage.Stretch = Stretch.UniformToFill;
            previewImage.Margin = new Thickness(0, 0, 10, 0);
            RenderOptions.SetBitmapScalingMode(previewImage, BitmapScalingMode.HighQuality);
            Grid.SetRow(previewImage, 0);
            Grid.SetColumn(previewImage, 0);
            Grid.SetRowSpan(previewImage, 2);

            StackPanel spTitleAndVersion = new StackPanel();
            spTitleAndVersion.Orientation = Orientation.Horizontal;
            spTitleAndVersion.Margin = new Thickness(0, 0, 10, 0);


            Wpf.Ui.Controls.TextBlock title = new Wpf.Ui.Controls.TextBlock();
            title.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, dashboard.Descriptor.Name);
            title.Margin = new Thickness(0, 0, 5, 0);
            title.FontTypography = FontTypography.Caption;

            Wpf.Ui.Controls.TextBlock version = new Wpf.Ui.Controls.TextBlock();
            version.Text = dashboard.Descriptor.Version;
            SetThemeForeground(version, "TextFillColorSecondaryBrush");
            version.Margin = new Thickness(0, 0, 5, 0);
            version.FontTypography = FontTypography.Caption;

            Wpf.Ui.Controls.TextBlock author = new Wpf.Ui.Controls.TextBlock();
            author.Text = dashboard.Descriptor.Author;
            SetThemeForeground(author, "TextFillColorTertiaryBrush");
            author.Margin = new Thickness(0, 0, 5, 0);
            author.FontTypography = FontTypography.Caption;

            spTitleAndVersion.Children.Add(title);
            spTitleAndVersion.Children.Add(version);
            spTitleAndVersion.Children.Add(author);
            Grid.SetRow(spTitleAndVersion, 0);
            Grid.SetColumn(spTitleAndVersion, 1);

            Wpf.Ui.Controls.TextBlock description = new Wpf.Ui.Controls.TextBlock();
            description.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, dashboard.Descriptor.Description);
            SetThemeForeground(description, "TextFillColorTertiaryBrush");
            description.FontTypography = FontTypography.Caption;
            Grid.SetRow(description, 1);
            Grid.SetColumn(description, 1);

            var outputTargetSelector = CreateDashboardOutputTargetSelector(dashboard);
            outputTargetSelector.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(outputTargetSelector, 2);
            Grid.SetRowSpan(outputTargetSelector, 2);

            grid.Children.Add(previewImage);
            grid.Children.Add(spTitleAndVersion);
            grid.Children.Add(description);
            grid.Children.Add(outputTargetSelector);

            cardExpander.Header = grid;
            cardExpander.Margin = new Thickness(0, 0, 0, 3);

            var configGrid = new Grid();
            configGrid.ColumnDefinitions.Add(new ColumnDefinition());
            configGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(60, GridUnitType.Pixel) });
            configGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(260, GridUnitType.Pixel) });

            bool enabledPropertyEncountered = false;
            // content will be the dashboard configurations
            for (var i = 0; i < dashboard.DashboardConfigurations.PropertyName.Keys.Count; i++) {
                var index = i;
                var rowIndex = i;
                var configKey = dashboard.DashboardConfigurations.PropertyName.Keys.ElementAt(i);
                
                // ignore enabled property
                if (dashboard.DashboardConfigurations.PropertyName.Keys.ElementAt(i) == "dashboards.settings.enabled") {
                    enabledPropertyEncountered = true;
                    continue;
                }

                if (enabledPropertyEncountered) {
                    rowIndex = i - 1;
                }

                var configTooltip = dashboard.DashboardConfigurations.PropertyName[configKey];
                var configValue = dashboard.DashboardConfigurations.PropertyValue[i];
                var valueRange = dashboard.DashboardConfigurations.ValueRange[i];

                configGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(40, GridUnitType.Pixel) });
                var configNameStackPanel = new StackPanel();
                var configName = new Wpf.Ui.Controls.TextBlock();
                configName.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, configKey);
                // configName.SetResourceReference(Wpf.Ui.Controls.TextBlock.ToolTipProperty, configTooltip);
                configName.HorizontalAlignment = HorizontalAlignment.Left;
                var configTooltipText = new Wpf.Ui.Controls.TextBlock();
                configTooltipText.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, configTooltip);
                SetThemeForeground(configTooltipText, "TextFillColorTertiaryBrush");
                configTooltipText.FontTypography = FontTypography.Caption;
                configNameStackPanel.Children.Add(configName);
                configNameStackPanel.Children.Add(configTooltipText);

                Grid.SetRow(configNameStackPanel, rowIndex);
                Grid.SetColumn(configNameStackPanel, 0);
                configGrid.Children.Add(configNameStackPanel);

                if (configKey == "dashboards.settings.positionH") {
                    var comboBox = new ComboBox();
                    comboBox.ItemsSource = new List<string>() { 
                        I18NLoader.Instance["dashboards.settings.left"],
                        I18NLoader.Instance["dashboards.settings.center"],
                        I18NLoader.Instance["dashboards.settings.right"]
                        };
                    comboBox.Margin = new Thickness(0, 0, 10, 0);
                    comboBox.SelectedIndex = Convert.ToInt32(configValue) + 1;
                    comboBox.SelectionChanged += (sender, args) => {
                        dashboard.DashboardConfigurations.PropertyValue[index] = comboBox.SelectedIndex - 1;
                        dashboard.SaveConfig();
                    };
                    comboBox.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(comboBox, rowIndex);
                    Grid.SetColumn(comboBox, 2);
                    configGrid.Children.Add(comboBox);
                    continue;
                } 
                if (configKey == "dashboards.settings.positionV") {
                    var comboBox = new ComboBox();
                    comboBox.ItemsSource = new List<string>() { 
                        I18NLoader.Instance["dashboards.settings.top"],
                        I18NLoader.Instance["dashboards.settings.center"],
                        I18NLoader.Instance["dashboards.settings.bottom"]
                        };
                    comboBox.Margin = new Thickness(0, 0, 10, 0);
                    comboBox.SelectedIndex = Convert.ToInt32(configValue) + 1;
                    comboBox.SelectionChanged += (sender, args) => {
                        dashboard.DashboardConfigurations.PropertyValue[index] = comboBox.SelectedIndex - 1;
                        dashboard.SaveConfig();
                    };
                    comboBox.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(comboBox, rowIndex);
                    Grid.SetColumn(comboBox, 2);
                    configGrid.Children.Add(comboBox);
                    continue;
                }

                if (configValue.GetType() == typeof(bool)) {
                    var _toggleSwitch = new ToggleSwitch();
                    _toggleSwitch.IsChecked = (bool)dashboard.GetConfigByKey(configKey);
                    // _toggleSwitch.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                    _toggleSwitch.Margin = new Thickness(0, 0, 10, 0);
                    _toggleSwitch.Click += (sender, args) => {
                        dashboard.DashboardConfigurations.PropertyValue[index] = (bool)_toggleSwitch.IsChecked;
                        dashboard.SaveConfig();
                    };
                    _toggleSwitch.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(_toggleSwitch, rowIndex);
                    Grid.SetColumn(_toggleSwitch, 2);
                    configGrid.Children.Add(_toggleSwitch);
                } else if (configValue.GetType() == typeof(string)) {
                    var textBox = new Wpf.Ui.Controls.TextBox();
                    textBox.Text = (string)dashboard.GetConfigByKey(configKey);
                    // textBox.SetBinding(Wpf.Ui.Controls.TextBox.TextProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                    textBox.TextChanged += (sender, args) => {
                        dashboard.DashboardConfigurations.PropertyValue[index] = textBox.Text;
                        dashboard.SaveConfig();
                    };
                    textBox.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(textBox, rowIndex);
                    Grid.SetColumn(textBox, 2);
                    configGrid.Children.Add(textBox);
                } else if (configValue.GetType() == typeof(float) || 
                    configValue.GetType() == typeof(double) || 
                    configValue.GetType() == typeof(int) || 
                    configValue.GetType() == typeof(long)) {

                    if (valueRange.Count > 0) {
                        var sliderValue = Convert.ToDouble(dashboard.GetConfigByKey(configKey));
                        // add textblock show value
                        var valueTextBlock = new Wpf.Ui.Controls.TextBlock();
                        valueTextBlock.Text = sliderValue.ToString("N2");
                        valueTextBlock.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(valueTextBlock, rowIndex);
                        Grid.SetColumn(valueTextBlock, 1);
                        configGrid.Children.Add(valueTextBlock);

                        // use slider
                        var slider = new Slider();
                        slider.TickFrequency = 0.01;
                        slider.IsSnapToTickEnabled = true;
                        slider.Minimum = valueRange[0];
                        slider.Maximum = valueRange[1];
                        slider.Value = sliderValue;
                        slider.SmallChange = 0.01;
                        // slider.SetBinding(Slider.ValueProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                        slider.ValueChanged += (sender, args) => {
                            dashboard.DashboardConfigurations.PropertyValue[index] = (float)slider.Value;
                            valueTextBlock.Text = ((float)slider.Value).ToString("N2");
                            dashboard.SaveConfig();
                        };
                        slider.Margin = new Thickness(0, 0, 10, 0);
                        slider.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(slider, rowIndex);
                        Grid.SetColumn(slider, 2);
                        configGrid.Children.Add(slider);

                    } else {
                        var numberBox = new Wpf.Ui.Controls.NumberBox();
                        numberBox.Value = Convert.ToDouble(dashboard.GetConfigByKey(configKey));
                        // numberBox.SetBinding(Wpf.Ui.Controls.NumberBox.ValueProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                        numberBox.ValueChanged += (sender, args) => {
                            dashboard.DashboardConfigurations.PropertyValue[index] = (float)numberBox.Value;
                            dashboard.SaveConfig();
                        };
                        numberBox.Margin = new Thickness(0, 0, 10, 0);
                        numberBox.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(numberBox, rowIndex);
                        Grid.SetColumn(numberBox, 2);
                        configGrid.Children.Add(numberBox);
                    }
                }
            }

            cardExpander.Content = configGrid;

            DashboardItems.Add(cardExpander);
        }
    }

    private static void SetThemeForeground(
        Wpf.Ui.Controls.TextBlock textBlock,
        string resourceKey)
    {
        textBlock.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            resourceKey);
    }

    private static ImageSource LoadPreviewImage(Dashboard dashboard)
    {
        var imagePath = dashboard.PreviewImageFilePath;
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            return new BitmapImage(new Uri(imagePath, UriKind.Absolute));
        }

        return new BitmapImage(new Uri(
            "pack://application:,,,/ZTMZ.PacenoteTool.UI.Wpf;component/unknown_dashboard_preview_image.png"));
    }

    private Grid CreateDashboardOutputTargetSelector(Dashboard dashboard)
    {
        var selector = new Grid
        {
            Width = 128,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = I18NLoader.Instance["dashboards.settings.tooltip.outputTarget"]
        };

        var options = new[]
        {
            (DashboardOutputTarget.None, SymbolRegular.EyeOff20, "dashboards.settings.tooltip.outputTargetOff"),
            (DashboardOutputTarget.Overlay, SymbolRegular.Desktop20, "dashboards.settings.tooltip.outputTargetOverlay"),
            (DashboardOutputTarget.Web, SymbolRegular.Globe20, "dashboards.settings.tooltip.outputTargetWeb"),
            (DashboardOutputTarget.Both, SymbolRegular.WindowMultiple20, "dashboards.settings.tooltip.outputTargetBoth")
        };
        var selectedTarget = _gameOverlayManager.GetDashboardOutputTarget(dashboard);
        var buttons = new Dictionary<DashboardOutputTarget, ToggleButton>();

        for (var i = 0; i < options.Length; i++)
        {
            var option = options[i];
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            var button = new ToggleButton
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0),
                IsChecked = selectedTarget == option.Item1,
                ToolTip = I18NLoader.Instance[option.Item3],
                Content = new SymbolIcon { Symbol = option.Item2 }
            };
            ToolTipService.SetInitialShowDelay(button, 200);
            buttons.Add(option.Item1, button);
            button.Click += (_, _) =>
            {
                _gameOverlayManager.SetDashboardOutputTarget(dashboard, option.Item1);
                foreach (var targetButton in buttons)
                {
                    targetButton.Value.IsChecked = targetButton.Key == option.Item1;
                }
            };

            Grid.SetColumn(button, i);
            selector.Children.Add(button);
        }

        return selector;
    }
}
