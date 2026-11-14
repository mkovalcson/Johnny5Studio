namespace ServoAnimator;
// Keep rendering-specific curve types at the application boundary.
internal sealed class TimelineHardwarePose : PhysicalPoseEvaluator
{
    internal TimelineHardwarePose(IEnumerable<ServoCommand> commands, IReadOnlyDictionary<ServoNames, SplineCurve> curves,
        ServoConfiguration config, IReadOnlyDictionary<ServoNames, double> carry = null,
        IReadOnlyDictionary<(ServoNames, RobotControls), double> carryChildren = null,
        IReadOnlyDictionary<ServoNames, ServoSpeed> carrySpeeds = null)
        : base(commands, curves.ToDictionary(p => p.Key, p => (IPoseCurve)p.Value), config, carry, carryChildren, carrySpeeds) { }
}
