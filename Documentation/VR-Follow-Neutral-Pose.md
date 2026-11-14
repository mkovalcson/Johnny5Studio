# Follow neutral pose defaults

## Using the feature in VR

1. Start **Follow / re-center** while standing upright in the same relaxed, arms-down stance used for calibration. This captures the measured reference for that Follow session; calibration measurements are not overwritten.
2. Press **Left B**, choose **Neutral pose defaults**, then **Torso**.
3. Set **Neck lift** to the resting height you want. The far right is the modeled upper limit. Set **Chest bend** and **Waist bend** to the posture you want while standing in your measured neutral stance.
4. Crouch or bend forward to try it. Neck lift uses the first part of the movement to travel from that default to its bottom. Chest and waist bending follow after that. The VR Editor also waits for the actual speed-limited neck to arrive before allowing further forward torso bending.
5. Use **Head**, **Left arm**, **Right arm**, **Left hand**, and **Right hand** to adjust other tracked control defaults. Follow stays live while these pages are open; joystick travel is held while tuning. Left B closes; Right B stops Follow.

The 37 individual defaults cover neck lift, chest/waist bend and waist swivel; head turn/nod/tilt; shoulder rotation, arm twist, elbow, wrist pitch/roll for each arm; thumb and upper/lower finger joints for each hand. The disabled shoulder-hug motion, manual laser position and joystick-controlled robot travel are not included.

Values for body joints are shown in degrees; the three head controls use editor percentages. Asterisks mark custom settings. Sliders support pointing/trigger dragging and right-stick fine steps. Reset this section / Reset all removes custom defaults and restores the measured mapping.

Left/right means the robot's own sides. The defaults stay on those robot sides when Mirrored / Unmirrored changes. Tracking movement is measured relative to the corresponding reference stance and added to the chosen default, with joint limits and existing Follow speed multipliers still enforced. Neck lift instead scales its available downward travel so it can reach the full lower endpoint even from a high default.

The finger-point assist retains its explicit straight pointing finger and aligned wrist pose while pointing. Normal following resumes with the selected defaults when pointing ends.

## Saved files and compatibility

Defaults save automatically to **Config/VR/FollowNeutralPose.json**. Body values in this file are radians; head controls use native percentages. Keys refer to individual robot controls. Missing entries use the measured default; invalid keys/nonfinite values are discarded on loading and numeric values clamp to modeled ranges.

Calibration measurements remain in **Config/VR/BodyTrackingCalibration.json**. Speeds remain in **Config/VR/FollowSpeeds.json**. Existing calibrations do not need to be repeated for this feature. New offsets do not alter hardware calibration, sequence documents, or already recorded commands; future recording/overdubbing captures the resulting speed-limited pose through the existing recording path and armed control groups.

Forward bending now has the same neck-first phase as crouching. Approximately the first 35% of measured lowering is allocated to neck travel, after the standing sway dead zone. Body bending then uses the remainder. Modeled/configured joint limits and actual neck speed are respected. A narrower neck range cannot indefinitely block the torso.

The motion calculations and default catalog live in **Shared/Pose**. Live VR settings/menu integration is in **Johnny5VR**; the actual-joint sequencing gate is in **Johnny5VREditor**. Desktop playback behavior is unchanged.

## Validation

56 focused checks cover all slider endpoints, reference-relative mapping, robot-side mirroring, limit clamping, neck-first crouch and bow, actual-neck gating including configured range limits, reset, and persistence. The existing focused Follow control checks are also run. Release builds are produced; full regression and headset testing are not run.
