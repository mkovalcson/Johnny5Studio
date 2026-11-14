using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    /// <summary>Visuals with this name are LCD screens: a strip curved about its local z axis
    /// (screen facing +x, z up). The picture wraps round the curve, left to right as seen from
    /// the front, and glows regardless of lighting.</summary>
    internal const string LcdVisualName = "johnny5_lcd_screen";
    private readonly List<ImageBrush> _lcdBrushes = new();
    /// <summary>Width over height of the screen surface, for drawing content to fit.</summary>
    internal double LcdAspect { get; private set; } = .5625;
    internal bool HasLcd => _lcdBrushes.Count > 0;

    private GeometryModel3D CreateLcdVisual(MeshGeometry3D source, XElement visual)
    {
        var mesh = source.Clone();
        double aMin = double.MaxValue, aMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue, radius = 0;
        foreach (var p in mesh.Positions)
        {
            double a = Math.Atan2(p.Y, p.X);
            aMin = Math.Min(aMin, a); aMax = Math.Max(aMax, a); zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z);
            radius = Math.Max(radius, Math.Sqrt(p.X * p.X + p.Y * p.Y));
        }
        double span = Math.Max(1e-9, aMax - aMin), height = Math.Max(1e-9, zMax - zMin);
        var uv = new PointCollection(mesh.Positions.Count);
        foreach (var p in mesh.Positions) uv.Add(new Point((Math.Atan2(p.Y, p.X) - aMin) / span, (zMax - p.Z) / height));
        mesh.TextureCoordinates = uv;
        mesh.Freeze();
        LcdAspect = span * radius / height;
        var brush = new ImageBrush(LcdRenderer.Render(LcdShowCommand.Default, null, LcdAspect))
        { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill };
        _lcdBrushes.Add(brush);
        var materials = new MaterialGroup();
        materials.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(6, 6, 6))));
        materials.Children.Add(new EmissiveMaterial(brush));
        return new GeometryModel3D(mesh, materials) { Transform = ParseOriginTransform(visual.Element("origin")) };
    }

    /// <summary>Show a picture on every LCD screen in the model.</summary>
    internal void SetLcdImage(ImageSource image)
    {
        foreach (var brush in _lcdBrushes) brush.ImageSource = image;
    }
}
