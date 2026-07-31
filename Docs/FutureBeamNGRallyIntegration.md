# Future BeamNG.drive Rally Mode Integration

## Status

Deferred feasibility note. BeamNG.drive support is not implemented by this
document and no game files are modified by the application today.

The assessment below is based on BeamNG.drive's built-in Rally Mode, not on
the older community rally mods. A future integration must work with those mods
disabled.

## Assessment

Basic dashboard telemetry is feasible with BeamNG's documented UDP protocols:

- OutGauge provides speed, RPM, gear, temperatures, fuel, dashboard lights,
  throttle, brake, and clutch. Its `time` field is hardcoded to zero and it
  has no route, stage, or pacenote state.
- MotionSim provides world position, velocity, acceleration, orientation, and
  angular motion. It can provide derived vehicle speed and position, but not
  authoritative special-stage progress, timing, or pacenotes.

Neither standard protocol can provide the Rally Mode information required for
the co-driver workflow.

The installed game's Rally Mode Lua implementation currently contains that
information. Its `gameplay_rally` extension exposes a Rally Manager which can
provide active-stage state and time, route completion and total distance, and
the next pacenote. A compiled pacenote includes its ID, name, rendered text,
visual data, and distance-call information.

This makes native Rally Mode support feasible through a small companion
BeamNG game-engine Lua extension. It would read the existing manager state and
send it to Pacenote Tool; it would not replace, edit, or disable BeamNG's Rally
Mode.

## Proposed Direction

1. Add a BeamNG game profile with OutGauge as the primary dashboard source.
   MotionSim may be enabled when world position or motion data is useful.
2. Package an optional companion BeamNG mod containing a game-engine Lua
   bridge. The bridge must be explicitly enabled by the user.
3. While a Rally stage is loaded, the bridge should read
   `gameplay_rally.getRallyManager()` and send versioned UDP JSON packets to
   the desktop application.
4. Send a periodic stage snapshot containing at least the stage identifier,
   active/completed state, special-stage time, distance completed, total
   distance, completion percentage, and split data when available.
5. Send pacenote changes as events, including the built-in pacenote ID, name,
   rendered text, visual representation, and before/after distance-call data.
6. Optionally send the complete stage pacenote list after the stage loads, so
   Pacenote Tool can inspect or prepare a leg before the start.
7. Keep the bridge read-only. Do not control the game timer, route, vehicle,
   or BeamNG audio queue.

An illustrative stage snapshot is:

```json
{
  "protocolVersion": 1,
  "stageId": "small_island-intro_rally_stage1",
  "isActive": true,
  "stageTimeSeconds": 93.4,
  "distanceMeters": 1842.7,
  "totalDistanceMeters": 5120.0,
  "completion": 0.36
}
```

## Pacenote Behaviour

The first version should mirror BeamNG's built-in pacenotes for display,
recording, or dashboard use. It should not speak a second co-driver call by
default, because that would overlap BeamNG's own audio. Playing Pacenote Tool
audio should require an explicit user choice, normally together with muting
BeamNG's Rally Mode audio.

The bridge can expose BeamNG's current rendered text and visual note data. A
separate mapping layer is needed before Pacenote Tool can reliably translate
or re-voice every BeamNG pacenote in the application's selected UI language.

## Constraints And Risks

- If users choose not to enable the companion mod, only standard dashboard
  telemetry is available. Special-stage time, authoritative route distance,
  and built-in pacenotes cannot be recovered from OutGauge or MotionSim.
- Rally Mode's Lua interfaces are present in the current installed game, but
  they are not a documented stable external API. BeamNG updates can change the
  manager, pacenote model, or mission lifecycle.
- The bridge should report its BeamNG version and capability set, and the app
  must fail gracefully when the extension is unavailable or incompatible.
- The future implementation must avoid screen scraping, OCR, or audio
  recognition. The game's own Rally Mode state is more accurate and avoids
  language-dependent parsing.

## Feasibility Spike Exit Criteria

- OutGauge is decoded by Pacenote Tool and drives the existing dashboard.
- The companion bridge starts alongside an unmodified built-in Rally stage.
- The app receives correct start, active, finish, restart, and recovery state.
- Exported special-stage time and route progress match BeamNG's Rally HUD.
- Exported pacenote changes match BeamNG's visual and audio pacenotes.
- A complete stage pacenote list can be exported without editing the stage.
- The bridge handles a missing Rally Manager, non-rally gameplay, and a game
  update without crashing the game or the desktop application.

## References

- BeamNG protocol documentation: <https://documentation.beamng.com/modding/protocols/>
- BeamNG.drive v0.38 Rally Mode release notes: <https://www.beamng.com/game/news/patch/beamng-drive-v0-38/>
- Current game implementation to validate during the spike:
  `lua/ge/extensions/gameplay/rally.lua`,
  `lua/ge/extensions/gameplay/rally/rallyManager.lua`, and
  `lua/ge/extensions/gameplay/rally/notebook/pacenote.lua`.
