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
successful Atom_/Block_/IMMERSION_ lookup within two seconds of a pacenote start is sent;
one start is consumed once. This matches the earlier captured order but is not
proof against all preloading/caching behaviors. A short live end-to-end check is
still required. No original audio is suppressed. The callback queue is bounded;
only the worker touches the pipe. Calls older than 500 ms are dropped, queues
are discarded on reconnect, and a heartbeat detects a departed host.

MALE/FEMALE prefixes are removed for lookup. IMMERSION resources are logged,
not interpreted as countdown/Go/finish. Debug logs include the source tick,
arrival age, mapping, dispatch state and telemetry Go timestamp; native counters
report uncorrelated lookups and stale/queued calls every five seconds.
WRCG live calls are accepted during RaceBegin/CountingDown after core preparation
completes, without forcing Racing early. Pending calls are invalidated on a new
session. The telemetry-triggered Go cue is not yet synchronized to the game's
actual countdown; compare the diagnostic timeline before assigning sound events.

Mapping against official data.zdb covers 6,375/6,404 installed blocks. Six atom
types remain unresolved: around_panels, canyon, forest, go_back, under_arch and
UNDER_BRIDGE. A call containing any unresolved atom is skipped in its entirety
and logged once per atom. Do not silence the game's co-driver while relying on
this experimental mapping. WRC 7–10 and Proton injection are not implemented.
Probe and extraction instructions: `tools/WrcGenerationsProbe/README.md`.

## Telemetry foundation

Chicane mapping distinguishes the first turn from the entry side. Following the
in-game direction clarification, `CHICANE_RIGHT` (PN_RLChicane) maps to ZTMZ's
`left_entry_chicane`, and `CHICANE_LEFT` (PN_LRChicane) to `right_entry_chicane`.
The explicit `CHICANE_LEFTENTRY` / `CHICANE_RIGHTENTRY` atoms retain their named
entry side. Do not collapse these four atoms into two same-prefix aliases.

The token audit also distinguishes `keep_into` (linking hold/into) from `keep_in`
(inside line), retains emphasis in `def_dont_cut` with important/dont_cut, and
supports tightening to 7. `DONUT` is counterclockwise: the installed GUI maps
PN_DonutCounter to frame 93, Turns_L_TurnAround / XL_Turns_L_TurnAround; the
decoded atlas visibly confirms its arrows. It maps to `reverse_donut`.

The official data.zdb now defines under_bridge, under_arch, go_back, forest and
canyon in all ten token languages. These are data additions, not runtime-injected
tokens. `around_panels` uses the existing around_sign token. Existing voice packs
may lack recordings for the new tokens; a complete token mapping does not imply
complete audio coverage. Original game speech remains enabled. Do not substitute
bridge/over_bridge for under_bridge or invent directional audio fallbacks.

`_for` still uses the existing into approximation. The text "pour" alone does not
establish a distinct timing interval, so this audit does not invent one. Unused
atoms absent from the official vocabulary remain rejected rather than guessed.

`WrcGenerationsGameDataReader` uses the shared UDP receiver and converts validated
280-byte packets into GameData. Packet field reference:
https://github.com/PHARTGAMES/SpaceMonkey/blob/main/WRCGenAPI/WRCGenData.cs
The reference's later inline field numbers are inconsistent with its actual field
order; offsets follow the declarations (70 floats). Speed is converted m/s to km/h,
engine/max/idle RPM multiplied by 10, and gear retained as -1 reverse / 0 neutral.
The 2026-10-09 Harju capture decoded 25,722 packets without rejection. Max/idle
fields were 890/80, supporting the conversion to 8900/800 RPM. RaceBegin,
late attachment, progress and frozen-clock pause are supported; small backwards
clock corrections at the finish do not start another race. Finish detection uses
field 62's positive integer result milliseconds only when they match the lap clock
within 2 ms. The captured full stage produces one Racing -> RaceEnd transition;
a frozen clock alone is never finish evidence. Countdown voices now follow the
GUI countdown event's FMOD timeline, including Go before the car crosses the
start line; the user has verified countdown playback. On the verified Windows
build, a bounded read-only session reader supplies the actual selected car and
special (including reverse routes) using the installed game's display names.
It also reads the player's official penalty-inclusive result, matched to the
current run and UDP finish clock. Unknown builds retain UDP/audio and explicitly
log that the fallback finish time has unknown penalties. Completion percentage
remains unimplemented. See `Docs/WrcGenerationsLifecycle.md` for exact build,
offset evidence, timing, and the remaining live finish validation.
The saved Harju ghost independently confirms the user's 399.457 s result, versus
379.457 s in UDP: exactly 20 s, distinct from the 16 ms finish-clock correction.
No vehicle/stage names are guessed from RPM, distance or preloaded model order.

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
