using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace ZTMZ.PacenoteTool
{
    public class PrerequisitesCheck
    {
        private string _dr2settingsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config.xml");
        private string _dr2settingsVRFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/DiRT Rally 2.0/hardwaresettings/hardware_settings_config_vr.xml");
        public bool IsPassed { set; get; } = false;
        private XDocument _xmlFile;
        private XElement _udpNode;
        public bool Check()
        {
            bool b1 = true;
            if (File.Exists(this._dr2settingsFile))
            {
                b1 = this.Check(this._dr2settingsFile);   
            }
            if (File.Exists(this._dr2settingsVRFile))
            {
                return b1 && this.Check(this._dr2settingsVRFile);
            }
            return b1;
        }

        public void Write()
        {
            this.Write(this._dr2settingsFile);
            if (File.Exists(this._dr2settingsVRFile))
            {
                this.Write(this._dr2settingsVRFile);
            }
        }
        public bool Check(string file)
        {
            this._xmlFile = XDocument.Load(file);
            this._udpNode = this._xmlFile.Root.XPathSelectElement("./motion_platform/udp");
            if (this._udpNode.Attribute("enabled").Value != "true" || this._udpNode.Attribute("extradata").Value != "3")
            {
                return false;
            }

            this.IsPassed = true;
            return true;
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
