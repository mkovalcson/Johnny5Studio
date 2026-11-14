# Shared J5 Studio code

## Controls (first extraction)

`Controls/J5Studio.Controls.csproj` is a .NET 10 library referenced by J5 Studio Desktop,
VR Editor and VR Player. It contains:

- Logical command names, hardware control IDs and speed profile IDs.
- Native command ranges and nominal UI ranges.
- Physical membership of grouped controls, and grouped direction classification.
- Body joint definitions, shoulder aliases, hand membership and closing limits.
- Body angle mapping and built-in channel calibration, speed, acceleration and gang direction defaults.

Edit these definitions here, then rebuild all three applications. The `ServoAnimator`
namespace and original numeric enum identities are retained. The editors forward the
moved public types so existing assembly-qualified type lookups still resolve.

Configuration files remain in the root Config folder. The common configuration library below owns loading, saving and migrations. Applications apply those settings to their hardware, playback and interfaces. Existing `Shared/VR` bridge files remain source-linked.

The built-in collections retain their existing public API. Treat catalog arrays and
gang membership as definitions, not per-session state. `ControlDefaults.Create()` returns
fresh editable data; changing one configuration does not change another.

## Compatibility check

`Controls.Checks` captures the pre-extraction definitions and serialized commands in
`baseline.json`. It checks both editors against that baseline, including stock defaults,
body limits/aliases, native ranges, JSON round trips and mutable default isolation.
Run from the Studio root after building:

```powershell
dotnet build Shared/Controls.Checks/J5Studio.Controls.Checks.csproj -c Release
dotnet Shared/Controls.Checks/bin/Release/net10.0-windows/J5Studio.Controls.Checks.dll ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.dll Shared/Controls.Checks/baseline.json --require-shared
dotnet Shared/Controls.Checks/bin/Release/net10.0-windows/J5Studio.Controls.Checks.dll Johnny5VREditor/bin/Release/net10.0-windows/win-x64/AnimationEditorPlayer.dll Shared/Controls.Checks/baseline.json --require-shared
```

The VR Player's existing `--check` also exercises grouped calibration, legacy shoulder
playback, pose controls and saved movies/sequences with the shared library loaded.

## Configuration (second extraction)

`Configuration/J5Studio.Configuration.csproj` is referenced by all three applications.
It owns servo configuration, URDF calibration and legacy migrations, model/framing
settings, backdrop/stage settings, renderer profiles, saved scene/background libraries,
image import, model location discovery, and Config-relative path serialization.
Original type names and JSON shapes are preserved, with type forwarding from both editors.

The library targets Windows and retains WPF image decoding and brush validation used by
the existing backdrop code. It contains no editor windows, playback engine or hardware
connections. Moving rendering/image adapters behind a separate platform boundary can be
part of a later renderer extraction; this step preserves the existing image validation.

Use `ConfigurationStore.LoadServos(root)`, `LoadUrdf(root, modelFile)` and
`ReadScene(root, reference)` for explicit Config-root operations. URDF defaults layer the
chosen model's embedded calibration, saved overrides and existing version migrations.
Scoped folder resolution keeps concurrent loads independent. The VR Editor supplies its
host-selected folder through `FolderSettings.SessionConfigFolder`; loading another Config
does not change that session selection. VR-only viewer preferences remain in viewer.json.

`ConfigurationPaths` owns common path resolution/serialization, including legacy Config
asset recovery used by the Player. `ConfigurationAssets` retains destination/link checks
and hashing used by backdrop imports and package helpers. The editors keep thin
`ConfigPathService` facades for document/UI calls until those features are extracted.

`Configuration.Checks/baseline.json` records the pre-extraction defaults, current Config
files, scene schemas and legacy migration results. Run its compatibility checks against
either editor assembly using the Studio root Config. It also tests fresh temporary Config
folders for save/load, different model defaults, session isolation, concurrent loading,
saved scenes and relocation:

```powershell
dotnet build Shared/Configuration.Checks/J5Studio.Configuration.Checks.csproj -c Release
dotnet Shared/Configuration.Checks/bin/Release/net10.0-windows/J5Studio.Configuration.Checks.dll ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.dll Config Shared/Configuration.Checks/baseline.json
dotnet Shared/Configuration.Checks/bin/Release/net10.0-windows/J5Studio.Configuration.Checks.dll Johnny5VREditor/bin/Release/net10.0-windows/win-x64/AnimationEditorPlayer.dll Config Shared/Configuration.Checks/baseline.json
```

## Animation (third extraction)

`Animation/J5Studio.Animation.csproj` is referenced by all three applications. It owns:

- Servo commands and their JSON converter, sequence/movie/library document models and file handling.
- Audio trim metadata, linked-command trimming/restoration and copied take identities.
- Duplicate-command detection, conflict resolution and command edit-session merging.
- Cubic Hermite tangent/segment evaluation, prepared playback tracks and command timestamps.
- Existing desktop audio-corrected playback clock and VR elapsed-time transport clock.
- Shared command labels, category normalization and numeric camera/stage command helpers.

