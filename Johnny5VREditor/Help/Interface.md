# Editor Interface

The main window contains Commands, the URDF preview, Sequence timelines, Hardware, and the Movie Timeline. Use **Config > Reset Screen Layout** to recover an inaccessible pane or detached view.

## Commands and mappings

**+ Add**, **Edit** and **Delete** work at the Sequence cursor. Rows show control, Spline, Break Spline, Speed and Value. Double-click a row to edit its time group.

An enabled, connected controller can replace the docked command list with its active mapping. Disabling the controller restores Commands. When the URDF is undocked, Commands returns and the mapping can appear beside Hardware.

Green icons identify virtual body controls; red icons identify scene and presentation actions. Neither drives physical actuators. See [Commands pane and Pose editing](ServoGrid.md).

## Preview and Hardware

The URDF provides Drive, Collision Warning, Pose, camera and docking controls. URDF Drive governs the virtual model. **Drive HW** in Hardware governs physical output independently.

The Hardware strip stays in the main editor when the URDF is undocked. Connection indicators show device availability; controller buttons enable input. Connected, enabled and permitted to output are different states. Focus Control sets background permissions. Collision Safeguard can block controller-origin movement independently of visible warnings.

Collision Safeguard sits above the Maestro, Arduino and Tic device-status area.
Open Focus Control from Config. See [Drive HW and Hardware](LiveDriveHardware.md)
and [Focus Control](FocusControl.md).

## Sequence transport and timelines

The Sequence timeline shows waveform, audio handles, triangles, splines and cursor. Choose **Combined**, **Head & Audio / Body / Scene**, or an individual **Head & Audio only**, **Body only**, or **Scene only** view. The split view separates head/audio, virtual body and scene channels on one synchronized time scale. Individual views focus the editor without changing playback.

Zoom −, + and Fit precede transport controls. **Sequence** starts at the current cursor; Pause and Resume show its active state. Move the cursor to zero for a complete replay. Playback speed is a percentage; Volume changes audio playback. Audio +seconds reports elapsed timeline time since the most recent audio handle.

Record opens take configuration; Meeting opens live presentation configuration. Colored Show Group buttons restore hidden commands visually; those commands still play while hidden. See [Sequence Editor and Audio Timeline](SequenceEditor.md).

## Movie and status

Movie blocks reference Sequence files and have their own transport, zoom and cursor. Descriptions have an edit link; an asterisk marks unsaved documents.

The robot-status button beside the Movie controls shows the active input and permitted outputs. Click it for connections, blocking reasons and recent messages. HW ON means output is permitted and connected; it does not mean an actuator is moving. See [Movie Timeline](MovieTimeline.md).

## Layout and Help

Drag dividers to resize panes; the docked URDF's bottom-center handle changes its height. Window bounds, splitter positions, dock state, timeline layout, and camera orientation/zoom are retained in EditorLayout.json.

Closing a detached URDF docks it back. Use **Dock URDF** in the Commands header when it is inaccessible.

Stop playback completely, then press **F1** with the relevant control focused. Help is unavailable while running or paused. Contents are grouped by workflow; search ranks results across the groups. Start with [Workflow Guides](StepByStep.md).
