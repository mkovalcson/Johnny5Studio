# Follow smooth drive — 2026-11-09

Fixes choppy driving and turning in Follow mode. Changes both the VR Player (Johnny5VR) and the
VR Editor (Johnny5VREditor). Builds on VR-Bridge-Pace-And-Memory.md and
VR-Follow-Problem-Display.md.

## The problem

The headset draws 75 frames a second, but the robot only changed when a new scene came back
from the editor, about 21 times a second (47 ms). Three things made that worse:

1. **Uneven steps.** The player sent motion targets every third frame (about 25 Hz), and
   the bridge forwarded them whenever it happened to be awake. Because 25 and 21 Hz don't
   line up, some scenes carried one send's worth of movement and some carried two. At
   full stick the robot jumped about 4 cm or 4° per scene, with occasional double jumps.
   The whole robot (or, in "Keep robot framed" mode, the whole room) moved at once, so it
   showed far more than an arm stepping.
2. **No catch-up.** The editor capped Follow drive speed at exactly the player's drive
   speed (80 cm/s, 90°/s), and its time step is clamped to 50 ms. After any late update the
   robot fell behind for good. It trailed the stick and rolled on after release.
3. **Timer resolution.** Windows' default 15.6 ms timer made `Task.Delay(12)` take about
   25 ms, so the bridge's 33 ms pace measured 47 ms.

## What changed

### 1. Player blends between scenes while following (Renderer.Live.cs)

- `Renderer.SmoothLive` is set every frame from `editor.FollowActive` in
  `ViewerWindow.Editor.cs` (`PumpEditor`). It is on only while Follow runs. Everywhere else,
  scenes are drawn as they arrive, as before.
- When a scene arrives, `ApplyLive` fills `livePalette` with the new target matrices. Then
  `StartBlend` splits each node's matrix into scale, rotation and position
  (`Matrix4x4.Decompose`). The start is where that node is drawn right now, so a scene
  arriving mid-glide never jumps. Nodes are keyed by `VrNode.Id`, not by palette slot,
  because tread frames switch meshes and therefore slots.
- `AdvanceLive()` runs every rendered frame. It only does work while something is moving:
  it blends position and scale linearly and rotation with slerp, then uploads the palette.
- The glide lasts one **measured** scene interval (`LiveBlendIntervalMs`: a running average
  of arrival intervals, 80/20, clamped to 15–100 ms). Faster scenes later mean shorter
  glides and less delay, so nothing needs retuning if the editor speeds up.
- Cost: about one scene interval of extra delay (33–47 ms) while following, in exchange
  for movement every frame. It works the same whether the editor moves the robot
  ("Move robot through room") or the room around the robot ("Keep robot framed"), because
  it blends the final world matrices, not stage values.
- Not blended: dynamic mesh vertex contents (they still change once per scene), and a node
  that can't be decomposed (it snaps). Pose handles and picking use the scene's target
  pose, not the blended one.

### 2. One motion step per scene (VrBodyMotion.cs, EditorConnection.cs)

- `VrBodyMotion.Step` now offers the latest targets every frame (`Send`). The bridge keeps
  only the newest (`EditorConnection.Motion`, latest wins).
- `EditorConnection.Receive` sends motion **only** right before each scene request
  (`SendPause(motion:true)`). The waits in between still send pause, commands, pose and
  recording requests (`SendPause()`), but not motion. The editor reads the pipe in order and
  applies the motion batch on its UI thread before it builds the scene, so each scene
  reflects exactly one motion step.
- Sent batch ids are kept in a bounded set: the last 512, through `sentIds` plus
  `sentOrder`. They are used to recognise "Body capture error" replies. The old code
  cleared the whole set at 128.
- Recording in the editor captures one frame per applied batch, so recording is now one
  frame per scene cycle (about 21–30 Hz, similar to before).

### 3. Editor catch-up headroom (Johnny5VREditor/MainWindow.VrMotionCapture.cs)

The Follow stage speed cap is 1.25 × the player's drive speed (112.5°/s and 100 cm/s). If
you change the player's speeds in `Shared/Pose/BodyTrackingSolver.Drive` (`90` and `80`),
change these to match. Joint speeds are unchanged.

### 4. High-resolution bridge pace (HighResolutionTimer.cs, EditorConnection.cs)

`HighResolutionTimer` wraps `CreateWaitableTimerExW` with
`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`, available on Windows 10 1803 and later. It
falls back to `Task.Delay` when that's unavailable. The bridge's wait between scenes uses
it, so the 33 ms pace should now hold instead of stretching to 47 ms. This is not yet
measured on the gaming PC. Check `SceneCycle` in the next bridge-timing CSV.

## Checks

- Player `--check`: 3,962 pass (unchanged count; the new check runs in the smoke test).
- Player `--editor-smoke` runs `Renderer.VerifyLiveSmoothing`, which writes
  Diagnostics/live-smoothing-check.txt. It checks that:
  - a new scene starts from where the robot is drawn
  - halfway through the interval the robot is halfway
  - a scene arriving mid-glide does not jump
  - turning passes through the middle angle
  - outside Follow, scenes are drawn as they arrive
- Editor `--vr-pose-check <config> <report>`: 143 pass. This includes the new check
  "Follow turning has catch-up headroom" (4.58° in 40 ms, above the old 3.6°).
- Not yet seen in the headset.

## Files

- Johnny5VR: Renderer.Live.cs, ViewerWindow.Editor.cs, ViewerWindow.cs (hooks in the smoke
  check), VrBodyMotion.cs, EditorConnection.cs, HighResolutionTimer.cs (new)
- Johnny5VREditor: MainWindow.VrMotionCapture.cs, VrPoseChecks.cs
- Backups of the originals: Archive/DesktopChanges/vr-smooth-drive-20261109
- Update: Releases/VR/0.9.0/J5Studio-VR-Follow-Smooth-Drive-update.zip (cumulative on top of
  J5Studio-VR-Follow-Problem-Display-update.zip; replaces Johnny5VR.dll and
  Editor/AnimationEditorPlayer.dll)

## Ideas not done

- Smoothing outside Follow, for example while dragging pose handles. The trade-off is the
  same one-interval delay.
- Extrapolating ahead instead of blending behind would remove the delay, but it overshoots
  when the stick is released.
- Still open from the logs:
  - the "pause playback and close dialogs" stop on the first Follow attempt; the editor
    doesn't report which condition failed
  - a 165–203 ms hitch when Follow starts
  - headset GPU time right at the 75 Hz limit (4360 × 4360 per eye render target)
