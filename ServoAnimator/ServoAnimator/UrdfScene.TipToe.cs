using System.Diagnostics;
using System.Numerics;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

// TipToe (B_TipToe) is a virtual body joint: the URDF has no joint for it. Every direct child of
// the root link gets one extra transform after its joint origin, in the root frame: track, tread
// and shroud links rotate front-down about the pelvis drum, the caster four-bar swings (one-piece
// upper brace, lower brace, column), then the whole robot tips back about the drum onto the caster
// wheel and rises (and moves forward, keeping the front rollers planted) so the treads stay on the
// floor; the caster wheels roll in. All of it comes from Shared/Pose/TipToeGeometry, so the VR
// Editor and VR Player match exactly.
// Initialized last, after treads, caster and laser, which capture their rest frames first; their
// part-level transforms sit below these link-level ones and keep working.
internal sealed partial class UrdfScene
{
    internal const string TipToeJoint = "TipToe";
    private TipToeGeometry _tipToe;
    private readonly List<(MatrixTransform3D Extra, string Link)> _tipToeTransforms = new();
    private double _tipToeRadians = double.NaN;
    internal bool HasTipToe => _tipToe != null;
    internal double TipToeRadians => double.IsNaN(_tipToeRadians) ? 0 : _tipToeRadians;
    internal TipToeGeometry.Pose TipToeMotion => _tipToe?.At(TipToeRadians) ?? default;

    private void InitializeTipToe(string urdfPath)
    {
        try
        {
            _tipToe = TipToeGeometry.FromUrdf(urdfPath);
            if (_tipToe == null) return;
            foreach (var joint in _parentJointByChild.Values.Where(j => j.ParentLink == _rootLinkName).ToArray())
            {
                var extra = new MatrixTransform3D(Matrix3D.Identity);
                var group = new Transform3DGroup();
                if (joint.Node.Transform != null) group.Children.Add(joint.Node.Transform);
                group.Children.Add(extra);
                joint.Node.Transform = group;
                _tipToeTransforms.Add((extra, joint.ChildLink));
            }
            SetTipToe(0);
        }
        catch (Exception ex) { _tipToe = null; Debug.WriteLine("TipToe unavailable: " + ex.Message); }
    }

    /// <summary>Applies a TipToe angle (radians, clamped to 0..61.27 degrees). True when the pose changed.</summary>
    internal bool SetTipToe(double radians)
    {
        if (_tipToe == null || !double.IsFinite(radians)) return false;
        radians = Math.Clamp(radians, 0, TipToeGeometry.MaximumRadians);
        if (radians == _tipToeRadians) return false;
        _tipToeRadians = radians;
        foreach (var (extra, link) in _tipToeTransforms) extra.Matrix = ToMatrix3D(_tipToe.RootChildMotion(link, radians));
        // The caster wheels roll in from behind; SetCasterSwivel adds this to their own travel.
        _tipToeWheelRoll = _tipToe.At(radians).CasterRoll / _tipToe.CasterWheelRadius * CasterWheelVisualRadius;
        ReapplyCasterSwivel();
        return true;
    }

    private static Matrix3D ToMatrix3D(Matrix4x4 m) => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
}
