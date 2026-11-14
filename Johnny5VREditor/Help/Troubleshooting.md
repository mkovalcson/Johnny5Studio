# Troubleshooting

## A configuration change is not affecting hardware

Confirm the change was saved in Servo Configuration and Drive HW is enabled. Physical movement uses Servo Configuration, not URDF Min/Zero/Max calibration.

## A URDF calibration change is not affecting the physical robot

This is expected. URDF Configuration controls model-space visualization. Physical PWM and direction are controlled separately in Servo Configuration.

## A stale URDF calibration appears after changing embedded defaults

An existing `URDFconfig.json` overrides the embedded URDF calibration. Update or remove the override if you want to use the model's embedded values.

## Duplicate Maestro port warning

Port values must be 0 through 23. Two different physical servos using the same port are highlighted bold red. The duplicated NeckTilt views across the two neck gang sections are intentional shared controls and are excluded from the duplicate warning.

## URDF Model window is missing after undocking

Use **Dock URDF** in the Commands header, or **Config > Reset Screen Layout** if the header is inaccessible. Closing the detached URDF Model window with its X also docks it back into the editor.

## Recent file is unavailable

Open Recent keeps missing paths visible but disabled. Use the normal Open Sequence or Open Movie command to locate the file again.

## Collision warnings look unexpected

Collision diagnostics intentionally check only the configured flap/eye contact surfaces. Servo bodies, flap arms, brackets and unrelated head/neck parts are excluded. Verify URDF calibration before treating a collision warning as representative of the physical robot.

## Webcam, microphone and Meeting problems

- Camera unavailable: choose the correct camera index, check Windows camera permission, and release the physical webcam in Teams/other camera applications. Teams should use OBS Virtual Camera while the face tracker owns the webcam.

- Tracker cannot start: run FaceTracking/Setup.ps1 for this copy's Python environment and model. Closing Webcam Face Tracking leaves capture running; Stop camera releases it.

- Piston rows cannot apply: disable the parent NeckNodUp/NeckTiltRight mappings before enabling LPiston/RPiston. The warning names conflicting rows. Use positive angular ranges and valid native output limits.

- No movement in a take: load the URDF, enable Drive, leave Pose editing, enable the selected controller and arm the intended parent/child controls. Mixed modes reserve webcam-owned actuators.

- Missing microphone: Refresh microphones and select a connected input. A remembered unavailable device remains unselected. Capture requires mono 44.1 kHz support and microphone permission.

- Audio too loud: lower device/input gain or increase microphone distance; clearing CLIP only clears the indicator. Meeting LED gain affects the picture, not speech or WAV recording level.

- Mouth reacts to room noise: expand Meeting's **Adjust mouth response**, raise **Ignore quiet sounds** or lower **Mouth sensitivity**. Increase **Light fade time** to soften decay.

- Controller does not drive after a meeting: Stop meeting restores its previous enable preference. Return inputs to neutral, then check the controller button, Focus Control permissions and URDF Drive. Drive HW remains off until deliberately enabled.

- Help topic appears missing after a source-only update: the Help folder and HelpIndex.json must accompany the executable. New F1 bindings become active after the updated source is built. An open help window caches content until the editor restarts.

## Recent features: quick checks

- **Saved vertical position changes on reopening:** check URDF Scene Setup, the selected robot file and the assigned sequence scene. Preserve Robot Framing retains the saved placement for the configured startup stage; explicit timeline or meeting presets can still replace it.

- **Model or backdrop disappeared after moving folders:** select the file at its new path. A backdrop can live anywhere, but its mesh paths must still resolve.

- **Finger bends stop sooner than expected:** the full-body neutral hand is nearly closed. Open with negative curl values. Coupled base/tip closing stops remain active even when Collision Safeguard is off.

- **Spin stops at a mapping endpoint:** select Relative joystick mode for continuous turning. Absolute, Set and Toggle intentionally use mapped angle destinations.

- **TV stays blank:** load a backdrop containing the selected screen link, add TV Front and TV Show, and check the imported media path/Windows codec support. The show begins after camera arrival.

- **Audio replacement refuses a timing JSON:** check its format/version and duration, and Reset trims on affected audio before re-timing.

- **A controller cannot take over a webcam control:** select the takeover checkbox independently of recording selection, map Face Tracking Override, and ensure that controller is connected and enabled.

- **Background arrows affect another app:** the movie transport currently owns them. Use square Stop or disable Movie Playback in Focus Control to release them.

- **Help opens the wrong topic:** stop playback, focus the desired field/window, and press F1 again. Search by the exact command name if needed.
