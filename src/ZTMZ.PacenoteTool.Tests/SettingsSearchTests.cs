using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Xunit;
using ZTMZ.PacenoteTool.WpfGUI.Helpers;
using ZTMZ.PacenoteTool.WpfGUI.Services;

namespace ZTMZ.PacenoteTool.Tests;

public class SettingsSearchTests
{
    [Fact]
    public void TitleBarSearchPopupKeepsResultCommandsBoundOutsideTheWindowTree()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                // Exercise the real titlebar XAML without starting the application's game/audio services.
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName,
                           "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "MainWindow.xaml")))
                    directory = directory.Parent;
                Assert.NotNull(directory);
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName,
                    "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "MainWindow.xaml"));
                System.Xml.Linq.XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";
                System.Xml.Linq.XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var titleMarkup = new System.Xml.Linq.XElement(document.Descendants(ui + "TitleBar").Single());
                foreach (var attribute in titleMarkup.DescendantsAndSelf().Attributes()
                             .Where(a => a.Name.LocalName is "TextChanged" or "GotKeyboardFocus" or "PreviewKeyDown").ToArray())
                    attribute.Remove();
                titleMarkup.Descendants(ui + "ImageIcon").Single().SetAttributeValue("Source",
                    "pack://application:,,,/ZTMZ.PacenoteTool.WpfGUI;component/Assets/app.ico");
                var resources = new System.Xml.Linq.XElement(p + "Grid.Resources",
                    document.Root!.Element(ui + "FluentWindow.Resources")!.Elements());
                var markup = new System.Xml.Linq.XElement(p + "Grid",
                    document.Root.Attributes().Where(a => a.IsNamespaceDeclaration), resources, titleMarkup);
                var host = (Grid)System.Windows.Markup.XamlReader.Parse(markup.ToString());
                host.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                host.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                var result = new ZTMZ.PacenoteTool.Core.SettingSearchResult(
                    new("VoicePage", "tabs.voices", "collision", []), "Collision sound", "Co-driver");
                object? chosen = null;
                var command = new CommunityToolkit.Mvvm.Input.RelayCommand<object>(value => chosen = value);
                host.DataContext = new { ViewModel = new SearchViewModel
                {
                    Title = "ZTMZ Pacenote Tool — a long localized title",
                    SettingsQuery = "collision", NoSettingsResults = false,
                    SettingsResults = new[] { result }, OpenSettingCommand = command
                }};
                window = new Window { Content = host, Width = 1180, Height = 200, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();
                var box = (System.Windows.Controls.TextBox)host.FindName("SettingsSearchBox");
                var popup = (System.Windows.Controls.Primitives.Popup)host.FindName("SettingsSearchPopup");
                var titleBar = host.Children.OfType<Wpf.Ui.Controls.TitleBar>().Single();
                Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome((Grid)titleBar.CenterContent));
                foreach (var width in new[] { 1180d, 1600d })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    var origin = box.TranslatePoint(default, titleBar);
                    var actions = (StackPanel)titleBar.TrailingContent;
                    Assert.InRange(origin.Y + box.ActualHeight, 0, titleBar.ActualHeight);
                    Assert.True(origin.X + box.ActualWidth <= actions.TranslatePoint(default, titleBar).X);
                }
                popup.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                popup.Child.UpdateLayout();
                var items = (ItemsControl)host.FindName("SettingsSearchResults");
                var container = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(0);
                var button = (Wpf.Ui.Controls.Button)System.Windows.Media.VisualTreeHelper.GetChild(container, 0);
                Assert.Same(command, button.Command);
                Assert.Same(result, button.CommandParameter);
                Assert.Equal(box.ActualWidth, ((Border)popup.Child).ActualWidth);
                button.Command.Execute(button.CommandParameter);
                Assert.Same(result, chosen);
                popup.IsOpen = false;
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public sealed class SearchViewModel
    {
        public string Title { get; set; } = "";
        public string SettingsQuery { get; set; } = "";
        public bool NoSettingsResults { get; set; }
        public ZTMZ.PacenoteTool.Core.SettingSearchResult[] SettingsResults { get; set; } = [];
        public System.Windows.Input.ICommand? OpenSettingCommand { get; set; }
    }

    [Fact]
    public void PackagedCatalogContainsAllSevenPagesAndUniqueAnchors()
    {
        var entries = SettingsSearchCatalog.Load();
        Assert.Equal(7, entries.Select(e => e.PageId).Distinct().Count());
        Assert.True(entries.Count >= 60);
        Assert.Equal(entries.Count, entries.Select(e => (e.PageId, e.Anchor)).Distinct().Count());
        Assert.Contains(entries, e => e.Anchor == "voiceStartup.setting" && e.PageId == "GeneralPage");
        Assert.Contains(entries, e => e.ResourceKeys.Contains("settings.tooltip.playCollisionSound"));
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Anchor)));
    }

    [Fact]
    public void LocateAndRevealFindsCollapsedExpanderContent()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var target = new Border();
                SettingsSearch.SetAnchor(target, "settings.test");
                var expander = new Expander { IsExpanded = false, Content = target };
                Assert.Same(target, SettingsSearch.Find(expander, "settings.test"));
                SettingsSearch.Reveal(target);
                Assert.True(expander.IsExpanded);
                Assert.Null(SettingsSearch.Find(expander, "missing"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
