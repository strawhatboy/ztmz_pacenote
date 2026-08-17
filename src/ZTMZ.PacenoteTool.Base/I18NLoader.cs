using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
// using System.Windows;
using Newtonsoft.Json.Linq;
using Neo.IronLua;
using System.Linq.Expressions;

namespace ZTMZ.PacenoteTool.Base
{
    // Singleton
    public class I18NLoader
    {
        public const string FallbackCultureName = "en-us";

        //public static string I18NPath = "lang";

        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public List<string> cultures;
        public List<string> culturesFullname;

        public Dictionary<string, Dictionary<string, string>> Resources;

        public string CurrentCultureName { set; get; }
        public IDictionary<string, string> CurrentCulture { set; get; }
        private IDictionary<string, string> FallbackCulture { set; get; }

        // public ResourceDictionary CurrentDict { get; private set; }

        public bool IgnoreCase { get; private set; }


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

        private I18NLoader(bool ignoreCase = true)
        {
            IgnoreCase = ignoreCase;
        }

        public void Initialize(IList<string> i18nPaths)
        {
            Resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            cultures = new List<string>(); 
            culturesFullname = new List<string>();
            CultureInfo[] cinfo = CultureInfo.GetCultures(CultureTypes.AllCultures);
            var cultureDict = cinfo.ToDictionary(a => a.Name.ToLowerInvariant(), a => a.NativeName, StringComparer.OrdinalIgnoreCase);

            if (cultureDict.ContainsKey("zh-hans-cn"))
            {
                cultureDict["zh-cn"] = cultureDict["zh-hans-cn"];
            }
            // shit, dotnet6 has no zh-cn
            CurrentCultureName = CultureInfo.CurrentCulture.Name.ToLowerInvariant();

            var jsonFiles = new List<string>();
            foreach (var path in i18nPaths)
            {
                if (Directory.Exists(path))
                {
                    jsonFiles.AddRange(Directory.GetFiles(path, "*.json"));
                }
            }

            foreach (var jsonFile in jsonFiles)
            {
                // load all files like "en-us.json" or "en-us.codemasters.json"
                var jsonFileWithoutDir = Path.GetFileName(jsonFile);
                var fileNameWithoutExtension = jsonFileWithoutDir.Substring(0, jsonFileWithoutDir.IndexOf('.')).ToLowerInvariant();
                if (cultureDict.TryGetValue(fileNameWithoutExtension, out var cultureFullname))
                {
                    if (!cultures.Contains(fileNameWithoutExtension)) {
                        cultures.Add(fileNameWithoutExtension);
                        culturesFullname.Add(cultureFullname);
                        Resources.Add(fileNameWithoutExtension, new Dictionary<string, string>());
                    }
                }

                
                if (!Resources.ContainsKey(fileNameWithoutExtension))
                {
                    _logger.Warn("culture {0} not found when loading i18n json file {1}, skipping this file.", fileNameWithoutExtension, jsonFile);
                    continue;
                }

                var content = File.ReadAllText(jsonFile);
                var jObj = JObject.Parse(content);
                _logger.Debug("loading i18n json file {0}", jsonFile);
                readJson(fileNameWithoutExtension, jObj);
            }

            Resources.TryGetValue(FallbackCultureName, out var fallbackCulture);
            FallbackCulture = fallbackCulture;
            if (FallbackCulture == null)
            {
                _logger.Warn("Fallback localization culture {0} was not loaded.", FallbackCultureName);
            }

            SetCulture(CurrentCultureName);
        }

        // after setCulture, the CurrentCulture will be changed, 
        // need to apply CurrentCulture to WPF Application.Current.Resources
        // or use I18NLoader.Instance[] to get the value of the key
        public void SetCulture(string culture)
        {
            culture = culture?.ToLowerInvariant() ?? string.Empty;
            if (TryResolveCulture(culture, out var resolvedCulture, out var resources))
            {
                CurrentCultureName = resolvedCulture;
                CurrentCulture = CreateEffectiveCulture(resources);
            } else
            {
                CurrentCultureName = FallbackCultureName;
                CurrentCulture = CreateEffectiveCulture(null);
            }

            // List<ResourceDictionary> resources = new List<ResourceDictionary>();
            // //I18NLoader.Instance.Initialize();
            // //I18NLoader.Instance.SetCulture("zh-CN");
            // CurrentDict = new ResourceDictionary();
            // //newDict.Source = new Uri("https://gitee.com/ztmz/ztmz_pacenote/" + I18NLoader.Instance.CurrentCultureName);
            // foreach (var key in I18NLoader.Instance.CurrentCulture.Keys)
            // {
            //     CurrentDict.Add(key, I18NLoader.Instance.CurrentCulture[key]);
            // }
            // Application.Current.Resources.MergedDictionaries.Add(CurrentDict);
        }

