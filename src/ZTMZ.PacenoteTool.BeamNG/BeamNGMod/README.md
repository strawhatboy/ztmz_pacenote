# ZTMZ Pacenote Rally Bridge

This mod is installed by ZTMZ Pacenote Tool into the selected BeamNG.drive user
folder. It does not replace or edit BeamNG Rally Mode files.

1. In BeamNG, enable `OutGauge UDP protocol` and set its target to
   `127.0.0.1:44444`.
2. In `Options > Controls`, bind `Toggle ZTMZ Pacenote Rally Bridge`.
3. Start a Rally Mode stage and use that action once to begin the local bridge.

The bridge sends Rally state and built-in pacenote metadata only to
`127.0.0.1:44445`. This default is below Windows' normal dynamic UDP range,
but the desktop tool still checks for an actual local port conflict before it
initializes BeamNG. It does not control the stage, timer,
vehicle, or BeamNG audio.
