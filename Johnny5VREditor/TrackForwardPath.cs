namespace ServoAnimator;
internal sealed class TrackForwardPath : TimelineTravelPath
{
    public TrackForwardPath(IEnumerable<ServoCommand> commands, double origin, double heading,
        SplineCurve speedCurve, SplineCurve headingCurve, double spin, double rotate,
        SplineCurve spinCurve, SplineCurve rotateCurve, double casterRadius, double casterTrail = 0)
        : base(commands, origin, heading, speedCurve, headingCurve, spin, rotate, spinCurve, rotateCurve, casterRadius, casterTrail) { }
}
