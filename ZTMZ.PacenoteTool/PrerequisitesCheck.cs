﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public enum PrerequisitesCheckResultCode
    {
        OK = 100,
        PORT_NOT_OPEN = 400,
        PORT_NOT_MATCH = 401,
        UNKNOWN = 800,
    }
    public class PrerequisitesCheckResult
    {
        public PrerequisitesCheckResultCode Code { set; get; }
        public bool IsOK { set; get; } = true;
        public string Msg { set; get; } = "";
    }
    public class PrerequisitesCheck
    {
        private string _dr2settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config.xml");
        private string _dr2settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config_vr.xml");
        public bool IsPassed { set; get; } = false;
        private XDocument _xmlFile;
        private XElement _udpNode;
        public PrerequisitesCheckResultCode Check()
        {
            PrerequisitesCheckResult b1 = new PrerequisitesCheckResult();
            PrerequisitesCheckResult b2 = new PrerequisitesCheckResult();
            if (File.Exists(this._dr2settingsFile))
            {
                b1 = this.Check(this._dr2settingsFile);
            }
            if (File.Exists(this._dr2settingsVRFile))
            {
                b2 = this.Check(this._dr2settingsVRFile);
            }
            if (b1.IsOK && b2.IsOK)
            {
                return PrerequisitesCheckResultCode.OK;
            }
            if (!b1.IsOK)
            {
                return b1.Code;
            }
            if (!b2.IsOK)
            {
                return b2.Code;
            }
            return PrerequisitesCheckResultCode.UNKNOWN;
        }

        public void Write()
        {
            this.Write(this._dr2settingsFile);
            if (File.Exists(this._dr2settingsVRFile))
            {
                this.Write(this._dr2settingsVRFile);
            }
        }
        public PrerequisitesCheckResult Check(string file)
        {
            this._xmlFile = XDocument.Load(file);
            this._udpNode = this._xmlFile.Root.XPathSelectElement("./motion_platform/udp");
            if (this._udpNode.Attribute("enabled").Value != "true" || this._udpNode.Attribute("extradata").Value != "3")
            {
                return new PrerequisitesCheckResult()
                {
                    IsOK = false,
                    Msg = "",
                    Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN,
                };
            }

            if (this._udpNode.Attribute("port").Value != Config.Instance.UDPListenPort.ToString())
            {
                return new PrerequisitesCheckResult()
                {
                    IsOK = false,
                    Msg = "",
                    Code = PrerequisitesCheckResultCode.PORT_NOT_MATCH,
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
            this._udpNode.SetAttributeValue("enabled", "true");
            this._udpNode.SetAttributeValue("extradata", "3");
            this._xmlFile.Save(file);
        }

        
    }
}
