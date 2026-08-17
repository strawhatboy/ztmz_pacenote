using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
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
        ArgumentNullException.ThrowIfNull(asm);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        resourceName = resourceName.ToLowerInvariant();
        var resName = asm.GetName().Name + ".g.resources";
        using (var stream = asm.GetManifestResourceStream(resName))
        {
            if (stream != null)
            {
                using var reader = new System.Resources.ResourceReader(stream);
                reader.GetResourceData(resourceName, out _, out var data);
                var result = Encoding.UTF8.GetString(data);
                var strStopCharIndex = result.LastIndexOf('\0');
                return strStopCharIndex < result.Length - 1
                    ? result[(strStopCharIndex + 1)..]
                    : result;
            }
        }

        var manifestResourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.Equals(resourceName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith('.' + resourceName, StringComparison.OrdinalIgnoreCase));
        if (manifestResourceName == null)
        {
            throw new MissingManifestResourceException(
                $"Resource {resourceName} was not found in assembly {asm.GetName().Name}.");
        }

        using var manifestStream = asm.GetManifestResourceStream(manifestResourceName)
            ?? throw new MissingManifestResourceException(
                $"Resource stream {manifestResourceName} could not be opened.");
        using var manifestReader = new StreamReader(
            manifestStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return manifestReader.ReadToEnd();
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
