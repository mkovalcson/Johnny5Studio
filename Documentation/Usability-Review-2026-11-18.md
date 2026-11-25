# Prioritized behavior and usability review

Reviewed November 18, 2026 against the working checkout. These are open findings,
not authorization to implement fixes. Source inspection covered Desktop/VR movie
playback, recovery, Follow hand input, camera shots, launchers and version identity,
plus the Face Tracking Apply workflow. Live headset and physical hardware
reproduction were outside this review. P1 means address before the next release,
P2 means address soon, and P3 means a useful refinement.

## P1 — Resolved in the local 2.17.0 build

Items 1–4 below record the original findings. The local fixes use coordinated
launchers, source/protocol compatibility validation, complete trigger recovery,
and explicit idle Pause/Resume and Next behavior. See the root CHANGELOG.md.
Live headset testing is pending.

1. **Development launchers can run an older app while the new source has built successfully.**
   The root VR shortcuts launch the runtime-specific win-x64 output. Current VR
   changes are built in the ordinary Release output, and the Player opens its
   published packaging/editor-2.16.1 copy. The runtime-specific Player and that
   published Editor are dated November 14; ordinary Release builds are dated
   November 17. This can make a new feature appear absent or produce a bridge
   mismatch. Provide one coordinated build/launch path and display build identity
   and executable location in About.
   Evidence: [VR Editor shortcut](../J5%20Studio%20VR%20Editor.cmd),
   [editor selection](../Johnny5VR/ViewerWindow.Editor.cs).

2. **A matching version stamp does not currently guarantee a matching build.**
   The working checkout still records 2.16.1 although the published 2.16.1 packages
   predate Follow hand controls, camera shots and expressive idle. The camera
   change also advances the scene protocol to 12. Player startup compares product
   versions, which can match across those incompatible builds. Allocate a new
   version before distributing these changes and add build/protocol identity to
   the compatibility check.
   Evidence: [version record](../ServoAnimator/Build/PROJECT_VERSION.json),
   [startup comparison](../Johnny5VR/ViewerWindow.Editor.cs),
   [scene protocol](../Shared/VR/SceneWire.cs).

3. **Recovery omits assigned movie sequence triggers.**
   Both EditorRecoverySnapshot.Save implementations copy sequence pathname,
   description, duration, looping and idle mood, but omit Trigger. Recovering
   unsaved movie work can therefore lose the keyboard cues the user assigned.
   Include all movie block metadata in recovery and verify a save/recover round trip.
   Evidence: [Desktop recovery](../ServoAnimator/ServoAnimator/EditorRecoverySnapshot.cs),
   [VR Editor recovery](../Johnny5VREditor/EditorRecoverySnapshot.cs).

4. **VR idle transport can do something different from its button label.**
   Expressive idle keeps the Player clock running, so its VR playback menu says
   Pause. PlaybackPlayer.Toggle instead calls Next while idling. On the final
   block, Next stops the clock but leaves the idle flag set, freezing the motion
   while still reporting an idle state. Decide on explicit Pause/Resume Idle and
   Next behavior and use it consistently across the menu, transport and desktop buttons.
   Evidence: [Player transport behavior](../Johnny5VR/Playback.cs),
   [VR menu label](../Johnny5VR/ViewerWindow.VrTasks.cs).

## P2 — Address soon

5. **Manual finger Point does not align the wrist as requested.**
   Hand presets change finger/thumb targets. Manual mode supplies null finger
   samples to the body solver, which removes its automatic point/wrist alignment.
   Wrist pitch and roll continue to follow the controller. The finger can therefore
   point with the wrong hand orientation instead of extending straight from the
   wrist with the lower finger underneath. Add the explicit preset wrist alignment
   and define its release behavior.
   Evidence: [manual presets](../Johnny5VR/FollowHands.cs),
   [solver input routing](../Johnny5VR/VrBodyMotion.cs),
   [tracked point alignment](../Shared/Pose/BodyTrackingSolver.cs).

