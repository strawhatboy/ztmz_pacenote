# WRC 7 investigation and implementation

## Read-only retirement capture

`capture-results.py <PID> <image-base> --output <new-file.jsonl> [--seconds 600]`
checks the executable hash, single-player profile layout and reciprocal stats
ownership. It samples raw result flags every 20 ms and flushes changes plus
one-second timer snapshots directly to the new output file. It does not bind a
UDP port, inject, or modify game memory/assets; ZTMZ can remain running.
Use `--seconds 0` for an interactive test: capture continues until this game
process exits or the probe is stopped with Ctrl+C. Finite captures default to
600 seconds (maximum 1800); always check for `capture-ended` before claiming
an action was recorded. The file is flushed immediately in both modes.

```powershell
$wrc7Process = Get-Process WRC7
python tools/Wrc7Probe/capture-results.py $wrc7Process.Id $wrc7Process.MainModule.BaseAddress.ToInt64() --output tmp/wrc7-results.jsonl
```

Start before driving, capture an explicit Give up, and compare Restart separately.
Raw flag capture does not itself enable retirement detection in the application.
Decoder checks: `python -m unittest discover -s tools/Wrc7Probe -p test_capture_results.py`.

This is development tooling, not a completed game provider. Do not advertise
WRC 7 support or ship the observation DLLs in the installer yet.

## Verified on the installed Windows build

- Executable: WRC7.exe, x64, SHA256
  `0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633`.
- FMOD 1.9.5 uses `fmod64.dll` / `fmodstudio64.dll`. Do not reuse WRCG's
  FMOD 2 parameter function signatures.
- PKG v2 has a plaintext header/index and a 40-byte entry trailer, unlike
  WRCG's encrypted packages and 48-byte trailer. LZ4 decoding is shared.
  Read base packages then patches in ordinal order. Source game files remain
  unchanged; extracted resources stay in an external inspection directory.
- The installed PACENOTES.CFG contains 139 uncommented atoms. There is no
  WRCG-style PACENOTEBLOCKS.CFG. Detailed and accessible sounds overlap but
  do not have identical meanings: detailed LEFT_4 uses PN_Left4, while
  accessible LEFT_5 and LEFT_6 both use PN_Left4. Keep both dictionaries and
  their candidate sets. Never pick the first match or assume six detailed grades.
- English PLOC names start at byte 4 as null-terminated UTF-16LE strings.
- Current test: Monte Carlo / Corps–La Salle en Beaumont / Ford Fiesta R2.
  The live selected profile reads `Ford_Fiesta_R2_Duggan`; its car name ID
  is 0x8a6, resolving to `Ford Fiesta R2` in the installed English table.
- Game manager global RVA: 0x1091b80; expected vtable RVA: 0xdb1b60.
  Profile vector: manager+0x17798 .. +0x177a0, stride 0x170;
  player-to-profile mapping starts at +0x177b0. Profile+0x20 is the car
  definition; definition+0x08 is the localized name ID.
- Participant vector: manager+0xb0 .. +0xb8; participant+0x38 is its
  race statistics, with a back-reference at statistics+0x908.
  Raw milliseconds at +0x6c and penalty milliseconds at +0x92c are candidates.
  ProcessRaceStats (RVA 0x2900b0) adds the penalty to raw time and stores raw
  and total into profile+0x154/+0x158, with result-ready byte at +0x160.
  These still require pause/restart/finish/penalty validation before production.

## Audio observations

`build-trace.bat` builds the scoped Studio observer and injector, reusing the
WRCG observation code with WRC7-specific compile-time settings. The injector
rejects other executable names and non-x64 processes. Worker-only log writes
and a bounded callback queue keep disk access off game/audio callbacks.

The live drive produced Studio events for FiestaR2 gears, suspension, tires,
and impacts, but no VOICE/Pacenotes events and no sound-info lookups. The game
contains independent `DATA:/SOUNDS/VOICE/.../PACENOTES/MALE/*.PAF` resources.
Consequently WRCG's programmer-sound bridge cannot simply be renamed for WRC7.

`build-core-trace.bat` builds a separate observer for FMOD Core System.playSound
and records Sound.getName together with result and initial paused state. This
does not yet equate a paused playSound call with audible playback. If the game
starts paused channels, the production bridge must observe their actual release
and reject loads/preloads. Both observers preserve original game audio and stay
loaded until the game exits; the game installation is not modified.

