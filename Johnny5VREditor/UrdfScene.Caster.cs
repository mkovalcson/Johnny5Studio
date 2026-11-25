using System.Diagnostics;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private sealed record CasterVisual(Matrix3D OriginalToWorld, Matrix3D WorldToLink, MatrixTransform3D Transform,int Wheel,Point3D Centre);
    private readonly List<CasterVisual> _casterVisuals = new();
    private Point3D _casterPivot;
    internal double CasterDistanceBehind(double pivotX) => _casterVisuals.Count > 0 ? Math.Max(.01, Math.Abs(pivotX - _casterPivot.X)) : .5;
    /// <summary>Horizontal distance from the swivel axis back to the wheel axle; 0 when the wheel sits under the swivel.</summary>
    internal double CasterTrail { get; private set; }
    private double _casterLastAngle = double.NaN;
    private double _casterLastLeft=double.NaN,_casterLastRight=double.NaN;
    // Extra wheel travel from TipToe (the wheels roll in as the arm swings down and the body moves forward).
    private const double CasterWheelVisualRadius = .075;
    private double _tipToeWheelRoll, _casterLastRoll = double.NaN;
    private void ReapplyCasterSwivel() => SetCasterSwivel(double.IsNaN(_casterLastAngle) ? 0 : _casterLastAngle,
        double.IsNaN(_casterLastLeft) ? 0 : _casterLastLeft, double.IsNaN(_casterLastRight) ? 0 : _casterLastRight);
    private void InitializeCaster()
    {
        if (!TryTransformLinkPoint("body_caster_yoke_link", new Point3D(), out _casterPivot)) return;
        if (TryTransformLinkPoint("body_caster_axle_link", new Point3D(), out var axle))
        {
            double trail = Math.Sqrt((axle.X - _casterPivot.X) * (axle.X - _casterPivot.X) + (axle.Y - _casterPivot.Y) * (axle.Y - _casterPivot.Y));
            CasterTrail = trail >= .005 ? trail : 0;
        }
        try
        {
            // Swivel the complete twin-wheel fork assembly, leaving its support arm,
            // spring and stationary head in place. Only visual transforms are changed.
            foreach (var pair in _linkVisuals.Where(p => p.Key.StartsWith("body_caster_", StringComparison.Ordinal)))
            {
                string part = pair.Key[12..];
                if (!(part.StartsWith("yoke") || part.StartsWith("fork") || part.StartsWith("axle") || part.StartsWith("wheel") || part.StartsWith("hub"))) continue;
                var world = TreadLinkWorld(pair.Key); if (!world.HasInverse) continue;
                var inverse = world; inverse.Invert();
                int side=0;var centre=new Point3D();
                if(part.StartsWith("wheel")||part.StartsWith("hub"))
                {
                    side=part.Contains("_l_")?1:part.Contains("_r_")?2:0;
                    if(side!=0&&!TryTransformLinkPoint(side==1?"body_caster_wheel_l_link":"body_caster_wheel_r_link",new Point3D(),out centre))side=0;
                }
                foreach (var visual in pair.Value)
                {
                    var original = visual.Transform.Value; original.Append(world);
                    var transform = new MatrixTransform3D(visual.Transform.Value);
                    _casterVisuals.Add(new(original, inverse, transform,side,centre)); visual.Transform = transform;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine("Rear caster animation unavailable: " + ex.Message); }
    }
    internal void SetCasterSwivel(double degrees,double left=0,double right=0)
    {
        if (!double.IsFinite(degrees)||!double.IsFinite(left)||!double.IsFinite(right) || _casterLastAngle == degrees&&_casterLastLeft==left&&_casterLastRight==right&&_casterLastRoll==_tipToeWheelRoll) return;
        _casterLastAngle = degrees;
        _casterLastLeft=left;_casterLastRight=right;_casterLastRoll=_tipToeWheelRoll;
        var tipToe = CasterWheelTravel.DuringTipToe(degrees, _tipToeWheelRoll, CasterTrail);
        degrees = tipToe.Angle;
        double radiusScale = CasterWheelVisualRadius / (_tipToe?.CasterWheelRadius ?? CasterWheelVisualRadius);
        left += tipToe.Left * radiusScale;
        right += tipToe.Right * radiusScale;
        foreach (var visual in _casterVisuals)
        {
            var matrix = visual.OriginalToWorld;
            if(visual.Wheel!=0)matrix.RotateAt(new Quaternion(new Vector3D(0,1,0),(visual.Wheel==1?left:right)/CasterWheelVisualRadius*180/Math.PI%360),visual.Centre);
            matrix.RotateAt(new Quaternion(new Vector3D(0, 0, 1), degrees), _casterPivot);
            matrix.Append(visual.WorldToLink); visual.Transform.Matrix = matrix;
        }
    }
}
