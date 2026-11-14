# Follow Me — arm and hand speed page

While following, press **Left B → Follow speed sliders…**. The first page now shows all six primary sliders together:

1. All fingers + thumbs · L/R
2. Wrist roll · L/R
3. Wrist pitch · L/R
4. Arm twist · L/R
5. Elbow · L/R
6. Shoulder rotate · L/R

Each ranges from 0.1× to 5× of the original speed limit and applies to both sides. The wrist, arm twist and elbow sliders existed previously further down the list; they are now immediately visible.

**All fingers + thumbs** sets every thumb and upper/lower finger joint to the chosen absolute multiplier, including curl, tip/end joints and spread. It does not multiply the previous individual settings again. It does not change wrist pitch, wrist roll, elbow or arm twist.

**Advanced finger speeds…** retains the individual finger-joint sliders. If those values differ, the main combined slider displays **Mixed** and its track represents their average. Moving it sets all finger joints to one speed, replacing the differing values. Existing individual settings are preserved until you adjust a slider.

**Head, body and travel speeds…** contains the remaining controls. Back from either secondary page returns to the main arm/hand page. Follow remains active while adjusting all these pages; joystick travel is paused for menu adjustment. Point and hold the trigger on a slider, or select its row and use right stick left/right for 0.1× changes.

Adjustments apply immediately and save automatically in **Config/VR/FollowSpeeds.json** using the existing per-joint keys. The combined slider is a UI convenience, so the VR Editor continues to receive the same supported per-joint speed settings. No bridge protocol or desktop Editor changes were needed.

Validation: Release publish succeeded. **57 focused Follow speed checks** and **52 Follow-controls checks** passed, including all-finger coverage, separate wrist/arm speeds, mixed-value handling, persistence, live packets and first-page placement. Full suite and headset testing were not run.
