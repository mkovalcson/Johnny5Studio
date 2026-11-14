using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private sealed class TreadLayer
    {
        internal GeometryModel3D Visual;
        internal MeshGeometry3D[] Frames;
        internal int Index = -1;
    }
    private sealed record TreadRotor(Matrix3D OriginalToWorld, Matrix3D WorldToLink,
        MatrixTransform3D Transform, Vector3D Axis, Point3D Centre, double DegreesPerPhase);
    private sealed class TreadSide
    {
        internal readonly List<TreadLayer> Layers = new();
        internal readonly List<TreadRotor> Rotors = new();
        internal double LastDistance = double.NaN;
    }
    private TreadSide _treadLeft, _treadRight;
    private int _treadPhases;
    private double _treadStep;
    internal double TreadGauge { get; private set; } = .42;

    private Matrix3D TreadLinkWorld(string name)
    {
        var result = Matrix3D.Identity;
        while (true)
        {
            if (_links.TryGetValue(name, out var link)) result.Append(link.Transform.Value);
            if (!_parentJointByChild.TryGetValue(name, out var joint)) break;
            result.Append(joint.Node.Transform.Value); name = joint.ParentLink;
        }
        return result;
    }
    private void InitializeTreads()
    {
        string path = Path.Combine(_baseDirectory, "Meshes", "Body", "Treads", "tread_animation.json");
        if (!File.Exists(path)) return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            int phases = root.GetProperty("phases").GetInt32();
            double step = root.GetProperty("phaseStep_m").GetDouble();
            if (phases < 2 || phases > 240 || !double.IsFinite(step) || step <= 0) return;
            var sides = root.GetProperty("sides");
            TreadSide ReadSide(string name)
            {
                var side = new TreadSide();
                if (!sides.TryGetProperty(name, out var definition)) return side;
                foreach (var layer in definition.GetProperty("layers").EnumerateArray())
                {
                    try
                    {
                        string link = layer.GetProperty("urdfLink").GetString(), visualName = layer.GetProperty("urdfVisual").GetString();
                        if (!_visuals.TryGetValue(visualName, out var visual) || !_linkVisuals.TryGetValue(link, out var visuals) || !visuals.Contains(visual))
                            throw new InvalidDataException("Missing tread visual: " + visualName);
                        var files = layer.GetProperty("frames").EnumerateArray().Select(p => p.GetString()).ToArray();
                        if (files.Length != phases) throw new InvalidDataException("Incorrect tread frame count.");
                        var frames = files.Select(file =>
                        {
                            string full = Path.GetFullPath(Path.Combine(_baseDirectory, file));
                            if (!ConfigPathService.IsWithin(_baseDirectory, full)) throw new InvalidDataException("Tread frame must be inside its model folder.");
                            var mesh = PrimitiveMeshes.Stl(full, new Vector3D(1, 1, 1)); mesh.Freeze(); return mesh;
                        }).ToArray();
                        side.Layers.Add(new TreadLayer { Visual = visual, Frames = frames });
                    }
                    catch (Exception ex) { Debug.WriteLine("Tread layer skipped: " + ex.Message); }
                }
                if (definition.TryGetProperty("rotators", out var rotators))
                    foreach (var rotor in rotators.EnumerateArray())
                    {
                        var a = rotor.GetProperty("axisWorld").EnumerateArray().Select(n => n.GetDouble()).ToArray();
                        var c = rotor.GetProperty("centreWorld").EnumerateArray().Select(n => n.GetDouble()).ToArray();
                        double degrees = rotor.GetProperty("degreesPerPhase").GetDouble();
                        if (a.Length != 3 || c.Length != 3 || a.Concat(c).Any(n => !double.IsFinite(n)) || !double.IsFinite(degrees)) continue;
                        var axis = new Vector3D(a[0], a[1], a[2]); if (axis.Length < 1e-9) continue; axis.Normalize();
                        foreach (var linkName in rotor.GetProperty("urdfLinks").EnumerateArray().Select(n => n.GetString()))
                        {
                            if (!_linkVisuals.TryGetValue(linkName, out var visuals)) { Debug.WriteLine("Tread rotor link missing: " + linkName); continue; }
                            var world = TreadLinkWorld(linkName); if (!world.HasInverse) continue;
                            var inverse = world; inverse.Invert();
                            foreach (var visual in visuals)
                            {
                                var original = visual.Transform.Value; original.Append(world);
                                // Capture first; install only after the complete manifest was parsed.
                                var transform = new MatrixTransform3D(visual.Transform.Value);
                                side.Rotors.Add(new TreadRotor(original, inverse, transform, axis, new Point3D(c[0], c[1], c[2]), degrees));
                                _pendingTreadTransforms.Add((visual, transform));
                            }
                        }
                    }
                return side;
            }
            _pendingTreadTransforms.Clear();
            var left = ReadSide("left"); var right = ReadSide("right");
            // Tread centre lines, rather than the outer hub caps, determine turn travel.
            if (TryTransformLinkPoint("body_track_front_roller_left_link", new Point3D(), out var l) &&
                TryTransformLinkPoint("body_track_front_roller_right_link", new Point3D(), out var r))
            { double gauge = (l - r).Length; if (double.IsFinite(gauge) && gauge > .01) TreadGauge = gauge; }
            foreach (var item in _pendingTreadTransforms) item.Visual.Transform = item.Transform;
            foreach (var layer in left.Layers.Concat(right.Layers))
            {
                // Animated STL frames contain complete faces; retire any static exterior-only back mesh.
                if (_openBackVisuals.TryGetValue(layer.Visual, out var back)) back.Geometry = null;
                layer.Visual.BackMaterial = layer.Visual.Material;
                _originalMaterials[layer.Visual] = (layer.Visual.Material, layer.Visual.BackMaterial);
            }
            _treadPhases = phases; _treadStep = step; _treadLeft = left; _treadRight = right;
            SetTreadTravel(0, 0);
        }
        catch (Exception ex) { Debug.WriteLine("Tread animation unavailable: " + ex.Message); }
        finally { _pendingTreadTransforms.Clear(); }
    }
    private readonly List<(GeometryModel3D Visual, MatrixTransform3D Transform)> _pendingTreadTransforms = new();

    internal void SetTreadTravel(double leftForwardMetres, double rightForwardMetres)
    {
        if (_treadLeft == null || !double.IsFinite(leftForwardMetres) || !double.IsFinite(rightForwardMetres)) return;
        void Apply(TreadSide side, double forward)
        {
            if (side.LastDistance == forward) return;
            side.LastDistance = forward;
            double phase = -forward / _treadStep; // Manifest increasing phases drive backwards.
            int index = (int)((Math.Floor(phase) % _treadPhases + _treadPhases) % _treadPhases);
            foreach (var layer in side.Layers)
                if (layer.Index != index) { layer.Visual.Geometry = WeatheredTreadFrame(layer.Frames[index]); layer.Index = index; }
            foreach (var rotor in side.Rotors)
            {
                var matrix = rotor.OriginalToWorld;
                matrix.RotateAt(new Quaternion(rotor.Axis, phase * rotor.DegreesPerPhase % 360), rotor.Centre);
                matrix.Append(rotor.WorldToLink); rotor.Transform.Matrix = matrix;
            }
        }
        Apply(_treadLeft, leftForwardMetres); Apply(_treadRight, rightForwardMetres);
    }
}
