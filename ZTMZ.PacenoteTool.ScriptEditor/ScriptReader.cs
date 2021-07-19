using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
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
        public IList<PacenoteRecord> PacenoteRecords { set; get; } = new List<PacenoteRecord>();

        public static ScriptReader ReadFromFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            return ReadFromLines(lines);
        }

        public static ScriptReader ReadFromString(string str)
        {
            var lines = str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ReadFromLines(lines);
        }

        public static ScriptReader ReadFromLines(string[] lines)
        {
            ScriptReader reader = new ScriptReader();
            for (int lineNo = 0; lineNo < lines.Length; lineNo++)
            {
                var line = lines[lineNo];
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

        public static ScriptReader ReadFromJson(string jsonFile)
        {
            var content = File.ReadAllText(jsonFile);
            var records = JsonConvert.DeserializeObject<List<CrewChiefPacenoteRecord>>(content);
            PacenoteRecord record = new PacenoteRecord();
            PacenoteRecord lastRecord = null;
            ScriptReader reader = new ScriptReader();
            for (var i = 0; i < records.Count; i++)
            {
                var r = records[i];
                r.Pacenote = r.Pacenote.Trim();
                r.Modifier = r.Modifier.Trim();
                if (r.Pacenote == "detail_distance_call" && i != records.Count - 1)
                {
                    var distance_to_call = (int)(records[i + 1].Distance - r.Distance) / 10 * 10;
                    var distance_label = string.Format("number_{0}", distance_to_call);
                    if (ScriptResource.PACENOTES.ContainsKey(distance_label))
                    {
                        record.Distance = r.Distance;
                        record.Pacenotes.Add(new Pacenote() { Note = distance_label });
                        reader.PacenoteRecords.Add(record);
                        lastRecord = record;
                    } // else ignore this call
                    continue;
                }
                if (r.Pacenote == "detail_distance_call" && i == records.Count - 1)
                {
                    // ignore this
                    continue;
                }

                if (reader.PacenoteRecords.Count > 0 && r.Distance == lastRecord.Distance)
                {
                    //merge them
                    lastRecord.Pacenotes
                        .AddRange(PacenoteRecord.FromCrewChiefPacenoteRecord(r).Pacenotes);
                    continue;
                }

                record = PacenoteRecord.FromCrewChiefPacenoteRecord(r);
                reader.PacenoteRecords.Add(record);
                lastRecord = record;
                record = new PacenoteRecord();
            }

            return reader;
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            foreach (var pacenoteRecord in PacenoteRecords)
            {
                sb.AppendLine(pacenoteRecord.ToString());
            }

            return sb.ToString();
        }
    }
}