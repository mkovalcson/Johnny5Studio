# VR body calibration

Open the VR Editor with the full-body model. From the VR home menu choose **Follow / Record · Body movements → Start guided calibration**. The panel stays fixed in the room. Keep both controllers tracked and face the same direction throughout the measurement.

1. Stand upright with arms down, then raise straight arms overhead and hold them straight forward. For the 90° step, keep upper arms forward and point forearms up.
2. For the neutral arm/wrist pose, put your elbows beside your ribs, forearms forward and palms facing each other. Bend each wrist up and down, then rotate each palm down and up. Keep the forearm still for the wrist steps. For arm twist, keep the wrist straight and sweep the bent forearm inward and outward.
3. Capture open and closed hands. Pull and release the trigger, then use the three-second countdown to get your fingers into position. Thumb maps to thumb, index to upper finger, middle to lower finger. Ring and little fingers are ignored. Without skeletal finger data, arm and torso calibration can continue, but finger following is unavailable.
4. Crouch upright, then stand again and bend forward from the waist. These are posture references; physical walking and turning do not drive the robot's base.

Pull and release either trigger to arm each measurement. If a check is amber, correct the pose and hold still; the armed measurement retries without another trigger pull. Use **Repeat previous calibration step** to redo the preceding measurement; **Right B** cancels. The animated guide distinguishes bending the wrist, rotating the palm, and sweeping the forearm.

For wrist and twist steps, the triangle on each live gauge shows your current measurement; the green band shows the acceptable range. A wrist endpoint needs about 10° of movement. Paired endpoint directions must be about 104° apart or more. If the wrist roll axis looks too much like bend, straighten your wrist and turn your palm, then repeat the pair. Read the amber text for the measured value and the required threshold.

Calibration now estimates the wrist pivot from the neutral and wrist endpoint poses on each side. When the fit succeeds, it refits the arm from the wrist rather than the controller handle, so palm turns cause less unwanted elbow and shoulder movement. If a fit is rejected, that side keeps its grip-origin measurements and the feedback says so. Older profiles still load; repeat calibration to learn the wrist pivots.

The completed profile saves in **Config/VR/BodyTrackingCalibration.json**. Start Follow while standing upright with relaxed arms to establish your session reference. The headset and controllers estimate shoulders and elbows; they do not directly measure those joints.