        private bool TryResolveCulture(
            string culture,
            out string resolvedCulture,
            out Dictionary<string, string> resources)
        {
            if (Resources.TryGetValue(culture, out resources))
            {
                resolvedCulture = culture;
                return true;
            }

            if (culture.Equals("zh-hans-cn", StringComparison.OrdinalIgnoreCase)
                && Resources.TryGetValue("zh-cn", out resources))
            {
                resolvedCulture = "zh-cn";
                return true;
            }

            if (!culture.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase))
            {
                var languagePrefix = culture.Split('-')[0];
                var regionalFallback = cultures.FirstOrDefault(candidate =>
                    candidate.StartsWith(languagePrefix + "-", StringComparison.OrdinalIgnoreCase));
                if (regionalFallback != null && Resources.TryGetValue(regionalFallback, out resources))
                {
                    resolvedCulture = regionalFallback;
                    return true;
                }
            }

            resolvedCulture = string.Empty;
            resources = null;
            return false;
        }

        private IDictionary<string, string> CreateEffectiveCulture(IDictionary<string, string> culture)
        {
            var effectiveCulture = new Dictionary<string, string>();
            if (FallbackCulture != null)
            {
                foreach (var resource in FallbackCulture)
                {
                    effectiveCulture[resource.Key] = resource.Value;
                }
            }

            if (culture != null)
            {
                foreach (var resource in culture)
                {
                    effectiveCulture[resource.Key] = resource.Value;
                }
            }

            return effectiveCulture;
        }

        private void readJson(string culture, JObject root) {
            foreach (var child in root.Children())
            {
                if (child is JProperty property)
                {
                    readJson(culture, property.Value);
                }
            }
        }

        private void readJson(string culture, JToken unknownToken) {
            if (unknownToken is JValue value)
            {
                var propertyName = unknownToken.Path;
                var valueStr = value.Value.ToString();
                _logger.Trace("trying to assign i18n property {0} {1} with value: {2}", culture, propertyName, valueStr);
                if (IgnoreCase) {
                    Resources[culture][propertyName.ToLower()] = valueStr;
                }
                Resources[culture][propertyName] = valueStr;
            }
            else if (unknownToken is JObject obj)
            {
                readJson(culture, obj);
            }
            else if (unknownToken is JArray array)
            {
                foreach (var x in array) {
                    readJson(culture, x);
                }
            }
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
                    var propertyName = constructPropertyName(properties);
                    var value = reader.Value.ToString();
                    _logger.Trace("trying to assign i18n property {0} {1} with value: {2}", culture, propertyName, value);
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

        public string this[string idx]
        {
            get
            {
                if (IgnoreCase) {
                    idx = idx.ToLower();
                }
                if (this.CurrentCulture != null && this.CurrentCulture.ContainsKey(idx))
                {
                    return this.CurrentCulture[idx].ToString();
                }

                if (FallbackCulture != null && FallbackCulture.ContainsKey(idx))
                {
                    return FallbackCulture[idx].ToString();
                }

                return idx;
            }
        }

        public string ResolveByKey(string idx) {
            return this[idx];
        }

        public string ResolveByKeyAndCulture(string idx, string culture) {
            if (IgnoreCase) {
                idx = idx.ToLowerInvariant();
            }
            culture = culture?.ToLowerInvariant() ?? string.Empty;
            if (Resources.TryGetValue(culture, out var resources) && resources.ContainsKey(idx)) {
                return resources[idx].ToString();
            }
            if (FallbackCulture != null && FallbackCulture.ContainsKey(idx)) {
                return FallbackCulture[idx].ToString();
            }

            return idx;
        }
    }
}
