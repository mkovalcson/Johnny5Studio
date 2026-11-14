# Follow metal gloves, rigid LCDs and mode synchronization — 2026-11-08

## Visible changes
Four separate 20 cm billboarding labels are replaced by one rigid 7.5 cm LCD on each metal glove. A and B are rows of one texture and have exactly one controller-relative transform; they no longer move or rotate relative to each other or independently face the head. A is closer to the wrist, B closer to the fingers. The left LCD shows the computer's local time in h:mm AM/PM format, then the active joystick target and mirrored/unmirrored mode. The right LCD shows B Stop/Back and A Menu. The JOYSTICKS CONTROL caption explicitly identifies the selected target, not what pressing B next would select.

Glove geometry is rendered in the VR scene and follows each tracked controller. Four fingers and thumb have three articulated segments driven by their own five SteamVR skeletal curl values. They estimate joint bends from curls, not full joint tracking. Missing curls use a modest resting bend; missing controller/headset tracking, loss of focus or stopping Follow hides the affected glove/display. Human glove finger assignment is independent of robot finger mapping. Raw controller pose is used, unaffected by Mirrored mode. LCD textures update only for settings changes or once per clock minute; geometry uses a reusable dynamic GPU buffer uploaded once per frame and reused for both eyes/mirror.

## Movement/audio audit
Before this change, the source routed MoveMe=true to viewer locomotion and MoveMe=false to robot Drive; the label and audio callback used the same boolean. A reversed movement route was not reproduced in source tests. Replaced the ambiguous boolean storage with FollowMovementTarget (Robot/Viewer), deriving movement gates, label and audio cue from that explicit value. Default remains Robot. Added full Follow-wrapper checks: in Robot mode joystick forward changes robot stage X and leaves viewer origin unchanged; after left B selects Viewer, robot stage X is held, viewer origin changes and the cue is MoveMe. Active sticks target is also included in controller diagnostics.

Audio could become stale after rapid toggles because confirmations queued. Each toggle category now has a serial number: a new movement selection interrupts/skips older movement cues; a mapping toggle supersedes older mapping cues without replacing the movement selection. Off/zero volume still cancels all confirmations. Normal audio feedback volume/mode settings remain unchanged.

## Source
Johnny5VR/FollowMovementTarget.cs; VrBodyMotion.cs; VrSession.cs; FollowControllerLabels.cs; ControllerGlove.cs; Renderer.Gloves.cs; Renderer.cs; Shaders/Scene.hlsl; ControllerFeedback.cs; GloveChecks.cs; Diagnostics.cs; Program.cs. No shared libraries, protocol, desktop editor or models were changed.

## Validation
3945 Player checks PASS, including mode routing through actual Follow wrapper, audio state, independent glove curls, geometry normals, rigid controller LCD transforms, and AM/PM including midnight. Actual Direct3D glove preview rendered; the same LCD bitmap/transforms were composited to reproduce SteamVR's separate overlay layer in a desktop diagnostic. Live editor smoke PASS: 30 scene frames / 28 WPF frames; real pose and recording pipes passed. Test Config isolated; viewer preferences restored. Controller fit/alignment, LCD readability and audio on the headset still require gaming-PC testing.

## Package
Releases/VR/0.9.0/J5Studio-VR-Metal-Gloves-LCD-update.zip
Cumulative since Johnny5VR-0.9.0-cumulative-update-2026-11-08.zip, including intervening Point Gesture, Follow Modes, Controller Feedback and Task Menus/Transport updates. Close both VR applications and extract over the folder containing Johnny5VR.exe. Preserve Editor and Shaders subfolders. The matching Shaders/Scene.hlsl MUST be replaced along with Johnny5VR.dll. No Config, Settings, saved calibration or robot models are overwritten.
