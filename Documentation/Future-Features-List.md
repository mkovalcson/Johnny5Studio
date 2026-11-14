# Future features list

Recorded November 8, 2026; updated November 11, 2026. Remaining ideas are for later exploration, not authorization to implement them now.

## User-preferred starting features

1. **Recorded-take comparison and adjustment.** Draft replay with existing audio is available. Remaining: quickly compare original and draft performances, trim start/end, and adjust timing.

## Gesture tools and selective command editing

The user specifically likes mirroring, left/right swapping, intensity adjustment and duration scaling.

Desired selection workflow:

1. Select a time range of command lists on the timeline.
2. Select a subset of controls from the commands in that range.
3. Mirror, swap left/right, or adjust intensity or duration for only the selected controls and commands.

Explore how mirroring differs from swapping: mirror should produce anatomically symmetric motion with appropriate direction reversal; swapping should exchange left/right performances. Preserve unselected controls. Before implementation, agree on intensity reference (neutral/default or another pivot), duration anchor, and handling of commands beyond the selected range. Preview and undo would support review. Saving selected performances as reusable library gestures is also an idea.

## Other ideas to explore

- **Non-destructive animation layers:** separate face, body, hand and travel takes with mute, solo, offset and strength controls; retain earlier performances when overdubbing.
- **Calibration quality and quick recalibration:** confidence by measurement, repeat individual steps, profiles for different people, and a quick controller-placement check.
- **Travel paths and planning:** floor routes, heading markers and editable waypoints; coordinate recorded gestures with planned travel and tread/caster animation.
- **Remaining scene staging ideas:** save starting marks and create look/stand/reach reference targets. The shared URDF prop library and saved placements are implemented.
- **Performance diagnostics:** tracking interruptions, dropped motion samples, joint limits and servo-speed lag, with timeline markers for review.

## VR performance follow-ups

Added November 12, 2026, from the bridge-timing and follow-problems logs. Related work so far:
VR-Follow-Smooth-Drive.md and VR-Follow-Scene-Rate-And-Treads.md.

- **Say why the editor can't be posed.** Follow now waits up to 10 s at start, but the editor still only reports yes/no (`VrPoseAllowed` in MainWindow.VrPose.cs). Send the specific reason (playing, dialog open with its title, recording, speed calibration, drag in progress) to the overhead panel and the follow-problems log, so remaining cases can be fixed at the source. Needs a pose-state field, so Player and Editor update together.
- **Hitch when Follow starts.** One 165–203 ms frame each time Follow starts, plus 7–32 ms garbage-collection pauses for a few seconds after the full scene load. Find what Follow start creates or rebuilds, then warm it up or reuse it.
- **Headset GPU headroom.** Logged GPU time averaged 13.6 ms against the 13.3 ms budget at 75 Hz, with a 4360 × 4360 render target per eye (SteamVR resolution well above 100%). Show render resolution and GPU time in a status panel, and offer a quality setting so the player stays within budget.
- **Smoothing outside Follow.** Scene blending and player-drawn treads only run while following. They could also cover pose-handle dragging and editor playback in VR, at the cost of about one scene interval of delay.

## Body-tracking quality follow-ups

From the body-tracking review of November 12, 2026 (see VR-Follow-Wrist-Pivot.md for the one that was done). Recordings from `Diagnostics/tracking-*.bin` and `--replay-tracking` let these be tested against real movements without the headset.

