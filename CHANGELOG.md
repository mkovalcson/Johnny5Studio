# J5 Studio changelog

## Unreleased

Current coordinated build version: **2.18.7**.

The starting point for this list is the last locally known GitHub push to `main`, commit `77a94cc`.
The last tagged release is [v2.16.1](https://github.com/mkovalcson/Johnny5Studio/releases/tag/v2.16.1).

### Added

- **TV Show settings.** Both editors open a Build form with media selection, fit, start time, playback length, loop, and volume. A limited video holds its final frame until the next TV cue or return. Older cues with no length keep their original playback behavior.

- **Desktop webcam overdub on an existing body take.** Ready to Record checks existing Neck Turn, Nod, and Tilt motion and lets you choose the source. Front, Robot POV, and Wide previews show the combined pose and shared playhead. Logical nod and tilt stay independently editable; direct piston tracks use paired source selection. Mixed webcam takes support Xbox or Steam with a floating controller mapping view.

- **Help by application.** Both editors label Help topics by app and can filter contents and search to the current app. Help now covers grouped Library Sequence insertion and overlap choices, puts Camera Shots in the Desktop scene workflow, and leads the Desktop webcam guide with calibration and preview steps.

- **Face Tracking mapping tools.** Both editors show live input and draft target meters with neutral, dead-zone and limit markers, plus the last applied target after smoothing. Learn Range offers movement-specific instructions, a three-second countdown, six seconds of sampling, and Accept/Retry/Cancel. Preview enablement uses applied mappings independently of pending draft edits.

- **Expressive movie idle.** In Desktop and VR Editor, right-click a movie sequence to choose Attentive, Curious, Tired, or Impatient. The hourglass replaces looping for that block. The sequence plays once, then idles until Next or another cue starts. Mood choices persist in movies and recovery. Standalone VR playback uses the same moods.

- **VR Follow hand controls.** Start in Movement mode with capacitive finger tracking on. Click the right stick to toggle tracking. The left glove shows its status. Either trigger toggles Movement and Hand mode when free of the lowered laser preview. The right glove shows the mode, with spoken confirmation when enabled. In Hand mode each stick opens or closes a hand proportionally and can select a point or fist. Returning to Movement resumes tracking or holds the last manual pose, depending on the tracking toggle. The left stick click still raises or lowers the visible laser. [Follow mode help](Shared/UserHelp/VrFollowing.md)
- **Automatic timeline camera shots.** Add Tight, Wide, or left or right Hand shots in either editor, and return to Standard view when needed. Tight tracks Johnny's shoulders and leaves room for head movement and flaps. Wide frames his whole body using the stage's back-wall direction or a configurable offset. Hand shots leave space ahead of the selected hand. Choose an instant cut or a timed blend that defaults to two seconds. The shots drive the Desktop URDF preview and VR Clean Output while the headset view follows the user. [VR camera shot help](Shared/UserHelp/VrGuide.md#camera-shots-for-clean-output)

### Fixed

- **TV playback on the first run.** Both editors warm the player before the cue, limit repeated seeks, and predecode pictures for short, non-looping excerpts. The decoder accounts for padded picture rows to prevent diagonal distortion.
- **URDF Beam audio and cadence.** Both editors import external Beam audio into Config/Library/Audio and store a portable reference. Beam visuals can update at up to 60 Hz with fewer camera recalculations.
- **Development launchers.** Root shortcuts build the solution before launching the current configuration. The development VR Player opens that configuration's VR Editor output.
- **VR build compatibility.** Player startup and VR packaging verify release version, source build identity and scene protocol. The editors' About dialogs and the Player's Build information show the identity and application location.
- **Movie recovery.** Assigned sequence keyboard triggers survive autosave and recovery in both editors alongside loop and idle choices.
- **VR standalone idle transport.** Pause/Resume controls hold and continue idle in the current sequence. Next advances separately, including a button on the compact panel. Next at the final block leaves idle running.

### Compatibility and verification

- Desktop webcam overdub: the Release solution builds, seven targeted merge and camera assertions pass, and 20 face mapping UI assertions pass. Live webcam and physical controller checks remain pending.

- Help by application: all three Release apps built and 152 focused Help checks passed, including Desktop and VR Editor filtering, search, navigation, and rendering.

- Face Tracking tools: the Release solution builds with zero warnings or errors, and 20 focused UI and learned-range assertions pass using synthetic camera input. Live webcam verification remains pending.

- The 2.17.0 solution builds. Focused checks passed: 139 idle playback assertions and 18 build identity, launcher routing, transport label and editor recovery assertions. The matching Editor passes the command-line compatibility check; the older published 2.16.1 Editor is rejected. Live headset and physical hardware checks remain pending.

- The camera shot feature changes the VR scene bridge protocol. Deploy the updated VR Player and VR Editor together.
- Expressive idle passed 133 focused checks for persistence, control limits, live waiting, manual advance, stopping, and older movies. Live headset and physical hardware checks are pending.
- The Release solution built successfully. The focused Follow controls checks passed (68), and the focused camera and bridge checks passed (65). Live headset and capture-display verification is pending.

## 2.24.6 (2026-11-25)

[Release notes](Documentation/J5%20Studio%202.24.6%20Release%20Notes.docx) cover changes since 2.16.1.

### Added

- **Lightweight is the bundled default.** Desktop, VR Editor, and VR Player start with the new full-body model. Its dimensions and joint pivots follow the CAD-based Full Detail model. Its static visual meshes use 1.43 million triangles, about 77% fewer than Full Detail.
- **Model-specific scene setup.** The editors list installed robots by name and load the selected model's URDF configuration and supported accessory options.
- **Deferred preview loading and idle rendering.** Editor startup loads the robot before scenery and tread animation. The preview holds the last frame while idle and redraws on demand.
- **Timeline groups and ripple editing.** In both editors, hover over a selected marker to see its group name, or right-click to see the highlighted group heading. Ctrl-select groups and individual commands across lanes and drag them together. Ripple moves later commands in time. Group edit mode keeps the group intact while individual command lists are moved or edited, and rippling the final list shifts the rest of the sequence.
- **VR rehearsal and overdub.** The compact panel supports draft recording, replay, and keeping a take. Body recording can include microphone audio and optional Applio conversion.

### Fixed

- **CAD alignment and motion.** Updated torso and head leveling, the rear waist actuator, shoulder alignment, accessory pivots, and the torso and cradle covers.
- **TipToe caster steering.** Desktop, VR Editor, and VR Player swivel the caster toward the tracks as the four-bar moves forward and reverse the roll as it lowers.

### Packaging and verification

- Both Windows x64 packages were built from the live 2.24.6 source. Desktop, VR Editor, and VR Player report 2.24.6.
- Both ZIPs passed integrity and model-reference checks. Live headset playback remains unverified.

## Released

See the [GitHub releases](https://github.com/mkovalcson/Johnny5Studio/releases) for v2.16.1 and its downloads.
