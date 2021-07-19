using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using CsvHelper;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    public class ScriptResource
    {
        public static Dictionary<string, string> PACENOTES { private set; get; } = loadCsv("./pacenotes.csv");
        public static Dictionary<string, string> MODIFIERS { private set; get; } = loadCsv("./modifiers.csv");

        private static Dictionary<string, string> loadCsv(string path)
        {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var def = new
                {
                    Description = string.Empty,
                    Id = string.Empty
                };
                var records = csv.GetRecords(def);
                return records.ToDictionary(r => r.Id, r => r.Description);
            }
        }
    }
}