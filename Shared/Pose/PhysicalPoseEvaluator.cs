namespace ServoAnimator;

/// <summary>Evaluates physical channel targets from the same authored values and
/// spline curves as the URDF, independently of graph visibility.</summary>
public class PhysicalPoseEvaluator
{
    private readonly Dictionary<ServoNames, ServoCommand[]> _parents;
    private readonly Dictionary<(ServoNames, RobotControls), ServoCommand[]> _children;
    private readonly Dictionary<RobotControls, ServoCommand[]> _targets, _speeds;
    private readonly IReadOnlyDictionary<ServoNames, IPoseCurve> _curves;
    private readonly ServoConfiguration _config;
    private readonly IReadOnlyDictionary<ServoNames, double> _carry;
    private readonly IReadOnlyDictionary<(ServoNames, RobotControls), double> _carryChildren;
    private readonly IReadOnlyDictionary<ServoNames, ServoSpeed> _carrySpeeds;
    private readonly Dictionary<RobotControls, ServoNames[]> _owners;

    public PhysicalPoseEvaluator(IEnumerable<ServoCommand> commands, IReadOnlyDictionary<ServoNames, IPoseCurve> curves,
        ServoConfiguration config, IReadOnlyDictionary<ServoNames, double> carry = null,
        IReadOnlyDictionary<(ServoNames, RobotControls), double> carryChildren = null,
        IReadOnlyDictionary<ServoNames, ServoSpeed> carrySpeeds = null)
    {
        _curves = curves; _config = config; _carry = carry; _carryChildren = carryChildren; _carrySpeeds = carrySpeeds;
        var ordered = commands.Where(c => !c.IsTextServo && !ServoCommand.IsUrdfOnly(c.Servo)).OrderBy(c => c.OffsetSeconds).ToArray();
        _parents = ordered.Where(c => !c.Disable && !c.Control.HasValue).GroupBy(c => c.Servo).ToDictionary(g => g.Key, g => g.ToArray());
        _children = ordered.Where(c => !c.Disable && c.Control.HasValue).GroupBy(c => (c.Servo, c.Control.Value)).ToDictionary(g => g.Key, g => g.ToArray());
        var channels = ordered.SelectMany(c => (c.Control.HasValue ? new[] { c.Control.Value } : ServoConfiguration.ControlsFor(c.Servo))
            .Select(control => (Control: control, Command: c))).GroupBy(p => p.Control).ToArray();
        _targets = channels.ToDictionary(g => g.Key, g => g.Select(p => p.Command).ToArray());
        _speeds = channels.ToDictionary(g => g.Key, g => g.Select(p => p.Command).Where(c => !c.Disable && c.Speed != ServoSpeed.NoChange).ToArray());
        _owners = Enum.GetValues<ServoNames>().Where(s => !ServoCommand.IsTextValued(s) && !ServoCommand.IsUrdfOnly(s))
            .SelectMany(s => ServoConfiguration.ControlsFor(s).Select(control => (Control: control, Servo: s)))
            .GroupBy(p => p.Control).ToDictionary(g => g.Key, g => g.Select(p => p.Servo).Distinct().ToArray());
    }

