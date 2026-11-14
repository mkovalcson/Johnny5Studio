# Follow instability rollback and diagnostics — 2026-11-08

## What the supplied log shows

Source: E:/bridge-timing-20261108-132007.csv. Five Follow runs ended after approximately 1, 1, 6, 11 and 5 logged seconds. These are one-second mode samples, not exact event durations. The Follow samples averaged 75.0 fps, with no SteamVR-reported dropped frames. The worst Follow frame interval was 25.77 ms. One Follow row recorded 20.5 ms total GC pause time; this is a per-row sum, not proof of one pause of that length.

The rows immediately before/after exits show scene application below approximately 1.3 ms and scene-round-trip maxima around 19–24 ms. There is no consistent large render or bridge stall around the exits. The roughly 189 ms scene-application stall at 13:21:01 happened during initial scene loading, before Follow began.

The prior CSV did not record controller pose validity, focus changes or exact Follow-stop reasons. Smooth headset timing cannot prove controller tracking is healthy, nor rule out a software regression. The user's report that instability began after the performance update is treated as a regression requiring rollback and diagnosis.

## Conservative rollback

Restores the original fixed 16 ms bridge delay after consumption and the original dynamic-mesh rebuild/upload behavior. Removes dynamic geometry signature skipping, retained dynamic upload buffers and reused transform/material arrays introduced in the recent performance update. Existing static-mesh caching is retained. Lower allocation and faster scene application from that optimization are intentionally relinquished while stability is checked.

Keeps the requested doubled wrist/thumb/finger Follow limits, existing shoulder/elbow boost, Follow backdrop preferences, overhead-panel preference, glove displays and previous cumulative features. The existing 150 ms tracking-loss timeout is unchanged. No extended timeout or stale-controller pose extrapolation is added to hide the failure.

## New CSV diagnostics

- hmdInvalidFrames, leftInvalidFrames, rightInvalidFrames: per-row counts of invalid SteamVR poses for the headset and controller roles.
- inputFocusLostFrames: frames without input focus or with the SteamVR dashboard visible.
- followTrackingHoldFrames: invalid-tracking frames while Follow was active; no new motion is sent during these interruptions.
- followStops: number of Follow stops in the row.
- lastTrackingProblem: last invalid device, role/device ID, connected/valid flags and SteamVR tracking-result enum, with millisecond timestamp.
- lastFollowStop: exact reason and millisecond timestamp. Reasons distinguish the B button, tracking loss, menu/input blocking, playback/dialog state, editing-context changes, recording or other errors.

Last-problem/reason strings persist so a short event remains visible in later rows. Compare their timestamps to the counters to distinguish a new event from retained history. These details are added to the existing bridge-timing CSV, so sending the next log is sufficient to identify the stop path. A lost controller is not silently replaced with an extrapolated pose.

## Install

Close VR Player and VR Editor. Extract J5Studio-VR-Follow-Stability-Rollback-update.zip over the existing portable VR Player folder, replacing files and keeping Editor/Shaders subfolders. This is cumulative since Johnny5VR-0.9.0-cumulative-update-2026-11-08.zip. Config, saved settings, models and calibration are not included or overwritten. Prior performance/timing documents describe historical builds; this rollback document describes the current bridge/geometry behavior. The regular Desktop Editor is unchanged.

## Validation and limits

3,953 Player checks passed, including unchanged 150 ms stop behavior, robot pose holding without sending motion on invalid tracking, and recovery within the existing interval. The real Player/Editor connection and conservative GPU path are tested separately. No physical SteamVR controllers/headset are attached here; recovery of gaming-PC stability must be verified there. This rollback does not establish the exact regression cause.
