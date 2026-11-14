# Follow Smoothing and Input Scaling

While following, open **Left B → Smoothing / Input Scaling** and select a category. Head, torso, each arm and each hand have their own pages. Side labels refer to the robot's sides.

- **Smoothing** ranges from 0 to 300 ms. A higher setting dampens brief changes but adds some lag. Start with 30–60 ms if a control jitters. Zero leaves the input unfiltered.
- **Input Scaling** ranges from 0% to 200%. At 50%, a 20° movement from the reference stance requests 10° from the robot's neutral pose. At 200%, it requests 40°, subject to joint limits. 100% is normal; 0% requests the neutral position. Scaling leaves the neutral setting unchanged.
- **Crouch / forward bend** shapes the shared input before it is split into neck and torso movement. The neck still lowers first; chest and waist bend also have their own controls.
- Existing speed limits, joystick travel, and the assisted pointing pose continue to work as before.

Hold the trigger on a slider or use the right stick to step. Settings apply live and save automatically to **Config/VR/FollowInputTuning.json**. **Reset this section** restores defaults for the displayed adjustment only. Defaults are 100% input scaling and 0 ms smoothing. Calibration and neutral pose files are unchanged.

## Travel Extents

In **Left B → Smoothing / Input Scaling → Travel Extents**, choose a section and control. Adjust **Minimum** and **Maximum**, then select **Save to URDF Configuration**. The limits come from the editor's current URDF configuration and save to the same **Config/URDFconfig.json** used by the desktop settings. Back or Left B before Save drops the draft. The right stick steps by 0.1 in the displayed unit.

Follow stays live while you edit. Recording or playback must be stopped, and any unsaved configuration changes resolved before saving a range. The head controls retain their existing shared ranges, and each child's zero is only moved if necessary to stay inside the new endpoints. Body direction, speed limits, and Follow tuning settings are separate. If another editor changes the range, reopen Travel Extents before trying again.
