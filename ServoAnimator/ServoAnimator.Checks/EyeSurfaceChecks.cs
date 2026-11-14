using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using ServoAnimator;

internal static partial class Program
{
    private static void EyeSurfaceChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string output = PreviewDirectory("eye-surface-previews"); Directory.CreateDirectory(output);
        foreach (string model in new[] { "Johnny5Head", "Johnny5FullBody" })
        {
            string root = Path.GetFullPath(Path.Combine("Config", "Models", "Robots", model));
            string file = Path.Combine(root, "johnny5_head.urdf");
            var scene = UrdfScene.Load(file, optimizeExterior: false);
            var visuals = (Dictionary<string, GeometryModel3D>)typeof(UrdfScene).GetField("_visuals", flags).GetValue(scene);
            int checkedVisuals = 0;
            var neckParts = new HashSet<string>();
            foreach (var visual in XDocument.Load(file).Descendants("visual"))
            {
                var geometry = visual.Element("geometry");
                string source = geometry?.Element("mesh")?.Attribute("filename")?.Value;
                if (!PrimitiveMeshes.IsRoundedHeadSurface(source)) continue;
                var actual = (MeshGeometry3D)visuals[visual.Attribute("name").Value].Geometry;
                var scaleText = geometry.Element("mesh").Attribute("scale")?.Value ?? "1 1 1";
                var scaleValues = scaleText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var raw = PrimitiveMeshes.Stl(Path.Combine(root, source), new Vector3D(scaleValues[0], scaleValues[1], scaleValues[2]));
                Check(actual.Positions.SequenceEqual(raw.Positions) && actual.TriangleIndices.SequenceEqual(raw.TriangleIndices) && actual.Bounds == raw.Bounds,
                    model + ": smoothing preserves every eye vertex, triangle, hole and dimension");
                Check(actual.Normals.All(n => double.IsFinite(n.X + n.Y + n.Z) && Math.Abs(n.Length - 1) < 1e-6), "Eye normals are finite unit vectors");
                int changed = actual.Normals.Zip(raw.Normals).Count(pair => (pair.First - pair.Second).Length > .001);
                Check(changed > 0, model + ": curved eye surfaces receive interpolated normals: " + source);
                var collisionVersion = (MeshGeometry3D)typeof(UrdfScene).GetMethod("CreateGeometry", flags).Invoke(scene, new object[] { geometry, false });
                Check(collisionVersion.Normals.SequenceEqual(raw.Normals) && !ReferenceEquals(collisionVersion, actual), "Collision geometry retains original face normals and a separate cache entry");
                if (visual.Attribute("name").Value is "neck_static_black" or "neck_static_cad_e9e9eb" or "simple_mouth_static_e9e9eb")
                {
                    string part = visual.Attribute("name").Value; neckParts.Add(part);
                    Render(raw, Path.Combine(output, model + "-" + part + "-before.png"));
                    Render(actual, Path.Combine(output, model + "-" + part + "-after.png"));
                }
                if (visual.Attribute("name").Value is "left_eye_gimbal_a0a0a0" or "left_eye_tube" or "vent_left_fin1_01" or "vent_right_fin1_01" or "static_01" or "static_02" or "static_03" or "rear_cover_cad" or "upper_left_flap_panel" or "upper_right_flap_panel" or "nose_body_01" or "nose_basket_01")
                {
                    string part = visual.Attribute("name").Value switch { "left_eye_tube" => "cylinder", "left_eye_gimbal_a0a0a0" => "gimbal", var name => name };
                    Render(raw, Path.Combine(output, model + "-" + part + "-before.png"));
                    Render(actual, Path.Combine(output, model + "-" + part + "-after.png"));
                }
                if(visual.Attribute("name").Value.StartsWith("cad_shoulder_"))
                {
                    string part=visual.Attribute("name").Value;
                    Render(raw,Path.Combine(output,model+"-"+part+"-before.png"));
                    Render(actual,Path.Combine(output,model+"-"+part+"-after.png"));
                }
                checkedVisuals++;
            }
            Check(checkedVisuals >= 36, "Eyes, nose, ten vent fins, vent drives, side panels, curved flaps and rear cover use smooth curved surfaces: " + model);
            Check(neckParts.Count == 3, model + ": rubber bellows and both upper/lower piston socket pairs have smooth shading");
            RearCoverAlignmentChecks(visuals, file, model);
        }
        var cylinder = PrimitiveMeshes.Cylinder(.05, .1, 24);
        var smooth = PrimitiveMeshes.SmoothSurfaceNormals(cylinder);
        Check(cylinder.Positions.SequenceEqual(smooth.Positions), "Cylinder silhouette is preserved");
        for (int i = 0; i < cylinder.Positions.Count; i++)
        {
            var original = cylinder.Normals[i]; var normal = smooth.Normals[i];
            if (Math.Abs(original.Z) > .99)
                Check(Vector3D.DotProduct(original, normal) > .99999, "Cap normals stay flat across the sharp cylinder rim");
            else
                Check(Vector3D.DotProduct(original, normal) > .99999, "Curved wall normals follow the cylinder radius without flat bands");
        }
        Check(!PrimitiveMeshes.IsRoundedEyeSurface("eye_gimbal_a0a0a0_collision_full.stl") && !PrimitiveMeshes.IsRoundedEyeSurface("shoulder.stl"),
            "Smoothing excludes collision files and unrelated robot parts");
        Check(!PrimitiveMeshes.IsRoundedHeadSurface("Meshes/SimpleMouth/static_737373.stl")
            && !PrimitiveMeshes.IsRoundedHeadSurface("Meshes/SimplifiedHead2/vent_left_fin1_collision_full.stl"),
            "Head smoothing excludes matching mouth colors and vent collision meshes");
        Check(!PrimitiveMeshes.IsRoundedHeadSurface("Meshes/NeckAssembly/left_top_ball_collision_full.stl")
            && !PrimitiveMeshes.IsRoundedHeadSurface("Meshes/NeckAssembly/left_piston.stl")
            && !PrimitiveMeshes.IsRoundedHeadSurface("Meshes/Other/left_top_ball.stl"),
            "Neck smoothing is limited to the selected visual meshes");
        Check(PrimitiveMeshes.IsRoundedHeadSurface("Meshes/Body/shoulder_cad_sleeve_left.stl")
            && PrimitiveMeshes.IsRoundedHeadSurface("Meshes/Body/shoulder_cad_sleeve_right.stl")
            && !PrimitiveMeshes.IsRoundedHeadSurface("Meshes/Body/shoulder_cad_sleeve_left_collision_full.stl")
            && !PrimitiveMeshes.IsRoundedHeadSurface("Meshes/Other/shoulder_cad_sleeve_left.stl"),
            "Both shoulder sleeves receive curved shading without changing collision or unrelated meshes");

        void Render(MeshGeometry3D mesh, string path)
        {
            var bounds = mesh.Bounds;
            var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(65, 65, 65)));
            group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, 1, -2)));
            var material = new MaterialGroup(); material.Children.Add(new DiffuseMaterial(Brushes.LightGray)); material.Children.Add(new SpecularMaterial(Brushes.White, 32));
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
            var offset = new Vector3D(.35, -.5, 1); offset.Normalize(); offset *= size * 2.5;
            var viewport = new Viewport3D { Width = 600, Height = 600, Camera = new PerspectiveCamera(center + offset, -offset, new Vector3D(0, 1, 0), 30) { NearPlaneDistance = .00001, FarPlaneDistance = 100 } };
            viewport.Children.Add(new ModelVisual3D { Content = group });
            var host = new Grid { Width = 600, Height = 600, Background = Brushes.DimGray }; host.Children.Add(viewport);
            host.Measure(new Size(600, 600)); host.Arrange(new Rect(0, 0, 600, 600)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(600, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
        }
    }
}
