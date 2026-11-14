# VR Follow repeated-work optimizations — 2026-11-09

## Scope

Changes are confined to Johnny5VREditor, Johnny5VR and the Shared/VR transport source linked by those two projects. The regular Desktop Editor does not compile that transport and is not rebuilt or deployed by this update. The independent wrist/arm calibration, smooth scene interpolation, current bridge pacing, drive catch-up, reduced-allocation geometry uploads, problem display and tracking/stop diagnostics are retained.

## Four changes

1. Batch motion application. All targets are validated and speed/range limited before application. Per-view joint and stage updates run with intermediate collision refreshes suppressed. Pose overlay and collision state are refreshed once after the batch. Control values remain synchronized for desktop editing. Normal joint calibration, coupled neck/piston handling, treads/casters and recording capture remain in place.
2. Cache control metadata. Names, keys, command ranges and ganged flags are reused for a loaded scene/configuration. Actual positions and values are still evaluated for each pose snapshot so labels/picking do not use old locations. Head command values are grouped once rather than repeatedly scanned per handle. Scene/configuration replacement clears the metadata cache.
3. Cache unchanged Follow editing context. The sequence fingerprint and final context token are reused while document identity, command-list identity/count, editor edit generation, description/background/audio/spline metadata, paths, cursor, playback state and model context remain unchanged. Normal command edits/Undo/insertion pass through the editor edit-generation path, invalidating immediately, including edits that keep the same command count. Active spline/marker/clip drags invalidate the cache and block Follow before the drag-completed refresh. Recording retains its stable recording token and existing context rules. Outside Follow, the original full fingerprint path remains.
4. Cache unchanged scene branches and transfer deltas. Captured branches are reused only when the WPF model revision, inherited transform, robot identity and backdrop inclusion match. Freezable change notifications invalidate changed transforms, nested materials/brushes, mesh edits and topology. Material descriptions/texture snapshots are cached independently. Scene packets send added/changed nodes and changed material records, plus explicit node removals. Meshes/textures continue to use the existing known-asset exchange.

## Transfer correctness

Scene protocol version is now 7. Update Player and Editor together. Every packet is merged on the receiver background thread before the latest complete frame is published to the renderer. Skipping a displayed frame does not skip a delta. Requests acknowledge the last merged sequence; an unknown acknowledgement forces a full snapshot. Connections, scene changes and periodic recovery points send full snapshots. A mismatched delta baseline is rejected. Lighting, framing/status and pose metadata remain current in each packet. The renderer still receives complete frames and retains its current interpolation and dynamic-geometry behavior.

## New diagnostic columns

- EditorUiWait: time waiting for the scene-capture work to reach the editor UI thread.
- EditorMotion: duration applying an accepted motion batch, including its validation/limits and final refresh.
- EditorCapture: scene-graph capture duration.
- EditorPose: duration collecting current pose/control state.
- EditorEncode: delta preparation plus encoding and background scheduling time for that response.
- SceneChangedNodes / SceneChangedMaterials: descriptions sent per packet (full snapshots include all).
- SceneCachedBranches / SceneVisitedBranches: reused branches versus branches that needed examination. One hit at the root can reuse an entire model subtree.
- PoseMetadataBuilds / ContextFingerprintBuilds: cache rebuilds since the preceding scene capture, including builds during motion processing.

Timings have N / AvgMs / MaxMs columns. Counts have Avg / Max columns. Existing SceneRoundTrip, SceneDecode, SceneCycle, allocation, tracking and Follow-stop fields remain. Encoding and capture run on different threads; their values should not be added to headset-frame work as though all execute on the rendering thread.

## Validation

Player regression checks and isolated VR Editor pose/recording checks pass. Focused checks cover one refresh per motion batch; unchanged metadata/context reuse; same-count command, cursor and metadata invalidation; moving branches; brush/material replacement; mutable mesh revisions; topology addition/removal; backdrop toggles; changed transforms/materials; missing-baseline recovery; scene replacement; and skipping an intermediate displayed frame. The real Player/Editor pipe smoke check passes scene rendering, pose/preview/discard, recording review/discard and standalone resume with protocol 7. Initial local snapshots exercise full transfer and subsequent snapshots exercise deltas. Actual performance and controller stability must still be measured with the headset on the gaming PC; no frame-rate gain is promised from local checks.

## Install

Close VR Player and VR Editor. Extract J5Studio-VR-Follow-Repeated-Work-update.zip over the existing portable VR folder, replacing files and preserving Editor/Shaders subfolders. This cumulative update builds on J5Studio-VR-Independent-Wrist-Arm-Calibration-update.zip and retains the other AI's Smooth Drive improvements. Config, models, settings and calibration are not included or overwritten. Existing version-2 or version-3 calibration works as before; this optimization update does not require another calibration.

For the next comparison, use the same scene and roughly two minutes of similar Follow movements. Send the new bridge-timing CSV. The new columns separate editor UI waiting, actual model work, capture, metadata and encoding costs while preserving the exact tracking/stop reasons.
