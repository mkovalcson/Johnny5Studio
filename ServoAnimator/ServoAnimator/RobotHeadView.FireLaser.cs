using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    // Laser bolts live beside the robot in the presentation group, in the robot model's coordinates, so they sit in
    // the URDF scene (lit by nothing: they glow) and follow stage moves like the robot does.
    private readonly Model3DGroup _laserShots = new();
    // Where each bolt left the laser: kept while it flies, so moving the laser afterwards doesn't swing it.
    private readonly Dictionary<(double, int), (Point3D Muzzle, Vector3D Aim)> _shotAims = new();
    private readonly Dictionary<string, (Material Core, Material Glow)> _shotMaterials = new(StringComparer.OrdinalIgnoreCase);
    private static readonly MeshGeometry3D BoltMesh = CreateBoltMesh(), FlashMesh = CreateFlashMesh();
    internal int LaserShotModels => _laserShots.Children.Count;

    /// <summary>Draw the bolts in flight (see <see cref="FireLaserTimeline.ShotsAt"/>). Nothing shows unless the
    /// laser itself is showing.</summary>
    internal void SetLaserShots(IReadOnlyList<LaserShot> shots)
    {
        if (_presentationModel != null && !_presentationModel.Children.Contains(_laserShots)) _presentationModel.Children.Add(_laserShots);   // after each model load
        if (_laserShots.Children.Count == 0 && shots.Count == 0 && _shotAims.Count == 0) return;
        _laserShots.Children.Clear();
        bool ready = _scene != null && _scene.LaserReadyToFire && _scene.TryLaserMuzzle(out _, out _);
        var flying = new HashSet<(double, int)>();
        if (ready)
            foreach (var shot in shots)
            {
                var key = (shot.CommandSeconds, shot.Index);
                flying.Add(key);
                if (!_shotAims.TryGetValue(key, out var from))
                {
                    _scene.TryLaserMuzzle(out var muzzle, out var aim);
                    _shotAims[key] = from = (muzzle, aim);
                }
                var (core, glow) = ShotMaterials(shot.Settings.Color);
                // The bolt thins away over the last 15% of its range.
                double fade = 1 - BeamTimeline.Ramp(shot.Front / shot.Settings.Range, .85, 1);
                if (shot.Front - shot.Tail > 1e-4 && fade > .01)
                {
                    var tail = from.Muzzle + from.Aim * shot.Tail;
                    double length = shot.Front - shot.Tail;
                    _laserShots.Children.Add(new GeometryModel3D(BoltMesh, core) { BackMaterial = core, Transform = BoltTransform(tail, from.Aim, length, .0045 * fade) });
                    _laserShots.Children.Add(new GeometryModel3D(BoltMesh, glow) { BackMaterial = glow, Transform = BoltTransform(tail, from.Aim, length, .011 * fade) });
                }
                if (shot.Age < .08)                                    // muzzle flash
                {
                    double r = .03 * (1 - shot.Age / .08);
                    _laserShots.Children.Add(new GeometryModel3D(FlashMesh, glow) { BackMaterial = glow, Transform = new MatrixTransform3D(new Matrix3D(r, 0, 0, 0, 0, r, 0, 0, 0, 0, r, 0, from.Muzzle.X, from.Muzzle.Y, from.Muzzle.Z, 1)) });
                }
            }
        foreach (var key in _shotAims.Keys.Where(k => !flying.Contains(k)).ToList()) _shotAims.Remove(key);
    }

    private (Material Core, Material Glow) ShotMaterials(string color)
    {
        if (_shotMaterials.TryGetValue(color ?? "", out var materials)) return materials;
        if (!LcdShowCommand.TryParseColor(color, out var c)) LcdShowCommand.TryParseColor(FireLaserCommand.DefaultColor, out c);
        static byte Lift(byte v) => (byte)(v + (255 - v) * .7);           // the core is nearly white-hot
        var core = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Colors.Black)), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(Lift(c.R), Lift(c.G), Lift(c.B)))) } };
        var glow = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))), new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B))) } };
        core.Freeze(); glow.Freeze();
        return _shotMaterials[color ?? ""] = (core, glow);
    }

    /// <summary>Maps the unit bolt (x 0..1, radius 1) onto a bolt from <paramref name="tail"/> along <paramref name="aim"/>.</summary>
    private static Transform3D BoltTransform(Point3D tail, Vector3D aim, double length, double radius)
    {
        var up = Math.Abs(aim.Z) < .9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
        var side = Vector3D.CrossProduct(up, aim); side.Normalize();
        var over = Vector3D.CrossProduct(aim, side);
        return new MatrixTransform3D(new Matrix3D(
            aim.X * length, aim.Y * length, aim.Z * length, 0,
            side.X * radius, side.Y * radius, side.Z * radius, 0,
            over.X * radius, over.Y * radius, over.Z * radius, 0,
            tail.X, tail.Y, tail.Z, 1));
    }

    /// <summary>A bolt along x from 0 to 1, radius 1, with pointed ends.</summary>
    private static MeshGeometry3D CreateBoltMesh()
    {
        var mesh = new MeshGeometry3D();
        var rings = new[] { (0.0, 0.0), (.12, 1.0), (.88, 1.0), (1.0, 0.0) };
        const int sides = 10;
        foreach (var (x, r) in rings)
            for (int i = 0; i < sides; i++)
            {
                double a = 2 * Math.PI * i / sides;
                mesh.Positions.Add(new Point3D(x, r * Math.Cos(a), r * Math.Sin(a)));
            }
        for (int ring = 0; ring < rings.Length - 1; ring++)
            for (int i = 0; i < sides; i++)
            {
                int a = ring * sides + i, b = ring * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
            }
        mesh.Freeze();
        return mesh;
    }

    /// <summary>A unit sphere for the muzzle flash.</summary>
    private static MeshGeometry3D CreateFlashMesh()
    {
        var mesh = new MeshGeometry3D();
        const int rings = 6, sides = 10;
        for (int j = 0; j <= rings; j++)
        {
            double phi = Math.PI * j / rings;
            for (int i = 0; i < sides; i++)
            {
                double a = 2 * Math.PI * i / sides;
                mesh.Positions.Add(new Point3D(Math.Sin(phi) * Math.Cos(a), Math.Sin(phi) * Math.Sin(a), Math.Cos(phi)));
            }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < sides; i++)
            {
                int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
            }
        mesh.Freeze();
        return mesh;
    }
}
