
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

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HudPageVM : ObservableObject {
    [ObservableProperty]
    private ObservableCollection<object> _dashboardItems = new();

    
    private GameOverlayManager _gameOverlayManager;

    public HudPageVM(GameOverlayManager gameOverlayManager) {
        _gameOverlayManager = gameOverlayManager;

        foreach (var dashboard in _gameOverlayManager.Dashboards) {
            var cardExpander = new CardExpander();
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            Wpf.Ui.Controls.TextBlock title = new Wpf.Ui.Controls.TextBlock();
            title.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, dashboard.Descriptor.Name);

            Wpf.Ui.Controls.TextBlock description = new Wpf.Ui.Controls.TextBlock();
            description.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, dashboard.Descriptor.Description);
            description.Appearance = TextColor.Tertiary;
            description.FontTypography = FontTypography.Caption;
            Grid.SetRow(description, 1);

            // toggle button in 2nd column
            ToggleSwitch toggleSwitch = new ToggleSwitch();
            toggleSwitch.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding("IsEnabled") { Source = dashboard.Descriptor });
            toggleSwitch.IsEnabledChanged += (sender, args) => {
                dashboard.Descriptor.IsEnabled = (bool)toggleSwitch.IsChecked;
                dashboard.SaveConfig();
            };
            toggleSwitch.Margin = new Thickness(0, 0, 10, 0);

            Grid.SetColumn(toggleSwitch, 1);
            Grid.SetRowSpan(toggleSwitch, 2);

            grid.Children.Add(title);
            grid.Children.Add(description);
            grid.Children.Add(toggleSwitch);

            cardExpander.Header = grid;
            cardExpander.Margin = new Thickness(0, 0, 0, 5);

            var configGrid = new Grid();
            configGrid.ColumnDefinitions.Add(new ColumnDefinition());
            configGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40, GridUnitType.Pixel) });
            configGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Auto) });
            // content will be the dashboard configurations
            for (var i = 0; i < dashboard.DashboardConfigurations.PropertyName.Keys.Count; i++) {
                var index = i;
                var configKey = dashboard.DashboardConfigurations.PropertyName.Keys.ElementAt(i);
                var configTooltip = dashboard.DashboardConfigurations.PropertyName[configKey];
                var configValue = dashboard.DashboardConfigurations.PropertyValue[i];
                var valueRange = dashboard.DashboardConfigurations.ValueRange[i];

                configGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(40, GridUnitType.Pixel) });
                var configName = new Wpf.Ui.Controls.TextBlock();
                configName.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, configKey);
                configName.SetResourceReference(Wpf.Ui.Controls.TextBlock.ToolTipProperty, configTooltip);
                configName.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(configName, i);
                Grid.SetColumn(configName, 0);
                configGrid.Children.Add(configName);

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
                    Grid.SetRow(comboBox, i);
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
                    Grid.SetRow(comboBox, i);
                    Grid.SetColumn(comboBox, 2);
                    configGrid.Children.Add(comboBox);
                    continue;
                }

                if (configValue.GetType() == typeof(bool)) {
                    var _toggleSwitch = new ToggleSwitch();
                    _toggleSwitch.IsEnabled = (bool)dashboard.GetConfigByKey(configKey);
                    // _toggleSwitch.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                    _toggleSwitch.Margin = new Thickness(0, 0, 10, 0);
                    _toggleSwitch.IsEnabledChanged += (sender, args) => {
                        dashboard.DashboardConfigurations.PropertyValue[index] = (bool)_toggleSwitch.IsChecked;
                        dashboard.SaveConfig();
                    };
                    _toggleSwitch.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(_toggleSwitch, i);
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
                    Grid.SetRow(textBox, i);
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
                        Grid.SetRow(valueTextBlock, i);
                        Grid.SetColumn(valueTextBlock, 1);
                        configGrid.Children.Add(valueTextBlock);

                        // use slider
                        var slider = new Slider();
                        slider.Minimum = valueRange[0];
                        slider.Maximum = valueRange[1];
                        slider.Value = sliderValue;
                        // slider.SetBinding(Slider.ValueProperty, new Binding(configKey) { Source = dashboard.DashboardConfigurations });
                        slider.ValueChanged += (sender, args) => {
                            dashboard.DashboardConfigurations.PropertyValue[index] = (float)slider.Value;
                            valueTextBlock.Text = ((float)slider.Value).ToString("N2");
                            dashboard.SaveConfig();
                        };
                        slider.Margin = new Thickness(0, 0, 10, 0);
                        slider.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(slider, i);
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
                        Grid.SetRow(numberBox, i);
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