The engine observer (`build-core-trace.bat engine`) successfully captured the
actual voice path at AudioDriverFModStudio::FModPlaySound, RVA `0x5385d0`.
BaseSound begins with an MSVC string (length +16, capacity +24); snapshot it
before calling the original. FMOD's own sound name is empty for these voices.
Recorded resources include `PN_INTOLEFT1.PAF`, `PN_CAUTION_EXCLA_03.PAF`,
`PN_NARROWS.PAF`, `PN_ANDRIGHT3.PAF`, and `PN_INTOKINKS.PAF` under
`DATA:/SOUNDS/VOICE/EN/PACENOTES/MALE/`.

`PaceNotesMerging` supplies the combined-sound decomposition; do not infer it by
removing prefixes. The resolver preserves candidate groups and order, including
the ambiguity of RIGHT_3/RIGHT_4 in accessible mode. No production token mapping
or voice-mode inference is claimed yet.

`build-live.bat` builds the development pipe bridge and injector. It snapshots
resource names, queues them without waiting on the audio thread, and performs
all I/O on a worker. The receiver supports reconnect and rejects stale records.
The driver returns zero on failure; disassembly shows that its second boolean
argument controls whether the channel remains paused. The bridge rejects those
initially paused calls, but this filter still needs live end-to-end verification.

The engine observer already patches this same entry point in the current game
process. **Restart WRC7 before testing the live bridge**, otherwise its prologue
guard deliberately refuses installation. Do not layer it over the observer.
The research engine observer generates a local allocator source with a 1.5 GiB
search radius; the shared MinHook submodule is not modified. However, a subsequent
main-menu launch had no free region anywhere in that range, so this is not a
reliable live-bridge strategy.

The live bridge now uses `FixedEntryHook.h`: a 14-byte indirect absolute branch
over the exact 17-byte, position-independent entry. It preserves all registers,
checks the entire entry, allocates the original-call trampoline anywhere, and
suspends existing peer threads during installation. If any thread is executing
the overwritten entry, installation fails without changing it. All allocations,
enumeration and handle opens precede thread suspension. This is deliberately
specific to the verified WRC7 build, not a general-purpose instruction relocator.
Native smoke tests exercise original/replacement calls and duplicate rejection.
The live bridge returned READY in the restarted game's main menu. On the next
drive it delivered seven actual calls (2026-10-10 19:10:24..28 +08:00):
`PN_RIGHT3`, `PN_SNOW_EXCLA_02`, `PN_KEEPIN`, `PN_ANDLEFT3`, `PN_TIGHTEN`,
`PN_INTORIGHT3`, `PN_TIGHTEN1`. Receive-side record age was 0 or 16 ms;
this is not an end-to-end audio latency measurement. The playback filter
therefore passes real voice calls. Pause/queued-start behavior still needs
separate lifecycle validation.

`Wrc7LiveSession` now receives these messages and publishes the shared
`LivePacenoteEvent` contract. `Wrc7LivePacenoteDecoder` preserves combined-call
order and requires vocabulary completeness. Corner conversion uses the game's
accessible-sound definitions; unknown settings accept only agreeing possible
interpretations. Ambiguous calls and
unaudited direction/grade cases are rejected with a diagnostic, not guessed.
All seven captured calls resolve against the actual official data.zdb. Focused
tests include pipe transport, stale records, candidate agreement, and missing
vocabulary rejection (22 WRC7/WRCG catalog tests passed).
These components are now wired to a registered WRC7 game provider through
IGameLivePacenoteSource and the shared host's audio/dashboard lifecycle. The
generic bridge configuration/confirmation/status interfaces serve both WPF and
Avalonia. Controls remain opt-in. Native build/publish targets stage the bridge,
injector, game image and language files; the release payload checker requires
them. The first host test confirmed playback but exposed skipped ambiguous calls.

### Independent sound-detail settings

Chicane direction was independently checked against WRC7's installed assets:
`PN_LRChicane` -> FLS frame 41 -> `XL_Chicane_L` (bottom entrance on the right);
`PN_RLChicane` -> frame 42 -> `XL_Chicane_R` (bottom entrance on the left).
They map to `right_entry_chicane` and `left_entry_chicane`, respectively, in
both voice-detail modes. The original images are inspected locally, not shipped.
Reproduce with the probe's `<game> tmp/wrc7-icons --icons` extraction command,
then `decode-pacenote-icons.py tmp/wrc7-icons "" 108` (WRC7 PSK count offset).

