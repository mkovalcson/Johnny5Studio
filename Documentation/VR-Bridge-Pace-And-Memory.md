# VR bridge pace and memory — 2026-11-08

Two VR Player changes, shipped as separate updates so each can be tested on its own.
Both build on J5Studio-VR-Follow-Stability-Rollback-update.zip. The VR Editor is unchanged.

## What the timing log showed

Source: E:/bridge-timing-20261108-133224.csv (rollback build, 2 minutes of Follow).

- The headset frame loop was healthy: 75 fps, no late frames, no reprojection or dropped
  frames during Follow, render-thread work about 1.8 ms of the 13.3 ms budget.
- The robot's pose reached the player only about 20 times a second. Each update was strictly
  sequential: scene request round trip (~17 ms, the editor building the scene on its UI
  thread), decode (~3.5 ms), waiting for the render thread to take the scene (up to 13 ms),
  then a fixed 16 ms delay that Windows often stretches to ~31 ms.
- The player allocated about 310 MB/s during Follow (14 MB/s standalone), causing one or two
  full garbage collections a second with pauses up to ~10 ms. The cause was ApplyLive
  rebuilding the 38,400 dynamic vertices into a growing list, copying it, and creating a new
  GPU buffer on every scene update (about 12 MB per update).
- Left/right controller tracking stayed valid for the whole Follow run. Follow stopped once,
  at 13:35:39, because the headset itself reported Calibrating_OutOfRange.

## Change 1 — bridge pace (J5Studio-VR-Bridge-Pace-update.zip)

File: Johnny5VR/EditorConnection.cs.

- Scene requests are paced start to start at 33.3 ms (30 updates/second,
  `EditorConnection.SceneCycleMs`). There is no longer a fixed delay after each update.
  An update that takes longer than 33 ms is followed by the next request at once.
- The bridge no longer waits for the render thread to take a scene. The newest scene
  replaces an untaken one (latest wins); each scene carries its full mesh and texture list,
  so skipping one is safe.
- Pause, commands, navigation, pose and Follow motion are still sent while waiting.
  Switching to standalone still drops a scene that arrives after the switch.
- Why not faster: the editor builds each scene on its UI thread (~15 ms). 30 Hz uses about
  half of that thread and leaves the rest for timeline and Follow motion handling.
- New timing column: SceneCycleN / SceneCycleAvgMs / SceneCycleMaxMs (start to start of
  scene requests). SceneCycleN per row is the robot update rate.

## Change 2 — memory (J5Studio-VR-Bridge-Pace-Memory-update.zip, includes change 1)

File: Johnny5VR/Renderer.Live.cs.

- Dynamic geometry is still rebuilt and uploaded for every scene update. There is no change
  detection and no skipping (the rolled-back optimization had both). The difference is that
  the vertices go into one reused array and one reused GPU buffer (Dynamic usage,
  write-discard map). The buffer grows by 25% when a larger mesh arrives and is never
  shrunk; the draw uses the current vertex count.
- The bone-transform and material arrays are reused while their sizes match their GPU
  buffers.
- DynamicBufferAllocations in the CSV now counts actual GPU buffer creations (it used to log
  1 for every update). Expect 0 after the first update.
- The renderer check (live-frame-cache-check.txt) now verifies: one upload per update, no new
  buffer per update, a mesh revision uploads into the same buffer, the buffer grows for a
  larger mesh, and a smaller mesh draws only its own vertices.

## Validation on the development PC (no headset)

- 3,952 player checks passed with each change.
- Real player/editor smoke test passed with each change (pose, standalone switch/resume,
  30 scene updates, editor window images).
- Smoke-test steady state: allocation fell from ~190 MB/s to ~60 MB/s, with no GPU buffer
  creation per update. Full collections remained only during full scene reloads.
- The 30 Hz rate cannot be measured here: without SteamVR the smoke test is short and the
  editor is also streaming window images. Confirm it with a gaming-PC timing log.

## What to check in the next timing log

- SceneCycleAvgMs about 33–40 and SceneRoundTripN about 25–30 per row during Follow.
- allocMB well below 310 and gc2 mostly 0 during Follow.
- leftInvalidFrames / rightInvalidFrames and lastTrackingProblem around any Follow stop.
  Controller validity comes directly from SteamVR (bPoseIsValid). The player only reads it,
  so the bridge pace and memory changes cannot make a controller lose tracking. Follow
  still stops after 150 ms of invalid tracking (unchanged).
- If Follow stops, install the pace-only ZIP to tell whether change 2 is involved, or the
  rollback ZIP to rule out both.

## Backups

Archive/DesktopChanges/vr-bridge-cadence-20261108/Johnny5VR: EditorConnection.cs,
Renderer.Live.cs and BridgeTiming.cs before these changes.