- **Body turn robust to one-arm reaches.** `bodyYaw` follows the line between the controllers whenever head and hand line roughly agree. Reaching or pointing with one hand swings that line 30–50° without the body turning, which then shifts Neck Turn, Waist Swivel and both shoulder positions. Weight or gate the update by hand symmetry (similar height and distance from the head); asymmetry means a reach, not a turn.
- **Elbow hint from controller orientation.** Two-bone IK uses a fixed elbow hint (down, slightly out and forward) for every pose. The elbow position sets the forearm direction, which is the base for wrist pitch and roll, so wrist readings drift with arm pose. Use the grip axis and palm direction from the controller as the hint ("swivel angle from hand orientation").
- **Roll and arm-twist gains.** Calibrated wrist roll is normalised to ±1 and multiplied by π, so about 70° of real roll becomes 180° on the robot. Arm twist maps the calibrated sweep to the full ±90° and switches off abruptly below 15° of elbow bend. Consider 1:1 angles with clamping (or a visible gain) and a fade over 15–40° of bend.
- **Calibrated neck pivot.** The pivot is a fixed 11 cm down / 9 cm back from the eyes for everyone, and shoulders are stored relative to it, so a different head or headset fit shifts the shoulder estimate when looking down. A short "nod with hands at rest" step could measure it.
- **Pose-level filtering.** Optional adaptive filter on the three raw poses before solving, cheaper than per-control output smoothing and keeps controls coherent.

## Additional Feature Ideas

1. **Named Follow presets.** Save neutral pose, speed, smoothing, and input scaling together. Switch between presets such as Conversation, Expressive, and Precise Hands without recalibrating. Keep URDF travel limits separate. This is the suggested first priority.
2. **Take bin.** Keep multiple drafts of the same section and audition them before choosing which to keep. Starting another take should leave earlier drafts available.
3. **Prop interactions.** Give props named grab points and timeline commands for attaching, releasing, and interacting with them. Use the one-button remote as the first example: pick it up, press its button to cue a scene animation or sound, and set it down.
4. **Camera bookmarks and shot cues.** Save views such as a wide shot, face close-up, or hand detail, and switch between them on the timeline for desktop preview or Clean Output. Leave headset viewing under the user's control.
5. **Motion cleanup for recorded takes.** Preview and apply cleanup to a selected section: remove tiny movements, smooth a shaky gesture, or reduce redundant commands without changing live Follow settings.

## Completed from this list

- **Follow Smoothing and Input Scaling.** Live per-control tuning with 0–300 ms smoothing and 0–200% input scaling around the neutral pose. Crouch and bend share input tuning to preserve neck-first movement. See VR-Follow-Input-Tuning.md.

- **Shared URDF prop library and stage placement.** Import reusable URDF objects and position multiple instances in a 3D backdrop in Desktop and VR Editor. Saved scenes and background presets preserve placement; standalone VR can render the props during saved-scene playback. Includes a one-button remote. See URDF-Prop-Library.md.

- **Compact VR rehearsal and recording.** Record and Overdub with a three-second countdown, Stop with elapsed time, then Replay, Keep, or Discard from the same panel. Replay uses sequence audio without editing the timeline. See VR-Rehearsal-Panel.md.

- **Follow neutral pose defaults — November 11, 2026.** Live defaults for 37 tracked head, torso, arm and hand controls, measured-reference-relative motion, automatic Config/VR persistence and per-section/all resets. Neck lowering precedes forward torso bending for crouch and bow, including waiting for actual speed-limited neck arrival. Calibration measurements and existing joint/speed limits remain in use. See VR-Follow-Neutral-Pose.md.

- **Named groups and a group manager — November 11, 2026.** Shared by Desktop and VR Editor: saved group names, command spans, affected controls/sections, select, display hide/show, duplicate at cursor and signed timing offsets. Groups can have independent show/hide timeline strips with synchronized zoom/cursor and group-specific dragging. Save, undo/redo and recovery retain group metadata.
- **Workspace space for group timelines — November 11, 2026.** Float/dock Control View, Xbox and Steam Controller mapping views, and Stream Deck connection/enable controls. The upper editing row collapses when Control View and URDF are floating and no controller mapping remains docked.
- Existing features to preserve rather than reimplement: grouped library-sequence insertion and overlap review; proportional timing offset; Follow speed sliders; calibration alignment guides; tread/caster animation; timing and Follow diagnostic logs. These do not complete selective gesture tools, non-destructive layers or the remaining diagnostics ideas above.

## Next discussion

The user would like to explore other ideas as well before deciding what to implement. Keep this list available for future planning; do not begin feature implementation solely because an item is listed here.
