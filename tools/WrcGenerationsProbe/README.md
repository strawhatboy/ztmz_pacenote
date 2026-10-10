# WRC Generations FMOD diagnostic probe

## Read-only session diagnostics

`dotnet run --project tools/WrcGenerationsProbe/Session -- <WRCG-PID> [UDP-port]`
uses the production, executable-fingerprint-guarded metadata reader. It prints
the selected special/car, result readiness and read cost. With a port it waits
up to two minutes for one local UDP packet and prints the first lifecycle event's
identity. Close ZTMZ before binding its port. Pause can stop UDP; a timeout does
not mean the memory read failed. No game settings, assets or memory are changed.

Extract `--session` reads local metadata definitions, including English/Chinese
PLOC tables, for investigation. `inspect-session.py` inspects executable strings,
references and disassembly with optional locally installed `pefile`/`capstone`.
`read-session-memory.py` is a developer-only bounded raw inspector for the exact
build documented in `Docs/WrcGenerationsLifecycle.md`; prefer the guarded Session
probe for normal verification. None of these tools or extracted game resources
are packaged in ZTMZ.

For local icon verification, run Extract with `--icons` and a separate output
directory, then `python decode-pacenote-icons.py <output-directory>`. This decodes
the original BC3 PTX atlas and PSK regions; FLS labels identify the exact sprite.
`--list-icons` lists candidate archive resources without extracting extra assets.
Extracted game resources and derived images belong only in ignored local output,
never in the distributed application. `update-token-database.py` maintains the
five official WRCG token additions in the repository data.zdb. For a closed local
test app, `--merge-test-data <data.zdb>` backs up that database and inserts only
those definitions; it does not replace unrelated records or run at app startup.

`CountdownTimeline.cpp` is a separate, silent bank inspector. Compile it with
MSVC C++20, then pass the installed WRCG directory as its sole argument. It loads
the game's FMOD DLLs and Common/GUI banks locally, prints the 54321Go/321Go sound
callbacks and event lengths, and exits. It does not inject into a running game,
modify assets, or redistribute banks. Missing vehicle DSP plugins are allowed
only in this offline inspector. Callback positions precede audible playback due
to FMOD buffering; do not use those positions as exact authored cue timestamps.

This development tool observes the installed x64 WRCG FMOD 2.02.06 C++ exports.
It does not implement a game provider, replace audio, mute the game, or infer tokens.
It is not included in the installer. Close the game to unload the probe.

## Capture

1. Run `build.bat` on Windows with Visual Studio 2022 Community C++ tools.
2. Start WRC Generations and reach its menu. Find its `WRCG.exe` PID.
3. Run `bin\WrcGenerationsInject.exe <PID>` from a terminal with matching access.
4. Check for `READY` in `bin\WrcGenerationsTrace.log`.
5. Drive through several calls, a restart/countdown, and preferably a finish.
   Note the game language and corresponding spoken calls for comparison.

The injector accepts only x64 WRCG.exe and its adjacent diagnostic DLL. It refuses
duplicate loads. Injection success alone does not confirm successful hooks: inspect
the log's READY/error line. No game installation files are replaced.

## Evidence to collect

`sound-info` captures programmer-sound lookup names and the original result code.
`pacenote-start` and parameter records are filtered to `event:/VOICE/Pacenotes`,
as listed in the installed `WIN32/Sounds/GUIDs.txt`. Parameter IDs are raw pairs,
not yet mapped to token names. Instance pointers correlate start and parameter calls.
Timestamps are monotonic milliseconds since Windows boot, followed by thread ID.

The bounded queue drops records under contention rather than delaying audio for
logging. `DROPPED` records mean the capture is incomplete. The worker drains to disk
every 50 ms; process termination can lose trailing records. Keep sessions short.
The sound-name column is truncated at 800 characters. No audio is extracted.

If no relevant events appear, investigate C entry points, cached/preloaded sounds,
bulk parameter updates and programmer-sound callbacks. Export availability alone
does not prove that gameplay calls these C++ entry points. Do not convert lookup
records directly into playback without testing preloading, repeated calls and order.

Reuse candidates after validation: ACR's transport and live-event handling.
WRCG resource naming, event ordering, race state and token mapping remain separate.

## Resolve captured names

Run the narrow archive reader with .NET 10 (output must be outside the game):

```powershell
dotnet run --project tools/WrcGenerationsProbe/Extract -- '<game directory>' '<temporary output directory>'
./tools/WrcGenerationsProbe/Resolve-Trace.ps1 -DefinitionsDirectory '<temporary output directory>'
```

The reader scans archive indexes and extracts only pacenote CFG files, not audio.
Its manifest records source archives and extracted SHA-256 hashes. Keep extracted
game data outside version control. The resolver validates every block reference
against the PaceNotes table and prints successful sound lookups in capture order.
AdditionalInformation is a separate visual table and must not override sound atoms.
Unmapped resources remain visible; immersion speech is not silently interpreted as
race state. These are game atoms, not yet a production ZTMZ token mapping.

The installed version yielded 6,404 blocks using 169 distinct atoms. Actual capture:

- `Atom_m30`: 30 metres.
- `Block_5258`: RIGHT_3 / tightens / hard_brake / SQUARE_RIGHT.
- `Block_5259`: _into / FLAT_LEFT / m150.
- `Block_5260`: RIGHT_4 / tightens / small_cut / _into / LEFT_3 / small_cut.

Next integration gates: validate lookup versus audible playback (including restart
and cached sounds), map atoms to the official token vocabulary with explicit unknowns,
then connect the shared live-pacenote pipeline. Telemetry and race lifecycle still
need separate evidence; the sound probe does not supply them.