    private static ServoCommand Last(ServoCommand[] commands, double time)
    {
        if (commands == null) return null;
        int lo = 0, hi = commands.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (commands[mid].OffsetSeconds <= time) lo = mid + 1; else hi = mid; }
        return lo > 0 ? commands[lo - 1] : null;
    }

    private (double Value, double? Time) Parent(ServoNames servo, double time)
    {
        var command = Last(_parents.GetValueOrDefault(servo), time);
        double value = command?.NumericValue ?? (_carry?.GetValueOrDefault(servo) ?? Math.Max(0, ServoCommand.RangeFor(servo).Min));
        if (command != null && _curves.TryGetValue(servo, out var curve) && curve.T.Length >= 2 && time >= curve.T[0])
            value = Math.Clamp(SplineUtil.Eval(curve.T, curve.V, curve.M, time, curve.BreakAfter), curve.Min, curve.Max);
        return (value, command?.OffsetSeconds);
    }

    public ServoCommand[] Evaluate(double time)
    {
        var result = new List<ServoCommand>();
        var nod = Parent(ServoNames.NeckNodUp, time); var tilt = Parent(ServoNames.NeckTiltRight, time);
        var pistons = NeckMix.Pistons(_config, nod.Value, tilt.Value);
        double lastNeck = Math.Max(nod.Time ?? double.NegativeInfinity, tilt.Time ?? double.NegativeInfinity);
        foreach (var (control, owners) in _owners)
        {
            ServoNames servo = owners[0]; double value = 0, latest = double.NegativeInfinity;
            if (NeckMix.IsPiston(control))
            {
                servo = ServoNames.NeckNodUp;
                value = control == RobotControls.NeckTiltLeft ? pistons.Left : pistons.Right;
                ServoCommand child = null;
                foreach (var owner in new[] { ServoNames.NeckNodUp, ServoNames.NeckTiltRight })
                {
                    var candidate = Last(_children.GetValueOrDefault((owner, control)), time);
                    if (candidate != null && MoviePoseContinuity.ChildOverrides(lastNeck, candidate.OffsetSeconds) && (child == null || candidate.OffsetSeconds >= child.OffsetSeconds)) child = candidate;
                }
                if (child != null) value = NeckMix.ToNodUnits(_config, child.Servo, control, child.NumericValue);
            }
            else
            {
                foreach (var owner in owners.OrderBy(s => s == ServoNames.BothEyePop ? 1 : 0))
                {
                    var parent = Parent(owner, time);
                    var child = Last(_children.GetValueOrDefault((owner, control)), time);
                    double key = child != null && MoviePoseContinuity.ChildOverrides(parent.Time, child.OffsetSeconds)
                        ? child.OffsetSeconds : parent.Time ?? double.NegativeInfinity;
                    // An untouched BothEyePop must not replace carried individual eye values.
                    if (owner == ServoNames.BothEyePop && double.IsNegativeInfinity(key)) continue;
                    if (key < latest) continue;
                    latest = key; servo = owner;
                    double? carryChild = _carryChildren?.TryGetValue((owner, control), out double carried) == true ? carried : null;
                    value = MoviePoseContinuity.ChildValue(parent.Value, parent.Time, child, carryChild);
                }
            }
            var target = Last(_targets.GetValueOrDefault(control), time);
            var speed = Last(_speeds.GetValueOrDefault(control), time)?.Speed ?? _carrySpeeds?.GetValueOrDefault(servo, ServoSpeed.Default) ?? ServoSpeed.Default;
            var command = new ServoCommand { Servo = servo, Control = control, OffsetSeconds = ServoCommand.TimeKey(time),
                NumericValue = (int)Math.Round(value), Speed = speed, Disable = target?.Disable == true };
            command.ClampToRange(); result.Add(command);
        }
        return result.ToArray();
    }
}

/// <summary>Keep repeated positions quiet; change speed only when the active profile changes.</summary>
public sealed class TimelineHardwareChanges
{
    private readonly Dictionary<RobotControls, ServoCommand> _sent = new();
    public void Reset() => _sent.Clear();
    public ServoCommand[] Take(IEnumerable<ServoCommand> pose)
    {
        var result = new List<ServoCommand>();
        foreach (var command in pose)
        {
            var control = command.Control.Value;
            _sent.TryGetValue(control, out var previous);
            bool profileChanged = previous == null || previous.Speed != command.Speed || previous.Disable;
            bool changed = previous == null || previous.Disable != command.Disable ||
                !command.Disable && (previous.Servo != command.Servo || previous.NumericValue != command.NumericValue || profileChanged);
            if (!changed) continue;
            var output = command.Clone();
            if (!profileChanged) output.Speed = ServoSpeed.NoChange;
            result.Add(output); _sent[control] = command.Clone();
        }
        return result.ToArray();
    }
}
