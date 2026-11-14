namespace ServoAnimator;
/// <summary>Distances in metres; headings and caster angles in degrees.</summary>
public record struct GroundTravelPose(double X, double Y, double Forward, double Caster, double CasterTarget,
    double LastHeading, double WheelLeft = 0, double WheelRight = 0);
/// <summary>Shared 20 ms midpoint integration with exact command boundaries and deterministic
/// 100 ms checkpoints. Values come from the host's prepared timeline tracks.</summary>
public sealed class GroundTravelPath
{
    private readonly Func<ServoNames, double, double> _value;
    private readonly double[] _events;
    private readonly double _origin, _casterRadius, _casterTrail, _halfWidth, _boundaryTolerance;
    private readonly bool _hasMotion;
    private readonly List<GroundTravelPose> _positions = new();
    public GroundTravelPath(Func<ServoNames, double, double> value, IEnumerable<double> events, double origin,
        double initialHeading, double casterRadius, double casterTrail = 0, double halfWidth = .034875,
        bool hasMotion = true, double boundaryTolerance = 0)
    {
        _value = value; _events = events.Distinct().Order().ToArray(); _origin = origin;
        _casterRadius = casterRadius; _casterTrail = casterTrail; _halfWidth = halfWidth;
        _hasMotion = hasMotion; _boundaryTolerance = boundaryTolerance;
        _positions.Add(new(0, 0, 0, 0, 0, initialHeading));
    }
    private GroundTravelPose Integrate(GroundTravelPose position, double start, double end)
    {
        int nextEvent = Array.BinarySearch(_events, start + _boundaryTolerance);
        nextEvent = nextEvent < 0 ? ~nextEvent : nextEvent + (_boundaryTolerance == 0 ? 1 : 0);
        while (start < end - 1e-10)
        {
            double next = Math.Min(end, start + .02);
            if (nextEvent < _events.Length && _events[nextEvent] <= next)
            { next = _events[nextEvent++]; if (next <= start) continue; }
            double middle = (start + next) / 2;
            double distance = _value(ServoNames.TrackForward, middle) * (next - start) / 100;
            var front = TrackForwardDirection.OnFloor(_value(ServoNames.URDFStageHeading, middle),
                _value(ServoNames.URDFSpin, middle), _value(ServoNames.URDFRotate, middle));
            double intervalEnd = Math.BitDecrement(next);
            double heading = _value(ServoNames.URDFStageHeading, intervalEnd) + _value(ServoNames.URDFSpin, intervalEnd);
            double before=position.Caster;
            if (_casterTrail > 0)
            {
                position.CasterTarget = CasterSwivel.TravelDirection(distance, heading - position.LastHeading, _casterRadius) ?? position.CasterTarget;
                position.Caster = CasterSwivel.Roll(position.Caster, distance, heading - position.LastHeading, _casterRadius, _casterTrail);
            }
            else
            {
                position.CasterTarget = CasterSwivel.Target(position.CasterTarget, distance, heading - position.LastHeading, _casterRadius);
                position.Caster = CasterSwivel.Advance(position.Caster, position.CasterTarget, next - start);
            }
            var wheels=CasterWheelTravel.Step(before,position.Caster,distance,heading-position.LastHeading,_casterRadius,_halfWidth);
            position.WheelLeft+=wheels.Left;position.WheelRight+=wheels.Right;
            position.LastHeading = heading;
            position.X += distance * front.X; position.Y += distance * front.Y; position.Forward += distance; start = next;
        }
        return position;
    }
    public GroundTravelPose At(double time)
    {
        if (!_hasMotion || time <= _origin) return _positions[0];
        time = Math.Clamp(time, _origin, Math.Max(_origin, 86400));
        int index = (int)Math.Floor((time - _origin) * 10);
        while (_positions.Count <= index)
        {
            int previous = _positions.Count - 1;
            _positions.Add(Integrate(_positions[previous], _origin + previous / 10.0, _origin + (previous + 1) / 10.0));
        }
        return Integrate(_positions[index], _origin + index / 10.0, time);
    }
}
