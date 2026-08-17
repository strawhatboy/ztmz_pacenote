# Flatpak Development Build

This directory contains the local development package for the native Linux
bootstrap. It is not ready for submission to Flathub.

## Prerequisites

Install `flatpak`, `flatpak-builder`, and the Flathub remote. The manifest uses
the Freedesktop 25.08 runtime and the matching .NET 10 SDK extension.

Generate `nuget-sources.json` whenever package references change:

```bash
python3 flatpak-dotnet-generator.py \
  --dotnet 10 \
  --freedesktop 25.08 \
  nuget-sources.json \
  ../../src/ZTMZ.PacenoteTool.Linux/ZTMZ.PacenoteTool.Linux.csproj
```

Build and install the development package from the repository root. Keep the
builder work and state directories outside the source tree so they cannot be
included by the local `dir` source:

```bash
flatpak-builder \
  --user \
  --force-clean \
  --install-deps-from=flathub \
  --install \
  --state-dir=/tmp/ztmz-flatpak-state \
  /tmp/ztmz-flatpak-build \
  packaging/flatpak/io.github.strawhatboy.ZTMZPacenoteTool.Devel.yml
```

Run the bootstrap:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel
```

The package installs the official pacenote database, localization files, and
the portable Codemasters UDP provider. To verify that a listener can bind
inside the sandbox without starting a game:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel \
  --listen-game "Dirt Rally 2.0" \
  --listen-seconds 5
```

`--share=network` is required so telemetry sent by a Proton game can reach the
Flatpak process. No host filesystem permission is granted by this development
manifest.

The permanent application ID, AppStream metadata, desktop integration, and
runtime permissions will be finalized when the Linux host provides a usable UI.
