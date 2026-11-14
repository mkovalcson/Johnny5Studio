# Controller toggle audio feedback — 2026-11-08

Available in the VR A menu > Body calibration and motion following > Controller feedback, and in the Player desktop sidebar > Controller feedback.

Options: Sounds / Voice / Off. Default Sounds, volume 40%. Independent volume slider and four previews in both locations. Changes apply and save immediately to Johnny5VR/Settings/viewer.json (ControllerConfirmation and ControllerFeedbackVolume). Existing settings files inherit defaults. Saved preferences are separate from Config/body calibration.

Sounds are synthesized locally: Mirrored rising bright clicks; Unmirrored falling bright clicks; Move Me pneumatic hiss-click; Move Robot low servo-clunk. Sounds last 180 or 260 ms. Voice uses the installed Windows SAPI voice at a brisk rate to name the new selection. Uses current Windows/default audio output. No audio files/downloads required. Off or zero volume cancels queued audio. Confirmation audio is independent of sequence speech and mouth amplitude.

Follow buttons emit exactly one notification per rising edge, naming the newly selected state. Disabled/focus-blocked buttons emit none. Audio rendering/playback runs on its own background STA worker, not the render loop. VR volume supports pointer-trigger dragging and right-stick X adjustment while its row is selected. The desktop preference window provides the same choices, updates when the VR menu changes, and preserves pending scene edits.

Source: ControllerFeedback.cs; ViewerWindow.Feedback.cs; ViewerSettings.cs; VrBodyMotion.cs; ViewerWindow.cs; ViewerWindow.BodyMotion.cs; VrMenu.cs. Tests: ControllerFeedbackChecks.cs and BodyMotionChecks.cs.

Validation: clean Player build/publish; 3927 Player checks passed, including distinct bounded cue lengths, settings serialization/defaults, toggle callback edges and slider coordinates. Desktop settings and VR menu screenshots inspected for clipping; four SAPI voice phrases rendered silently to WAV files successfully. Actual headset audio output/balance remains to be tested on the gaming PC.

Package: Releases/VR/0.9.0/J5Studio-VR-Controller-Feedback-update.zip. Requires the November 8 cumulative 0.9.0 base; includes subsequent Follow Controller Modes and Point Gesture changes. Close both VR applications and extract into the folder containing Johnny5VR.exe. No Config/Settings/models or Desktop Editor files replaced.
