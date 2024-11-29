using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base
{

    public class AudioFile
    {
        //public string Extension { set; get; }
        //public string FileName { set; get; }
        //public string FilePath { set; get; }
        public int Distance { set; get; }

        //public AudioFileReader AudioFileReader { set; get; }
        //public byte[] Content { set; get; }
        public AutoResampledCachedSound Sound { set; get; } = null;
    }

    public class CoDriverPackageInfo
    {
        public string id { set; get; }  // uuid
        public string name { set; get; }
        public string description { set; get; }
        public string gender { set; get; }
        public string language { set; get; }
        public string homepage { set; get; }
        public string version { set; get; }

        [JsonIgnore] public string Path { set; get; }

        [JsonIgnore]
        public string DisplayText =>
            string.Format("[{0}][{1}] {2}", language, GenderStr, name);

        public override string ToString()
        {
            return DisplayText;
        }

        [JsonIgnore]
        public string GenderStr
        {
            get
            {
                switch (gender)
                {
                    case "M":
                    {
                        return I18NLoader.Instance["misc.gender_male"];
                    }
                    case "F":
                    {
                        return I18NLoader.Instance["misc.gender_female"];
                    }
                }

                return I18NLoader.Instance["misc.gender_unknown"];
            }
        }
    }

    public class CoDriverPackage
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public CoDriverPackageInfo Info { set; get; }
        public ConcurrentDictionary<int, ConcurrentBag<string>> id2tokensPath { private set; get; } = new();
        public ConcurrentDictionary<int, ConcurrentBag<AutoResampledCachedSound>> id2tokens { private set; get; } = new();
        public ConcurrentDictionary<string, ConcurrentBag<string>> tokensPath { private set; get; } = new();

        public ConcurrentDictionary<string, ConcurrentBag<AutoResampledCachedSound>> tokens { private set; get; } = new();

        public async void Export(string path)
        {
            // zip the folder and save it to the path
            if (!Directory.Exists(Info.Path)) {
                return;
            }

            var zipPath = Path.Combine(path, Info.name + ".zip");
            if (File.Exists(zipPath)) {
                await Task.Run(() => File.Delete(zipPath));
            }

            await Task.Run(() => ZipFile.CreateFromDirectory(Info.Path, zipPath, CompressionLevel.SmallestSize, false));
        }

        public async static Task<CoDriverPackage> Load(string codriverPath) {
            CoDriverPackage package = new CoDriverPackage();
            _logger.Debug("loading codriver sounds from {0}", codriverPath);

            // try load info
            var infoFilePath = Path.Join(codriverPath, Constants.CODRIVER_PACKAGE_INFO_FILENAME);
            if (File.Exists(infoFilePath))
            {
                try
                {
                    package.Info =
                        JsonConvert.DeserializeObject<CoDriverPackageInfo>(await File.ReadAllTextAsync(infoFilePath));
                    package.Info.Path = codriverPath;
                }
                catch
                {
                    // boom
                }
            }
            else
            {
                package.Info = new CoDriverPackageInfo()
                {
                    name = codriverPath,
                    Path = codriverPath,
                    version = "0.0.0",
                };
            }

            _logger.Debug("loaded codriver info: {0}", package.Info);
            List<string> filePaths = new List<string>();
            // try file directly

            foreach (var supportedFilter in Constants.SupportedAudioTypes)
            {
                filePaths.AddRange(Directory.GetFiles(codriverPath, supportedFilter));
            }

            // _logger.Debug("found {0} sounds from files directly", filePaths.Count);

            foreach (var f in filePaths)
            {
                if (Config.Instance.PreloadSounds)
                {
                    _logger.Trace("preloading sound from file {0}", f);
                    package.tokens[Path.GetFileNameWithoutExtension(f)] =
                        new ConcurrentBag<AutoResampledCachedSound>() { new AutoResampledCachedSound(f) };
                }
                else
                {
                    package.tokensPath[Path.GetFileNameWithoutExtension(f)] =
                        new ConcurrentBag<string>() { f };
                }
            }
            // _logger.Debug("loaded {0} sounds from files directly", filePaths.Count);

            // not found, try folders
            var soundFilePaths = Directory.GetDirectories(codriverPath);
            // _logger.Debug("found {0} subfolders", soundFilePaths.Length);
            foreach (var soundFilePath in soundFilePaths)
            {
                filePaths.Clear();
                //var soundFilePath = string.Format("{0}/{1}", codriverPath, keyword);
                if (Directory.Exists(soundFilePath))
                {
                    if (Config.Instance.PreloadSounds)
                    {
                        package.tokens[Path.GetFileName(soundFilePath)] = new ConcurrentBag<AutoResampledCachedSound>();
                    }
                    else
                    {
                        package.tokensPath[Path.GetFileName(soundFilePath)] = new ConcurrentBag<string>();
                    }
                    // load all files
                    foreach (var supportedFilter in Constants.SupportedAudioTypes)
                    {
                        filePaths.AddRange(Directory.GetFiles(soundFilePath, supportedFilter));
                    }

                    foreach (var filePath in filePaths)
                    {
                        if (Config.Instance.PreloadSounds)
                        {
                            package.tokens[Path.GetFileName(soundFilePath)].Add(new AutoResampledCachedSound(filePath));
                        }
                        else
                        {
                            package.tokensPath[Path.GetFileName(soundFilePath)].Add(filePath);
                        }
                    }
                }
            }
            _logger.Debug("loaded {0} sounds for {1}", Config.Instance.PreloadSounds ?
                package.tokens.Count :
                package.tokensPath.Count, package.Info);

            // append tokens & tokensPath to id2tokens & id2tokensPath
            foreach (var id in Script.ScriptResource.Instance.PacenoteDict.Keys)
            {
                var filenames = Script.ScriptResource.Instance.FilenameDict[id];
                foreach (var filename in filenames) {
                    if (Config.Instance.PreloadSounds && package.tokens.ContainsKey(filename))
                    {
                        if (package.id2tokens.ContainsKey(id)) {
                            var tmpList = package.tokens[filename].ToList();
                            for (int i = 0; i < package.tokens[filename].Count; i++) {
                                package.id2tokens[id].Add(tmpList[i]);
                            }
                        }
                        else {
                            package.id2tokens[id] = package.tokens[filename];
                        }
                    }
                    if (package.tokensPath.ContainsKey(filename))
                    {
                        if (package.id2tokensPath.ContainsKey(id)) {
                            var tmpList = package.tokensPath[filename].ToList();
                            for (int i = 0; i < package.tokensPath[filename].Count; i++) {
                                package.id2tokensPath[id].Add(tmpList[i]);
                            }
                        }
                        else {
                            package.id2tokensPath[id] = package.tokensPath[filename];
                        }
                    }
                }
            }
            _logger.Debug("loaded {0} sounds for {1} on id2tokens", Config.Instance.PreloadSounds ?
                package.id2tokens.Count :
                package.id2tokensPath.Count, package.Info);

            return package;
        }
        
        public async static Task<CoDriverPackage> Import(string path)
        {
            // import a codriver package from a zip file
            if (!File.Exists(path)) {
                return null;
            }

            var zipPath = path;
            var extractPath = Path.Combine(AppLevelVariables.Instance.GetPath(Constants.PATH_CODRIVERS), Path.GetFileNameWithoutExtension(zipPath));
            if (Directory.Exists(extractPath)) {
                await Task.Run(() => Directory.Delete(extractPath, true));
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath));

            return await Load(extractPath);
        }
    }
}
