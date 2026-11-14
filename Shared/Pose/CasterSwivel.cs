namespace ServoAnimator;

public static class CasterSwivel
{
    // Forward and turn motion must cover the same time interval. The rear caster
    // sees both longitudinal travel and lateral travel caused by rotation.
    // Target/Advance steer a caster with no trail (wheel under the swivel), which
    // can roll either way, so it never turns more than 90 degrees.
    public static double Target(double currentTarget, double forwardMotion, double turnMotion, double rearDistance)
    {
        if (Math.Abs(forwardMotion) <= 1e-10)
        {
            if (Math.Abs(turnMotion) <= 1e-9) return currentTarget;
            // Keep an already-sideways fork there when reversing an in-place spin.
            return Math.Abs(currentTarget) >= 89.999 ? currentTarget : -Math.Sign(turnMotion) * 90;
        }
        double sideways = -rearDistance * turnMotion * Math.PI / 180;
        double angle = Math.Atan2(sideways, forwardMotion) * 180 / Math.PI;
        // A wheel can roll backwards: reverse need not flip the fork through 180 degrees.
        if (angle > 90) angle -= 180;
        if (angle < -90) angle += 180;
        return angle;
    }
    public static double Advance(double angle, double target, double seconds)
        => angle + Math.Clamp(target - angle, -180 * Math.Max(0, seconds), 180 * Math.Max(0, seconds));

    /// <summary>Direction the swivel axis travels, in degrees relative to the robot's front (0 forward, 180 reverse),
    /// or null when it does not move.</summary>
    public static double? TravelDirection(double forwardMotion, double turnMotion, double rearDistance)
    {
        double sideways = -rearDistance * turnMotion * Math.PI / 180;
        if (Math.Abs(forwardMotion) <= 1e-10 && Math.Abs(sideways) <= 1e-10) return null;
        return Math.Atan2(sideways, forwardMotion) * 180 / Math.PI;
    }

    // A pushed wheel whose swing has (nearly) cancelled out is treated as swinging at this fraction of its
    // full rate, as a real castor always is slightly off balance, so it swings round within a few trail lengths
    // instead of balancing indefinitely (reversing straight, or reversing an in-place spin).
    private const double PushedNudge = .2;

    /// <summary>
    /// A trailing caster rolled by the robot's motion. Angle 0 is the wheel straight behind the swivel; positive
    /// turns it anticlockwise seen from above, through the full circle (wrapped to -180..180). The wheel only rolls
    /// along its plane, so the fork turns at |v|·sin(travel − angle)/trail in the room while the robot turns under
    /// it: it settles trailing the swivel's travel (under the arm when reversing) and swings round when the
    /// direction changes. Motion is integrated in short steps, so the result does not depend on the frame rate.
    /// </summary>
    public static double Roll(double angle, double forwardMotion, double turnMotion, double rearDistance, double trail)
    {
        if (trail <= 1e-4) return angle;
        double sidewaysTotal = -rearDistance * turnMotion * Math.PI / 180;
        double travel = Math.Sqrt(forwardMotion * forwardMotion + sidewaysTotal * sidewaysTotal);
        if (travel <= 1e-10 && Math.Abs(turnMotion) <= 1e-9) return angle;
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Max(travel / (trail / 10), Math.Abs(turnMotion) / 2)), 1, 5000);
        double forward = forwardMotion / steps, turn = turnMotion * Math.PI / 180 / steps, sideways = sidewaysTotal / steps;
        double distance = Math.Sqrt(forward * forward + sideways * sideways), direction = Math.Atan2(sideways, forward);
        double theta = angle * Math.PI / 180;
        for (int i = 0; i < steps; i++)
        {
            double rate = distance * Math.Sin(direction - theta) / trail - turn, push = PushedNudge * distance / trail;
            // A pushed wheel (swivel moving toward it) balanced with no net swing is an unstable equilibrium.
            if (Math.Cos(direction - theta) < 0 && Math.Abs(rate) < push) rate = rate < -1e-12 ? -push : push;
            theta += rate;
        }
        return Wrap(theta * 180 / Math.PI);
    }

    public static double Wrap(double degrees)
    {
        degrees %= 360;
        if (degrees > 180) degrees -= 360;
        if (degrees <= -180) degrees += 360;
        return degrees;
    }
}
