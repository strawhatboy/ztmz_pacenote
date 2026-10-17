# Missing graphical calls while audio plays

Investigated on 2026-10-17 after an RBR report. Two display-path failures were
reproduced with the production roadbook Lua script and managed game context:

* Two calls at 12:00:00.100 and 12:00:00.500: the managed Lua renderer kept only
  the first. `tostring(DateTimeOffset)` omitted fractional seconds, so the
  timestamp/source deduplication key treated the second dispatch as a duplicate.
* Three calls published before the next dashboard update: only the last remained
  in `GameContext.CurrentPacenote`. This loss also affects the native in-game and
  browser paths, which consume sampled telemetry frames rather than every audio
  dispatch event.

Neither mechanism controls audio playback. The shipped RBR definition's 248
mapped ZTMZ token IDs all have icon aliases; no missing stock icon mapping was
found. Custom definitions and the user's exact live incident were not captured.
The earlier `9aa4e6b` roadbook change handled ACR fragment grouping and animations,
not these two failures.

## Fix

Each playback snapshot carries a unique `DispatchId`. The shared game context
retains an immutable window of the latest 32 dispatched calls; a stage/session
reset clears it through the existing `CurrentPacenote = Empty` paths. Both full
scene and delta telemetry include this window. Each dashboard independently
consumes unseen dispatches in order, retaining ACR group updates and the user's
configured number of history rows. Repeated frames do not replay old rows.

This is a bounded display history, not an audio playback queue. It does not hold
each intermediate call on screen for a minimum duration, change audio timing,
or recover more than 32 dispatches after a long rendering interruption.

## Regression coverage

`RoadbookDispatchTest` exercises same-second calls, identical timestamps,
multiple dispatches between frames, repeated frames, reset, bounded retention,
and ACR grouping through managed objects and serialized full/delta contexts.
The pre-fix reproductions returned `6_left` instead of `6_left|2_right`, and
`crest` instead of `6_left|2_right|crest`.

`RoadbookGroupingTest` retains coverage for older hosts without the new window
and identity fields. Existing native built-in dashboard checks exercise the
updated script in the x86 Lua host used by RBR.
