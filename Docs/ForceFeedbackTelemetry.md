# Force-feedback dashboard telemetry

`GameData.ForceFeedback` is an optional `ForceFeedbackTelemetrySnapshot` with
`Source`, `CapturedAtUtc`, and signed `OutputFraction` in `[-1, 1]`. Zero is a valid
centered output. Missing, failed-read, nonfinite and out-of-range values are
unavailable, rather than silently becoming zero or retaining the previous output.
It describes the game's normalized FFB command, not measured wheel torque in Nm.
The sign is preserved without claiming a universal physical left/right convention.

## ACR source verification

Verified on 2026-10-17 against `acr.exe` SHA256
`42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB`.

- The existing `Local\acpmf_physics` mapping contains the 32-bit float at byte
  offset **308 (`0x134`)**, represented by `ACRPhysicsData.ForceFeedbackValue`.
  The packed physics structure remains 800 bytes; no mapping or relay ABI changes.
- Native output calculation at RVA `0x4f01627` applies its multipliers, then
  `0x4f01639`–`0x4f0165f` clamps to `+1.0/-1.0` and stores at simulation `+0x2d88`.
  Clamp constants are at RVAs `0xa8dae80` and `0xa8dafc8`.
- The physics shared-memory writer begins at `0x6299c50`. Its local packet starts
  at `rbp+0x60`; instructions `0x629a3b4/0x629a3c4` copy simulation `+0x2d88` to
  packet `+0x134`. The same simulation field is consumed by the FFB device path
  at `0x4f0966c`.
- The first live driving recording varied independently of steering, spanning
  approximately `-0.288..+0.477`; the user confirmed ACR's FFB HUD bar moved.
- A second recording through production `ACRGameDataReader.ReadAll` and the
  converter covered a stage restart, turns and pause: **3,644 samples**, all valid
  and exactly matching the raw value, range **-0.44920456..+0.3914524**. It included
  **1,950 zero samples** and ended at zero after pausing.

The shipped provider uses shared memory only for this feature, with no additional
process-memory reads or injection. `ACRGameRawData.PhysicsDataValid` records an
actual successful read separately from default struct values. It is outside the
packed ABI. The Proton reader sets it only after decoding a complete telemetry
frame, and uses the same converter. Relay compatibility is covered by a synthetic
packed-payload test; live verification above was on Windows, not Proton.

## RBR source verification

Verified on 2026-10-17 against the SSE executable SHA256
`5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B`, with
NGP 7.5.779.508 SHA256
`CF351CDF99F880631ED2CB1D9B6B5C9DB7D7C233AD905740393C37DF967801EC`.

The production reader follows RBR's native getter at `0x4C5F90`: controller
`[0x8951D0]`, state `[controller+4]`, processed force `state+0x20`, multiplier
`0x1660ED8`, inversion when `0x1660EDC == 0`. It verifies the executable hash
and native getter instructions, and rejects partial reads, invalid pointers,
nonfinite/out-of-range values and observed state changes. Calibration mode 1 is
not road feedback. Only racing/start states 1/16 publish values; pause, menus,
replay, read failures and disconnect clear them. No graphics hooks are needed.

NGP's hook at `0x100EE330` dispatches the processed force to RBR's listener,
then calls that native getter and stores the result at its own singleton
`0x113DBA38+0x168` (preferred DLL addresses; relocate at runtime). The production
reader does not depend on these NGP offsets or recreate NGP's shaping formula.

The read-only comparison captured 6,572 samples without read errors, spanning
approximately -0.406523..+0.135415 and active/paused states. Native listener and
NGP cached samples matched exactly in 6,286 samples; asynchronous polling can
observe different simulation updates between those two reads. No FFB wheel
was connected. This validates a calculated game signal, **not** physical wheel
output, torque, device clipping or a universal left/right convention. Device
gain, deadzone and additional effects are downstream and are not represented.
The dashboard's amber threshold indicates the normalized game-signal limit.

The legacy `RBRMemData.FFBValue` scalar is populated for compatibility; the
optional snapshot is the authoritative availability signal used by GameData.
`tools/RbrForceFeedbackProbe` records the actual production reader without
loading graphics hooks. Focused tests cover signed/zero output, scaling and
inversion, calibration, pause/replay, invalid/partial memory, modified code,
restart pointer replacement, mapping and disconnect. The 30 focused RBR FFB
and powertrain tests passed.

Live production verification also passed after the separate GaugerPlugin crash
fix: the user restarted, drove and paused. Two bounded production recordings
contain 9,373 reads (7,933 available, including 1,035 valid zeros, and 1,440
unavailable), with output from -0.99960274 to +0.9999948. The final 649 reads
were unavailable after pause; the process and dashboard remained running.
Recordings: `tmp/rbr-ffb-production-20261017.jsonl` and
`tmp/rbr-ffb-production-20261017-continuation.jsonl`. These are observations of
the compiled production reader; the installed WPF application received only
the native graphics crash fix during this session, not a full telemetry update.

## Dashboard behavior

- Classic: white FFB arc around the rotating steering wheel, starting at twelve
  o'clock and sweeping up to 150 degrees left/right. The wheel shrinks only while
  FFB is present, fitting the same cell. Follows **Show steering**.
- WRC 2025 and WRC 2025 v2: FFB follows the original curved steering path above the panel;
  the original red dot and fading trail remain.
  Follows **Show steering indicator**.
- Liquid Glass: one shared steering/FFB track inside the existing panel.

The gauges use white signed FFB fill and red steering markers. Classic retains
its thin red angular cursor; Liquid Glass draws its dot last with a dark outline.
There are no FFB text labels. Missing FFB leaves the ordinary steering gauge.
The FFB fill turns amber at magnitude `>=0.99`, indicating proximity to the normalized output
limit, not proof of hardware clipping. Unsupported games show no FFB rail or
placeholder. The renderer is shared across WPF/Avalonia standalone, browser and
native in-game clients; both JSON telemetry projections carry the optional snapshot.
Debug telemetry also includes the snapshot through `GameData.ToString()`.

Regression coverage includes conversion, read availability, invalid values, the
Proton packed field and disconnect, both JSON paths, real CLR-backed Liquid Glass,
browser execution of all four dashboards, and native Lua rendering with signed,
zero, missing and invalid output plus steering visibility switches.

`tools/ACRTelemetryProbe --watch-ffb <new-jsonl-path>` records the raw and converted
values through the production read path, without starting the audio bridge. The
recorder stops after 6,000 samples (20 ms target interval), or Ctrl+C.
