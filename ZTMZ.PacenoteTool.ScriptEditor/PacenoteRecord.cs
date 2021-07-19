using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    public class Pacenote
    {
        public string Note { set; get; }
        public IList<string> Modifiers { set; get; } = new List<string>();

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

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(string.Empty);
            sb.Append(this.Note);
            foreach (var mod in this.Modifiers)
            {
                sb.Append("/");
                sb.Append(mod);
            }

            return sb.ToString();
        }
    }

    public class PacenoteRecord
    {
        public float Distance { set; get; }
        public List<Pacenote> Pacenotes { set; get; } = new List<Pacenote>();
        
        public string Comment { set; get; }

        public static PacenoteRecord GetFromLine(string line)
        {
            line = line.Trim();
            var commentIndex = line.IndexOf('#');
            string realContent;
            string comment;
            if (commentIndex != -1)
            {
                realContent = line.Substring(0, commentIndex);
                comment = line.Substring(commentIndex);
            }
            else
            {
                realContent = line;
                comment = string.Empty;
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

            for (int i = 1; i < parts.Length; i++)
            {
                var note = Pacenote.LoadFromString(parts[i].Trim());
                if (note != null)
                {
                    result.Pacenotes.Add(note);
                }
            }

            result.Comment = comment;

            return result;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(((int) this.Distance).ToString());
            foreach (var pacenote in Pacenotes)
            {
                sb.Append(",");
                sb.Append(pacenote.ToString());
            }

            sb.Append("\t");
            sb.Append(this.Comment);

            return sb.ToString();
        }

        public static PacenoteRecord FromCrewChiefPacenoteRecord(CrewChiefPacenoteRecord record)
        {
            PacenoteRecord ret = new PacenoteRecord();
            ret.Distance = record.Distance;
            var pn = new Pacenote()
            {
                Note = record.Pacenote
            };
            if (record.Modifier != "none")
            {
                pn.Modifiers = pn.Modifiers.Concat(record.Modifier.Split(',')).ToList();
            }
            ret.Pacenotes.Add(pn);
            
            return ret;
        }
    }
}