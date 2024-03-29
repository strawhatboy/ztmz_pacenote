using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZTMZ.PacenoteTool.Base
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
            if (!ScriptResource.PACENOTES.ContainsKey(note) && !ScriptResource.ALIAS_CONSTRUCTED.ContainsKey(note))
            {
                throw new Exception(note);
            }

            result.Note = parts[0].Trim();
            for (int i = 1; i < parts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                {
                    var mod = parts[i].Trim();
                    if (!ScriptResource.MODIFIERS.ContainsKey(mod) && !ScriptResource.ALIAS_CONSTRUCTED.ContainsKey(mod))
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
                if (!string.IsNullOrEmpty(mod))
                {
                    sb.AppendFormat("/{0}", mod);
                }
            }

            return sb.ToString();
        }
    }

    public class PacenoteRecord
    {
        public float? Distance { set; get; }
        public List<Pacenote> Pacenotes { set; get; } = new List<Pacenote>();

        public string RawText { set; get; }

        public string Comment { set; get; }

        public static PacenoteRecord GetFromLine(string line)
        {
            line = line.Trim();
            var commentParseResult = PacenoteRecord.ParseComment(line);
            var realContent = commentParseResult[0];
            var comment = commentParseResult[1];
            var rawTextParseResult = PacenoteRecord.ParseRawText(realContent);
            realContent = rawTextParseResult[0];
            var rawText = rawTextParseResult[1];
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
            result.RawText = rawText;

            return result;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (this.Distance.HasValue)
            {
                sb.Append(((int)this.Distance).ToString());
            }

            foreach (var pacenote in Pacenotes)
            {
                sb.Append(",");
                sb.Append(pacenote.ToString());
            }

            if (!string.IsNullOrEmpty(this.Comment))
            {
                if (this.Distance.HasValue)
                {
                    sb.Append("\t");
                }

                sb.Append(this.Comment);
            }

            return sb.ToString();
        }

        public static PacenoteRecord FromCrewChiefPacenoteRecord(DynamicPacenoteRecord record)
        {
            PacenoteRecord ret = new PacenoteRecord();
            ret.Distance = record.Distance;
            var pn = new Pacenote()
            {
                Note = record.Pacenote
            };
            if (record.Modifier != "none")
            {
                pn.Modifiers = pn.Modifiers.Concat(from p in record.Modifier.Split(',') select p.Trim()).ToList();
            }
            ret.Pacenotes.Add(pn);

            return ret;
        }

        public static string[] ParseComment(string line)
        {
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

            return new string[]
            {
                realContent, comment
            };
        }

        public static string[] ParseRawText(string line)
        {
            var commentIndex = line.IndexOf('>');
            string realContent;
            string rawText;
            if (commentIndex != -1)
            {
                realContent = line.Substring(0, commentIndex);
                rawText = line.Substring(commentIndex+1);
            }
            else
            {
                realContent = line;
                rawText = string.Empty;
            }

            return new string[]
            {
                realContent, rawText
            };
        }

        public static string[] RawTextToAliases(string rawText)
        {
            var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rawText = string.Join(' ', parts);
            var len = parts.Length;
            var cur = 0;
            List<string> result = new();
            while (cur < len)
            {
                int wordLen = Math.Min(len - cur, 4);
                while (wordLen > 0)
                {
                    var key = string.Join(' ', parts.Skip(cur).SkipLast(len - cur - wordLen));
                    if (ScriptResource.ALIAS_SPEECH_DICT[wordLen - 1].ContainsKey(key))
                    {
                        // jackpot
                        result.Add(ScriptResource.ALIAS_SPEECH_DICT[wordLen - 1][key]);
                        break;
                    }
                    wordLen--;
                }
                if (wordLen == 0)
                {
                    result.Add(parts[cur]); // not recognized.
                    cur += 1;
                } else
                {
                    cur += wordLen;
                }
            }
            return result.ToArray();
        }

        public static string AliasesToPacenotes(string[] aliaes)
        {
            StringBuilder sb = new();
            for (var i = 0; i < aliaes.Length; i++)
            {
                var a = aliaes[i];
                if (ScriptResource.ALIAS_CONSTRUCTED.ContainsKey(a))
                {
                    var alias = ScriptResource.ALIAS_CONSTRUCTED[a];
                    if (alias.Item1 == ScriptResource.TYPE_PACENOTE && !ScriptResource.MODIFIERS.ContainsKey(alias.Item2) || i == 0)
                    {
                        sb.Append(",");
                        sb.Append(a);
                    }
                    else
                    {
                        sb.Append("/");
                        sb.Append(a);
                    }
                } else
                {
                    // not recognized
                    sb.Append("!");
                    sb.Append(a);
                }
            }
            return sb.ToString();
        }
    }
}
