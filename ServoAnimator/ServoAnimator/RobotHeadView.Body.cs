namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private readonly Dictionary<ServoNames, double> _bodyPositions = new();
    internal double BodyPosition(ServoNames servo)
    {
        if(PairedBodyControls.Find(servo) is {} pair)return PairedBodyControls.Position(pair,_urdfConfiguration,BodyPosition);
        if (BodyControlCatalog.IsLegacyShoulder(servo))
        {
            var legacy = BodyControlCatalog.Find(servo);
            var current = BodyControlCatalog.Find(BodyControlCatalog.Canonical(servo));
            var oldRange = _urdfConfiguration?.BodySetting(legacy) ?? BodyMotionSetting.Default(legacy);
            var newRange = _urdfConfiguration?.BodySetting(current) ?? BodyMotionSetting.Default(current);
            return oldRange.CommandForAngle(newRange.Map(BodyPosition(current.Servo), current), legacy);
        }
        if (!BodyControlCatalog.IsHandGang(servo)) return _bodyPositions.GetValueOrDefault(servo);
        var values = BodyControlCatalog.HandMembers(servo).Where(c => _scene?.HasJoint(c.Joint) == true).Select(c =>
        {
            var setting = _urdfConfiguration?.BodySetting(c) ?? BodyMotionSetting.Default(c);
            double a = setting.Map(-100, c), b = setting.Map(100, c);
            double min = Math.Min(a, b), max = Math.Max(a, b);
            double angle = setting.Map(_bodyPositions.GetValueOrDefault(c.Servo), c);
            double amount = (angle - min) / Math.Max(1e-12, max - min);
            return 100 * Math.Clamp(c.Joint.StartsWith("FingerSpread", StringComparison.Ordinal) ? amount : 1 - amount, 0, 1);
        }).ToArray();
        return values.Length == 0 ? 0 : values.Average();
    }
    private bool HasBodyControl(BodyControlDefinition control) => _scene != null &&
        (BodyControlCatalog.IsPairedGang(control.Servo)
            ? BodyControlCatalog.PairedMembers(control.Servo).All(s=>_scene.HasJoint(BodyControlCatalog.Find(s).Joint))
            : BodyControlCatalog.IsHandGang(control.Servo)
            ? BodyControlCatalog.HandMembers(control.Servo).Any(c => _scene.HasJoint(c.Joint))
            : _scene.HasJoint(control.Joint));
    private IEnumerable<(ServoNames Servo, double Value)> HandOpeningValues(ServoNames servo, double value)
    {
        if (_scene == null || !double.IsFinite(value)) yield break;
        value = Math.Clamp(value, 0, 100);
        foreach (var member in BodyControlCatalog.HandMembers(servo).Where(c => _scene.HasJoint(c.Joint)))
        {
            var range = _urdfConfiguration?.BodySetting(member) ?? BodyMotionSetting.Default(member);
            double a = range.Map(-100, member), b = range.Map(100, member);
            double min = Math.Min(a, b), max = Math.Max(a, b);
            bool spread = member.Joint.StartsWith("FingerSpread", StringComparison.Ordinal);
            double angle = spread ? min + (max - min) * value / 100 : max - (max - min) * value / 100;
            yield return (member.Servo, range.CommandForAngle(angle, member));
        }
    }
    internal void ApplyBodyPose(IEnumerable<ServoStateRow> rows, ISet<ServoNames> manualOverrides = null)
    {
        bool suppressed = _suppressCollisionRefresh;
        long before = _poseRevision;
        try
        {
            _suppressCollisionRefresh = true;
            // A gang without a command must not close every hand on every frame.
            // Apply defaults first, then commands in time order so a later single
            // joint command can refine a previously ganged hand pose.
            var resolved = new Dictionary<ServoNames, (ServoNames Servo, double Value)>();
            foreach (var row in rows.Where(r => !(BodyControlCatalog.IsGang(r.Servo) || BodyControlCatalog.IsLegacyShoulder(r.Servo))
                    || r.Offset.HasValue || manualOverrides?.Contains(r.Servo) == true)
                .OrderBy(r => manualOverrides?.Contains(r.Servo) == true ? 1 : 0)
                .ThenBy(r => r.Offset ?? double.NegativeInfinity)
                .ThenBy(r => BodyControlCatalog.IsGang(r.Servo) ? 0 : 1))
            {
                if(PairedBodyControls.Find(row.Servo) is {} pair)
                    foreach(var member in PairedBodyControls.Resolve(pair,_urdfConfiguration,row.Value))resolved[member.Servo]=member;
                else if (BodyControlCatalog.IsHandGang(row.Servo))
                    foreach (var member in HandOpeningValues(row.Servo, row.Value)) resolved[member.Servo] = member;
                else resolved[BodyControlCatalog.Canonical(row.Servo)] = (row.Servo, row.Value);
            }
            // Resolve overlapping controls before touching the scene, avoiding
            // resetting and reapplying the hand twice on every playback frame.
            foreach (var item in resolved.Values) ApplyBodyControl(item.Servo, item.Value);
        }
        finally { _suppressCollisionRefresh = suppressed; }
        if (_poseRevision != before) { UpdatePoseOverlayLayout(); if (!suppressed) RefreshCollisionState(); }
    }
    internal void ApplyBodyControl(ServoNames servo, double value)
    {
        var control = BodyControlCatalog.Find(servo);
        if (control == null || _scene == null || !double.IsFinite(value)) return;
        if (BodyControlCatalog.IsLegacyShoulder(servo))
        {
            var canonical = BodyControlCatalog.Find(BodyControlCatalog.Canonical(servo));
            var oldRange = _urdfConfiguration?.BodySetting(control) ?? BodyMotionSetting.Default(control);
            var newRange = _urdfConfiguration?.BodySetting(canonical) ?? BodyMotionSetting.Default(canonical);
            ApplyBodyControl(canonical.Servo, newRange.CommandForAngle(oldRange.Map(value, control), canonical));
            return;
        }
        if(PairedBodyControls.Find(servo) is {} pair)
        {
            foreach(var member in PairedBodyControls.Resolve(pair,_urdfConfiguration,value))ApplyBodyControl(member.Servo,member.Value);
            _pose.BodyPositions.Remove(servo);
            return;
        }
        if (BodyControlCatalog.IsHandGang(servo))
        {
            value = Math.Clamp(value, 0, 100);
            bool suppressed = _suppressCollisionRefresh;
            long before = _poseRevision;
            try
            {
                _suppressCollisionRefresh = true;
                foreach (var member in HandOpeningValues(servo, value)) ApplyBodyControl(member.Servo, member.Value);
                _bodyPositions[servo] = value;
                // Persist expanded joint values in poses, avoiding competing aliases.
                _pose.BodyPositions.Remove(servo);
            }
            finally { _suppressCollisionRefresh = suppressed; }
            if (_poseRevision != before && !suppressed) { UpdatePoseOverlayLayout(); RefreshCollisionState(); }
            return;
        }
        value = Math.Clamp(value, -100, 100);
        _bodyPositions[servo] = value;
        _pose.BodyPositions[servo] = value;
        var setting = _urdfConfiguration?.BodySetting(control) ?? BodyMotionSetting.Default(control);
        if (_scene.SetConfiguredBodyJoint(control.Joint, setting.Map(value, control)))
        {
            _poseRevision++;
            if (!_suppressCollisionRefresh) { UpdatePoseOverlayLayout(); RefreshCollisionState(); }
        }
    }
}
