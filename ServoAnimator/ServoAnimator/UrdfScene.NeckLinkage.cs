using System.Globalization;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private UrdfJoint _neckFrontFollower;
    private string _neckAnchorLink;
    private Vector3D _neckRestDirection, _neckStretchAxis;
    private Point3D _neckStretchPivot;
    private double _neckSleeveLength, _neckRodRestLength;
    private readonly List<(Matrix3D Original, MatrixTransform3D Transform)> _neckRodVisuals = new();
    private double _neckLastScale = double.NaN;

    private void InitializeNeckLinkage(XElement robot)
    {
        var metadata = robot.Element("johnny5_neck_linkage");
        if ((string)metadata?.Attribute("version") != "1") return;
        string jointName = (string)metadata.Attribute("frontJoint");
        string anchor = (string)metadata.Attribute("anchorLink"), rod = (string)metadata.Attribute("rodLink");
        bool ReadVector(string key, out Vector3D value)
        {
            value = new();
            var fields = ((string)metadata.Attribute(key) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3) return false;
            var numbers = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i])) return false;
            value = new(numbers[0], numbers[1], numbers[2]); return true;
        }
        if (string.IsNullOrEmpty(jointName) || !_joints.TryGetValue(jointName, out var follower)
            || string.IsNullOrEmpty(anchor) || !_links.ContainsKey(anchor)
            || string.IsNullOrEmpty(rod) || !_linkVisuals.TryGetValue(rod, out var visuals)
            || !ReadVector("direction", out var direction) || direction.LengthSquared < 1e-12
            || !ReadVector("stretchAxis", out var axis) || axis.LengthSquared < 1e-12
            || !ReadVector("stretchPivot", out var pivot)
            || !double.TryParse((string)metadata.Attribute("restLength"), NumberStyles.Float, CultureInfo.InvariantCulture, out double length)
            || !double.IsFinite(length) || length <= .001
            || !double.TryParse((string)metadata.Attribute("sleeveLength"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sleeve)
            || !double.IsFinite(sleeve) || sleeve < 0) return;
        direction.Normalize(); axis.Normalize();
        _neckFrontFollower = follower; _neckAnchorLink = anchor;
        _neckRestDirection = direction; _neckStretchAxis = axis;
        _neckStretchPivot = new(pivot.X, pivot.Y, pivot.Z);
        _neckRodRestLength = length; _neckSleeveLength = sleeve;
        foreach (var visual in visuals)
        {
            var original = visual.Transform?.Value ?? Matrix3D.Identity;
            var transform = new MatrixTransform3D(original);
            _neckRodVisuals.Add((original, transform)); visual.Transform = transform;
        }
        UpdateNeckLinkage();
    }

    private void UpdateNeckLinkage()
    {
        if (_neckFrontFollower == null || !TryTransformLinkPoint(_neckAnchorLink, new(), out var anchor)) return;
        // Work in the unrotated follower joint frame: the direction from its
        // upper pin to the fixed lower socket gives an exact signed angle.
        var frame = _neckFrontFollower.OriginMatrix;
        frame.Append(TreadLinkWorld(_neckFrontFollower.ParentLink));
        if (!frame.HasInverse) return;
        frame.Invert();
        var targetPoint = frame.Transform(anchor);
        var target = new Vector3D(targetPoint.X, targetPoint.Y, targetPoint.Z);
        double distance = target.Length;
        if (!double.IsFinite(distance) || distance <= _neckSleeveLength + .001) return;
        target.Normalize();
        double angle = Math.Atan2(Vector3D.DotProduct(_neckFrontFollower.Axis, Vector3D.CrossProduct(_neckRestDirection, target)),
            Vector3D.DotProduct(_neckRestDirection, target));
        _neckFrontFollower.SetPosition(angle);
        double scale = (distance - _neckSleeveLength) / _neckRodRestLength;
        if (Math.Abs(scale - _neckLastScale) < 1e-10) return;
        _neckLastScale = scale;
        // Extend just the covered front rod along its own axis. Its upper gland
        // remains fixed at the pin, and the lower end stays inside the socket.
        var u = _neckStretchAxis; double k = scale - 1;
        var stretch = new Matrix3D(1+k*u.X*u.X, k*u.X*u.Y, k*u.X*u.Z, 0,
            k*u.Y*u.X, 1+k*u.Y*u.Y, k*u.Y*u.Z, 0,
            k*u.Z*u.X, k*u.Z*u.Y, 1+k*u.Z*u.Z, 0, 0, 0, 0, 1);
        var moved = stretch.Transform(_neckStretchPivot);
        stretch.OffsetX = _neckStretchPivot.X - moved.X;
        stretch.OffsetY = _neckStretchPivot.Y - moved.Y;
        stretch.OffsetZ = _neckStretchPivot.Z - moved.Z;
        foreach (var visual in _neckRodVisuals)
        {
            var matrix = visual.Original; matrix.Append(stretch); visual.Transform.Matrix = matrix;
        }
    }
}
