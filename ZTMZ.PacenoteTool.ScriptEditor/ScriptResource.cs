using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CsvHelper;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    public class ScriptResource
    {
        public static string TYPE_PACENOTE = "[路书]";
        public static string TYPE_MODIFIER = "[路书修饰]";
        public static string TYPE_FLAG = "[标志]";
        public static string TYPE_ALIAS = "[代称]";
        public static Dictionary<string, Tuple<string, string>> ALIAS { private set; get; } = loadAlias("./aliases.csv");

        // type, token, description, speech
        public static Dictionary<string, Tuple<string, string, string, string>> ALIAS_CONSTRUCTED { private set; get; } = new Dictionary<string, Tuple<string, string, string, string>>();

        public static List<Dictionary<string, string>> ALIAS_SPEECH_DICT { private set; get; } = new List<Dictionary<string, string>>(4)
        {
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
        };
        public static Dictionary<string, Tuple<string, bool>> RAW_PACENOTES = loadPacenotes("./pacenotes.csv");
        public static Dictionary<string, string> PACENOTES { private set; get; } = new Dictionary<string, string>();   // = loadCsv("./pacenotes.csv");
        public static Dictionary<string, string> MODIFIERS { private set; get; } = new Dictionary<string, string>();    // = loadCsv("./modifiers.csv");
        public static Dictionary<string, string> FALLBACK { private set; get; } = loadFallback("./fallback.csv");
        public static Dictionary<string, string> FLAGS { private set; get; } = loadCsv("./flags.csv");

        private static BitmapImage _imgFlag = new BitmapImage(new Uri("pack://application:,,,/flag.png"));
        private static BitmapImage _imgAlias = new BitmapImage(new Uri("pack://application:,,,/alias.png"));
        private static BitmapImage _imgModifier = new BitmapImage(new Uri("pack://application:,,,/modifier.png"));
        private static BitmapImage _imgPacenote = new BitmapImage(new Uri("pack://application:,,,/pacenote.png"));


        // public static System.Windows.Media.ImageSource IMG_FLAG = new BitmapImage(new Uri("./flag.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_PACENOTE = new BitmapImage(new Uri("./pacenote.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_MODIFIER = new BitmapImage(new Uri("./modifier.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_ALIAS = new BitmapImage(new Uri("./alias.png", UriKind.Relative));

        static ScriptResource()
        {
            // load PACENOTES & MODIFIERS
            foreach (var p in RAW_PACENOTES)
            {
                if (p.Value.Item2)
                {
                    MODIFIERS[p.Key] = p.Value.Item1;
                }
                PACENOTES[p.Key] = p.Value.Item1;
            }

            foreach (var alias in ALIAS)
            {
                var res = CheckAlias(alias.Key);
                if (res.Item1)
                {
                    //legal alias
                    ALIAS_CONSTRUCTED[alias.Key] = new Tuple<string, string, string, string>(res.Item2, res.Item3, res.Item4, alias.Value.Item2);

                    var speeches = alias.Value.Item2.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in speeches)
                    {
                        var speech = s.Trim();
                        var len = speech.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        ALIAS_SPEECH_DICT[len - 1][speech] = alias.Key;
                    }
                }
            }
        }

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
        private static Dictionary<string, Tuple<string, bool>> loadPacenotes(string path)
        {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var def = new
                {
                    Description = string.Empty,
                    Id = string.Empty,
                    CanBeModifier = 0,
                };
                var records = csv.GetRecords(def);
                return records.ToDictionary(r => r.Id, r=> new Tuple<string, bool>(r.Description, Convert.ToBoolean(r.CanBeModifier)));
            }
        }
        private static Dictionary<string, Tuple<string, string>> loadAlias(string path)
        {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var def = new
                {
                    Alias = string.Empty,
                    Token = string.Empty,
                    Speech = string.Empty,
                };
                var records = csv.GetRecords(def);
                return records.ToDictionary(r => r.Alias, r => new Tuple<string, string>(r.Token, r.Speech));
            }
        }
        private static Dictionary<string, string> loadFallback(string path)
        {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var def = new
                {
                    Token = string.Empty,
                    Fallback_Token = string.Empty,
                };
                var records = csv.GetRecords(def);
                return records.ToDictionary(r => r.Token, r => r.Fallback_Token);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alias"></param>
        /// <returns>
        /// OK, type(pacenote/modifier/flag), token, desc
        /// </returns>
        public static Tuple<bool, string, string, string> CheckAlias(string alias)
        {
            if (!ALIAS.ContainsKey(alias))
            {
                return new Tuple<bool, string, string, string>(false, null, null, null);
            }

            var token = ALIAS[alias].Item1;
            if (PACENOTES.ContainsKey(token))
            {
                return new Tuple<bool, string, string, string>(true, TYPE_PACENOTE, token, PACENOTES[token]);
            }
            if (MODIFIERS.ContainsKey(token))
            {
                return new Tuple<bool, string, string, string>(true, TYPE_MODIFIER, token, MODIFIERS[token]);
            }
            if (FLAGS.ContainsKey(token))
            {
                return new Tuple<bool, string, string, string>(true, TYPE_FLAG, token, FLAGS[token]);
            }
            return new Tuple<bool, string, string, string>(false, null, null, null);
        }

        public static string GetTokenDescription(string token)
        {
            if (ScriptResource.ALIAS_CONSTRUCTED.ContainsKey(token))
            {
                var aliasRes = ScriptResource.ALIAS_CONSTRUCTED[token];
                token = $"{ScriptResource.TYPE_ALIAS}{aliasRes.Item1} {aliasRes.Item2}: {aliasRes.Item3}";
            }
            else if (ScriptResource.PACENOTES.ContainsKey(token))
            {
                token = $"{ScriptResource.TYPE_PACENOTE} {token}: {ScriptResource.PACENOTES[token]}";
            }
            else if (ScriptResource.MODIFIERS.ContainsKey(token))
            {
                token = $"{ScriptResource.TYPE_MODIFIER} {token}: {ScriptResource.MODIFIERS[token]}";
            }
            else if (ScriptResource.FLAGS.ContainsKey(token))
            {
                token = $"{ScriptResource.TYPE_FLAG} {token}: {ScriptResource.FLAGS[token]}";
            }
            return token;
        }

        public static ImageSource GetImageByType(string type)
        {
            if (type == TYPE_FLAG)
            {
                return _imgFlag;
            } else if (type == TYPE_ALIAS)
            {
                return _imgAlias;
            } else if (type == TYPE_MODIFIER)
            {
                return _imgModifier;
            } else if (type == TYPE_PACENOTE)
            {
                return _imgPacenote;
            }

            return null;
        }
    }
}