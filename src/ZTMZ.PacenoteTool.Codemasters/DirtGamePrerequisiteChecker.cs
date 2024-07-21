
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ZTMZ.PacenoteTool.Base.Game;
using System.Xml.XPath;
using ZTMZ.PacenoteTool.Base;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ZTMZ.PacenoteTool.Codemasters;

public class DirtGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private string _dr1settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally/hardwaresettings/hardware_settings_config.xml");
    private string _dr1settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally/hardwaresettings/hardware_settings_config_vr.xml");
    private string _dr2settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config.xml");
    private string _dr2settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config_vr.xml");
    public bool IsPassed { set; get; } = false;
    private XDocument? _xmlFile;
    private IEnumerable<XElement>? _udpNode;

    private string configPort = "59996";
    private string configIP = "127.0.0.1";
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        var settingsFile = "";
        var settingsVRFile = "";
        if (game.Name.Equals(DirtRally.GameName))
        {
            settingsFile = _dr1settingsFile;
            settingsVRFile = _dr1settingsVRFile;
        }
        else if (game.Name.Equals(DirtRally2.GameName))
        {
            settingsFile = _dr2settingsFile;
            settingsVRFile = _dr2settingsVRFile;
        }
        else
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        if (!(File.Exists(settingsFile) || File.Exists(settingsVRFile)))
        {
            // none of these 2 files exist, so the game was not installed
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        PrerequisitesCheckResult b1 = new PrerequisitesCheckResult();
        PrerequisitesCheckResult b2 = new PrerequisitesCheckResult();

        if (File.Exists(settingsFile))
        {
            try {
                b1 = this.Check(game, settingsFile);
            } catch (Exception e) {
                _logger.Error(e, "Error while checking prerequisites for {0} at {1}", game.Name, settingsFile);
                b1 = new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN, IsOK = false };
            }
        }
        if (File.Exists(settingsVRFile))
        {
            try {
                b2 = this.Check(game, settingsVRFile);
            } catch (Exception e) {
                _logger.Error(e, "Error while checking prerequisites for {0} at {1}", game.Name, settingsVRFile);
                b2 = new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN, IsOK = false };
            }
        }
        if (b1.IsOK && b2.IsOK)
        {
            return b1;
        }
        if (!b1.IsOK)
        {
            return b1;
        }
        if (!b2.IsOK)
        {
            return b2;
        }
        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
    }
    public void ForceFix(IGame game)
    {
        var settingsFile = "";
        var settingsVRFile = "";
        if (game.Name.Equals(DirtRally.GameName))
        {
            settingsFile = _dr1settingsFile;
            settingsVRFile = _dr1settingsVRFile;
        }
        else if (game.Name.Equals(DirtRally2.GameName))
        {
            settingsFile = _dr2settingsFile;
            settingsVRFile = _dr2settingsVRFile;
        }
        else
        {
            return;
        }
        if (File.Exists(settingsFile))
        {
            this.Write(settingsFile);
        }
        if (File.Exists(settingsVRFile))
        {
            this.Write(settingsVRFile);
        }
    }

    public PrerequisitesCheckResult Check(IGame game, string file)
    {
        configPort = ((UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name]).Port.ToString();
        configIP = ((UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name]).IPAddress;
        this._xmlFile = XDocument.Load(file);
        this._udpNode = this._xmlFile.Root.XPathSelectElements("./motion_platform/udp");

        foreach (var node in this._udpNode)
        {
            if (this.CheckUdpNode(game, node))
            {
                this.IsPassed = true;
                return new PrerequisitesCheckResult()
                {
                    IsOK = true,
                    Msg = "",
                    Code = PrerequisitesCheckResultCode.OK,
                };
            }
        }

        return new PrerequisitesCheckResult()
        {
            IsOK = false,
            Msg = "",
            Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN,
            Params = new List<object> { 
                game.Name,
                file
            }
        };
    }

    private bool CheckUdpNode(IGame game, XElement node) {
        if (node.Attribute("enabled").Value != "true" || node.Attribute("extradata").Value != "3")
        {
            return false;
        }

        if (!node.Attribute("port").Value.Equals(configPort))
        {
            return false;
        }

        return true;
    }

    public void Write(string file)
    {
        this._xmlFile = XDocument.Load(file);
        var motionPlatformNode = this._xmlFile.Root.XPathSelectElement("./motion_platform");
        if (motionPlatformNode != null)
        {
            var udpNode = new XElement("udp");
            udpNode.SetAttributeValue("enabled", "true");
            udpNode.SetAttributeValue("extradata", "3");
            udpNode.SetAttributeValue("ip", configIP);
            udpNode.SetAttributeValue("port", configPort);
            udpNode.SetAttributeValue("delay", "1");
            motionPlatformNode.Add(udpNode);
        }
        this._xmlFile.Save(file);
    }
}
