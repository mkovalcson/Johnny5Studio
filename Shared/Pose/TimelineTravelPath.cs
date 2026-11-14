namespace ServoAnimator;

public class TimelineTravelPath
{
    private readonly GroundTravelPath _path;
    private readonly ServoCommand[] _speed, _heading, _spin, _rotate;
    private readonly double[] _events;
    private readonly IPoseCurve _speedCurve, _headingCurve, _spinCurve, _rotateCurve;
    private readonly double _defaultHeading, _defaultSpin, _defaultRotate;
    public TimelineTravelPath(IEnumerable<ServoCommand> commands, double origin, double heading, IPoseCurve speedCurve, IPoseCurve headingCurve, double spin, double rotate, IPoseCurve spinCurve, IPoseCurve rotateCurve, double casterRadius, double casterTrail = 0)
    {
        _defaultHeading = heading; _defaultSpin = spin; _defaultRotate = rotate;

        _speed = commands.Where(c => c.Servo == ServoNames.TrackForward && c.OffsetSeconds >= origin).OrderBy(c => c.OffsetSeconds).Select(c => c.Clone()).ToArray();
        _heading = commands.Where(c => c.Servo == ServoNames.URDFStageHeading && c.OffsetSeconds >= origin).OrderBy(c => c.OffsetSeconds).Select(c => c.Clone()).ToArray();
        _spin = commands.Where(c => c.Servo == ServoNames.URDFSpin && c.OffsetSeconds >= origin).OrderBy(c => c.OffsetSeconds).Select(c => c.Clone()).ToArray();
        _rotate = commands.Where(c => c.Servo == ServoNames.URDFRotate && c.OffsetSeconds >= origin).OrderBy(c => c.OffsetSeconds).Select(c => c.Clone()).ToArray();
        _events = _speed.Concat(_heading).Concat(_spin).Concat(_rotate).Select(c => c.OffsetSeconds).Distinct().OrderBy(t => t).ToArray();
        _speedCurve = speedCurve; _headingCurve = headingCurve; _spinCurve = spinCurve; _rotateCurve = rotateCurve;
        _path = new GroundTravelPath((servo, time) => servo switch
        {
            ServoNames.TrackForward => Value(_speed, _speedCurve, time, 0),
            ServoNames.URDFStageHeading => Value(_heading, _headingCurve, time, _defaultHeading),
            ServoNames.URDFSpin => Value(_spin, _spinCurve, time, _defaultSpin),
            ServoNames.URDFRotate => Value(_rotate, _rotateCurve, time, _defaultRotate),
            _ => 0
        }, _events, origin, heading + spin, casterRadius, casterTrail,
            hasMotion: _speed.Length != 0 || _spin.Length != 0 || _heading.Length != 0);
    }
    private static double Value(ServoCommand[] commands, IPoseCurve curve, double time, double fallback)
    {
        int lo = 0, hi = commands.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (commands[mid].OffsetSeconds <= time) lo = mid + 1; else hi = mid; }
        if (lo == 0) return fallback;
        var command = commands[lo - 1];
        if (command.Disable) return 0;
        return curve?.T?.Length >= 2 && time >= curve.T[0]
            ? Math.Clamp(SplineUtil.Eval(curve.T, curve.V, curve.M, time, curve.BreakAfter), curve.Min, curve.Max) : command.NumericValue;
    }
    public (double X, double Y) At(double time) { var p = _path.At(time); return (p.X, p.Y); }
    public double ForwardDistanceAt(double time) => _path.At(time).Forward;
    public (double Angle, double Target, double Left, double Right) CasterAt(double time)
    { var p = _path.At(time); return (p.Caster, p.CasterTarget, p.WheelLeft, p.WheelRight); }
}
