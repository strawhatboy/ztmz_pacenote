using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class AboutView : UserControl, INotifyPropertyChanged
{
    private string _dataFolderSize = string.Empty;
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;
    private event PropertyChangedEventHandler? ViewPropertyChanged;

    public AboutView()
    {
        AppLogo = TryLoadBitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"));
        DataContext = this;
        InitializeComponent();
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ViewPropertyChanged += value;
        remove => ViewPropertyChanged -= value;
    }

    public Bitmap? AppLogo { get; }

    public string VersionText => $"{Assembly.GetEntryAssembly()?.GetName().Version} [{ToolUtils.GetToolVersion()}]";

    public string DataFolderSize
    {
        get => _dataFolderSize;
        private set
        {
            if (_dataFolderSize == value)
                return;

            _dataFolderSize = value;
            ViewPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataFolderSize)));
        }
    }

    public bool IsAutoUpdate
    {
        get => _isAutoUpdate;
        set
        {
            if (_isAutoUpdate == value)
                return;

            _isAutoUpdate = value;
            Config.Instance.CheckUpdateWhenStartup = value;
            Config.Instance.SaveUserConfig();
            ViewPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutoUpdate)));
        }
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        DataFolderSize = await Task.Run(CalculateDataFolderSize);
    }

    private static Bitmap? TryLoadBitmap(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CalculateDataFolderSize()
    {
        try
        {
            long size = 0;
            var dataFolder = AppLevelVariables.Instance.AppConfigFolder;
            foreach (var file in new DirectoryInfo(dataFolder).EnumerateFiles("*", SearchOption.AllDirectories))
                size += file.Length;

            return FormatSize(size);
        }
        catch
        {
            return "N/A";
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        decimal value = bytes;
        while (unit < suffixes.Length - 1 && Math.Round(value / 1024) >= 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:n1} {suffixes[unit]}";
    }

    private async void OpenDataFolder_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var directory = AppLevelVariables.Instance.AppConfigFolder;
        if (await DesktopFolderLauncher.TryOpenAsync(directory))
        {
            return;
        }

        await ShowTextDialogAsync(
            AvaloniaLocalization.Get("ui.openDataFolder"),
            string.Format(AvaloniaLocalization.Get("settings.tooltip.folderOpenFailed"), directory));
    }

    private async void MoveDataFolder_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (AppLevelVariables.Instance.IsPortableVersion())
        {
            await ShowTextDialogAsync(
                AvaloniaLocalization.Get("dataFolderMigration.title"),
                AvaloniaLocalization.Get("dataFolderMigration.portableNotSupported"));
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider || topLevel is not MainWindow mainWindow)
        {
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = AvaloniaLocalization.Get("dataFolderMigration.selectDestination")
        });
        var destinationPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        try
        {
            var sourcePath = AppLevelVariables.Instance.AppConfigFolder;
            var plan = await Task.Run(() => new DataFolderMigrationService()
                .CreatePlan(sourcePath, destinationPath));
            var confirmation = FaContentDialogFactory.Create(new FAContentDialog
            {
                Title = AvaloniaLocalization.Get("dataFolderMigration.title"),
                Content = new SelectableTextBlock
                {
                    MaxWidth = 640,
                    TextWrapping = TextWrapping.Wrap,
                    Text = string.Format(
                        AvaloniaLocalization.Get("dataFolderMigration.confirmation"),
                        plan.SourcePath,
                        plan.DestinationPath,
                        FormatSize(plan.TotalBytes),
                        FormatSize(plan.RequiredFreeSpaceBytes),
                        FormatSize(plan.AvailableFreeSpaceBytes))
                },
                PrimaryButtonText = AvaloniaLocalization.Get("dataFolderMigration.start"),
                CloseButtonText = AvaloniaLocalization.Get("dataFolderMigration.cancel"),
                DefaultButton = FAContentDialogButton.Primary
            });
            if (await confirmation.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            {
                return;
            }

            Config.Instance.SaveUserConfig();
            AvaloniaDataFolderMigrationLauncher.Start(new AvaloniaDataFolderMigrationRequest(
                sourcePath,
                plan.DestinationPath,
                Environment.ProcessId,
                Config.Instance.Language));
            mainWindow.RequestDataFolderMigrationShutdown();
        }
        catch (InsufficientDataFolderMigrationSpaceException exception)
        {
            await ShowTextDialogAsync(
                AvaloniaLocalization.Get("dataFolderMigration.title"),
                string.Format(AvaloniaLocalization.Get("dataFolderMigration.insufficientSpace"),
                    FormatSize(exception.RequiredBytes), FormatSize(exception.AvailableBytes)));
        }
        catch (Exception exception)
        {
            await ShowTextDialogAsync(
                AvaloniaLocalization.Get("dataFolderMigration.title"),
                string.Format(AvaloniaLocalization.Get("dataFolderMigration.failed"), exception.Message));
        }
    }

    private async void Staff_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowControlDialogAsync(AvaloniaLocalization.Get("settings.staff"), BuildStaffContent());

    private async void Sponsors_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowControlDialogAsync(AvaloniaLocalization.Get("ui.sponsors"), BuildSponsorsContent());

    private async void UpdateHistory_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var beta = ToolUtils.GetToolVersion() == ToolVersion.TEST;
        var fileName = beta
            ? string.Equals(Config.Instance.Language, "zh-cn", StringComparison.OrdinalIgnoreCase)
                ? "更新记录beta.zh-cn.txt"
                : "更新记录beta.en-us.txt"
            : "更新记录.txt";
        await ShowTextDialogAsync(AvaloniaLocalization.Get("ui.tb_updates"), await ReadBundledTextAsync(fileName));
    }

    private async void Eula_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowTextDialogAsync(
            AvaloniaLocalization.Get("ui.eula"),
            await ReadBundledTextAsync(IsChinese ? "eula-cn.txt" : "eula.txt"));

    private async void License_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowTextDialogAsync(
            AvaloniaLocalization.Get("ui.license"),
            await ReadBundledTextAsync(IsChinese ? "license-cn.txt" : "license.txt"));

    private async void OpenSource_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowControlDialogAsync(AvaloniaLocalization.Get("ui.opensoftware"), BuildOpenSourceContent());

    private async void ContactUs_Click(object? sender, RoutedEventArgs eventArgs) =>
        await ShowControlDialogAsync(AvaloniaLocalization.Get("ui.contactUs"), BuildContactContent());

    private bool IsChinese => string.Equals(Config.Instance.Language, "zh-cn", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ReadBundledTextAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            return $"{fileName} was not found in the application package.";

        return await File.ReadAllTextAsync(path);
    }

    private async Task ShowTextDialogAsync(string title, string text)
    {
        var content = new SelectableTextBlock
        {
            MaxWidth = 560,
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
        await ShowControlDialogAsync(title, content);
    }

    private async Task ShowControlDialogAsync(string title, Control content)
    {
        var dialog = FaContentDialogFactory.Create();
        dialog.Title = title;
        dialog.Content = content;
        dialog.CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok");
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private Control BuildStaffContent()
    {
        var content = new StackPanel { MaxWidth = 560, Spacing = 6 };
        AddHeading(content, "staff.specialThanks");
        AddNames(content, ["李总(KyoLee)", "7颗豆子(Meeke777)", "小贤少少(xxss)"]);
        AddHeading(content, "staff.audioPkg");
        AddNames(content,
        [
            "Bigboxx", "圣沙蒙VK", "紫藤林沫", "DR2车手老王", "权威Authority", "wha1ing", "Hippopo",
            "大李子小妖", "mesa", "沧竹烽-南沢いずみ(天津话)", "左衛門", "Chester117", "Kiko(萝莉音)",
            "Nooblesone泡面君", "古轩言", "图图", "简哲", "Jeffery", "吉米(山东寿光话)"
        ]);
        AddHeading(content, "staff.script");
        AddNames(content,
        [
            "含蓄", "Hippopo", "回家的誘いをかける", "栗悟饭とカメハメ波", "Meeke777", "O.Z.(Gliese-436b)",
            "Silenig", "smoke(DR2略略略)", "Zexx", "年轮", "DR2车手老王", "沧竹烽", "花腩居士", "Panda2Bad",
            "李昕珂", "Xhown", "Yang", "凌绝", "冰彻天空", "Nekuz", "Sebastian", "Erwachsene", "米酷二二三",
            "牛爷爷", "毅杨千秋（千年老妖）"
        ]);
        AddHeading(content, "staff.rnd");
        AddCreditGrid(content,
        [
            ("staff.rnd_staff.mainProgrammer", "草帽不是猫"),
            ("staff.rnd_staff.uiDesigner", "无状态选手  ·  SamLIU"),
            ("staff.rnd_staff.vrSupport", "子夜(Lw_Ziye)"),
            ("staff.rnd_staff.pluginDeveloper", "北欧之尘(AaronFangArt)"),
            ("staff.rnd_staff.initialIdea", "7颗豆子(Meeke777)  ·  幻想最初(Greened U)"),
            ("staff.rnd_staff.ideaProvider", "Bigboxx"),
            ("staff.rnd_staff.tester", "Bigboxx  ·  DR2车手老王  ·  含蓄\n沧竹烽  ·  年轮")
        ]);
        AddHeading(content, "staff.club");
        AddCreditGrid(content,
        [
            ("staff.club_staff.clubLeader", "李总(KyoLee)"),
            ("staff.club_staff.eventOrganizer", "小贤少少(xxss)"),
            ("staff.club_staff.toolOrganizer", "无状态选手  ·  DR2车手老王"),
            ("staff.club_staff.specialGuest", "钣金霍  ·  无状态选手")
        ]);
        AddHeading(content, "staff.all");
        content.Children.Add(new TextBlock { Text = AvaloniaLocalization.Get("staff.missing"), HorizontalAlignment = HorizontalAlignment.Center });
        return content;
    }

    private Control BuildSponsorsContent()
    {
        var content = new StackPanel { MaxWidth = 560, Spacing = 6 };
        AddHeading(content, "ui.sponsors_major");
        AddNames(content, [SponsorCatalog.MajorSponsor]);
        AddHeading(content, "ui.sponsors_supporters");
        AddNames(content, SponsorCatalog.SponsorNames);
        return content;
    }

    private static void AddHeading(Panel content, string key) => content.Children.Add(new TextBlock
    {
        Text = AvaloniaLocalization.Get(key),
        FontWeight = FontWeight.SemiBold,
        FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 12, 0, 0)
    });

    private static void AddNames(Panel content, IReadOnlyList<string> names)
    {
        var panel = new WrapPanel { MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var name in names)
            panel.Children.Add(new TextBlock { Text = name, Margin = new Thickness(5, 0, 5, 0) });
        content.Children.Add(panel);
    }

    private static void AddCreditGrid(Panel content, IReadOnlyList<(string TitleKey, string Names)> credits)
    {
        var grid = new Grid
        {
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("2*,3*")
        };
        for (var index = 0; index < credits.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (titleKey, names) = credits[index];
            grid.Children.Add(new TextBlock
            {
                Text = AvaloniaLocalization.Get(titleKey),
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 10, 0),
                [Grid.RowProperty] = index
            });
            grid.Children.Add(new TextBlock
            {
                Text = names,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 0, 0),
                [Grid.RowProperty] = index,
                [Grid.ColumnProperty] = 1
            });
        }
        content.Children.Add(grid);
    }

    private Control BuildContactContent()
    {
        var grid = new Grid
        {
            MaxWidth = 560,
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 10
        };
        AddContactRow(grid, 0, "ui.contactUs_gitee", "https://gitee.com/ztmz/ztmz_pacenote", "https://gitee.com/ztmz/ztmz_pacenote");
        AddContactRow(grid, 1, "ui.contactUs_github", "https://github.com/strawhatboy/ztmz_pacenote", "https://github.com/strawhatboy/ztmz_pacenote");
        AddContactRow(grid, 2, "ui.raise_issue", "https://gitee.com/ztmz/ztmz_pacenote/issues", "https://gitee.com/ztmz/ztmz_pacenote/issues");
        AddStaticContactRow(grid, 3, "ui.contactUs_QQ", "207790761 (ZTMZ Club 总群)\n697673264 (ZTMZ Next Generation Pacenote)\n658110104 (RBR拉力 Simrallycn中国总群)");
        AddContactRow(grid, 4, "ui.tb_friendlink", "https://www.simrallycn.top/index", "https://www.simrallycn.top/index");
        return grid;
    }

    private void AddContactRow(Grid grid, int row, string titleKey, string caption, string target)
    {
        grid.Children.Add(new TextBlock { Text = AvaloniaLocalization.Get(titleKey), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, [Grid.RowProperty] = row });
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap },
            [Grid.RowProperty] = row,
            [Grid.ColumnProperty] = 1
        };
        button.Click += (_, _) => OpenExternal(target);
        grid.Children.Add(button);
    }

    private static void AddStaticContactRow(Grid grid, int row, string titleKey, string text)
    {
        grid.Children.Add(new TextBlock { Text = AvaloniaLocalization.Get(titleKey), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top, [Grid.RowProperty] = row });
        grid.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, [Grid.RowProperty] = row, [Grid.ColumnProperty] = 1 });
    }

    private Control BuildOpenSourceContent()
    {
        var content = new StackPanel { MaxWidth = 560, Spacing = 3 };
        AddHeading(content, "ui.opensoftware_dependency");
        foreach (var (caption, url) in Dependencies)
            AddExternalLink(content, caption, url);
        AddHeading(content, "ui.opensoftware_reference");
        foreach (var (caption, url) in References)
            AddExternalLink(content, caption, url);
        return content;
    }

    private void AddExternalLink(Panel content, string caption, string url)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap }
        };
        button.Click += (_, _) => OpenExternal(url);
        content.Children.Add(button);
    }

    private void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // A dialog is intentionally not shown here: it would be behind the active content dialog.
        }
    }

    private static readonly (string Caption, string Url)[] Dependencies =
    [
        ("[MIT] Newtonsoft.Json", "https://www.newtonsoft.com/json"),
        ("[MIT] NAudio", "https://github.com/naudio/NAudio"),
        ("[MIT] PromptDialog", "https://github.com/manuelcanepa/wpf-prompt-dialog"),
        ("[MIT] AvalonEdit", "http://avalonedit.net/"),
        ("[MS-LT] WindowsAPICodePack-Shell", "https://github.com/aybe/Windows-API-Code-Pack-1.1"),
        ("[Apache-2.0] Vosk", "https://alphacephei.com/vosk/"),
        ("[MIT] GameOverlay.Net", "https://github.com/michel-pi/GameOverlay.Net"),
        ("[MIT] CoDriver-Splitter", "https://github.com/CookiePLMonster/CoDriver-Splitter"),
        ("[MIT] Material Design Xaml Toolkit", "https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit"),
        ("[Inno Setup License] Inno Setup", "https://jrsoftware.org/isinfo.php"),
        ("[MIT] Inno Setup Chinese Simplified Translation", "https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation"),
        ("[BSD-3-Clause] NLog", "https://github.com/NLog/NLog"),
        ("[MIT] CommunityToolkit.Mvvm", "https://github.com/CommunityToolkit/dotnet"),
        ("[MIT] Microsoft.Xaml.Behaviors.Wpf", "https://github.com/microsoft/XamlBehaviorsWpf"),
        ("[MIT] WPF-UI", "https://github.com/lepoco/wpfui"),
        ("[MIT] Monaco Editor", "https://github.com/microsoft/monaco-editor"),
        ("[MIT] Fengari Web", "https://github.com/fengari-lua/fengari-web"),
        ("[MS-PL, Apache-2.0] CsvHelper", "https://github.com/JoshClose/CsvHelper"),
        ("[LGPL-2.1] SoundTouch.Net", "https://github.com/owoudenberg/soundtouch.net"),
        ("[Apache-2.0] NeoLua", "https://github.com/neolithos/neolua"),
        ("[MIT] SevenZipExtractor", "https://github.com/adoconnection/SevenZipExtractor"),
        ("[MIT] Lib.Harmony", "https://github.com/pardeike/Harmony"),
        ("[Apache-2.0] Dapper", "https://github.com/DapperLib/Dapper"),
        ("[MIT] FFMpegCore", "https://github.com/rosenbjerg/FFMpegCore?tab=MIT-1-ov-file#readme"),
        ("[MIT] obs-websocket-dotnet", "https://github.com/BarRaider/obs-websocket-dotnet"),
        ("[LGPL-3.0] SharpSevenZip", "https://github.com/JeremyAnsel/SharpSevenZip")
    ];

    private static readonly (string Caption, string Url)[] References =
    [
        ("[BSD-3-Clause] CrewChiefV4", "https://gitlab.com/mr_belowski/CrewChiefV4"),
        ("[MIT] dr2_logger", "https://github.com/ErlerPhilipp/dr2_logger"),
        ("Dirt Telemetry Tool", "https://forums.codemasters.com/topic/9721-dirt-telemetry-tool-cortextuals-version/"),
        ("[Custom] NGPCarMenu", "https://github.com/mika-n/NGPCarMenu"),
        ("SimHub", "https://www.simhubdash.com/")
    ];
}
