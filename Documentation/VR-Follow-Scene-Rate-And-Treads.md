# Follow scene rate, tread belts and editor wait — 2026-11-12

Changes the VR Player (Johnny5VR), the VR Editor (Johnny5VREditor) and the shared scene format
(Shared/VR/SceneWire.cs, linked into both). **Scene protocol is now version 11. Install Player and
Editor together.** If they differ, the player shows "Editor and VR viewer protocol versions
differ".

Builds on:
- VR-Follow-Smooth-Drive.md: per-frame blending between scenes while following.
- VR-Follow-Repeated-Work-Optimizations.md, by the other AI: delta scenes and cached capture.

## 1. Faster scenes while following (EditorConnection.cs)

The delta scenes and capture caching left the editor with spare time, but the bridge was
still fixed at 30 scenes a second. While Follow runs, the cycle now adapts:

- `EditorConnection.TargetCycle(following, editorSceneMs, playerApplyMs)` picks the longest
  of these three:
  - **Editor limit:** scene work may use at most 40% of the editor's UI thread
    (`EditorUiShare`). Scene work is `UiWaitMs + MotionMs + CaptureMs + PoseMs` from each
    packet's `VrEditorMetrics`, averaged 80/20.
  - **Player limit:** the player's `ApplyLive` may use at most 25% of its render thread
    (`PlayerApplyShare`). `ViewerWindow.Editor.PumpEditor` reports it via
    `EditorConnection.ReportApply`.
  - **Fastest:** 75 Hz (`FastestFollowCycleMs`).
- The result is never slower than the normal 30 Hz (`SceneCycleMs`). Outside Follow it is
  always 30 Hz, which leaves the editor responsive for the desktop panel.
- The chosen cycle is logged as `SceneTargetCycleMs` in the bridge-timing CSV. Compare it
  with `SceneCycle`, which is measured.
- UI wait is counted on purpose: if the editor's UI thread is busy with something else, the
  rate backs off.
- Motion is still sent once per scene request (see VR-Follow-Smooth-Drive.md), so faster
  scenes also mean more frequent motion steps. The blending glide shortens automatically,
  because it uses the measured interval.

## 2. Tread belts drawn by the player (SceneWire.cs, VrEditorMode.cs, UrdfScene.Treads.cs, Renderer.Live.cs)

**Problem.** The belt is 12 STL frames, each a 2.61 mm step
(Models/Robots/Johnny5FullBody/Meshes/Body/Treads/tread_animation.json). The editor swaps
the belt's mesh as it travels. At 80 cm/s, one 30 Hz scene advances about 10 of the 12
frames, so the belt appeared to crawl backwards (the wagon-wheel effect), and it stepped
even while smoothing moved the robot every frame.

**Format.**
- `VrFrame.Treads` is a `VrTreads(PhaseStep, LeftTravel, RightTravel, Layers)`, where each
  layer is `VrTreadLayer(Node, Left, Frames)`.
- It is written after the editor metrics in every packet, delta or full, so travel is always
  current.
- `VrTreads.FrameIndex(travel, step, frames)` is the editor's mapping from
  `UrdfScene.SetTreadTravel`: phase = −travel/step, then floor and wrap.

**Editor.**
- `UrdfScene.VrTreadState()` returns the step, each belt's `LastDistance` and every layer's
  frame meshes, weathered when weathering is on.
- `VrGraphCapture.Capture(..., treads)` adds every frame's `VrMesh` to the scene's meshes and
  lists only layers whose belt node is in the scene. Mesh conversion moved into
  `VrGraphCapture.MeshData` so the frames reuse it.
- Frames go through the normal known-mesh exchange, so they are sent once.

**Player.**
- `EditorConnection` keeps tread frame meshes in the used set.
- `ApplyLive` gives every (belt node, frame mesh) pair its own static slot and vertex batch.
- While `SmoothLive` is on, `StartTreads` blends belt travel the same way pieces glide: from
  the travel drawn now to the scene's travel over one measured interval. It snaps on jumps
  over 0.5 m, such as a seek.
- `DrawTreads(t)` runs each headset frame. It picks the frame for the blended travel, copies
  the belt node's blended matrix to that frame's slot, and moves the visible surface to it.
  The other frames' surfaces are zero-alpha and clipped by the shader.
- Outside Follow, or if a frame mesh is missing or dynamic, the editor's own frame is shown
  as before.
- The rollers already rotate as ordinary pieces (about 18° per 30 Hz scene), so the
  existing blending handles them.

## 3. Follow waits for the editor at start (VrBodyMotion.cs)

Choosing Follow while the VR Editor isn't running starts it first. Just after it opens, the
editor can briefly say it can't be posed: a loading window, or playback finishing. Follow
used to stop on its first frame with "pause playback and close dialogs", using a scene from
before Follow started. Now, while Follow is starting (`waitingStart`), it waits up to
`EditorWaitMs` (10 s), shows "Starting follow — waiting for the editor…", and logs
"Follow waiting for the editor" once to the follow-problems log. It stops only if the editor
still can't be posed after that. Once Follow is running, it still stops at once as before.

Not done: the editor still doesn't say *which* condition blocks posing
(`MainWindow.VrPose.cs`, `VrPoseAllowed`).

## Checks

- Player `--check`: 4,054 pass. Added to BridgeChecks:
  - tread protocol round trip
  - travel current in deltas
  - frame index mapping
  - five scene-rate cases
- Player `Renderer.VerifyLiveSmoothing` (runs in `--editor-smoke`) now also checks that:
  - the first scene shows its own belt frame
  - a new scene starts from the frame drawn
  - halfway, the in-between frame is shown at the belt's place
  - the belt ends on the scene's frame
  - large jumps snap
  - outside Follow, the editor's frame is shown
- Editor `--vr-pose-check`: 183 pass, including "The VR scene lists every tread belt frame,
  the frame shown and the belt travel".
- Not yet seen in the headset. The scene rate actually reached on the gaming PC is unknown
  until a new bridge-timing CSV comes back.

## Test fixtures and dev editor copy

- The player checks read `Johnny5VR/Diagnostics/pose-control-catalog.bin` and
  `pose-control-bounds.json`, written by the editor's `--vr-pose-check`. The copies there
  dated from Nov 9, before the other AI's pose format change on Nov 11, so the player checks
  failed. Both were refreshed from a new editor check run.
- When run from its build folder, the player launches the editor from
  `Johnny5VR/packaging/editor-0.9.0`. Its `AnimationEditorPlayer.dll` and `.pdb` were
  replaced with this build so the smoke test uses protocol 11.
- Originals of all of these are in the backup folder.

## Files

- Shared/VR/SceneWire.cs
- Johnny5VREditor: VrEditorMode.cs, UrdfScene.Treads.cs, VrPoseChecks.cs
- Johnny5VR: EditorConnection.cs, Renderer.Live.cs, BridgeTiming.cs (`SceneTargetCycleMs`),
  ViewerWindow.Editor.cs, VrBodyMotion.cs, BridgeChecks.cs
- Backups: Archive/DesktopChanges/vr-scene-rate-treads-20261112. This includes the old
  pose fixtures and the old packaged editor DLL.
