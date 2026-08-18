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

The script creates a self-contained Avalonia 12 + FluentAvalonia desktop
publish, packages the portable Codemasters plugin, official `data.zdb`,
localization files, Web Dashboard client and packages, fonts, pacenote icons,
the default co-driver, and the default profile, then writes the AppImage under
`artifacts/appimage/output`.

By default the release resources are read from `~/.local/share/ztmz`. Override
that root with `ZTMZ_PACKAGE_DATA_HOME`, or set
`ZTMZ_DEFAULT_CODRIVER_SOURCE` and `ZTMZ_DEFAULT_PROFILE_SOURCE` separately.

`ztmz.png` is the correctly sized 512 px Linux packaging derivative of the
current product icon. The AppDir installs it as
`com.ztmz.PacenoteTool.png` so the icon name matches the Wayland application ID
and desktop entry ID. Regenerate it when the canonical artwork changes.

Use an explicit development version when required:

```bash
ZTMZ_APPIMAGE_VERSION=2.99.99.46-dev \
APPIMAGETOOL=/path/to/appimagetool \
  packaging/appimage/build-appimage.sh
```

Build only the AppDir when appimagetool or FUSE is unavailable:

```bash
ZTMZ_APPDIR_ONLY=1 packaging/appimage/build-appimage.sh
artifacts/appimage/ZTMZ_PacenoteTool.AppDir/AppRun
```

The AppImage can also use the runtime's extract-and-run fallback:

```bash
./ZTMZClub_PacenoteTool_2.99.99.45-x86_64.AppImage --appimage-extract-and-run
```

## Runtime Data

The package is read-only. Runtime data remains in the XDG data root, normally
`~/.local/share/ztmz`, and can be overridden with `ZTMZ_DATA_HOME`. Packaged
resources are resolved through `ZTMZ_RESOURCE_HOME` inside the AppImage.

The desktop host uses Avalonia's native Wayland backend when `WAYLAND_DISPLAY`
is available. Set `ZTMZ_DISPLAY_BACKEND=x11` to force the X11/XWayland backend
for troubleshooting.

The AppImage opens the Avalonia desktop host without a terminal. The separate
`ZTMZ.PacenoteTool.Linux` project remains available for command-line bootstrap,
telemetry, and audio diagnostics during development.
