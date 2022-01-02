using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using System.Windows;

namespace ZTMZ.PacenoteTool.Base
{
    // Singleton
    public class I18NLoader
    {
        //public static string I18NPath = "lang";

        public List<string> cultures = new List<string>();
        public List<string> culturesFullname = new List<string>();

        public Dictionary<string, Dictionary<string, string>> Resources = new Dictionary<string,Dictionary<string, string>>();

        public string CurrentCultureName { set; get; }
        public IDictionary<string, string> CurrentCulture { set; get; }

        public ResourceDictionary CurrentDict { get; private set; }


        private static I18NLoader _instance;
        public static I18NLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new I18NLoader();
                }
                return _instance;
            }
        }

        private I18NLoader()
        {
        }

        public void Initialize(string i18nPath = "lang")
        {
            CultureInfo[] cinfo = CultureInfo.GetCultures(CultureTypes.AllCultures);
            var cultureDict = cinfo.ToDictionary(a => a.Name.ToLower(), a => a.DisplayName);

            if (cultureDict.ContainsKey("zh-hans-cn"))
            {
                cultureDict["zh-cn"] = cultureDict["zh-hans-cn"];
            }
            // shit, dotnet6 has no zh-cn
            CurrentCultureName = CultureInfo.CurrentCulture.Name.ToLower();

            // load from I18NPath
            var jsonFiles = Directory.GetFiles(i18nPath, "*.json");
            foreach (var jsonFile in jsonFiles)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(jsonFile).ToLower();
                if (cultureDict.ContainsKey(fileNameWithoutExtension))
                {
                    cultures.Add(fileNameWithoutExtension);
                    culturesFullname.Add(cultureDict[fileNameWithoutExtension]);
                    Resources.Add(fileNameWithoutExtension, new Dictionary<string, string>());
                }

                JsonTextReader reader = new JsonTextReader(new StringReader(File.ReadAllText(jsonFile)));
                List<string> properties = new List<string>();
                readJson(fileNameWithoutExtension, reader, properties);
            }

            SetCulture(CurrentCultureName);
        }

        public void SetCulture(string culture)
        {
            culture = culture.ToLower();
            if (Resources.ContainsKey(culture))
            {
                CurrentCulture = Resources[culture];
            } else
            {
                SetCulture("en-us");
                return;
            }

            List<ResourceDictionary> resources = new List<ResourceDictionary>();
            //I18NLoader.Instance.Initialize();
            //I18NLoader.Instance.SetCulture("zh-CN");
            CurrentDict = new ResourceDictionary();
            //newDict.Source = new Uri("https://gitee.com/ztmz/ztmz_pacenote/" + I18NLoader.Instance.CurrentCultureName);
            foreach (var key in I18NLoader.Instance.CurrentCulture.Keys)
            {
                CurrentDict.Add(key, I18NLoader.Instance.CurrentCulture[key]);
            }
            Application.Current.Resources.MergedDictionaries.Add(CurrentDict);
        }

        private void readJson(string culture, JsonTextReader reader, List<string> properties)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    properties.Add(reader.Value.ToString());
                }

                else if (reader.TokenType == JsonToken.String)
                {
                    Resources[culture][constructPropertyName(properties)] = reader.Value.ToString();
                    properties.RemoveAt(properties.Count - 1);
                }

                else if (reader.TokenType == JsonToken.StartObject)
                {
                    continue;
                }

                else if (reader.TokenType == JsonToken.EndObject)
                {
                    if (properties.Count > 0)
                        properties.RemoveAt(properties.Count - 1);
                }
            }
        }

        private string constructPropertyName(List<string> queue)
        {
            return string.Join(".", queue);
        }
    }
}
