# Future SteamOS and Avalonia Migration

## Status

Deferred design note. This is not a committed roadmap and does not change the
current Windows WPF product.

## Current Assessment

Moving the desktop application to Avalonia is feasible for a future
cross-platform edition, but a full drop-in replacement is not practical as one
change. The WPF UI, Direct2D overlay, SteamVR overlay, Windows registry access,
and Windows-specific audio stack must be separated first.

The most realistic Linux target is SteamOS Desktop Mode. Gamescope and the
in-game overlay path need a separate feasibility spike; they should not be
assumed to work simply because the main UI starts on Linux.

## Proposed Direction

1. Keep the current WPF application as the supported Windows host.
2. Introduce platform-neutral services for configuration, paths, game data,
   audio queueing, and dashboard data.
3. Add an Avalonia desktop host after those services can be referenced without
   WPF or Win32 dependencies.
4. Use the existing web dashboard as an early Linux-friendly, second-screen HUD
   option while a native cross-platform HUD is evaluated.
5. Treat the native Direct2D and SteamVR overlays as optional Windows features
   in the first cross-platform release.

## Audio Direction

The current NAudio implementation is Windows-specific. A future Linux edition
should first evaluate NAudio 3's ALSA and SoundFile packages in a small spike.
PortAudio is only an output layer and would still require decoding, mixing,
resampling, and device-management work, so it is not the default first choice.

## Game Support Scope

UDP-based games are the best first candidates, subject to validating their
configuration and Proton file locations. Registry, memory-reader, Direct2D, and
Windows-only integration paths should be marked unsupported until platform
specific replacements exist.

## Exit Criteria For A Feasibility Spike

- Avalonia host starts on SteamOS Desktop Mode.
- A selected UDP game supplies telemetry under Proton.
- A co-driver package plays through the selected Linux audio device.
- The web dashboard or an Avalonia dashboard renders live telemetry.
- Configuration and package paths work without Windows registry access.
