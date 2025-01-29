using System.IO;
using IniParser;
using IniParser.Model;

namespace ZTMZ.PacenoteTool.RBR;

public static class RBRHelper {
    public static bool checkIfIniFileValid(string iniFilePath)
    {
        if (!File.Exists(iniFilePath)) 
        {
            // RSF rbr not valid or uninstalled properly.
            return false;
        } else {
            try {
                var parser = new FileIniDataParser();
                IniData data = parser.ReadFile(iniFilePath);
            } catch {
                // RSF rbr not valid or uninstalled properly.
                return false;
            }
        }
        return true;
    }
}
    