6. **Idle ends by returning to the authored ending pose, which can make Next look like a reset.**
   Idle modifies the view and may send hardware commands, but leaves timeline rows
   at the authored endpoint. CaptureMovieCarryPose reevaluates that endpoint and
   EndMovieIdle restores it. The next sequence therefore inherits the authored
   pose rather than the currently visible idle pose. This conflicts with the
   documented expectation that movie transitions retain the preceding physical
   pose. Capture the actual idle pose, or blend deliberately into the next cue.
   The visible amount of movement needs a live check.
   Evidence: [idle termination](../Shared/EditorUI/MovieExpressiveIdle.cs),
   [movie carry capture](../ServoAnimator/ServoAnimator/MainWindow.xaml.cs),
   [movie continuity help](../ServoAnimator/ServoAnimator/Help/MovieTimeline.md).

7. **Tight-shot headroom uses stock dimensions instead of configured movement limits.**
   TightEnvelope uses fixed offsets around the current neck reference. Both camera
   paths also refit current visual bounds as the robot articulates. This does not
   establish the requested stable frame covering the full configured head/flap
   movement range. Custom limits or a changed model can exceed the envelope, and
   changing bounds may alter the zoom. Derive and cache an envelope from the
   model/configuration, then validate extremes and a fixed composition.
   Evidence: [fixed envelope](../Shared/Animation/CameraShots.cs),
   [editor framing](../Shared/EditorUI/CameraShotEditor.cs),
   [standalone framing](../Johnny5VR/AnimationRig.CameraShots.cs).
   Framing severity needs rendered verification.

8. **Changing stick modes while a stick is deflected immediately reuses that input.**
   Switching to Hand can immediately open/close a hand or select a Point/Fist.
   Switching back can immediately move Johnny or the viewer. Add a center-to-arm
   gate after switching and an indicator such as Center sticks to continue.
   This is a proposed usability improvement; the current routing directly exposes
   the existing stick values on the frame of the switch.
   Evidence: [mode and movement routing](../Johnny5VR/VrSession.cs),
   [hand update](../Johnny5VR/FollowHands.cs).

9. **The trigger's current action is hidden when the laser changes state.**
   A trigger fires only when CanFireLaser is true; otherwise it toggles Hand/Movement.
   While the visible laser is lowering or raising, a press changes joystick modes.
   With the laser down, a user must raise it before using a trigger to change modes.
   This follows the requested conditional mapping, but the gloves show the mode
   without identifying the trigger's current action. Show Trigger: Fire laser,
   Switch mode, or Laser moving, and consider reserving presses during travel.
   Evidence: [trigger routing](../Johnny5VR/VrSession.cs),
   [laser readiness](../Johnny5VR/VrBodyMotion.cs),
   [glove display](../Johnny5VR/FollowControllerLabels.cs).

10. **Resolved locally — Face Tracking preview checkbox applied unrelated pending edits.**
    Checking Drive URDF preview calls Apply(), which applies the whole draft.
    Someone adjusting mappings and then enabling the preview can apply those
    mappings before choosing the main Apply button. Make the checkbox's effect
    explicit, or give preview enablement its own operation.
    Evidence: [Face Tracking window](../ServoAnimator/ServoAnimator/FaceTrackingWindow.cs).
    The VR Editor has the same window implementation.
    Resolution: preview now has its own callback and retains the last applied
    mappings. Apply and Save remain explicit actions. Both editors use the new
    behavior.

## P3 — Useful refinements

11. **Wide-shot orientation depends on a particular back-wall link name.**
    The editor recognizes backdrop_wall_back_link. A custom backdrop without
    that name falls back to its X direction, which may give a side-on shot.
    Stage Front offset helps, but requires knowing the wall detection convention.
    Add a scene-level Pick back wall / Set stage front action and show which
    reference the Wide shot is using.
    Evidence: [wall identifier](../Shared/Animation/CameraShots.cs),
    [editor detection](../Shared/EditorUI/CameraShotEditor.cs),
    [standalone detection](../Johnny5VR/SceneData.cs).

When addressing an item, update this document with the resolution and relevant
verification. Add completed user-visible changes to the root CHANGELOG.md.
