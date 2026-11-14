# J5 Animation Editor & Player

The Windows editor combines timed commands, audio, controller/webcam recording, URDF scenes and movie cues. Live Drive optionally sends physical commands to Johnny 5 hardware.

## Current user documentation

The maintained operator documentation is in [Help](Help/GettingStarted.md) and ships beside the executable. Stop playback completely and press F1 for context help.

- [Workflow guides](Help/StepByStep.md): step-by-step tasks.
- [Commands and playback](Help/SequenceEditor.md): cursor starts, splines and audio timing.
- [Scenes and placement](Help/UrdfFraming.md): robot, background and camera.
- [Save and export](Help/Saving.md): editable files, hardware samples and reusable items.
- [Troubleshooting](Help/Troubleshooting.md): symptoms and recovery.
- [API connection](Help/EditorApi.md) and [AI operator reference](Help/AnimationEditor_API_For_AI.md): automation.

Sequence playback starts at the cursor. Nod and tilt combine through the neck mixer used by face tracking. Break Spline holds the starting value until the next point. Hide Group only affects display; its commands still play.

## Build and run

Use the .NET 10 SDK on Windows. From X:\Johnny5Studio\ServoAnimator:

```powershell
dotnet build ServoAnimatorFaceTracking.slnx -c Release
./ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.exe
```

Ship the executable with its supporting Help, models, meshes and runtime dependencies. Config > Set Paths shows the active Configuration folder. External robot/scenery files must retain their relative mesh folders. The solution README documents face-tracking setup.

ServoAnimator.Checks provides regression verification. Hardware timeline checks evaluate output targets without connecting the robot.

## Maintaining documentation

HelpIndex.json supplies titles, categories, keywords and related links. Keep IDs stable because F1 bindings depend on them. Put task steps in workflow guides and field details in reference pages. Use current UI labels and distinguish native command units from physical angles and graph normalization.

The previous mixed manual/changelog is preserved in [the October 8, 2026 history snapshot](../internalDocumentation/ANIMATION_EDITOR_HISTORY_2026-10-08.md). Its retired workflows are historical material, not current instructions.
