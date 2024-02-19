using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI.Game;

[GameConfigPane(typeof(CommonGameConfigs))]
public partial class CommonGameConfigsSettingsPane : IGameConfigSettingsPane
{
    CommonGameConfigs _config;
    bool _isInitialized = false;
    GridLength _rowHeight = new GridLength(40);
    public CommonGameConfigsSettingsPane(CommonGameConfigs config)
    {
        _config = config;
        InitializeComponent();
    }

    public override void InitializeWithGame(IGame game)
    {
        if (_isInitialized)
            return;

        _isInitialized = true;

        int index = 0;
        foreach (var cName in _config.PropertyName)
        {
            this.grid_Main.RowDefinitions.Add(new RowDefinition() { Height = _rowHeight });
            var tb = new Wpf.Ui.Controls.TextBlock();
            tb.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, cName.Key);
            tb.SetResourceReference(Wpf.Ui.Controls.TextBlock.ToolTipProperty, cName.Value);
            Grid.SetRow(tb, index);
            Grid.SetColumn(tb, 0);
            tb.VerticalAlignment = VerticalAlignment.Center;
            this.grid_Main.Children.Add(tb);

            var value = _config.PropertyValue[index];
            if (value.GetType() == typeof(bool))
            {
                int _index = index;
                ToggleSwitch tbtn = new ToggleSwitch() { IsChecked = (bool)_config.PropertyValue[_index] };
                tbtn.HorizontalAlignment = HorizontalAlignment.Right;
                tbtn.Click += (sender, args) => {
                    _config.PropertyValue[_index] = (bool)tbtn.IsChecked;
                    base.RestartNeeded?.Invoke();
                    Config.Instance.SaveGameConfig(game);
                };
                Grid.SetRow(tbtn, _index);
                Grid.SetColumn(tbtn, 1);
                tbtn.VerticalAlignment = VerticalAlignment.Center;
                
                this.grid_Main.Children.Add(tbtn);
            } else if (value.GetType() == typeof(string)) 
            {
                int _index = index;
                Wpf.Ui.Controls.TextBox tbox = new Wpf.Ui.Controls.TextBox() { Text = (string)_config.PropertyValue[_index] };
                // tbox.HorizontalAlignment = HorizontalAlignment.Right;
                tbox.TextChanged += (sender, args) => {
                    _config.PropertyValue[_index] = tbox.Text;
                    base.RestartNeeded?.Invoke();
                    Config.Instance.SaveGameConfig(game);
                };
                Grid.SetRow(tbox, _index);
                Grid.SetColumn(tbox, 1);
                tbox.VerticalAlignment = VerticalAlignment.Center;
                this.grid_Main.Children.Add(tbox);
            }

            index++;
        }
    }
}
