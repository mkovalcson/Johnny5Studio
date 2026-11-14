# Drive HW and Hardware

Drive HW sends editor movement to the physical Johnny 5 hardware. It is independent of the URDF viewer's Drive toggle.

Sequence playback initializes physical targets from the pose at the current cursor,
including parent/child overrides and the active speed profile. The URDF snaps to
that same starting pose; the physical mechanism travels to its targets under its
configured speed/acceleration limits. Enabled splines supply continuously updated
physical targets at the preview update rate, including Break Spline holds. Nod and
tilt use the same complementary piston mixer as face tracking. Hidden command
groups remain part of playback.

## Maestro servos

Logical servo values are mapped through Servo Configuration to Maestro ports and PWM Min/Default/Max values. Physical Direction and gang-relative Direction are combined so mirrored servos move correctly.

A command with Speed set to N/C sends only position. Default, Slow, Fast or Crawl sends the profile's Maestro Speed and Acceleration before the target. Ganged speed changes are sent to every Maestro child servo.

## Eye Pop

Left and Right Eye Pop use Tic controllers rather than Maestro PWM. Hardware Defaults commands both Eye Pops to zero.

## Arduino RGB

RGBCommand text is sent through the Arduino lighting path. Hardware Defaults sends `ClearAll`.

## Live controls

- Drive HW: enables/disables live physical driving from editor movement.
- Disable Servos: disables Maestro PWM so the servos go limp.
- Hardware Defaults: moves Maestro channels to configured Default PWM, resets Eye Pop and clears Arduino lighting.

Connection indicators for Maestro, Arduino and the two Tic controllers appear in Hardware. The robot-status button beside the Movie controls explains permitted outputs and blocking reasons.

## Configuration changes

Saving Servo Configuration applies changes immediately to subsequent physical movement. Port, PWM, direction, Speed and Acceleration changes do not require restarting the application.
