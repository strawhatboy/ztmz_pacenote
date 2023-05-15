using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI.Game;

public partial class MemoryGameConfigSettingsPane : IGameConfigSettingsPane
{
    MemoryGameConfig _config;
    bool _isInitialized = false;
    public MemoryGameConfigSettingsPane(MemoryGameConfig config)
    {
        _config = config;
        InitializeComponent();
    }

    public override void InitializeWithGame(IGame game)
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        this.tb_MemoryRefreshRate.Value = (uint)_config.RefreshRate;
        this.tb_MemoryRefreshRate.ValueChanged += (s, e) =>
        {
            _config.RefreshRate = (float)this.tb_MemoryRefreshRate.Value.Value;
            base.RestartNeeded?.Invoke();
            Config.Instance.SaveGameConfig(game);
        };
    }
}
