# KT integrations

Portable KT game provider. First target: experimental Windows WRC Generations
(Steam 1953520, WRCG.exe). WRC 7–10 are not yet verified or advertised as supported.

`WrcGenerationsPacenoteCatalog` resolves programmer-sound names using locally
extracted game configuration. It preserves atom order and repeated calls, rejects
missing atom references, and leaves immersion/unknown resources unresolved.
The game data is not embedded or redistributed in this assembly.

WPF and Avalonia discover the same provider and show generic UDP/bridge settings.
Select the game, configure UDP (default 127.0.0.1:20778), start WRCG, then use
"Connect live pacenotes" in its settings and accept the injection confirmation.
The status changes to connected only after the injected worker sends READY.
It does not silently inject. Restarting ZTMZ reconnects to a still-injected game.

The DLL hooks FMOD 2.02.06's pacenote event start and sound-info lookup. Only a
successful Atom_/Block_ lookup within two seconds of a pacenote start is sent;
one start is consumed once. This matches the earlier captured order but is not
proof against all preloading/caching behaviors. A short live end-to-end check is
still required. No original audio is suppressed. The callback queue is bounded;
only the worker touches the pipe. Calls older than 500 ms are dropped, queues
are discarded on reconnect, and a heartbeat detects a departed host.

Mapping against official data.zdb covers 6,375/6,404 installed blocks. Six atom
types remain unresolved: around_panels, canyon, forest, go_back, under_arch and
UNDER_BRIDGE. A call containing any unresolved atom is skipped in its entirety
and logged once per atom. Do not silence the game's co-driver while relying on
this experimental mapping. WRC 7–10 and Proton injection are not implemented.
Probe and extraction instructions: `tools/WrcGenerationsProbe/README.md`.

## Telemetry foundation

`WrcGenerationsGameDataReader` uses the shared UDP receiver and converts validated
280-byte packets into GameData. Packet field reference:
https://github.com/PHARTGAMES/SpaceMonkey/blob/main/WRCGenAPI/WRCGenData.cs
The reference's later inline field numbers are inconsistent with its actual field
order; offsets follow the declarations (70 floats). Speed is converted m/s to km/h,
engine/max/idle RPM multiplied by 10, and gear retained as -1 reverse / 0 neutral.
The 2026-10-09 Harju capture decoded 25,722 packets without rejection. Max/idle
fields were 890/80, supporting the conversion to 8900/800 RPM. RaceBegin,
late attachment, progress and frozen-clock pause are supported; small backwards
clock corrections at the finish do not start another race. Finish detection,
countdown, stage/car identity and completion percentage remain unimplemented.
The recorded clock ends at 379.457 s; the user reported 399.457 s and noted likely
off-track/reset penalties. The exact +20 s is consistent with a penalty, but no
penalty field is verified. Do not present the telemetry clock as a penalty-inclusive
classification time. This differs from the separate 16 ms finish-clock correction.
No vehicle/stage names are guessed from RPM or distance.

`WrcGenerationsPrerequisiteChecker` locates Documents through the shared Windows /
Proton locator for Steam app 1953520. It recognizes UserSettings.cfg (observed on
this machine) and UserSetting.cfg. Repair changes only the four telemetry values,
preserves encoding, backs up the previous file and refuses ambiguous assignments
or repair while the game is marked running. The endpoint comes from UdpGameConfig;
it must match the receiver, including 127.0.1.1 versus 127.0.0.1. Do not hard-code
SimHub's 22888 port or bind two applications to it; use a configured relay for
coexistence. Forwarded mode bypasses game-file checking and repair.

Native source is in tools/WrcGenerationsProbe/Bridge.cpp. The KT project compiles
and stages the DLL/injector incrementally on Windows; shared KT.GameProvider.targets
includes assembly, translations, native files and MinHook license in both hosts.
The WPF batch staging paths restore these after clearing the games directory.
The archive loader reads installed PKGs without modifying or redistributing them.

Offline validation:
```
dotnet test src/ZTMZ.PacenoteTool.Portable.Tests --filter FullyQualifiedName~WrcGenerations
dotnet run --project tools/WrcGenerationsProbe/Replay -- <telemetry.bin> <game-directory> <data.zdb>
```
The tests cover units, malformed packets, setting preservation, late attachment,
clock correction, mapping and named-pipe readiness/stale-message rejection.
