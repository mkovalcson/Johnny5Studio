# Follow mode: controllers and tuning

In VR, open **Follow / Record · Body movements → Follow my movements / re-center**. Stand upright with arms relaxed and release the trigger while tracking starts. If the editor is busy, Follow waits up to 10 seconds: pause playback and close any editor dialogs. If the timeline or cursor changes during Follow, re-center before resuming.

- **Left stick:** drives Johnny forward/back and turns his base. **Right stick:** moves you forward/back and turns your view. There is no Move Me/Move Robot toggle. Your physical walking or turning does not move Johnny's base.
- **Left A:** toggles Mirrored/Unmirrored. **Left B:** opens or closes Selections. **Right A:** opens the regular menu. **Right B:** stops Follow or goes back in menus.
- With the laser accessory visible, click the left stick to raise or lower it. The left trigger previews its effect once it has lowered; this does not add a timeline command.
- The glove displays show the button labels and mirror state; the left glove includes local time. Pointer rays normally stay hidden while following and return for menu interaction.

Follow settings can use the editor's loaded 3D backdrop or hide it. The overhead status is hidden by default, but a tracking interruption can still display a status message. **Waist swivel from body tracking is off by default**: the timeline keeps control of it. Turn it on to let agreed headset and controller yaw swivel the waist without turning the base. The switch also applies during recording; arming the Torso group alone does not turn waist tracking on.

In **Left B → Neutral pose defaults**, set where each tracked joint rests in your measured stance. On a crouch or forward bend the neck lowers first, then the chest and waist bend. [Smoothing, Input Scaling and Travel Extents](VR-Follow-Input-Tuning.md) explain the separate adjustments. **Follow speed sliders** control how fast joints can respond; paired arm speeds apply to both sides, and the main page includes all fingers and thumbs plus separate wrist roll, wrist pitch, arm twist, elbow and shoulder rotation.

[Record and overdub](VR-Rehearsal-Panel.md) from the compact panel. Neck Turn/Nod/Tilt are one capture group, off by default; neck lift is part of Torso. [TipToe](UrdfTipToe.md) is a posed or keyed control and is not mapped to body tracking.
