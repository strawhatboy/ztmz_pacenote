using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.Base
{
    public class ScriptFlags
    {
        public static readonly string DYNAMIC = "dynamic";
        public static readonly string AUTHOR = "author";
    }


    public class ScriptFlagParser
    {
        public static IList<string> ParseFlag(string line, out string comment)
        {
            line = line.Trim();
            var commentParseResult = PacenoteRecord.ParseComment(line);
            var realContent = commentParseResult[0];
            comment = commentParseResult[1];
            if (realContent.StartsWith("@"))
            {
                return realContent.Substring(1).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }

            return null;
        }

        public static string ToString(string flag, IList<string> parameters, string comment)
        {
            return string.Format("@{0} {1}\t{2}", flag, string.Join(' ', parameters), comment);
        }
    }

    public class ScriptParseException : Exception
    {
        public ScriptParseException(int line, string token)
        {
            this.Line = line;
            this.UnexpectedToken = token;
        }

        public int Line { set; get; }
        public string UnexpectedToken { set; get; }
    }

    public class ScriptReader
    {
        public static string DEFAULT_AUTHOR = "???";
        public IList<string> Flags { set; get; } = new List<string>();
        public IDictionary<string, List<string>> FlagParameters { set; get; } = new Dictionary<string, List<string>>();
        public IDictionary<string, string> FlagComments { set; get; } = new Dictionary<string, string>();
        public IList<PacenoteRecord> PacenoteRecords { set; get; } = new List<PacenoteRecord>();

        public static ScriptReader ReadFromFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            return ReadFromLines(lines);
        }

        public bool IsDynamic => this.HasFlag(ScriptFlags.DYNAMIC);

        public bool HasFlag(string flag)
        {
            return this.Flags.Contains(flag);
        }

        public string Author =>
            this.HasFlag(ScriptFlags.AUTHOR) ? string.Join(' ', this.FlagParameters[ScriptFlags.AUTHOR]) : DEFAULT_AUTHOR;

        public static ScriptReader ReadFromString(string str)
        {
            var lines = str.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            return ReadFromLines(lines);
        }

        public static ScriptReader ReadFromLines(string[] lines)
        {
            ScriptReader reader = new ScriptReader();
            for (int lineNo = 0; lineNo < lines.Length; lineNo++)
            {
                var line = lines[lineNo];
                // try flags first
                var comment = string.Empty;
                var flag = ScriptFlagParser.ParseFlag(line, out comment);
                if (flag != null && flag.Count > 0)
                {
                    if (reader.Flags.Contains(flag[0]))
                    {
                        reader.FlagParameters[flag[0]].AddRange(flag.Skip(1).ToList());
                        reader.FlagComments[flag[0]] += comment;
                    }
                    else
                    {
                        reader.Flags.Add(flag[0]);
                        reader.FlagParameters.Add(flag[0], flag.Skip(1).ToList());
                        reader.FlagComments.Add(flag[0], comment);
                    }


                    continue;
                }

                try
                {
                    var record = PacenoteRecord.GetFromLine(line);
                    if (record != null)
                    {
                        reader.PacenoteRecords.Add(record);
                    }
                }
                catch (Exception e)
                {
                    throw new ScriptParseException(lineNo, e.Message);
                }
            }

            return reader;
        }

        // CrewChief pacenote is actually the same as RBR pacenote
        public static ScriptReader ReadFromJson(string jsonFile)
        {
            var content = File.ReadAllText(jsonFile);
            var records = JsonConvert.DeserializeObject<List<DynamicPacenoteRecord>>(content);
            
            // records.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return ReadFromDynamicPacenoteRecords(records);
        }

        public static ScriptReader ReadFromDynamicPacenoteRecords(List<DynamicPacenoteRecord> records)
        {
            PacenoteRecord lastRecord = null;
            ScriptReader reader = new ScriptReader();
            for (var i = 0; i < records.Count; i++)
            {
                PacenoteRecord record = new PacenoteRecord();
                var r = records[i];
                r.Pacenote = r.Pacenote.Trim();
                r.Modifier = r.Modifier.Trim();
                if (r.Pacenote.EndsWith("distance_call") && i != records.Count - 1)
                {
                    var distance_to_next = (records[i + 1].Distance - r.Distance);
                    var distance_to_call_rounded = (int) MathF.Floor(distance_to_next / 10f) * 10;
                    var distance_label = string.Format("number_{0}", distance_to_call_rounded);
                    record.Distance = r.Distance;
                    if (Script.ScriptResource.Instance.FilenameToIdDict.ContainsKey(distance_label) && distance_to_call_rounded >= 30)
                    {
                        var distance_pacenote = new Pacenote() {Note = distance_label};
                        if (Config.Instance.ConnectNumericDistanceCallToPreviousPacenote && lastRecord != null)
                        {
                            // merge it to last call if there is last call
                            lastRecord.Pacenotes.Add(distance_pacenote);
                        } else 
                        {
                            // if there's no last call or config says no merge, add it as a separate call
                            record.Pacenotes.Add(distance_pacenote);
                            reader.PacenoteRecords.Add(record);
                            lastRecord = record;
                        }
                    } else {
                        // this could be 'and' or 'into'
                        if (distance_to_next < 15)
                        {
                            // insert an 'into' after this
                            record.Pacenotes.Add(new Pacenote() {Note = "detail_into"});
                            // merge with next
                            if (Config.Instance.ConnectCloseDistanceCallToNextPacenote && (i + 1) < records.Count)
                            {   // make sure i+1 is valid (to prevent index out of range)
                                record.Pacenotes.AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(records[i + 1]).Pacenotes);
                                i++;
                            }
                        } else if (distance_to_next < 30)
                        {
                            // insert an 'and' after this
                            record.Pacenotes.Add(new Pacenote() {Note = "detail_and"});
                            // merge with next
                            if (Config.Instance.ConnectCloseDistanceCallToNextPacenote && (i + 1) < records.Count)
                            {   // make sure i+1 is valid (to prevent index out of range)
                                record.Pacenotes.AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(records[i + 1]).Pacenotes);
                                i++;
                            }
                        } else {
                            // ignore this call
                            continue;
                        }

                        reader.PacenoteRecords.Add(record);
                        lastRecord = record;
                    }

                    continue;
                }

                if (r.Pacenote.EndsWith("distance_call") && i == records.Count - 1)
                {
                    // ignore this
                    continue;
                }

                if (lastRecord != null && 
                    reader.PacenoteRecords.Count > 0 && 
                    MathF.Round(r.Distance) == MathF.Round(lastRecord.Distance.Value))
                {
                    //merge them
                    lastRecord.Pacenotes
                        .AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(r).Pacenotes);
                    continue;
                }

                if (r.Pacenote.Contains("onto_"))
                {
                    r.Pacenote = r.Pacenote.Replace("onto_", "");
                }

                record = PacenoteRecord.FromCrewChiefPacenoteRecord(r);
                if (isCorner(r.Pacenote) && 
                    i != records.Count - 1 &&
                    isCorner(records[i + 1].Pacenote))
                {
                    var distance_to_next = records[i + 1].Distance - r.Distance;
                    if (distance_to_next < 15)
                    {
                        // insert an 'into' after this
                        record.Pacenotes.Add(new Pacenote() {Note = "detail_into"});
                        // merge with next
                        if (Config.Instance.ConnectCloseDistanceCallToNextPacenote)
                        {
                            record.Pacenotes.AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(records[i + 1]).Pacenotes);
                            i++;
                        }
                    } else if (distance_to_next < 30)
                    {
                        // insert an 'and' after this
                        record.Pacenotes.Add(new Pacenote() {Note = "detail_and"});
                        // merge with next
                        if (Config.Instance.ConnectCloseDistanceCallToNextPacenote)
                        {
                            record.Pacenotes.AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(records[i + 1]).Pacenotes);
                            i++;
                        }
                    }

                }
                reader.PacenoteRecords.Add(record);
                
                lastRecord = record;
                record = new PacenoteRecord();
            }

            // they're all dynamic.
            reader.Flags.Add(ScriptFlags.DYNAMIC);
            reader.FlagParameters.Add(ScriptFlags.DYNAMIC, new List<string>());
            reader.FlagComments.Add(ScriptFlags.DYNAMIC, "");

            return reader;
        }

        private static bool isCorner(string note) {
            return note.EndsWith("left", StringComparison.OrdinalIgnoreCase) || 
                note.EndsWith("right", StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            foreach (var flag in Flags)
            {
                sb.AppendLine(ScriptFlagParser.ToString(flag, FlagParameters[flag], FlagComments[flag]));
            }

            foreach (var pacenoteRecord in PacenoteRecords)
            {
                sb.AppendLine(pacenoteRecord.ToString());
            }

            return sb.ToString();
        }
    }
}