The verified executable's playback function at RVA `0xe9c90` reads the race
manager (`[base+0x1091b80]`, vtable `base+0xdb1b60`) fields `+0xec0`, `+0xed0`,
and `+0xee0` at `0xe9d60`. These are direction, hairpin and danger detail,
respectively. Its category table at `0x10af0a0` is DEFAULT=0, HAIRPIN=1,
DANGER=2; its detail table at `0x10aefc0` is Detailed=0, Accessible=1.
At `0xea150` the category selects the setting; `0xea199` selects the detailed
sound at atom+8 or the accessible sound at atom+0x50. These offsets are guarded
by the existing executable SHA256 check, not used for other game builds.

The receiver reads all three settings for each fresh voice message, logs changes,
and resolves each merged component using its own category. No additional hook
is needed. The running game used direction=Accessible, hairpin=Detailed,
danger=Detailed: a single global mode would be incorrect. Simplified hairpins
must discard open/acute detail, since the shared sound no longer encodes it.

The initial provider exposes verified raw timer metadata, car identity and
uniquely resolved stage identity. It does not provide vehicle physics telemetry,
countdown audio or final results. Frozen time means paused, never finished;
loss of a verified session means unknown, never a fabricated finish. Timer
regression or changed session identity prepares a new session.

Important localization finding: STAGENAME_MC_SS2 (ID 2041) is
`Agnières en Devoluy - Le Motty` in LOCALISATION_EN.PLOC but
`科尔普 - 博蒙地区拉萨尔` in LOCALISATION_CH_S.PLOC. The user's Chinese
name and the observed English name identify the same game entry. Different
translated text alone must not invalidate the stage mapping.

`Wrc7BridgeStatus` and `Wrc7BridgeError` are exported diagnostic variables:
status 1 initializing, -1 installation failed (Win32 error), 2 hook enabled,
3 pipe handshake sent. DLL module presence alone does not establish readiness.
The development DLL is named Wrc7BridgeAbsolute.dll to distinguish it from the
already loaded, failed allocator-based prototype; no failed hook was layered.

Additional read-only stage evidence: ObjectFactory global RVA `0x1091d78`,
selective-load mask at +0x108, currently `2`. Engine global RVA `0x1091d20`,
engine+0x68 -> object+0x40 -> level, with level-name string at +0x38, currently
`DATA:/LEVELS/MC_MONTECARLO/MC_MONTECARLO.PLM`. Combined with RALLIES.CFG this
matches Monte Carlo SS2 (SelectiveLoadId 1). Other stages can share a load ID;
this is not sufficient on its own to claim an exact stage identifier globally.

## Commands

```powershell
dotnet run --project tools/Wrc7Probe -c Release -- "K:/SteamLibrary/steamapps/common/WRC 7" tmp/wrc7-resources
python tools/Wrc7Probe/read-session-memory.py <pid> <module-base>
tools/Wrc7Probe/build-core-trace.bat
tools/Wrc7Probe/bin/Wrc7CoreTraceInject.exe <pid>
dotnet run --project tools/Wrc7Probe -c Release -- resolve tmp/wrc7-resources/PACENOTES.CFG PN_INTOLEFT1.PAF
# After restarting the game, start the receiver before injecting the live bridge:
dotnet run --project tools/Wrc7Probe -c Release -- listen <pid> tmp/wrc7-resources/PACENOTES.CFG
tools/Wrc7Probe/bin/Wrc7BridgeAbsoluteInject.exe <pid>
```

The Python probe opens QUERY_LIMITED_INFORMATION | VM_READ only, validates the
executable hash and manager vtable, and bounds pointer/array reads. Do not carry
its current raw timing fields into GameData as validated final results.

## Remaining integration gates

1. Validate actual host playback and the graphical pacenote dashboard, read the
   voice mode, and finish token semantics (especially remaining directional cases).
2. Verify current stage, direction and player selection rather than identifying
   a stage from cached/loaded resources. All rally stages may be loaded together.
3. Verify physics telemetry and race lifecycle (countdown, pause, restart,
   abandoned stage, process exit, finish including penalties).
4. Validate the registered provider's shared UI integration in both hosts and
   the final installer payload when preparing an actual release.
5. Only after end-to-end playback/dashboard verification, add a user-facing
   release-note entry. Keep WRCG's existing archive and audio behavior intact.

## Mechanical damage (2026-10-16)

The production provider now reads verified mechanical damage and wear. See
[WRC7 damage evidence](../../Docs/Wrc7Damage.md) for layouts, supported parts,
limitations and live regression coverage. `capture <PID> <seconds> <new.jsonl>`
records production telemetry/lifecycle/damage events without an audio hook.
