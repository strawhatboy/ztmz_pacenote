using System.IO;
using System.Reflection;
using System.Text;

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
        using (Stream stream = asm.GetManifestResourceStream(resourceName))
        using (StreamReader reader = new StreamReader(stream))
        {
            string result = reader.ReadToEnd();
            return result;
        }
    }
}

