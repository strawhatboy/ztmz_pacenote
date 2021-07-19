using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    
    public class Pacenote
    {
        public string Note { set; get; }
        public IList<string> Modifiers { set; get; }

        public static Pacenote LoadFromString(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return null;
            }

            var parts = str.Split('/');
            if (parts.Length == 0)
            {
                return null;
            }

            Pacenote result = new Pacenote();
            var note = parts[0].Trim();
            if (!ScriptResource.PACENOTES.ContainsKey(note))
            {
                throw new Exception(note);
            }
            result.Note = parts[0].Trim();
            result.Modifiers = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                {
                    var mod = parts[i].Trim();
                    if (!ScriptResource.MODIFIERS.ContainsKey(mod))
                    {
                        throw new Exception(mod);
                    }
                    result.Modifiers.Add(mod);   
                }
            }

            return result;
        }
    }
    
    public class PacenoteRecord
    {
        public float Distance { set; get; }
        public IList<Pacenote> Pacenotes { set; get; }

        public static PacenoteRecord GetFromLine(string line)
        {
            line = line.Trim();
            var commentIndex = line.IndexOf('#');
            string realContent;
            if (commentIndex != -1)
            {
                realContent = line.Substring(0, commentIndex);
            }
            else
            {
                realContent = line;
            }

            var parts = realContent.Split(',');
            if (parts.Length == 0)
            {
                return null;
            }

            var result = new PacenoteRecord();
            float dis;
            if (float.TryParse(parts[0].Trim(), out dis))
            {
                result.Distance = dis;
            }

            result.Pacenotes = new List<Pacenote>();
            for (int i = 1; i < parts.Length; i++)
            {
                var note = Pacenote.LoadFromString(parts[i].Trim());
                if (note != null)
                {
                    result.Pacenotes.Add(note);   
                }
            }

            return result;
        }
    }
}