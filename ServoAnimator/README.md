# J5 Animation Editor development workspace

This solution contains the J5 Animation Editor, its regression checks and MediaPipe face-tracking helper. Current user instructions are in [the application README](ServoAnimator/README.md) and [Workflow Guides](ServoAnimator/Help/StepByStep.md). The sections below cover face-tracking setup and development details.

From the repository’s `ServoAnimator` folder, open `ServoAnimatorFaceTracking.slnx` with the .NET 10 SDK installed, or build with `dotnet build ServoAnimatorFaceTracking.slnx -c Release`. The executable is `ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.exe`. Configuration lives in the active `Config` folder.

## Meeting mode

**Meeting**, beside Record, opens Meeting Configuration. Choose a microphone and one of the five controller/webcam sources. Microphone level monitoring is live; mouth LED gain, noise gate, and release time control the audio response. Settings are saved in `Config/MeetingSettings.json`. This mode creates no takes or audio files.

The window displays the current URDF background. Optional meeting overrides accept a color or image and are saved separately from the normal view. Start Meeting undocks the head, enables URDF drive, and hides the view overlays for OBS capture. The bright red **Stop meeting** button stops meeting input and mouth lighting and restores the prior background and overlay settings. Controller enable preferences from before the meeting are restored; held inputs must return to neutral. Physical Live Drive is turned off when starting and is not automatically re-enabled.

Meeting input continues while Teams has focus. Webcam mappings take precedence over overlapping controller actuators. The face tracker owns the physical webcam: capture the undocked robot window in OBS, start OBS Virtual Camera, and select **OBS Virtual Camera** in Teams. The editor does not configure OBS or Teams automatically. Microphone audio is used only for the URDF mouth; select your microphone separately in Teams for speech.

## Recording microphone audio

The Record from Controller / Webcam window includes **Record Audio**, a microphone selector, a live peak meter in dBFS, and a latched **CLIP** indicator (click to clear). Selecting a microphone starts level monitoring; audio is written only while a take is recording. Refresh microphones rescans connected input devices. Capture requests 16-bit PCM, mono, 44.1 kHz; an unsupported device format is reported rather than silently changing the format.

With Record Audio checked, name/save the sequence before arming the take. WAV files are stored beside the sequence as `SequenceName_Take1.wav`, `SequenceName_Take2.wav`, etc., without overwriting existing files. A provisional Play command is added at the take's starting cursor; it is excluded from playback while its WAV is being written. Replay Recording includes the recorded audio. Keep Take retains the WAV and Play command with the movement edits in one undo step. Discard removes that take's WAV and Play command. Undo after keeping leaves the WAV available for Redo. Audio recording temporarily uses 100% playback speed; closing the recording window restores the previous speed.

The **Start a sequence** panel also offers **New Sequence…**, prompting for a name and save location and creating normal sequence metadata with an empty commands list.

## Start tracking

The local Python environment and face model have been installed for this workspace. On another machine, install Python 3.10 x64 and run `FaceTracking/Setup.ps1` before using tracking. Keep the FaceTracking folder beside the source solution (or beside a deployed executable). Do not copy the virtual environment between machines; recreate it with Setup.ps1.

1. Open Config > Webcam Face Tracking and press Start camera. Camera index 0 is the default.
2. Face the camera comfortably and press Set neutral. Enable the mappings you want, adjust their input and output ranges, and save/apply mappings.
3. Optionally enable Drive URDF preview to check movement. This configuration preview does not drive physical hardware.
4. Close the configuration window. The camera remains running for recording.
5. Open the recording window and choose Webcam, Webcam + X-Box, or Webcam + Steam. Select the controls to record and press Ready to Record.
6. Use F9 in the editor to start/stop, or the configured controller recording action when using a controller. Review with Replay Recording, then Keep Take or discard. Existing smoothing, key reduction, overdubbing, and grouped takes apply.

