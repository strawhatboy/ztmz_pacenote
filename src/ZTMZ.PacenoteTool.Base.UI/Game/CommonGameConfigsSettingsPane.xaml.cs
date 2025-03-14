using System.IO;
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

            // this is a fix but not a good one
            if(index >= _config.PropertyValue.Count || index >= _config.PropertyType.Count)
            {
                // out of range
                break;
            }
            
            var value = _config.PropertyValue[index];
            var valueType = _config.PropertyType[index];
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
                if (valueType.StartsWith("file:"))
                {
                    int _index = index;
                    var fileType = valueType.Substring(5);

                    StackPanel sp = new StackPanel() { Orientation = Orientation.Horizontal };
                    Wpf.Ui.Controls.TextBlock vtb = new Wpf.Ui.Controls.TextBlock() { Text = Path.GetFileName(value.ToString()), FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center };
                    Wpf.Ui.Controls.Button btn = new Wpf.Ui.Controls.Button() { Icon = new Wpf.Ui.Controls.SymbolIcon{ Symbol = SymbolRegular.DocumentBulletList20 }, ToolTip = value };
                    btn.Click += (sender, args) => {
                        var dlg = new Microsoft.Win32.OpenFileDialog();
                        dlg.InitialDirectory = Path.GetDirectoryName(value.ToString());
                        dlg.DefaultExt = fileType;
                        dlg.Filter = $"{fileType.ToUpper()}|*.{fileType}";
                        if (dlg.ShowDialog() == true)
                        {
                            // tbox.Text = dlg.FileName;
                            _config.PropertyValue[_index] = dlg.FileName;
                            btn.ToolTip = dlg.FileName;
                            vtb.Text = Path.GetFileName(dlg.FileName);
                            base.RestartNeeded?.Invoke();
                            Config.Instance.SaveGameConfig(game);
                        }
                    };
                    btn.ToolTip = Path.GetFileName(value.ToString());
                    sp.Children.Add(vtb);
                    sp.Children.Add(btn);
                    Grid.SetRow(sp, _index);
                    Grid.SetColumn(sp, 1);
                    btn.VerticalAlignment = VerticalAlignment.Center;
                    btn.HorizontalAlignment = HorizontalAlignment.Right;
                    this.grid_Main.Children.Add(sp);

                } else {
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
            }

            index++;
        }
    }
}
