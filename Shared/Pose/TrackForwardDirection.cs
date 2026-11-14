namespace ServoAnimator;

public static class TrackForwardDirection
{
    // Robot local +X is its front. Match the model's Spin (Z), Rotate (-X),
    // then stage Heading (Z) transforms. Camera/backdrop view transforms are
    // deliberately excluded: these coordinates belong to the room's floor.
    public static (double X, double Y) OnFloor(double headingDegrees, double spinDegrees, double rotateDegrees)
    {
        const double radians = Math.PI / 180;
        spinDegrees %= 360; headingDegrees %= 360;
        double x = Math.Cos(spinDegrees * radians);
        double y = Math.Sin(spinDegrees * radians) * Math.Cos(rotateDegrees * radians);
        double length = Math.Sqrt(x * x + y * y);
        // A vertical front has no ground-plane direction.
        if (length < 1e-9) return (0, 0);
        double heading = headingDegrees * radians;
        return ((x * Math.Cos(heading) - y * Math.Sin(heading)) / length,
                (x * Math.Sin(heading) + y * Math.Cos(heading)) / length);
    }
}
