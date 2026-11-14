# URDF body controls

Config > URDF Configuration has separate Current Head configuration and Body show/hide sections. Use Config > URDF Scene Setup > Robot > Change Robot Model… to load the full-body johnny5_head.urdf and display these joints. A head-only URDF has no body joints to move.

The 38 independent body controls and two hand gangs begin with B_. They appear at the bottom of the Command Editor Control or Action picklist and after the ordinary controls/actions in both controller mapping target lists. They are unavailable to face tracking and are never sent to hardware, including when Drive HW is on. Hardware animation exports omit them; editable sequences preserve them.

Each command uses −100 to +100, except B_PanelsOpen, B_LaserLower and the hand gangs, which use 0 to 100. Zero is the model's as-built joint position. Negative and positive endpoints map separately to the configured minimum and maximum travel; the Body section shows those angles in degrees. You can override both endpoints beyond the URDF's declared limits: enter finite values with minimum less than maximum. If the entire range is above or below zero, command 0 uses the nearest endpoint. Reversed swaps input direction. The renderer honors configured ranges except for the marked full-body model's coupled finger/thumb closing stops. Automatic neck-linkage followers retain their mimic ratios throughout the overridden motion.

Default travel follows the supported body model, including the hand closing stops described below. **Restore model range** resets one control's endpoints. Save Default persists travel and reversal in URDFconfig.json. Back restores the opening settings, while Reset Defaults restores all model ranges. Preview sliders change only the URDF; create timeline commands to retain a motion.


**B_TipToe** raises the rear of both tracks while the caster linkage swings down. See [TipToe and caster linkage](UrdfTipToe.md) for its range and how to animate it.

## Full Body Pose

Enable the existing **Pose** mode in the URDF view. When the loaded model contains body joints, a collapsible **Full Body Pose** panel appears at the upper right. It uses the existing pose-slider style and groups the available controls into torso/neck lift, waist panels and laser, left and right arms, and left and right hands. Each slider has a numeric readout and a reset button; hover its readout for the mapped angle in degrees. **Reset body pose** resets the body controls to command 0 without changing the head pose. The panel scrolls within the viewport and hides when Pose mode is off or the model has no supported body joints.

Inserting the current pose into the timeline or saving a Library Pose includes the body positions. Loading that pose restores them, and the pose draft follows docking/undocking. Body controls remain URDF-only even when hardware drive is enabled.

Unsplined commands change immediately. Enable Spline in Edit Commands for smooth interpolation between body commands. Break Spline holds the starting value until the next command. Existing timing, grouping, hiding and undo behavior apply.

## Body control families

Left and Right variants are paired in picklists and configuration. Green icons identify Body controls.

- WaistLean, ChestTilt, WaistSwivel and NeckLift: torso and neck linkage. WaistSwivel turns the upper body on the waist swivel, just below the body hinge: positive turns it to the robot's left (anticlockwise seen from above), up to ±90°.
- **B_ShoulderRotateLeft / B_ShoulderRotateRight**: the shoulder hub rotation controls, using their original lateral hub axes. Travel is -1 radian (-57.30 degrees) to +205 degrees (3.577925 radians), allowing both shoulder housings to reach the raised vertical position in the default body pose. Former stock upper calibrations upgrade to the extended range; custom extents are retained. These remain independent controls.
- **B_ShoulderHugLeft / B_ShoulderHugRight**: NEW, separate hinges between the yoke indentations, around the upright shoulder axes. Travel is limited to -0.5 to +0.5 radians (about ±28.65°), matching the yoke clearance. Rotate and Hug are not aliases; each can be keyed, splined, posed, or mapped independently. ArmTwist also remains independent. The separate sideways ShoulderRaise axis remains disabled.
- ArmTwist, Elbow, WristPitch and WristRoll: arm and hand orientation.
- FingerSpreadUpper/Lower: independent finger separation.
- FingerCurlUpper/Lower and FingerTipCurlUpper/Lower: the existing base and middle-joint bends.
- FingerEndCurlUpper/Lower: the new independent outer fingertip bends. Left and Right variants appear alongside the corresponding finger controls in commands, controller mappings, and Full Body Pose. The default range is -0.30 to +0.007600 radians; negative opens the tips and positive closes them. The center-plane safeguard considers the base, middle, and outer bends together. Existing FingerTipCurl commands retain their original middle-joint behavior.
- ThumbCurl: the opposing digit's base bend.
- ThumbTipCurl: the thumb's independent outer hinge, available on the detailed-hand model. Use B_ThumbTipCurlLeft or B_ThumbTipCurlRight in commands, controller mappings, and the API. The Full Body Pose panel also includes these controls. Negative values open the tip; positive values close it. The default range is -0.30 to +0.006650 radians; the small positive range reflects the nearly closed default hand. The model's center-plane safeguard constrains base and tip closure together.

