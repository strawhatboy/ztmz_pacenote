# RBR Installed Co-driver Audio Integration

## Status

Research and the first implementation were completed on 2026-08-30. Pacenote
Tool now exposes an opt-in RBR game setting named `Use installed RBR co-driver
audio`.

The integration is RBR-specific. Pacenote Tool continues to own pacenote timing
and audio playback, but reads the selected co-driver clips directly from the
user's RBR installation. It does not copy, modify, or redistribute those files.

## Implemented Behavior

- The setting defaults to off and is available in the normal RBR game settings
  in WPF and Avalonia.
- The configured portable RBR executable is preferred; the registry remains a
  fallback when no executable is configured.
- `Plugins\Pacenote\PaceNote.ini` selects the active sound package and language.
- Package definitions are parsed from
  `Plugins\Pacenote\config\pacenotes\packages`.
- Exact and numbered audio variants are resolved in place beneath the selected
  package directory. Absolute and parent-directory paths are rejected.
- Original RBR pacenote types and modifier flags survive dynamic-record
  conversion and record merging, so a combined installed recording replaces
  its complete ZTMZ fallback token span exactly once.
- Every required modifier must resolve. If the base call or any modifier is
  unavailable, that source call falls back completely to the selected ZTMZ
  co-driver instead of producing a mixed or partial phrase.
- Countdown numbers, `Go`, and `Finish` use matching recordings from the
  selected Pacenote plugin package. Engine and cooling-system warnings use
  RBR's localized original speech under `Audio\Speech\Broken`.
- Calculated connectors and system sounds for which RBR has no matching
  recording continue to use the selected ZTMZ co-driver package. This includes
  collision-severity and individual puncture-position announcements.
- Package discovery occurs when RBR initializes, before its telemetry reader
  starts, so the first countdown call can use the installed voice. It is
  checked again while a stage's audio list is prepared, and refreshed after
  `PaceNote.ini` changes.

RBR's own co-driver playback is not modified automatically. Users must disable
it themselves to avoid hearing each call twice.

## Verified Installation Layout

The inspected installation at `L:\Richard Burns Rally` contains both the
original RBR co-driver audio and the Pacenote plugin's expanded sound packages:

```text
Audio\PaceNote\
Audio\Speech\PaceNote\
Plugins\Pacenote\PaceNote.ini
Plugins\Pacenote\config\pacenotes\
Plugins\Pacenote\sounds\<package name>\
```

The active Pacenote plugin settings were:

```ini
[SETTINGS]
sounds=Janne Laahanen EnNu B
language=english
```

`Plugins\Pacenote\sounds\Janne Laahanen EnNu B` contains 1,752 OGG clips.
The package includes numbered alternatives and complete phrases, for example
`six_left1.ogg` through `six_left4.ogg` and
`six_left_plus1.ogg` through `six_left_plus4.ogg`.

The original `Audio\Speech\PaceNote` folder provides a much smaller legacy
English vocabulary. It is useful as a fallback when the Pacenote plugin is not
installed, but the plugin package is the preferred source because it covers
the enhanced RBR vocabulary.

## Authoritative Runtime Mapping

Pacenote Tool uses `jannemod_v3.zdb`, not `Jannemod V3.csv`, for live RBR
pacenote conversion.

`RBRScriptResource.LoadData()` opens the configured ZDB and loads these tables:

- `pacenote`: RBR pacenote metadata by RBR ID.
- `pacenote_ztmz`: ordered RBR pacenote ID to ZTMZ token-ID mappings.
- `modifier_ztmz`: ordered RBR modifier flag to ZTMZ token-ID mappings.

`RBRGamePacenoteReader` uses the resulting dictionaries to build ZTMZ dynamic
pacenote records. The configured file defaults to the user-data copy of
`games\jannemod_v3.zdb`, and automatic pacenote-definition switching can load
a different ZDB for a stage. A sound adapter must therefore use the currently
loaded `RBRScriptResource` mapping instead of assuming the default database is
always active.

`Jannemod V3.csv` is currently copied to the RBR assembly output, but no runtime
code reads it. It may be retained as a development/reference artifact; it must
not become the authoritative source for this integration.

## Required Source Chain

An installed-audio adapter should combine the following sources:

1. Resolve the RBR root through `RBRGameInstallPathResolver`, respecting the
   user-configured executable before the registry fallback.
2. Read `Plugins\Pacenote\PaceNote.ini` to find the selected `sounds` package
   and language.
3. Use the currently loaded `jannemod_v3.zdb` or stage-specific ZDB to obtain
   each RBR ID's ordered ZTMZ token sequence.
4. Parse the Pacenote plugin package definitions under
   `Plugins\Pacenote\config\pacenotes` to obtain each RBR ID's `Snd<n>` file
   alternatives.
5. Resolve those filenames against
   `Plugins\Pacenote\sounds\<selected package>` and retain external read-only
   paths.
6. Fall back to the normal Pacenote Tool co-driver package for missing clips
   and system sounds for which the RBR installation has no matching recording.

When the Pacenote plugin is unavailable, a smaller adapter can map the known
legacy calls to `Audio\Speech\PaceNote`.

## Playback Model

The result should appear as a virtual, RBR-only co-driver package. It should
hold file paths in memory and feed them through the existing decoder and audio
mixer. It should not create a copied package in the Pacenote Tool data folder.

Numbered alternatives must remain grouped so playback can select a variant in
the same way as a normal Pacenote Tool package. Directly loading the RBR folder
as an ordinary package is insufficient because the existing package loader
uses filename stems as individual tokens.

