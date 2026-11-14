# Independent wrist and arm calibration — 2026-11-09

The calibration now has 21 steps. Wrist pitch and roll are measured one hand at a time, using separate opposite endpoints. A neutral pose uses upper arms down, elbows beside the ribs at 90 degrees, forearms forward and palms facing each other. Grip-specific pitch/roll axes are learned from controller rotation relative to the estimated forearm. Runtime mapping separates those axes so pure calibrated roll does not create wrist pitch, including with nonstandard grip orientations. Calibration rejects endpoints on the same side and roll axes that are too similar to the measured bend axis.

Left and right upper-arm twist each have inward/outward sweep steps. Keep the upper arm down and elbow beside the ribs, and sweep the bent forearm across/out from the body. The calibrated hand-position azimuth drives that side's ArmTwist joint; wrist orientation drives wrist pitch/roll. Upper-arm twist is an estimate from hand position, not direct elbow or upper-arm tracking. It is suppressed near a straight elbow, where the estimate is ambiguous. Unusual shoulder or elbow poses can still be ambiguous with only a headset and two hand controllers.

The target graphics are distinct: a side-view hinge for bending, an end-view circle and palm marker for wrist roll, and a top-view pivot/forearm sweep for arm twist. Gold highlights the active side. The calibration panel shows live alignment, measured movement angles and the arm-twist estimate. See VR-Calibration-Alignment-Help.md for all-step readiness and correction guidance. Finger steps retain the three-second preparation countdown after trigger release.

## Use

Open VR Follow / Record, then Start guided calibration · wrists / arm twist. Complete the new sequence, let it save, then stand upright with arms down and choose Follow / re-center. Existing calibration is not overwritten by installing this update. Older version-2 profiles load with their previous mapping and a recalibration reminder. Newly measured profiles save as version 3 with measured axes and arm-twist ranges in Config/VR/BodyTrackingCalibration.json; older software will reject them rather than silently interpret the axes incorrectly. Use the matching updated Player/Editor.

The other AI's recent scene pacing, reduced-allocation mesh upload, Follow problem display, smooth root interpolation, command queuing and drive catch-up changes are retained. The regular Desktop Editor is not deployed or changed by this VR package.

## Validation

Automated checks run the full sequence, test independent left/right controller-axis learning, pure roll versus pure pitch through the actual body solver, arm-twist ranges and straight-arm ambiguity, legacy-profile loading, profile serialization, finger countdown and existing Follow/recording behavior. Graphics are rendered from the same drawing routine used in VR. Physical controller calibration still needs validation on the gaming PC.

## Install

Close VR Player and VR Editor. Extract J5Studio-VR-Independent-Wrist-Arm-Calibration-update.zip over the existing portable VR folder, replacing files and preserving Editor/Shaders subfolders. The cumulative package is based on the latest Follow-Smooth-Drive update and retains its changes. Config, models and saved calibration/settings are not included or overwritten. Run guided calibration again to activate the new measurements.
