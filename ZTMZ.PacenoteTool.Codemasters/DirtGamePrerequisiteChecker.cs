
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ZTMZ.PacenoteTool.Base.Game;
using System.Xml.XPath;
using ZTMZ.PacenoteTool.Base;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Codemasters;

public class DirtGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    private string _dr1settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally/hardwaresettings/hardware_settings_config.xml");
    private string _dr1settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally/hardwaresettings/hardware_settings_config_vr.xml");
    private string _dr2settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config.xml");
    private string _dr2settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config_vr.xml");
    public bool IsPassed { set; get; } = false;
    private XDocument? _xmlFile;
    private XElement? _udpNode;

    private string configPort = "20777";
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
            b1 = this.Check(game, settingsFile);
        }
        if (File.Exists(settingsVRFile))
        {
            b2 = this.Check(game, settingsVRFile);
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
        this._xmlFile = XDocument.Load(file);
        this._udpNode = this._xmlFile.Root.XPathSelectElement("./motion_platform/udp");
        if (this._udpNode.Attribute("enabled").Value != "true" || this._udpNode.Attribute("extradata").Value != "3")
        {
            return new PrerequisitesCheckResult()
            {
                IsOK = false,
                Msg = "",
                Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN,
                Params = new List<object>() { game.Name, file }
            };
        }

        if (!this._udpNode.Attribute("port").Value.Equals(configPort))
        {
            return new PrerequisitesCheckResult()
            {
                IsOK = false,
                Msg = "",
                Code = PrerequisitesCheckResultCode.PORT_NOT_MATCH,
                Params = new List<object> { game.Name, file, this._udpNode.Attribute("port").Value, configPort }
            };
        }

        this.IsPassed = true;
        return new PrerequisitesCheckResult()
        {
            IsOK = true,
            Msg = "",
            Code = PrerequisitesCheckResultCode.OK,
        };
    }

    public void Write(string file)
    {
        this._xmlFile = XDocument.Load(file);
        this._udpNode = this._xmlFile.Root.XPathSelectElement("./motion_platform/udp");
        this._udpNode.Attribute("enabled").Value = "true";
        this._udpNode.Attribute("extradata").Value = "3";
        this._udpNode.Attribute("port").Value = configPort;
        this._xmlFile.Save(file);
    }
}
