using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms.Design;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base;
using Constants = ZTMZ.PacenoteTool.Base.Constants;

namespace ZTMZ.PacenoteTool.AudioPackageManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public Dictionary<string, CoDriverPackage> CoDriverPackages { set; get; } =
            new Dictionary<string, CoDriverPackage>();

        public static string CODRIVER_PACKAGE_INFO_FILENAME = "info.json";

        public IList<string> SupportedAudioTypes { set; get; } = new List<string>();
        public ZTMZAudioPlaybackEngine engine = new ZTMZAudioPlaybackEngine();

        private ObservableCollection<object> _DataContent = new();

        public ObservableCollection<object> DataContent
        {
            get { return _DataContent; }
            set
            {
                _DataContent = value;
                OnPropertyChanged("DataContent");
            }
        }

        public Dictionary<string, Tuple<string, bool>> Pacenotes { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            this.loadPacenotes();
            this.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;
            loadAudioPackages();
            this.lv_AudioPackages.ItemsSource = from p in this.CoDriverPackages.Values select p.Info;
            this.lv_Content.DataContext = this;
        }

        private void loadPacenotes()
        {
            // get pacenotes merged from system sound
            var pacenotes = new Dictionary<string, Tuple<string, bool>>();
            foreach (var p in ScriptResource.RAW_PACENOTES)
            {
                pacenotes[p.Key] = new Tuple<string, bool>(p.Value.Item1, p.Value.Item6);
            }

            foreach (var p in Constants.SYSTEM_SOUND)
            {

                pacenotes[p.Key] = new Tuple<string, bool>(p.Value, false);
            }

            this.Pacenotes = pacenotes;
        }

        private void loadAudioPackages()
        {
            foreach (var codriverPath in this.GetAllCodrivers())
            {
                this.CoDriverPackages[codriverPath] = new CoDriverPackage();

                // try load info
                var infoFilePath = Path.Join(codriverPath, CODRIVER_PACKAGE_INFO_FILENAME);
                if (File.Exists(infoFilePath))
                {
                    try
                    {
                        this.CoDriverPackages[codriverPath].Info =
                            JsonConvert.DeserializeObject<CoDriverPackageInfo>(File.ReadAllText(infoFilePath));
                        this.CoDriverPackages[codriverPath].Info.Path = codriverPath;
                    }
                    catch
                    {
                        // boom
                    }
                }

                List<string> filePaths = new List<string>();
                // try file directly

                foreach (var supportedFilter in this.SupportedAudioTypes)
                {
                    filePaths.AddRange(Directory.GetFiles(codriverPath, supportedFilter));
                }

                foreach (var f in filePaths)
                {
                    // if (Config.Instance.PreloadSounds)
                    // {
                    //     this.CoDriverPackages[codriverPath].tokens[Path.GetFileNameWithoutExtension(f)] =
                    //         new List<AutoResampledCachedSound>() { new AutoResampledCachedSound(f) };
                    // }
                    // else
                    // {
                    this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileNameWithoutExtension(f)] =
                        new List<string>() { f };
                    // }
                }

                // not found, try folders
                var soundFilePaths = Directory.GetDirectories(codriverPath);
                foreach (var soundFilePath in soundFilePaths)
                {
                    filePaths.Clear();
                    //var soundFilePath = string.Format("{0}/{1}", codriverPath, keyword);
                    if (Directory.Exists(soundFilePath))
                    {
                        // if (Config.Instance.PreloadSounds)
                        // {
                        //     this.CoDriverPackages[codriverPath].tokens[Path.GetFileName(soundFilePath)] = new List<AutoResampledCachedSound>();
                        // }
                        // else
                        // {
                        this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileName(soundFilePath)] =
                            new List<string>();
                        // }
                        // load all files
                        foreach (var supportedFilter in this.SupportedAudioTypes)
                        {
                            filePaths.AddRange(Directory.GetFiles(soundFilePath, supportedFilter));
                        }

                        foreach (var filePath in filePaths)
                        {
                            // if (Config.Instance.PreloadSounds)
                            // {
                            //     this.CoDriverPackages[codriverPath].tokens[Path.GetFileName(soundFilePath)].Add(new AutoResampledCachedSound(filePath));
                            // }
                            // else
                            // {
                            this.CoDriverPackages[codriverPath].tokensPath[Path.GetFileName(soundFilePath)]
                                .Add(filePath);
                            // }
                        }
                    }
                }
            }
        }

        public List<string> GetAllCodrivers()
        {
            var dirs = Directory.GetDirectories(AppLevelVariables.Instance.GetPath("codrivers\\")).ToList();
            if (Directory.Exists(Config.Instance.AdditionalCoDriverPackagesSearchPath))
            {
                dirs.AddRange(Directory.GetDirectories(Config.Instance.AdditionalCoDriverPackagesSearchPath));
            }

            return dirs;
        }

        private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            e.Handled = true;
        }

        private void Lv_AudioPackages_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.DataContent.Clear();
            if (this.lv_AudioPackages.SelectedItem == null)
            {
                return;
            }

            CoDriverPackageInfo pkgInfo = (CoDriverPackageInfo)this.lv_AudioPackages.SelectedItem;
            if (!this.CoDriverPackages.ContainsKey(pkgInfo.Path))
            {
                return;
            }

            var pkg = this.CoDriverPackages[pkgInfo.Path];


            foreach (var p in this.Pacenotes)
            {
                if (pkg.tokensPath.ContainsKey(p.Key) && pkg.tokensPath[p.Key].Count > 0)
                {
                    var token = pkg.tokensPath[p.Key];
                    this.DataContent.Add(new
                    {
                        Token = p.Key,
                        TokenDescription = p.Value.Item1,
                        IsAvailable = bool2str(true),
                        IsOfficial = bool2str(!p.Value.Item2),
                        FilesCount = token.Count,
                        Files = token.Select((s, index) =>
                            new { Index = index + 1, Uri = new System.Uri(s).AbsoluteUri.ToString() })
                    });
                }
                else
                {
                    this.DataContent.Add(new
                    {
                        Token = p.Key,
                        TokenDescription = p.Value.Item1,
                        IsAvailable = bool2str(false),
                        IsOfficial = bool2str(!p.Value.Item2),
                        FilesCount = 0,
                        Files = new List<string>()
                        {
                        }
                    });
                }
            }
        }

        private string bool2str(bool b)
        {
            return b ? "✅" : "❌";
        }

        private void hl_listen_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            engine.PlaySound(e.Uri.LocalPath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Hyperlink_Name_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Hyperlink el = (Hyperlink)sender;
            var path = el.Tag.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    System.IO.Path.GetFullPath(path)));
            }

            e.Handled = true;
        }

        private void Btn_Refresh_OnClick(object sender, RoutedEventArgs e)
        {
            loadAudioPackages();
            this.lv_AudioPackages.ItemsSource = from p in this.CoDriverPackages.Values select p.Info;
        }

        private void Btn_New_OnClick(object sender, RoutedEventArgs e)
        {
            // throw new NotImplementedException();
        }

        private void New_dialog_closing(object sender, DialogClosingEventArgs eventArgs)
        {
            if (eventArgs.Parameter != null && eventArgs.Parameter is Boolean)
            {
                if ((bool)eventArgs.Parameter == false)
                {
                    return;
                }
                // create audio pkg
                CoDriverPackageInfo info = new CoDriverPackageInfo()
                {
                    name = this.tb_name.Text,
                    description = this.tb_description.Text,
                    gender = this.cb_gender.SelectedIndex == 0 ? "M" : "F",
                    homepage = this.tb_homepage.Text,
                    language = this.tb_language.Text,
                    version = this.tb_version.Text
                };

                // convert name to a valid folder name
                var folderName = this.tb_name.Text.Trim();
                foreach (var invalidChar in Path.GetInvalidPathChars())
                {
                    folderName = folderName.Replace(invalidChar, '_');
                }

                // create the folder for audio pkg
                var audioPkgPath = Path.Combine(AppLevelVariables.Instance.GetPath("codrivers\\"), folderName);
                Directory.CreateDirectory(audioPkgPath);

                // create subfolders for pacenotes
                foreach (var p in this.Pacenotes.Keys)
                {
                    Directory.CreateDirectory(Path.Combine(audioPkgPath, p));
                }

                // put info json inside
                var infoFilePath = Path.Join(audioPkgPath, CODRIVER_PACKAGE_INFO_FILENAME);
                File.WriteAllText(infoFilePath, JsonConvert.SerializeObject(info));
            }
        }
    }
}
