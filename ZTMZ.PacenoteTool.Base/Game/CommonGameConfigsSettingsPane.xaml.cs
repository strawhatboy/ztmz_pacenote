using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ZTMZ.PacenoteTool.Base.Game;

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
            var tb = new TextBlock() { Text = I18NLoader.Instance[cName.Key], ToolTip = I18NLoader.Instance[cName.Value], Style = (Style)this.FindResource("MaterialDesignSubtitle1TextBlock") };
            Grid.SetRow(tb, index);
            Grid.SetColumn(tb, 0);
            tb.VerticalAlignment = VerticalAlignment.Center;
            this.grid_Main.Children.Add(tb);

            var value = _config.PropertyValue[index];
            if (value.GetType() == typeof(bool))
            {
                int _index = index;
                ToggleButton tbtn = new ToggleButton() { IsChecked = (bool)_config.PropertyValue[_index] };
                tbtn.Click += (sender, args) => {
                    _config.PropertyValue[_index] = (bool)tbtn.IsChecked;
                    base.RestartNeeded?.Invoke();
                    Config.Instance.SaveGameConfig(game);
                };
                Grid.SetRow(tbtn, _index);
                Grid.SetColumn(tbtn, 1);
                tbtn.VerticalAlignment = VerticalAlignment.Center;
                
                this.grid_Main.Children.Add(tbtn);
            }

            index++;
        }
    }
}