Head turning, pitch, roll, and squint are enabled initially. Output ranges use native editor units, not degrees. Input head angles are degrees; expression inputs are generally 0–1. Dead zone is a fraction of the normalized input span; smoothing is milliseconds. Child controls can be mapped independently. Enabled face mappings reserve their physical actuators during mixed recording so controller input cannot overwrite them. Head pitch mapped to NeckNodUp and roll mapped to NeckTiltRight are mixed into independent NeckTiltLeft and NeckTiltRight child commands under NeckNodUp. Their configured gang directions determine the mixing signs, and both outputs scale together if their combined demand exceeds the native limits. Independent child playback renders both pitch and roll. Other overlapping face mappings are rejected.

Tracking runs in a separate Python process. C# receives results and a small preview over redirected process streams. Samples older than 500 ms are ignored; sustained tracking loss stops a recording for review. Existing motion calibration and collision safeguards remain in the output path. Physical recording output requires the editor's existing Live Drive mode. Escape in Live Drive stops tracking along with playback and disables the servos. Face output pauses when the editor and its tracking/recording windows lose focus.

## Piston signals

`LPiston` and `RPiston` combine neutral-relative head pitch and roll before the ordinary mapping pipeline. Pitch moves both together; roll contributes opposite signs. Defaults: pitch/roll ranges 20 degrees, pitch gain 1, roll authority 0.5, and PreserveTilt clipping. PreserveTilt shifts both outputs together when necessary, retaining their difference at the expense of pitch; Independent clips each side separately. The live readout indicates clipping, including input-range saturation.

Use **Add piston mappings** to create disabled rows targeting `Child:NeckNodUp:NeckTiltLeft` and `Child:NeckNodUp:NeckTiltRight`, with input −1..+1, output −100..+100 and 200 ms smoothing. Disable the existing parent pitch/roll mappings before enabling these rows. Conflicts block Apply and identify the rows; unusual or swapped targets produce a warning. These target names use the editor's existing child syntax rather than introducing new parent controls.

The Piston signals settings group provides ranges, gains, direction reversal, and clipping mode. Valid changes affect an active URDF preview without restarting the camera; Save / apply persists them in mapping schema version 2. Set neutral captures independent pitch/roll offsets, and changing gains or ranges leaves these offsets intact. Existing settings files retain their mappings and inherit piston neutral from their head pitch/roll rows on first load. Positive pitch means nose up and positive roll means toward the performer's right; use the reversal options if the observed direction differs. Output continues through the existing mapping, calibrated speed, and collision-safeguard paths.

Math and conflict checks are available through `ServoAnimator.Checks --piston-signals`; this feature's changes were not compiled or tested, as requested.

## Limits and privacy

The Signal picklist exposes all 52 MediaPipe facial blendshape scores (including `_neutral`) plus ten derived signals: head yaw/pitch/roll, gaze X/Y, left/right brow, combined squint, LPiston, and RPiston. Expression scores generally use an input range of 0–1; set the mapping's input range accordingly when adding an expression mapping. Existing mappings remain unchanged when additional signals become available.

Head orientation is relative to the camera, not the torso. Eye-look expression coefficients provide approximate gaze direction; this is not calibrated screen gaze. Raw tracking data is not retained for later remapping. Recorded takes contain editable animation commands. Video is not saved.

The prebuilt MediaPipe 0.10.35 runtime includes usage telemetry with no supported opt-out switch. Its maintainers state that input media is not transmitted; see [MediaPipe issue 6291](https://github.com/google-ai-edge/mediapipe/issues/6291). Camera inference runs locally, but this dependency should not be described as network-free. Setup downloads packages and the official [Face Landmarker model](https://ai.google.dev/edge/mediapipe/solutions/vision/face_landmarker).

Focused checks cover mapping validation, webcam-only capture, combined controller capture, actuator ownership, editable grouped takes, and tracking loss. Model loading and inference were checked using the official sample portrait. A physical webcam and robot were not exercised during development.

The application documentation is maintained in `ServoAnimator/README.md`. This copy's deployment script packages the tracker, model, and setup files. Run the deployed `FaceTracking/Setup.ps1` before using tracking on its destination; the machine-specific virtual environment is deliberately excluded.
