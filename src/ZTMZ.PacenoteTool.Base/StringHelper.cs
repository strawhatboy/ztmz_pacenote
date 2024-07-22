using System.IO;
using System.Reflection;
using System.Text;
using Neo.IronLua;

namespace ZTMZ.PacenoteTool.Base;

public static class StringHelper 
{
    public static string InstanceToStringWithFields(object s) 
    {
        var sb = new StringBuilder();
        var fields = s.GetType().GetFields();
        for (var i = 0; i < fields.Length; i++)
        {
            var pInfo = fields[i];
            var name = pInfo.Name;
            if (pInfo.FieldType == typeof(float))
            {
                var value = (float)pInfo.GetValue(s);
                sb.Append((name + ":").PadRight(30)).Append(value.ToString("0.0"));
            }
            else
            {
                var value = pInfo.GetValue(s);
                sb.Append((name + ":").PadRight(30)).Append(value.ToString());
            }
            if (i != fields.Length - 1)
            {
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string ReadContentFromResource(Assembly asm, string resourceName)
    {
        resourceName = resourceName.lower();
        var resName = asm.GetName().Name + ".g.resources";
        using (var stream = asm.GetManifestResourceStream(resName))
        using (var reader = new System.Resources.ResourceReader(stream))
        {
            byte[] data;
            string resType;
            reader.GetResourceData(resourceName, out resType, out data);
            var result = System.Text.Encoding.UTF8.GetString(data);
            var strStopCharIndex = result.LastIndexOf('\0');
            if (strStopCharIndex < result.Length - 1) 
            {
                result = result.Substring(strStopCharIndex + 1);
                return result;
            }

            return result;
        }
    }

    public static void ExtractFileFromResource(Assembly asm, string resourceName, string fileName) {
        
        resourceName = resourceName.lower();
        var resName = asm.GetName().Name + ".g.resources";
        using (var stream = asm.GetManifestResourceStream(resName))
        using (var file = new FileStream(fileName, FileMode.Create, FileAccess.Write))
        {
            stream.CopyTo(file);
        }
    }
}

