
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

/// <summary>
/// Set some specific configurations when updated to a specific version
/// </summary>
public class UpdateConfigSetter
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private Dictionary<Version, Action<ZTMZPacenoteTool>> _configSetters = new ();

    private Version currentVersion = new Version(UpdateService.CurrentVersion);

    public UpdateConfigSetter(ZTMZPacenoteTool tool)
    {
        this._configSetters.Add(new Version("2.99.99.20"), tool => {
            // set default udp port to 59996 for Dirt Rally 1.0/2.0
            var dr1 = tool.Games.FirstOrDefault(a => a.Name == "Dirt Rally 1.0");
            var dr2 = tool.Games.FirstOrDefault(a => a.Name == "Dirt Rally 2.0");

            var setGamePort = (IGame game) => {
                if (game != null) {
                    UdpGameConfig cfg = (UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name];
                    cfg.Port = 59996;
                    Config.Instance.SaveGameConfig(dr1);
                }
            };
            
            setGamePort(dr1);
            setGamePort(dr2);
        });
    }

    public void SetConfiguration(ZTMZPacenoteTool tool)
    {
        foreach (var item in _configSetters)
        {
            var updateFlagFile = AppLevelVariables.Instance.GetPath(item.Key.ToString());
            if (currentVersion < item.Key && !File.Exists(updateFlagFile))
            {
                _logger.Info("Setting configurations for version {0}", item.Key);
                item.Value(tool);
                File.Create(updateFlagFile);
            }
        }
    }
    
}

