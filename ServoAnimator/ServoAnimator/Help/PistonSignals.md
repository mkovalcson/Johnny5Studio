# LPiston and RPiston

These derived inputs combine head pitch and roll before the ordinary mapping row pipeline. Pitch moves both pistons together; roll moves them oppositely. +1 means up and −1 means down; adjust direction with Reverse pitch, Reverse roll or the row Reverse flag as needed.

## Setup

Use Add piston mappings in Webcam Face Tracking. It adds missing rows disabled, with input −1..+1, output −100..+100, smoothing 200 ms, and these targets:

- LPiston: Child:NeckNodUp:NeckTiltLeft
- RPiston: Child:NeckNodUp:NeckTiltRight

Open **Advanced neck calibration** to add piston mappings. Disable the existing parent pitch/roll mappings before enabling these rows. **Apply** activates the change for this session; **Save configuration** also persists it. Other or swapped targets are allowed with a warning. An enabled piston row alongside a NeckNodUp or NeckTiltRight parent row blocks Apply and identifies the conflicting rows.

## Piston settings

Pitch range and Roll range are positive degrees corresponding to full normalized input; both default to 20. Pitch gain defaults to 1.0 and Roll authority to 0.5; both range from 0 to 1. Reverse pitch and Reverse roll invert their respective inputs. Lower Pitch gain to reserve more travel for tilt without changing your comfortable angular range.

After subtracting stored neutral, pitch/range and roll/range are limited to ±1 and reversed if requested. Common = Pitch gain × normalized pitch. Differential = Roll authority × normalized roll. Raw left = common − differential; raw right = common + differential.

Clip mode defaults to PreserveTilt. It shifts both sides together into ±1, preserving their difference and sacrificing pitch; if that cannot fit, it clamps each side. Independent simply clamps each side, which can reduce tilt. The readout indicates piston clipping, including input-range saturation.

At defaults, full pitch alone gives +1/+1. Full positive roll alone gives −0.5/+0.5. Full positive pitch plus roll gives 0/+1 with PreserveTilt, or +0.5/+1 with Independent.

Valid piston-setting changes affect an active URDF preview without restarting the camera. Apply activates them for the session; Save configuration persists them. Range edits commit when the field loses focus; invalid ranges retain the previous value. Settings changes preserve neutral. Per-row ranges, dead zone, smoothing and reversal still apply afterward. Output retains the normal calibrated speed and collision-safeguard path.
