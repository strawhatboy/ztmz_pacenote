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
        public static Dictionary<string, string> ALIAS { private set; get; } = loadAlias("./aliases.csv");
        public static Dictionary<string, Tuple<string, string, string>> ALIAS_CONSTRUCTED { private set; get; } = new Dictionary<string, Tuple<string, string, string>>();
        public static Dictionary<string, string> PACENOTES { private set; get; } = loadCsv("./pacenotes.csv");
        public static Dictionary<string, string> MODIFIERS { private set; get; } = loadCsv("./modifiers.csv");

        public static Dictionary<string, string> FLAGS { private set; get; } = loadCsv("./flags.csv");

        // public static System.Windows.Media.ImageSource IMG_FLAG = new BitmapImage(new Uri("./flag.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_PACENOTE = new BitmapImage(new Uri("./pacenote.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_MODIFIER = new BitmapImage(new Uri("./modifier.png", UriKind.Relative));
        // public static System.Windows.Media.ImageSource IMG_ALIAS = new BitmapImage(new Uri("./alias.png", UriKind.Relative));

        static ScriptResource()
        {
            foreach (var alias in ALIAS)
            {
                var res = CheckAlias(alias.Key);
                if (res.Item1)
                {
                    ALIAS_CONSTRUCTED[alias.Key] = new Tuple<string, string, string>(res.Item2, res.Item3, res.Item4);
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
        private static Dictionary<string, string> loadAlias(string path)
        {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var def = new
                {
                    Alias = string.Empty,
                    Token = string.Empty,
                };
                var records = csv.GetRecords(def);
                return records.ToDictionary(r => r.Alias, r => r.Token);
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

            var token = ALIAS[alias];
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
                return new BitmapImage(new Uri("pack://application:,,,/flag.png"));
            } else if (type == TYPE_ALIAS)
            {
                return new BitmapImage(new Uri("pack://application:,,,/alias.png")); 
            } else if (type == TYPE_MODIFIER)
            {
                return new BitmapImage(new Uri("pack://application:,,,/modifier.png"));
            } else if (type == TYPE_PACENOTE)
            {
                return new BitmapImage(new Uri("pack://application:,,,/pacenote.png"));
            }

            return null;
        }
    }
}