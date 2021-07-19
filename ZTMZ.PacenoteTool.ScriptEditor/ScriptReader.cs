using System;
using System.Collections.Generic;
using System.IO;

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
            var lines = str.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
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
    }
}