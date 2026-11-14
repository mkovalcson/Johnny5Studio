using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

internal static partial class Program
{
    private static void RearCoverAlignmentChecks(Dictionary<string, GeometryModel3D> visuals, string file, string model)
    {
        var cover = visuals["rear_cover_cad"];
        var shell = visuals["static_03"];
        // Landmarks on the outer mating arcs at the CAD seam (mesh-local Z=0).
        // Fit their circles and compare the vertical centers after URDF origins,
        // rather than relying on the overall bounds of these irregular parts.
        foreach (bool left in new[] { false, true })
        {
            var coverCenter = Center(cover, left
                ? new[] { (-133.625, 20.750), (-138.763, 1.573), (-132.250, -19.808) }
                : new[] { (133.081, 19.614), (137.540, -.375), (131.076, -19.808) });
            var shellCenter = Center(shell, left
                ? new[] { (-136.096, 12.458), (-138.119, -2.098), (-131.663, -21.382) }
                : new[] { (133.600, 18.168), (138.101, 2.398), (131.663, -21.382) });
            double difference = Math.Abs(coverCenter.Z - shellCenter.Z);
            Check(difference < .00002, model + ": rear cover mating arc aligns vertically within 0.02 mm on " + (left ? "left" : "right"));
        }
        var xml = XDocument.Load(file);
        var visualOrigin = xml.Descendants("visual").Single(v => v.Attribute("name")?.Value == "rear_cover_cad").Element("origin");
        var collisionOrigin = xml.Descendants("collision").Single(v => v.Attribute("name")?.Value == "auto_collision_rear_cover_cad").Element("origin");
        Check(XNode.DeepEquals(visualOrigin, collisionOrigin), model + ": rear cover collision placement matches the visible part");
        string output = PreviewDirectory("eye-surface-previews");
        Render(false); Render(true);

        Point3D Center(GeometryModel3D visual, (double X, double Y)[] landmarks)
        {
            var mesh = (MeshGeometry3D)visual.Geometry;
            var seam = mesh.Positions.Where(p => Math.Abs(p.Z) < .000002).ToArray();
            var points = landmarks.Select(l => seam.MinBy(p => Math.Pow(p.X - l.X / 1000, 2) + Math.Pow(p.Y - l.Y / 1000, 2))).ToArray();
            var a = points[0]; var b = points[1]; var c = points[2];
            double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            double aa = a.X * a.X + a.Y * a.Y, bb = b.X * b.X + b.Y * b.Y, cc = c.X * c.X + c.Y * c.Y;
            var center = new Point3D((aa * (b.Y - c.Y) + bb * (c.Y - a.Y) + cc * (a.Y - b.Y)) / d,
                (aa * (c.X - b.X) + bb * (a.X - c.X) + cc * (b.X - a.X)) / d, 0);
            return visual.Transform.Transform(center);
        }

        void Render(bool before)
        {
            var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(90, 90, 90)));
            group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(1, -1, -2)));
            foreach (string name in new[] { "rear_cover_cad", "static_03", "static_02" })
            {
                var part = visuals[name].Clone();
                if (before && name == "rear_cover_cad")
                {
                    var matrix = part.Transform.Value; matrix.Translate(new Vector3D(0, 0, .001575));
                    part.Transform = new MatrixTransform3D(matrix);
                }
                group.Children.Add(part);
            }
            var target = new Point3D(-.043096, .103, .016);
            var offset = new Vector3D(-.22, .32, .17);
            var view = new Viewport3D { Width = 800, Height = 600, Camera = new PerspectiveCamera(target + offset, -offset, new Vector3D(0, 0, 1), 24) };
            view.Children.Add(new ModelVisual3D { Content = group });
            var host = new Grid { Background = Brushes.DimGray }; host.Children.Add(view);
            host.Measure(new Size(800, 600)); host.Arrange(new Rect(0, 0, 800, 600)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, model + "-rear-cover-alignment-" + (before ? "before" : "after") + ".png")); encoder.Save(stream);
        }
    }
}
