using System.Windows;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI.Game;


[GameConfigPane(typeof(UdpGameConfig))]
public partial class UdpGameConfigSettingsPane : IGameConfigSettingsPane
{
    UdpGameConfig _config;
    bool _isInitialized = false;

    public UdpGameConfigSettingsPane(UdpGameConfig config)
    {
        _config = config;
        InitializeComponent();

    }

    public override void InitializeWithGame(IGame game)
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        // port
        this.tb_UDPListenPort.Value = (uint)_config.Port;
        this.tb_UDPListenPort.ValueChanged += (s, e) =>
        {
            if (this.tb_UDPListenPort.Value == null)
                return;
            _config.Port = (int)this.tb_UDPListenPort.Value.Value;
            base.RestartNeeded?.Invoke();
            // open the port mismatch dialog next time.
            Config.Instance.WarnIfPortMismatch = true;
            Config.Instance.SaveGameConfig(game);
        };

        this.tb_UdpListenAddress.Text = _config.IPAddress;
        this.tb_UdpListenAddress.TextChanged += (s, e) =>
        {
            _config.IPAddress = this.tb_UdpListenAddress.Text;
            base.RestartNeeded?.Invoke();
            Config.Instance.SaveGameConfig(game);
        };
    }
}
