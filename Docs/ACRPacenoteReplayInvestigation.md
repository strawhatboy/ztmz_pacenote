# ACR previous-call replay investigation (2026-09-12)

## Confirmed cause after enabling Trace

Both playback routes are active: ACR's live bridge and the profile's
distance-triggered script. This is not a duplicate FMOD lookup in the captured
reproduction.

At 18:35:17.6806 ZTMZ logged `Playing live pacenote: LeftSquare`, following
the Caution/Brake/Junction fragments. It also loaded the 84-record script for
`Assetto Corsa Rally 威尔士 Afon Bidno`, containing the 903 m record
`caution,brake,junction,square_left`. Ordinary script playback logged `Playing 10`
at 18:35:19.1759 and `Playing 11` at 18:35:29.3259 while live playback continued.
The latter log numbers are the incremented audio-list indices, not raw script
record numbers (distance-call merging can change that list).

`ZTMZPacenoteTool` independently subscribes to live events and executes
`ProfileManager.Play()` from telemetry when a distance-script audio file exists.
There is no playback-source exclusion at that dispatch boundary. The loaded
profile script explains the earlier reported old phrases following new live
calls, since the two sources use different trigger timing.

## rc23 remediation

ACR now explicitly selects live-only pacenotes. Profile preparation clears any
previous distance queue and returns before resolving files or creating stage
directories. Telemetry dispatch also rejects distance playback for ACR. Other
games are unaffected; implementing the live-event interface alone does not
disable their distance scripts.

Both packaging scripts exclude the entire default ACR profile directory; Inno
Setup also excludes it to protect builds using an old staging directory.
The 2.99.99.51 startup migration directly deletes
`profiles/default/Assetto Corsa Rally` recursively, as explicitly requested by
the user. There is no hash filtering, archive, or backup. Other games and other
profiles are outside the deletion scope. Migration failure leaves the existing
version marker unset for retry on the next startup.

Follow-up: a 51 marker and a reported successful migration were observed while
the directory still existed. The historical log did not record the deletion path,
so its exact cause was not established. The user canceled further investigation;
the original one-time migration remains unchanged.

## Reproduction and observations

## Roadbook display fixes

ACR's shared-memory race time remained zero, preventing the roadbook's scroll
animation from advancing. Native dashboards now expose monotonic
`AnimationTimeSeconds` (Stopwatch), and browser Lua supplies performance.now().
The roadbook uses this animation clock instead of game time.

Only ACR assembles Start/Mid/End fragments into a display record and assigns a
group ID; Any starts an independent call. Late attachment at Mid/End starts a
new group. Audio still receives only the new fragment. ACR dispatch is serialized
to preserve fragment/snapshot order. Other live-event providers retain their
existing dispatch path and empty group ID, and continue creating one row per
snapshot. Same-group updates replace the current row without restarting the
scroll animation. Existing icons move to their new centered positions over
150 ms, and added icons fade in. A further fragment starts from the last rendered
positions/opacities. With zero history rows, the previous row slides upward and
fades out on the next call, then is discarded. It is never retained as history.
Icon-label spacing permits -0.40 to 0.30 of row height, defaulting to -0.25
to compensate for transparent PNG margins. Default row spacing is -0.10, with
a range of -0.30 to 0.08 of dashboard height.

At 18:57:38, Junction/GoRight/Dist100 also demonstrated a separate token bug:
GoRight mapped to go,right, where go is the race-start voice token. GoRight and
GoLeft now map to right and left; GoStraight remains go_straight.

The user hears a new live call followed by fragments already spoken. The initial
example was `5_right,tightens_to_3,2_left,tightens_to_1` followed by a partial repeat.
That exact example has not yet been captured.

Two development probes were attached to the already-running ACR process:

- `ACRFmodSoundInfoTrace.dll`: logs the raw FMOD programmer-sound lookup name to
  `tmp/acr_fmod_sound_info_trace.log`. The file contains an older session before
  the second `trace-started` marker; do not interpret those sessions as one stream.
- `ACRBridgePipeTrace.dll`: logs only JSON writes whose caller is inside the
  loaded `ACRFmodLivePacenoteBridge.dll`, including write result and byte count,
  to `tmp/acr_bridge_pipe_trace.log`. It observes writes without consuming the
  named pipe. This is a development-only, machine-specific probe, not packaged.

For the user's report of “40 followed by the already-spoken right-three-long”,
the new raw lookup was only `EN_0/PN_Dist40_Any_Any_1`. The outgoing bridge message
was a single successful write (140 bytes):

```json
{"version":1,"id":"fmod-22","tokens":["Dist40"],"rawSoundName":"EN_0/PN_Dist40_Any_Any_1","token":"Dist40","placement":"Any","variant":"1"}
```

The preceding raw calls were `Into`, `Right3-Long`. Neither was sent again in
that captured outgoing message. This narrows this reproduction to the receiving
application/audio side; it does not prove the root cause or rule out all other
game-side behavior in uncaptured cases.

## Checks and remaining work

- Installed and repository bridge DLL SHA-256 matched:
  `17FDA1C2408E248C6B9A8DE4E552EBC4FC1433F33FA34B8016501B2AF713F59B`.
- Installed Base DLL and local Release DLL differed overall, but the method-body
  IL bytes of `AutoResampledCachedSoundSampleProvider.Read`,
  `SequentialSampleProvider.Read`, and `VariSpeedSampleProvider.Read` matched.
- An offline probe using installed DLLs played separate synthetic sample markers
  through the cached-sound, tempo, sequential, and mixing providers at rates
  1.0, 1.3, and 0.8. No opposite-polarity leftovers or unexpected audible tails
  were found. This does not exercise the live receiver, voice-file decoder,
  complete playback engine, or WASAPI device.
- Core starts a separate `Task.Run` per live call: ordering is not guaranteed.
  This is a concern, not yet evidence of the reported replay's cause.
- The WPF log-level change handler only saves the configuration. Trace capture
  requires restarting ZTMZ in this version; ACR and both probes can stay running.
- Trace correlation subsequently confirmed both playback routes (see above).
- rc23 regression tests verify whole-directory removal, preservation of other
  games/profiles, repeated cleanup, missing directories, and clearing an old
  audio queue when starting ACR without accessing its distance reader.
- An `xcopy /L` dry run with the release exclusion retained 496 other files and
  listed no ACR profiles. The existing staging directory was not deleted;
  the installer exclusion also protects packaging from that old directory.

Build the outgoing-message probe with `tools/ACRPacenoteTrace/build.bat bridge-pipe-trace`.
The probe remains loaded until ACR exits.
