
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base.UI;
using System.Windows.Media;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HudPageVM : ObservableObject {

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
        Config.Instance.WebDashboardEnabled = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _webDashboardPort = Config.Instance.WebDashboardPort;

    partial void OnWebDashboardPortChanged(int value)
    {
        Config.Instance.WebDashboardPort = value;
        Config.Instance.SaveUserConfig();
        OnPropertyChanged(nameof(WebDashboardInstructions));
    }

    public string WebDashboardInstructions =>
        string.Format(I18NLoader.Instance["settings.tooltip.webDashboardInstructions"], WebDashboardPort);

    [ObservableProperty]
    private ObservableCollection<object> _dashboardItems = new();

    [RelayCommand]
    private void ShowInSeparateWindow() {
        // _gameOverlayManager.UninitializeOverlay();
        _gameOverlayManager.InitializeOverlayInSeparateWindow();
    }
    
    private GameOverlayManager _gameOverlayManager;

    [ObservableProperty]
    private bool _isHudInitializing = false;

    public HudPageVM(GameOverlayManager gameOverlayManager) {
        _gameOverlayManager = gameOverlayManager;
        _gameOverlayManager.OnGameOverlayInitializingStateChanged += (isInitializing) => {
            IsHudInitializing = isInitializing;
        };

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
            previewImage.Source = dashboard.PreviewImage;
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
            version.Appearance = TextColor.Secondary;
            version.Margin = new Thickness(0, 0, 5, 0);
            version.FontTypography = FontTypography.Caption;

            Wpf.Ui.Controls.TextBlock author = new Wpf.Ui.Controls.TextBlock();
            author.Text = dashboard.Descriptor.Author;
            author.Appearance = TextColor.Tertiary;
            author.Margin = new Thickness(0, 0, 5, 0);
            author.FontTypography = FontTypography.Caption;

            spTitleAndVersion.Children.Add(title);
            spTitleAndVersion.Children.Add(version);
            spTitleAndVersion.Children.Add(author);
            Grid.SetRow(spTitleAndVersion, 0);
            Grid.SetColumn(spTitleAndVersion, 1);

            Wpf.Ui.Controls.TextBlock description = new Wpf.Ui.Controls.TextBlock();
            description.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, dashboard.Descriptor.Description);
            description.Appearance = TextColor.Tertiary;
            description.FontTypography = FontTypography.Caption;
            Grid.SetRow(description, 1);
            Grid.SetColumn(description, 1);

            // toggle button in 2nd column
            ToggleSwitch toggleSwitch = new ToggleSwitch();
            toggleSwitch.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding("IsEnabled") { Source = dashboard.Descriptor });
            // toggle event
            toggleSwitch.Click += (sender, args) => {
                dashboard.SetIsEnable((bool)toggleSwitch.IsChecked);
                dashboard.SaveConfig();
            };
            toggleSwitch.Margin = new Thickness(0, 0, 10, 0);

            Grid.SetColumn(toggleSwitch, 2);
            Grid.SetRowSpan(toggleSwitch, 2);

            grid.Children.Add(previewImage);
            grid.Children.Add(spTitleAndVersion);
            grid.Children.Add(description);
            grid.Children.Add(toggleSwitch);

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
                configTooltipText.Appearance = TextColor.Tertiary;
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
}