Composite RBR calls require special handling. One RBR ID can map to an ordered
sequence such as `corner_6_left` followed by `detail_plus`, while its installed
audio may be one complete clip such as `six_left_plus1.ogg`. Mapping that clip
to either individual token would produce incorrect audio in other contexts.
The adapter should therefore support sequence-level matches before falling
back to token-by-token playback.

## Proposed Architecture

The feature should be implemented as an RBR-specific audio override rather
than importing the installed folder as a normal global co-driver package.

The Base project should expose an optional provider contract similar to:

```csharp
public interface IGamePacenoteAudioProvider
{
    bool TryResolve(
        PacenoteAudioCue cue,
        out PacenoteAudioResolution resolution);
}
```

The RBR module would implement this interface. Core and `ProfileManager` would
consume the interface without referencing the RBR assembly directly. Other
games would remain unaffected unless they implement the same capability in the
future.

### Preserve RBR Cue Identity

`RBRGamePacenoteReader.GetDynamicPacenoteRecord()` currently converts the RBR
pacenote type and modifier flags to ZTMZ tokens and then discards the original
values. The dynamic and final pacenote records should retain optional source
audio metadata containing at least:

```text
provider = rbr
pacenote type = ptype
modifier flags = pflag
```

This metadata must survive INI, DLS, and live-memory pacenote loading. Records
that are merged or augmented with calculated distance calls must retain an
ordered list of source cues rather than losing their association.

Preserving the original ID avoids reconstructing combined calls from split
ZTMZ tokens. For example, an RBR ID may map to `corner_6_left` followed by
`detail_plus`, while the sound package contains one natural recording such as
`six_left_plus1.ogg`. The provider can then play the combined clip exactly once.

### RBR Provider Snapshot

The RBR provider should build an immutable snapshot outside telemetry and UI
callbacks. It should:

1. Resolve the RBR installation root using `RBRGameInstallPathResolver`.
2. Read `Plugins\Pacenote\PaceNote.ini` and identify the selected sound package
   and language.
3. Parse `config\pacenotes` and all referenced package INIs.
4. Group every `Snd<n>` alternative by RBR pacenote ID.
5. Resolve each filename beneath the selected package directory.
6. Reject absolute paths and paths that escape the expected sound root.
7. Record coverage and missing-file diagnostics without failing game startup.

The snapshot should contain external file paths only. Decoded audio can use the
existing Pacenote Tool audio cache.

### Resolution Order

For each RBR source cue, playback should use this order:

1. Resolve an exact installed clip for the original RBR pacenote type.
2. Append installed modifier clips for each active RBR modifier flag in the
   plugin's defined order.
3. Randomly select among the available `Snd<n>` alternatives.
4. If an exact or modifier clip is unavailable, use the currently loaded ZDB
   conversion and normal ZTMZ token playback.
5. Resolve countdown, `Go`, `Finish`, engine, and cooling-system calls from the
   RBR installation, then use the selected ZTMZ co-driver package for system
   sounds not supplied by RBR.

The exact installed clip must take precedence over sequence-level inference.
Sequence matching is a fallback for records where source identity cannot be
preserved.

### ProfileManager Integration

When building script-mode `AudioFile` objects, `ProfileManager` should first ask
the active game's provider to resolve any source cues. Successfully resolved
segments should be appended through the existing decoder and mixer. Unresolved
segments should continue through the current token-by-token path.

This keeps ZTMZ responsible for distance, timing, sequential playback, volume,
speed, and audio-device selection. Only the source clips change.

## User Setting

RBR game settings should add an opt-in setting named approximately:

```text
Use installed RBR co-driver audio
```

When enabled, Pacenote Tool should attempt to load the package selected in
RBR's `PaceNote.ini` and use it for RBR pacenotes. The settings UI should show:

- Detected package name and language.
- Available, incomplete, or unavailable status.
- Mapping and file coverage where useful.
- A refresh action after the user changes the RBR package externally.

The override applies only while RBR is active. Every other game continues to
use the globally selected ZTMZ co-driver package. The setting should initially
default to off because RBR's own co-driver must be disabled to avoid hearing
each call twice.

## Lifecycle and User Experience

- Discover or refresh the virtual package after the RBR installation path and
  game configuration are loaded.
- Refresh it when the selected RBR sound package changes.
- Rebuild sequence mappings when RBR automatically switches the active ZDB.
- Keep unavailable or incomplete packages out of the global co-driver list, or
  label them clearly as RBR-only.
- Report missing files and mapping coverage without failing RBR initialization.
- Ensure RBR's own co-driver playback is disabled when Pacenote Tool plays the
  same installed voice, otherwise calls will be heard twice.

## Verification Plan

Automated coverage should include:

- Pacenote plugin root and package INI parsing.
- `Snd<n>` alternative grouping and deterministic test selection.
- Rejection of absolute and parent-directory sound paths.
- Preservation of RBR type and flags through INI, DLS, and memory readers.
- Correct single playback of a combined call.
- Modifier ordering and multiple active flags.
- Missing-file and missing-mapping fallback to normal ZTMZ audio.
- Refresh after `PaceNote.ini` package changes.
- Invalidation after stage-specific ZDB switching.
- No package discovery or playback changes for non-RBR games.

Manual verification should compare a stage with RBR's native co-driver first,
then repeat it with native playback disabled and the Pacenote Tool override
enabled. Calls, variants, modifiers, distances, and fallback system sounds
should be checked while monitoring the log for missing mappings.

## Legal and Safety Constraints

The feature should only reference files already present in the user's local
RBR installation. RBR and third-party co-driver audio must not be bundled,
uploaded, migrated into Pacenote Tool's data directory, or included in update
packages. All access should remain read-only.
