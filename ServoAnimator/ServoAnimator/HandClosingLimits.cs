namespace ServoAnimator;

// Closing range of the hand. The single finger (thumb) is hard-mounted on the palm
// between the two paired fingers (FullBodyModel/Body/thumb_mount_details.py), so the
// three digits curl in separate lanes and cannot meet: the fingers close over the
// top of the thumb as in the CAD drawing. Each curl is therefore limited only by its
// own range, sized to reach that closed pose. Keep synchronized with
// Body/hand_closing_limits.py and the URDF limits (finger_length_details.py).
// Shared hand range definitions live in J5Studio.Controls.

internal sealed partial class UrdfScene
{
    private bool _handCenterLimits;
    private readonly Dictionary<string, double> _handRequested = new(StringComparer.Ordinal);
    private bool TrySetHandJoint(string name, double requested, out bool changed)
    {
        changed = false;
        if (!_handCenterLimits || !double.IsFinite(requested)) return false;
        string side = name.EndsWith("Left", StringComparison.Ordinal) ? "Left" : name.EndsWith("Right", StringComparison.Ordinal) ? "Right" : null;
        if (side == null) return false;
        string kind = name[..^side.Length];
        if (kind is not ("ThumbCurl" or "ThumbTipCurl" or "FingerCurlUpper" or "FingerCurlLower" or "FingerTipCurlUpper" or "FingerTipCurlLower" or "FingerEndCurlUpper" or "FingerEndCurlLower")) return false;
        if (!_joints.ContainsKey(name)) return false;
        _handRequested[name] = requested;
        bool applied = false;
        void Set(string jointName, double value)
        {
            if (!_joints.TryGetValue(jointName, out var joint) || Math.Abs(joint.Position - value) < 1e-10) return;
            joint.SetPosition(value); applied = true;
        }
        double Request(string key) => _handRequested.TryGetValue(key, out var value) ? value : _joints.GetValueOrDefault(key)?.Position ?? 0;
        if (kind is "ThumbCurl" or "ThumbTipCurl")
        {
            string baseName = "ThumbCurl" + side, tipName = "ThumbTipCurl" + side;
            var limited = HandClosingLimits.ConstrainThumb(Request(baseName), Request(tipName));
            Set(baseName, limited.Base); Set(tipName, limited.Tip);
        }
        else
        {
            string finger = kind.EndsWith("Upper", StringComparison.Ordinal) ? "Upper" : "Lower";
            string baseName = "FingerCurl" + finger + side, tipName = "FingerTipCurl" + finger + side, endName = "FingerEndCurl" + finger + side;
            var limited = HandClosingLimits.ConstrainFinger(Request(baseName), Request(tipName), Request(endName));
            Set(baseName, limited.Base); Set(tipName, limited.Tip); Set(endName, limited.End);
        }
        changed = applied;
        return true;
    }
}
