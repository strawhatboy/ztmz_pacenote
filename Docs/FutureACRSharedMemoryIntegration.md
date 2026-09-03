# Assetto Corsa Rally Shared-Memory Research

## Status

Assetto Corsa Rally (ACR) shared memory is sufficient for dashboard telemetry,
but the public pages do not provide a reliable rally-stage lifecycle signal.
The optional FMOD live bridge now supplies authoritative countdown, pacenote,
damage, stage-start, and stage-finish events. Shared-memory-only operation must
still remain dashboard-only.

This conclusion is based on live ACR testing and a source-level comparison
with SimHub's `ACSharedMemory.dll`, which contains a dedicated `ACRManager`.

## Confirmed Shared-Memory Interface

Both Pacenote Tool and SimHub's ACR provider read only these three local
memory-mapped files:

```text
Local\acpmf_physics
Local\acpmf_graphics
Local\acpmf_static
```

The ACR graphics, physics, and static structures use sequential layout,
Unicode strings, and `Pack = 4`. The SimHub reference and Pacenote Tool use
the same field order and the same `AC_STATUS`, `AC_SESSION_TYPE`, and
`AC_FLAGS` value definitions. There is no extra ACR-specific map, UDP feed,
or rally timing channel in the SimHub implementation.

The reference examined was SimHub's `ACSharedMemory.dll` with SHA-256:

```text
EEC5042CF3F07A76018FBFD83E4CA6B0531F07FE48B6554B7A6F4F99AFC342D5
```

## SimHub Comparison

SimHub's `ACRManager` confirms the same limitation rather than providing a
missing workaround:

- `IsGameInRace()` returns `true` unconditionally.
- It exposes an `IsRace` expression based on
  `Graphics.Session == AC_RACE`, but that expression is not used by the ACR
  manager's state logic.
- It does not expose a session-type display value for ACR.
- Its only lifecycle-related check is pause detection from
  `Graphics.Status == AC_PAUSE`.

Therefore, importing SimHub's manager or treating its implementation as an
alternate state source would not improve Pacenote Tool's rally lifecycle
detection. SimHub also treats ACR as a telemetry/dashboard integration.

## Live ACR Behavior

During a real rally stage, physics telemetry remained live while the graphics
page reported values such as `AC_OFF` status and `AC_PRACTICE` session. The
graphics timing and distance values did not consistently align with the
rally-stage start and stop lines, and were not reliable enough to classify the
finish transition.

The following signals must therefore not independently trigger stage events:

- `Graphics.Status`, `Graphics.Session`, and flag fields.
- Current lap time, completed laps, or session timing fields.
- Accumulated distance divided by static track length.
- Vehicle speed, throttle, brake, or a transition from stationary to moving.

The last category is especially important: movement-based inference would
produce false starts while the player drives in menus, service areas, or
between attempts, and false finishes whenever the vehicle stops.

## Current Provider Contract

- Once valid ACR shared-memory data is available, Pacenote Tool may show
  dashboard telemetry.
- Without the optional bridge, the dashboard is not tied to a claimed
  rally-stage lifecycle.
- With the bridge enabled, FMOD events provide countdown audio, translated
  pacenotes, damage notifications, and authoritative stage transitions.
- On Windows, Pacenote Tool reads shared memory and the bridge named pipe
  directly. On Linux, `ACRProtonRelay.exe` remains inside ACR's Proton prefix
  and forwards both sources to the native Avalonia process.
- Pause remains a usable raw telemetry condition, but is not sufficient to
  establish the rest of the rally lifecycle.

The Linux transport and security design is documented in
`Docs/ACRProtonRelay.md`.

## Candidate Improvement: Progress Display

SimHub projects track progress from `Graphics.NormalizedCarPosition`, whereas
Pacenote Tool currently derives completion from accumulated distance divided
by static track length. The normalized field is a better candidate for a
dashboard-only progress display, but it must be validated on multiple ACR
rally stages before adoption. It does not by itself solve lifecycle detection.

## What Would Enable Reliable Lifecycle Support

The FMOD live bridge is the currently implemented ACR-specific authoritative
source. Future alternatives could include:

1. A documented rally telemetry, timing, or SDK interface.
2. A supported local bridge that receives explicit stage-state events from
   ACR.
3. A stable, independently validated game-generated event or timing output.

If the live bridge is disabled, incompatible with a new ACR build, or cannot
be started, keep dashboard-only behavior rather than introducing unsupported
heuristics.
