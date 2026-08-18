# AppImage Development Build

AppImage is the primary packaging target for the first native SteamOS build.
Unlike the Flatpak development package, it runs with the normal permissions of
the current user, so the native Linux game monitor can inspect `/proc` and find
Steam Proton games without a host lifecycle relay.

## Build

Run the build on an x86_64 Linux host with the .NET 10 SDK:

```bash
chmod +x packaging/appimage/build-appimage.sh
APPIMAGETOOL=/path/to/appimagetool \
  packaging/appimage/build-appimage.sh
```

The script creates a self-contained .NET publish, packages the portable
Codemasters plugin, official `data.zdb`, localization files, Web Dashboard
client and packages, fonts, pacenote icons, the default co-driver, and the
default profile, then writes the AppImage under `artifacts/appimage/output`.

By default the release resources are read from `~/.local/share/ztmz`. Override
that root with `ZTMZ_PACKAGE_DATA_HOME`, or set
`ZTMZ_DEFAULT_CODRIVER_SOURCE` and `ZTMZ_DEFAULT_PROFILE_SOURCE` separately.

`ztmz.png` is the correctly sized 512 px Linux packaging derivative of the
current product icon. Regenerate it when the canonical application artwork
changes.

Use an explicit development version when required:

```bash
ZTMZ_APPIMAGE_VERSION=2.99.99.46-dev \
APPIMAGETOOL=/path/to/appimagetool \
  packaging/appimage/build-appimage.sh
```

Build only the AppDir when appimagetool or FUSE is unavailable:

```bash
ZTMZ_APPDIR_ONLY=1 packaging/appimage/build-appimage.sh
artifacts/appimage/ZTMZ_PacenoteTool.AppDir/AppRun --help
```

The AppImage can also use the runtime's extract-and-run fallback:

```bash
./ZTMZClub_PacenoteTool_2.99.99.45-x86_64.AppImage --appimage-extract-and-run
```

## Runtime Data

The package is read-only. Runtime data remains in the XDG data root, normally
`~/.local/share/ztmz`, and can be overridden with `ZTMZ_DATA_HOME`. Packaged
resources are resolved through `ZTMZ_RESOURCE_HOME` inside the AppImage.

The current Linux entry point is a diagnostic console host, so the desktop
entry intentionally opens a terminal. It will become a normal graphical entry
when the Avalonia host is available.