Use the displayed degree limits and circular range controls in URDF Configuration. Native timeline/controller values remain -100 to +100, not degrees.

## Waist panels, LCD, laser and toolbox

- **B_PanelsOpen** (0 to 100): both blue waist panels swing open together on their outer hinges, showing the curved LCD between them. 0 is closed and 100 fully open (about 100 degrees).
- **LCD Show** (red command icon): what the LCD shows from that time on: text in a chosen colour, or a picture (contain, cover or stretch). With no LCD Show it reads "If you can read this, you should have asked me out on a date first!".
- **B_LaserLower** (0 to 100): the laser hinge behind the left shoulder. 0 is up at 70 degrees; 100 is lowered level, resting on the shoulder. The toolbox rides the same hinge.
- **Toolbox Swap** (red command icon): true shows the red toolbox on the hinge, false the laser. With no accessory command, URDF Configuration's **Default accessory** applies: None (initial default), Toolbox, or Laser. An explicit swap shows its selected accessory unless Hide Laser is active.
- **Fire Laser** (red command icon): a burst of glowing red bolts from the laser's firing window, flying straight out along the laser into the URDF scene, with a muzzle flash and a laser sound per bolt: a synthesized "pew", a medium blast, a heavy, guttural blast, none, or your own audio file. **Build…** sets the number of shots, interval, speed, range, bolt length, colour and sound. It fires only while the laser itself shows: add Toolbox Swap = false first to select and show it. Aim with B_LaserLower, B_WaistSwivel and the chest; each bolt keeps the direction it left in.
- **Hide Laser** (red command icon): true hides the whole assembly (support, laser and toolbox), so it costs nothing to draw; false shows it. With no Hide Laser it is shown.

These are display-only, like the other body controls. B_PanelsOpen and B_LaserLower appear in Full Body Pose under **Waist panels and laser**. The red commands take effect at the cursor: the latest enabled one at or before it decides. Controller buttons can also preview **Laser Show / Hide**, **Toolbox / Laser Swap** and **Fire Laser** (see Controllers). The API inserts them with `insert_lcd_show`, `insert_hide_laser`, `insert_toolbox_swap` and `insert_fire_laser`.

## Disassemble command

Add **Disassemble** in Edit Commands to make the full-body robot shudder and collapse into its separate parts on the floor. **Build…** chooses the direction: forwards falls apart and stays in pieces; reverse starts in pieces and reassembles, then normal servo control resumes. The text value is `{"reverse":false,"speed":1}`; speed ranges from 0.1 to 10. The clip lasts about 3.5 s at normal speed. The robot blends from its current pose during the shudder, so it does not snap. Seeking backwards is exact.

The effect needs the baked clip `Meshes/Body/FallApart/fall_apart.json` beside the loaded model. Regenerate it with `FullBodyModel/Body/bake_fall_apart.py` after changing the model. It is display-only and never drives hardware.

## Open or close a whole hand

Use **B_OpenLeftHand** or **B_OpenRightHand** in Full Body Pose, URDF Configuration, a timeline command, a controller mapping, or the API. Each slider runs from **0% (closed)** to **100% (fully open)**. It moves the finger spread axes, all three bends of each finger, and both thumb bends together through their configured travel. Closing safeguards still apply. These are virtual ganged controls, not additional URDF joints or physical outputs.

Individual joint controls remain available. A later individual timeline command overrides that joint after a hand-gang command; at the same time, individual commands take priority. Saved poses store the expanded joint values. The hand slider shows the average opening after individual edits. Its endpoints use the child joints' configured ranges, so it has no separate angular calibration.

NeckLift now allows an additional 25 degrees of backward spring rotation: its default angular travel is -20.05 degrees forward to +45.05 degrees backward relative to the built pose. The upper box follows level and the front follower adjusts to the lower socket. Stock saved backward limits upgrade with the next build; deliberate custom limits are retained.

In the URDF view’s **Joints** panel, all four native joints appear separately: **ShoulderRotateLeft**, **ShoulderRotateRight**, **ShoulderHugLeft**, and **ShoulderHugRight**. Animation Editor commands, controller mappings and API targets use the corresponding **B_** names. Reload the updated URDF model to refresh its joint list. Rotate and Hug use independent range settings.
