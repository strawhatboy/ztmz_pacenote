using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.Tests.WpfGUI;

public class HomePageRuntimeControlsTest
{
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
