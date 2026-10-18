using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.Tests.WpfGUI;

public class HomePageRuntimeControlsTest
{
    [Theory]
    [InlineData("overlayBackend.componentMissing")]
    [InlineData("overlayBackend.componentBlocked")]
    [InlineData("overlayBackend.componentInvalid")]
    [InlineData("overlayBackend.loadFailed")]
    [InlineData("overlayBackend.gameAccessDenied")]
    public void OverlayFailureFragmentShowsSummaryPathAndExpandableDiagnosticsAndClearsAfterRecovery(string statusKey)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName,
                           "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "HudPage.xaml")))
                    root = root.Parent;
                Assert.NotNull(root);
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName,
                    "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "HudPage.xaml"));
                var summaryElement = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                    attribute.Value == "{Binding ViewModel.OverlayBackendStatusKey}"));
                var markup = new System.Xml.Linq.XElement(summaryElement.Parent!);
                foreach (var attribute in document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                    markup.SetAttributeValue(attribute.Name, attribute.Value);
                var ns = markup.Name.Namespace;
                System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                markup.AddFirst(new System.Xml.Linq.XElement(ns + "StackPanel.Resources",
                    new System.Xml.Linq.XElement(ns + "BooleanToVisibilityConverter",
                        new System.Xml.Linq.XAttribute(x + "Key", "BooleanToVisibilityConverter"))));
                // Qualify the serialized namespace so the attached property's XName and its prefix stay aligned.
                // Changing only an xmlns declaration makes LINQ to XML emit the old namespace on the attribute.
                var standaloneMarkup = markup.ToString().Replace(
                    "clr-namespace:ZTMZ.PacenoteTool.WpfGUI.Helpers",
                    "clr-namespace:ZTMZ.PacenoteTool.WpfGUI.Helpers;assembly=ZTMZ.PacenoteTool.WpfGUI");
                var panel = (System.Windows.Controls.StackPanel)System.Windows.Markup.XamlReader.Parse(standaloneMarkup);
                using var translations = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
                    "src", "ZTMZ.PacenoteTool.I18N", "lang", "en-us.json")));
                foreach (var entry in translations.RootElement.GetProperty("overlayBackend").EnumerateObject())
                    panel.Resources["overlayBackend." + entry.Name] = entry.Value.GetString()!;
                panel.Resources["diagnostics.title"] = "Diagnostics";

                // Bypass constructors that start game, renderer, and desktop services, as in the gain binding test.
                var session = (NativeOverlaySessionController)RuntimeHelpers.GetUninitializedObject(typeof(NativeOverlaySessionController));
                var vm = (HudPageVM)RuntimeHelpers.GetUninitializedObject(typeof(HudPageVM));
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(HudPageVM).GetField("_overlaySession", flags)!.SetValue(vm, session);
                typeof(NativeOverlaySessionController).GetField("_tool", flags)!.SetValue(session,
                    RuntimeHelpers.GetUninitializedObject(typeof(ZTMZPacenoteTool)));
                var failureField = typeof(NativeOverlaySessionController).GetField("_backendFailure", flags)!;
                var info = new OverlayFailureInfo(statusKey, @"C:\ZTMZ\components\overlay.dll",
                    "System.IO.IOException: Test loader failure\n   at FakeOverlay.Load()");
                failureField.SetValue(session, info);
                panel.DataContext = new { ViewModel = vm };
                panel.Measure(new System.Windows.Size(600, 600));
                panel.Arrange(new System.Windows.Rect(0, 0, 600, 600));
                panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var summary = panel.Children.OfType<System.Windows.Controls.TextBlock>().Single();
                var detailsPanel = panel.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                var path = detailsPanel.Children.OfType<System.Windows.Controls.TextBlock>().Single();
                var expander = detailsPanel.Children.OfType<System.Windows.Controls.Expander>().Single();
                var diagnostics = Assert.IsType<System.Windows.Controls.TextBox>(expander.Content);
                var copy = detailsPanel.Children.OfType<System.Windows.Controls.Button>().Single();
                Assert.Equal(panel.Resources[statusKey], summary.Text);
                Assert.Equal(System.Windows.Visibility.Visible, detailsPanel.Visibility);
                Assert.Equal(info.ComponentPath, path.Text);
                Assert.False(expander.IsExpanded);
                Assert.True(diagnostics.IsReadOnly);
                Assert.Equal(info.ToDisplayText(), diagnostics.Text);
                Assert.DoesNotContain(info.Details, summary.Text);
                Assert.Same(vm.CopyOverlayDiagnosticsCommand, copy.Command);
                Assert.True(copy.Command!.CanExecute(null));

                failureField.SetValue(session, null);
                // Match the production refresh notifications without creating an Application/dispatcher singleton.
                var notify = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject).GetMethod("OnPropertyChanged",
                    flags, [typeof(string)])!;
                foreach (var property in new[] { nameof(vm.OverlayBackendStatusKey), nameof(vm.HasOverlayFailure),
                             nameof(vm.OverlayFailureComponentPath), nameof(vm.OverlayFailureDiagnostics) })
                    notify.Invoke(vm, [property]);
                vm.CopyOverlayDiagnosticsCommand.NotifyCanExecuteChanged();
                panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Assert.Equal(System.Windows.Visibility.Collapsed, detailsPanel.Visibility);
                Assert.Equal(string.Empty, path.Text);
                Assert.Equal(string.Empty, diagnostics.Text);
                Assert.Equal(panel.Resources[vm.OverlayBackendStatusKey], summary.Text);
                Assert.False(copy.Command.CanExecute(null));
            }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void GainSliderBindingDoesNotRoundMigratedVolumeOnPageLoad()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName,
                           "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "HomePage.xaml")))
                    root = root.Parent;
                Assert.NotNull(root);
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName,
                    "src", "ZTMZ.PacenoteTool.WpfGUI", "Views", "HomePage.xaml"));
                var sliderElement = document.Descendants().Single(element =>
                    element.Name.LocalName == "Slider" && (string?)element.Attribute("Name") == "s_volume");
                var markup = new System.Xml.Linq.XElement(sliderElement.Parent!);
                foreach (var attribute in document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                    markup.SetAttributeValue(attribute.Name, attribute.Value);
                var grid = (System.Windows.Controls.Grid)System.Windows.Markup.XamlReader.Parse(markup.ToString());
                var vm = CreateViewModel();
                var gain = PlaybackGain.FromLegacy(500);
                Apply(vm, Snapshot([], null) with { PlaybackGainDb = gain });
                grid.DataContext = new { ViewModel = vm };
                grid.Measure(new System.Windows.Size(400, 40));
                grid.Arrange(new System.Windows.Rect(0, 0, 400, 40));
                grid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var slider = grid.Children.OfType<System.Windows.Controls.Slider>().Single();
                Assert.Equal(-60, slider.Minimum);
                Assert.Equal(24, slider.Maximum);
                Assert.Equal(gain, slider.Value);
                Assert.Equal(gain, vm.PlaybackGainDb);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void GainSnapshotPreservesFractionalDecibelsWithoutWritingBack()
    {
        var vm = CreateViewModel();
        var decibels = PlaybackGain.FromLegacy(500);
        Apply(vm, Snapshot([], null) with { PlaybackGainDb = decibels });
        Assert.Equal(decibels, vm.PlaybackGainDb);
        Assert.Equal(PlaybackGain.Format(decibels), vm.PlaybackGainText);
    }

    [Fact]
    public void InitialSnapshotPopulatesBothListsAndSelections()
    {
        var vm = CreateViewModel();
        var package = Package("first");
        var device = new AudioOutputDeviceInfo("speakers", "Speakers", true);

        Apply(vm, Snapshot([package], package, [device], device));

        Assert.Same(package, Assert.Single(vm.CodriverPackageInfos));
        Assert.Same(package, vm.SelectedCodriver);
        Assert.Same(device, Assert.Single(vm.OutputDevices));
        Assert.Same(device, vm.SelectedOutputDevice);
    }

    [Fact]
    public void DownloadRefreshAddsPackageAndPreservesExistingSelection()
    {
        var vm = CreateViewModel();
        var existing = Package("existing");
        Apply(vm, Snapshot([existing], existing));
        var downloaded = Package("downloaded");

        Apply(vm, Snapshot([existing, downloaded], existing));

        Assert.Equal(2, vm.CodriverPackageInfos.Count);
        Assert.Same(downloaded, vm.CodriverPackageInfos[1]);
        Assert.Same(existing, vm.SelectedCodriver);
    }

    [Fact]
    public void UpdatedPackageWithSameIdentityReplacesMetadataAndSelection()
    {
        var vm = CreateViewModel();
        var old = Package("same-id");
        Apply(vm, Snapshot([old], old));
        var updated = Package("same-id");
        updated.name = "Updated name";
        updated.version = "2";

        // Selection may be represented by an older object with the same identity.
        Apply(vm, Snapshot([updated], old));

        Assert.Same(updated, Assert.Single(vm.CodriverPackageInfos));
        Assert.Same(updated, vm.SelectedCodriver);
        Assert.Equal("2", vm.SelectedCodriver!.version);
    }

    [Fact]
    public void UnrelatedStateChangeDoesNotResetLists()
    {
        var vm = CreateViewModel();
        var package = Package("first");
        var device = new AudioOutputDeviceInfo("speakers", "Speakers", true);
        var snapshot = Snapshot([package], package, [device], device);
        Apply(vm, snapshot);
        var changes = 0;
        ((ObservableCollection<CoDriverPackageInfo>)vm.CodriverPackageInfos).CollectionChanged += (_, _) => changes++;
        ((ObservableCollection<AudioOutputDeviceInfo>)vm.OutputDevices).CollectionChanged += (_, _) => changes++;

        Apply(vm, snapshot with { IsMuted = true });

        Assert.Equal(0, changes);
        Assert.Same(package, vm.SelectedCodriver);
        Assert.Same(device, vm.SelectedOutputDevice);
    }

    [Fact]
    public void EmptySnapshotClearsSelectionsAndLaterRefreshRestoresThem()
    {
        var vm = CreateViewModel();
        var package = Package("first");
        var device = new AudioOutputDeviceInfo("speakers", "Speakers", true);
        var populated = Snapshot([package], package, [device], device);
        Apply(vm, populated);

        Apply(vm, Snapshot([], null));

        Assert.Empty(vm.CodriverPackageInfos);
        Assert.Empty(vm.OutputDevices);
        Assert.Null(vm.SelectedCodriver);
        Assert.Null(vm.SelectedOutputDevice);
        Apply(vm, populated);
        Assert.Same(package, vm.SelectedCodriver);
        Assert.Same(device, vm.SelectedOutputDevice);
    }

    private static HomePageVM CreateViewModel()
    {
        // Isolate snapshot application from the constructor, which starts the real
        // tool, device discovery and other application services in the background.
        var vm = (HomePageVM)RuntimeHelpers.GetUninitializedObject(typeof(HomePageVM));
        vm.CodriverPackageInfos = new ObservableCollection<CoDriverPackageInfo>();
        vm.OutputDevices = new ObservableCollection<AudioOutputDeviceInfo>();
        vm.PropertyChanged += (_, args) =>
        {
            // Model the selection-change callbacks raised by bound controls.
            // No runtime service is installed: an unintended write would fail.
            if (args.PropertyName == nameof(vm.SelectedCodriver))
                vm.CodriverPackageSelectionChangedCommand.Execute(null);
            if (args.PropertyName == nameof(vm.SelectedOutputDevice))
                vm.OutputDeviceSelectionChangedCommand.Execute(null);
        };
        return vm;
    }

    private static CoDriverPackageInfo Package(string id) => new()
    {
        id = id, name = id, version = "1", Path = Path.Combine(Path.GetTempPath(), id)
    };

    private static RuntimeControlsSnapshot Snapshot(
        IReadOnlyList<CoDriverPackageInfo> packages,
        CoDriverPackageInfo? selected,
        IReadOnlyList<AudioOutputDeviceInfo>? devices = null,
        AudioOutputDeviceInfo? selectedDevice = null) =>
        new(packages, selected, devices ?? [], selectedDevice,
            false, false, false, false,
            new RuntimeDashboardStatus("Stopped", 0, "", "", false));

    private static void Apply(HomePageVM vm, RuntimeControlsSnapshot snapshot)
    {
        var method = typeof(HomePageVM).GetMethod("ApplyRuntimeControls",
            BindingFlags.Instance | BindingFlags.NonPublic, [typeof(RuntimeControlsSnapshot)]);
        Assert.NotNull(method);
        method.Invoke(vm, [snapshot]);
    }
}