Both editors forward the original public types to preserve assembly-qualified integrations.
The spline drawing controls remain in their applications and call the shared `SplineUtil`.
The Player's `MotionTrack` and clock retain small compatibility adapters over the shared code.
The Player reads movies through `MovieDocument` and sequences through `SequenceSource`.

There are two intentional file views: `AnimationDocument` preserves the native integer
editor model and its existing serialization, while `SequenceSource` is a disposable,
read-only view preserving fractional/scaled numeric values and unknown effect commands
for playback. Both live in the common library. The Player retains its effect/audio asset
adapters, warnings and duration calculations; application UIs, audio devices, hardware
output and render loops continue to own their existing application-specific behavior.

The clocks retain their distinct behavior: `PlaybackClock` gently follows played audio
samples in the editors; `TransportClock` supplies elapsed time and pause/resume in the
Player. This extraction does not change device rates or force one clock onto the other.

`Animation.Checks/editor-baseline.json` and `player-baseline.json` capture pre-extraction
file formats, labels, curve results, endpoint/timestamp behavior and duplicate key times.
The focused shared checks additionally cover trimming, restoration, clone isolation,
conflicts, curve agreement, clock continuity and read-only playback metadata:

```powershell
dotnet build Shared/Animation.Checks/J5Studio.Animation.Checks.csproj -c Release
dotnet Shared/Animation.Checks/bin/Release/net10.0-windows/J5Studio.Animation.Checks.dll ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.dll Shared/Animation.Checks/editor-baseline.json --shared
dotnet Shared/Animation.Checks/bin/Release/net10.0-windows/J5Studio.Animation.Checks.dll Johnny5VREditor/bin/Release/net10.0-windows/win-x64/AnimationEditorPlayer.dll Shared/Animation.Checks/editor-baseline.json
dotnet Shared/Animation.Checks/bin/Release/net10.0-windows/J5Studio.Animation.Checks.dll Johnny5VR/bin/Release/net10.0-windows/win-x64/Johnny5VR.dll Shared/Animation.Checks/player-baseline.json --player
```


## Pose and ground motion (fourth extraction)

`Pose/J5Studio.Pose.csproj` is referenced by all three applications and owns:

- Neck nod/tilt mixing, per-piston saturation, reversal handling, direct piston overrides,
  running neck state and export expansion.
- Physical timeline targets, logical gangs and child overrides, carried movie poses,
  native limits, speed-profile changes and suppression of repeated targets.
- Prepared-track parent/child selection and movie carry metadata used by VR playback.
- Deterministic floor travel, exact command boundaries, midpoint integration and seek checkpoints.
- Caster swivel, trailing-caster movement, twin caster-wheel travel and differential tread travel.

`IPoseCurve` exposes sampling arrays and limits without importing Skia or graph visibility.
The editors retain small `TimelineHardwarePose` and `TrackForwardPath` adapters. The Player's
`SecondaryMotionPath` supplies its prepared tracks to the same `GroundTravelPath` integrator.
Live curve arrays remain visible through adapters, so editing a curve cannot leave a stale snapshot.

Existing same-time behavior is explicit: the editors let a parent gang retake a child at
an equal timestamp; the VR prepared-track path retains its child-wins policy. This extraction
preserves those established boundaries rather than changing authored motion during a refactor.
The two host adapters also retain their existing initial-heading and event-boundary policies.
Physical/URDF calibration and body limits continue to use Shared/Controls and Shared/Configuration.

Hardware device output, render transforms, visual tread meshes, model geometry, UI editing,
RGB/audio effects and transport ownership stay with the applications. No Config file formats,
model files or timing rates were changed. `Shared/VR/CasterWheelTravel.cs` is now a source-linked
namespace compatibility facade over the common calculation, not another implementation.

`Pose.Checks/editor-baseline.json` and `vr-neck-baseline.json` contain frozen results captured
from the retained Step 3 editor/player builds before those publishes were refreshed. They cover
all sixteen neck reversal combinations, clipping, direct piston timestamps, forward/reverse travel,
turns, spline breaks, disabled travel, nonzero origins and backward seeks. Do not regenerate these
baselines from the current build just to make a failing regression pass.

```powershell
dotnet build Shared/Pose.Checks/J5Studio.Pose.Checks.csproj -c Release
dotnet Shared/Pose.Checks/bin/Release/net10.0-windows/J5Studio.Pose.Checks.dll Shared/Pose.Checks/editor-baseline.json
```

The fourth extraction passed 5,619 shared assertions plus the existing desktop physical timeline,
neck, caster, piston, movie/audio and VR runtime checks. See
`../Documentation/Shared-Pose-Step4.md` for validation and development launch details.
