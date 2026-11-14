# Webcam mapping and text corrections — 2.1.2

Implemented October 12, 2026 in the working source and Release executable.

## Webcam mapping rows

- Each row has a Speed profile: Default, Slow, Fast, Crawl, or N/C · Keep current.
  The selected profile follows the target through URDF preview, hardware output,
  and recording without changing the controller's selected speed.
- New rows use Default. Loading older files without Speed retains their previous
  N/C behavior. Saving adds the explicit per-row value without changing schema 2.
- Mixed head pitch and roll rows must select matching profiles because both
  address the same physical neck pair. Left/Right piston rows support different
  profiles. Eye Pop continues to use calibrated extension/retraction timing.
- Neutral displays up to five decimal places. Editing and saving retain full
  precision; opening/applying a mapping does not round the stored value.
- X removes its own row. + below the last row adds a disabled Default-speed
  mapping. The old top Add mapping and Remove mapping buttons are gone.
- Signal/Target choices and column headings use readable display names. Stable
  signal names, target strings and servo enum identifiers remain unchanged.
- Save errors remain visible instead of disappearing at the next tracking tick.

## High-priority audit corrections

- The command editor now describes independent Neck Nod and Neck Tilt spline
  curves, whose physical outputs mix into the two pistons.
- Webcam Help explains the selected row profile and N/C's retained profile,
  including the URDF's Default fallback when no profile is known.
- Servo Speed Calibration explains that webcam speed enforcement independently
  enables calibrated motion for its owned actuators, even with the general URDF
  calibration preference off. Both calibration tabs include the explanation.

## Naming conventions

- Drive HW consistently identifies physical-output permission in UI and Help.
- Xbox, Servo Speed Calibration and Undock use consistent names.
- The split timeline choice is Head & Audio / Body / Scene, wrapped to fit its
  existing toolbar width. Persisted layout values and indices stay unchanged.
- The nested background/scene draft editor uses Apply changes; the library's
  alternate save action is Save As….
- Hardware Defaults moves to configured physical defaults. The controller action
  is displayed as Zero numeric controls, retaining its legacy Default pose ID.
  Reset configuration defaults states the configuration reset's scope.
- Servo and child labels use readable names in command, mapping, recording and
  configuration views while JSON/API names remain stable.

## Verification

Release build succeeded. Isolated checks passed: webcam speed/mapping UI 41,
face tracking 17, controller recording 49, Servo Speed Calibration 289,
Help 109, scene setup 151, and general editor regression 6,697 assertions.
Shared controller speed checks passed 37 assertions; controller display and
collision safeguards passed 22 assertions.
Rendered mapping previews were inspected for readable dropdowns, five-place
Neutral display, row X placement and the bottom + control. Tests used simulated
face packets and never connected hardware or started the camera.

This change updates the working source and compiled executable. It does not
rebuild the deployment ZIP or sync the separate local repository release copy.
