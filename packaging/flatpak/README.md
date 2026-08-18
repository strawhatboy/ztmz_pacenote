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

The package installs the official pacenote database, localization files, the
portable Codemasters UDP provider, and the PortAudio output backend. On
startup, the host lists the co-driver packages and output devices it found.
WAV, AIFF, MP3, Ogg, FLAC, and Opus are decoded in-process through libsndfile;
WMA, AAC, MP4, and M4A use the FFmpeg supplied by the Freedesktop runtime.
To verify that a listener can bind inside the sandbox without starting a game:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel \
  --listen-game "Dirt Rally 2.0" \
  --listen-seconds 5
```

`--share=network` is required so telemetry sent by a Proton game can reach the
Flatpak process. `--socket=pulseaudio` gives PortAudio access to the user's
PipeWire/PulseAudio compatibility socket.

The development manifest grants narrowly scoped read/write access to the
native Steam data directory, the Flatpak Steam data directory, and
`/run/media`, where SteamOS normally mounts removable Steam libraries. ZTMZ
needs write access because prerequisite repair updates game configuration
files inside Proton prefixes. It does not receive access to the complete home
or host filesystem.

The shared Proton locator reads each Steam root's `steamapps/libraryfolders.vdf`
and selects the library containing the game's `appmanifest_<appid>.acf`. For
DiRT Rally 2.0 it then checks the Windows Documents directory inside
`steamapps/compatdata/690790/pfx`. Additional non-standard Steam roots can be
provided as a colon-separated list through `ZTMZ_STEAM_ROOTS`; the selected
directories must also be exposed to the sandbox with a matching Flatpak
filesystem override.

To verify decoding and audible playback using a file already visible inside
the sandbox, run:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel \
  --play-audio /path/visible/inside/the/sandbox/example.ogg
```

Use `--audio-device <id>` to select a non-default device from the startup list.

Start the browser dashboard and keep forwarding decoded game telemetry until
`Ctrl+C`:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel \
  --web-dashboard \
  --listen-game "Dirt Rally 2.0"
```

The startup output prints the browser URL and the number of loaded dashboard
packages. Add `--web-dashboard-port <1024-65535>` to override the configured
port. Supplying `--listen-seconds <1-300>` keeps the bounded diagnostic behavior.

To simulate that Proton-side traffic without installing a game, start the
listener in one terminal:

```bash
flatpak run io.github.strawhatboy.ZTMZPacenoteTool.Devel \
  --listen-game "Dirt Rally 2.0" \
  --listen-seconds 10
```

Then send valid Codemasters packets from the Linux host in another terminal:

```bash
python3 packaging/flatpak/send-codemasters-udp.py
```

The listener should report received and decoded samples, followed by the
simulated speed, gear, RPM, and stage distance. Because the sender runs outside
the Flatpak, this checks the same host-to-sandbox UDP boundary used by a game
running under Proton.

The permanent application ID, AppStream metadata, desktop integration, and
runtime permissions will be finalized when the Linux host provides a usable UI.
