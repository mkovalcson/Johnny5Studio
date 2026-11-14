# Naming and text audit — October 12, 2026

The high-priority corrections and naming conventions were subsequently applied
in 2.1.2. See [implementation and verification](NAMING_AND_WEBCAM_ROWS_2.1.2.md).
The findings below describe the original audit; medium-priority workflow items
remain follow-up work unless addressed in that implementation report.

Reviewed the current 2.1.0 source UI, Help, and the behavior behind the labels.
This is an audit, not a set of applied fixes. Historical release snapshots and
stable JSON/API identifiers are excluded from proposed product renaming.

## Behavior mismatches and misleading scope

| Priority | Current wording | Finding and recommended correction | Evidence |
| --- | --- | --- | --- |
| High | “Neck Nod and Neck Tilt share one spline.” | Incorrect: they have independent commands, spline flags and curves, then mix into the two pistons. Remove the shared-spline statement. | CommandEditorWindow.xaml.cs:420; CommandSplineDraft.cs:16–23; Help/SplineEditor.md:35 |
| High | Webcam speed Help says “Default unless a controller speed action changes it.” | The active command speed starts at N/C. N/C preserves each actuator's previously active profile; the preview falls back to Default only when none is remembered. Correct the Help and display the actual profile, or clearly identify Keep current/N/C. | Help/WebcamFaceTracking.md:24; MainWindow.Controllers.cs:24,451; RobotHeadView.SpeedCalibration.cs:55,90; Help/LiveDriveHardware.md:17 |
| High | “Use calibrated motion in URDF” appears to govern all URDF speed limiting. | The new webcam enforcement option explicitly enables calibrated motion for its owned outputs even with this general checkbox off. The behavior is intentional, but the general checkbox needs an exception explained beside it or in its tooltip. | SpeedCalibrationWindow.cs:19,212; RobotHeadView.SpeedCalibration.cs:29–30; Help/WebcamFaceTracking.md:26 |
| Medium | Recording Smoothing and Keyframe reduction tolerance remain ordinary active controls. | They do not alter speed-limited webcam tracks, although other tracks still use them. The recording window needs a visible explanation; disabling or dimming the controls is appropriate when every recorded track is protected. | ControllerRecordingWindow.cs:112–113; ControllerRecording.cs:134–136; Help/ControllerRecording.md:27 |
| Medium | Record tooltip and recording messages refer only to controllers. | Record supports webcam-only and mixed capture, plus optional microphone audio. “Use the controller to perform,” the Undo text and “Recorded controller performance” are inaccurate for webcam-only takes. Use source-aware messages or “Record a performance.” | MainWindow.xaml:120; MainWindow.ControllerRecording.cs:133,209,229 |
| Medium | “Clean view resets the camera.” | It resets the camera for non-3D backgrounds. The 3D URDF stage retains its current camera. Qualify the statement instead of suggesting all clean views reset. | UrdfConfigWindow.Presentation.cs:54; RobotHeadView.Meeting.cs:49 |
| Medium | Meeting Help says backgrounds are provided by URDF Configuration. | These controls moved to URDF Scene Setup > Background. Correct the location so an operator can find them. | Help/MeetingMode.md:39; Help/UrdfConfiguration.md:78 |
| Medium | “Display updates remain at 30 fps.” | This conflates timeline target/pose refreshes with calibrated motion and rendering. The latter run at display cadence, up to 60 callbacks per second. Say which updates are 30 Hz. | SpeedCalibrationWindow.cs:66; Help/UrdfViewer.md:64–65 |
| Medium | Hide Group and audio trim Help both describe commands as hidden. | Hide Group is visual and its commands still play. Audio trim archives/excludes linked commands from playback. The Help explains the distinction, but “Scrub commands…” does not communicate it in the dialog. Prefer “Exclude linked commands before audio start/after audio end.” | Help/CommandGroups.md:14–18; Help/RecordingAudio.md:27; MainWindow.AudioTrim.cs:77 |
| Medium | “Commit” remains the audio trim button caption after committing. | Its next click unlocks the row rather than committing anything. Change the caption with state, for example Apply and lock / Unlock. | MainWindow.AudioTrim.cs:52,81–89 |
| Medium | “Back” in URDF Configuration. | It restores the values present when the dialog opened, without navigating or closing. “Revert changes” communicates this action. | UrdfConfigWindow.xaml:14; Help/UrdfConfiguration.md:74 |
| Medium | Robot framing yaw/pitch are radians in the library editor, degrees in Scene Setup. | Both are valid representations, but similarly named fields use different units. Prefer degrees in both user interfaces and retain radians only in the saved/API representation. | BackdropEditorWindow.cs:92; MainWindow.UrdfModel.cs:294–295 |

## Naming consistency

| Current variants | Recommended convention | Evidence |
| --- | --- | --- |
| Drive HW, Live Drive, Drive Physical Model, Live Head | Use one physical-output name, such as Drive Hardware, and call the mechanism the physical robot. Reserve model for URDF Model. | MainWindow.xaml:203; Help/LiveDriveHardware.md:1–3; FocusControlWindow.cs:37; Help/MovieTimeline.md:40 |
| Xbox / X-Box | Xbox everywhere in visible text. Keep the existing ControllerKind.Xbox identifier. | MainWindow.xaml:400; ControllerMappingWindow.cs:80; ControllerDiagram.cs:278; ControllerRecordingWindow.cs:10 |
| Servo Speed Calibration / Speed Calibration / Calibrate Speeds | Servo Speed Calibration for the feature and dialog; consistent action captions. | MainWindow.xaml:395; SpeedCalibrationWindow.cs:44; ServoConfigWindow.xaml:50 |
| Load Scene / Scene settings / Apply Backdrop | Scene or Background according to the object being edited. The nested draft editor should say Apply changes; the outer library dialog saves the file. | Models.cs:243; BackdropEditorWindow.cs:18,24; BackgroundLibraryWindow.cs:59–72 |
| Save As / Save as new | Save As… consistently for saving another named item. | MainWindow.xaml:374,376; BackgroundLibraryWindow.cs:72 |
| UnDock / undock / URDF Model window | Undock for the action; URDF Model window for the detached view. | RobotHeadView.cs:2758; Help/UrdfViewer.md:22 |
| Head,Body,URDF | Head & Audio / Body / Scene would distinguish the three lanes; body and scene are both rendered in URDF. | MainWindow.xaml:152; Help/Interface.md |
| InputMin, OutputMax, SmoothingMs and raw CamelCase control names alongside human-readable actions | Use readable display labels with units, such as Input minimum and Smoothing (ms), while retaining serialized/API names unchanged. | FaceTrackingWindow.cs:87; Models.cs:243; CommandEditorWindow.xaml.cs:722 |
| Default pose / Default Positions / Reset Defaults | State the scope: zero numeric controls, move to configured hardware defaults, or reset configuration settings. These actions do not have identical lighting/body/configuration effects. | Help/Controllers.md:162–164; Help/LiveDriveHardware.md:29–31; Help/UrdfConfiguration.md |

## Suggested correction order

First correct the independent-neck-spline tooltip, N/C/profile explanation,
webcam speed override explanation, recording processing scope, and webcam
recording messages. Then correct clean-view and background-location Help.
Finally standardize display names, button case and units. Avoid renaming stable
command enums, JSON fields or API target strings as part of a wording pass.

No application code, live Config, libraries or release packages were modified
during this audit. Verification used local source and Help; the running editor
was not operated.
