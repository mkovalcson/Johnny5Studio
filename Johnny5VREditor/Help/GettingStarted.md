# Getting Started

- [Desktop Editor system requirements](DesktopSystemRequirements.md)
- [VR Player system requirements](VrSystemRequirements.md)

J5 Animation Editor combines editable motion commands, audio, controller/webcam capture, and a URDF preview. Physical hardware output is a separate option.

## Choose your workflow

- **Build an animation:** [Create and edit an animation](FirstAnimation.md).
- **Puppeteer and record:** [Map inputs and record a performance](CaptureGuide.md).
- **Record or process speech:** [Recording Audio](RecordingAudio.md) and [Trim and replace speech](AudioGuide.md).
- **Create a full-body scene:** [Build a scene and animate the robot](SceneGuide.md).
- **Present live in Desktop:** [Present through OBS or Teams](MeetingGuide.md).
- **Work in VR:** [Open the VR Editor and use body tracking](VrGuide.md).
- **Automate edits:** [Connect to the editor API](EditorApi.md).

## Three things to distinguish

A **Sequence** contains commands and audio on one timeline. A **Movie** orders Sequence files into cues. The **Library** stores reusable poses and sequences.

In Desktop, **URDF Drive** controls the preview and **Drive HW** controls physical output. The VR Editor sends its 3D preview to the VR Player. Green Body icons and red URDF/TV icons identify virtual-only controls; they are not physical servo commands.

**Record Position** creates commands. **Save Scene** stores robot, background, placement and camera together. **Save Sequence** and **Save Movie** save their animation documents. See [Save, export and reuse your work](Saving.md).

Sequence playback starts at the cursor. Move it to zero for a full replay. Spline enables interpolation; the legend hides only the graph. Break Spline holds a value until the next point. Hide Group hides commands visually while they continue playing.

## First setup

1. In Desktop, review the active Config folder under **Config > Set Paths**. In VR, choose the Config folder in the VR Player before opening the VR Editor.
2. Open or create a sequence. In Desktop, leave **Drive HW** off and **URDF Drive** on to preview the animation.
3. Use **Config > Reset Screen Layout** if the timeline or panels are inaccessible.

## Find help

Stop playback completely, then press **F1** over the relevant control or inside a settings window. Help is unavailable while running or paused. Search by a task such as “TV”, “overdub”, “Skip to”, “Expo”, “Spin”, or “background arrows”. Related-topic links appear at the bottom of each page. Check **Show only this application's topics** to narrow contents and search results. Clear the checkbox to browse the full reference. The line above each topic's heading lists the J5 Studio apps it applies to.
