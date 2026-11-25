# VR Player and VR Editor

- [VR Player system requirements](VrSystemRequirements.md)
- [Desktop Editor system requirements](DesktopSystemRequirements.md)

Use **J5 Studio VR Player** to enter SteamVR. The Player can play saved movies and sequences by itself. Open **Editor → Open / resume VR Editor** to edit a timeline or use body tracking. Select the same Config folder used by J5 Studio Desktop to share models, scenes, and sequences. Use matching Player and Editor builds when installing an update.

These guides are also available in the Desktop Editor for reference. The headset controls and live body calibration run in VR.

- [Body calibration](VrCalibration.md): guided poses, alignment gauges and wrist pivot.
- [Follow controls](VrFollowing.md): controller mappings, neutral pose, tuning and travel extents.
- [Rehearsal and recording](VR-Rehearsal-Panel.md): record or overdub, replay a draft, keep or discard.
- [TipToe](UrdfTipToe.md): pose or animate both tracks and the caster linkage.
- [Prop library](URDF-Prop-Library.md): add URDF objects to a saved 3D backdrop.
- [Clean Output](VR-Clean-Output.md) and [overlay capture](VR-Clean-Output-Overlays.md): route the preview to another display for OBS.
- [Tracking and timing diagnostics](VrDiagnostics.md): what to collect when movement stutters or calibration stalls.

## Checking your build

In the VR Player desktop sidebar, **Build information** shows the release,
source build identity, protocol and application location. Both editors show
these details in **Help → About**. The Player checks the matching Editor before
opening it. Development shortcuts rebuild the solution and launch its current
outputs. Portable releases use their bundled Editor.

## Expressive movie idle

In either editor, right-click a movie sequence and choose **Expressive idle after
sequence**, then a mood. The hourglass replaces the loop badge. That sequence
plays once and waits for **Next**, with subtle eye, head and flap movement.
Save the movie to keep the setting. The VR Player uses the same saved moods.
During standalone idle, Pause idle holds the current pose and Resume idle
continues it. Next advances separately. Next on the final block keeps idle
running; Stop ends it.

## Camera shots for Clean Output

Add [Camera Shot commands](CameraShots.md) in either editor to choose the framing for Desktop playback or VR Clean Output. VR headset views follow the headset.
