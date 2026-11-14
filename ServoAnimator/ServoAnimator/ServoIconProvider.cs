using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

/// <summary>Loads the compact ServoNames icon set once for picklists and command rows.</summary>
internal static class ServoIconProvider
{
    private static readonly IReadOnlyDictionary<ServoNames, ImageSource> Icons = BuildIcons();

    public static ImageSource For(ServoNames servo) =>
        Icons.TryGetValue(BodyControlCatalog.Canonical(servo), out var icon) ? icon : null;

    private static IReadOnlyDictionary<ServoNames, ImageSource> BuildIcons()
    {
        var files = ControlIconFiles.Files;

        var icons = files.ToDictionary(pair => pair.Key, pair => Load(pair.Value));
        foreach(var pair in PairedBodyControls.All)
        {
            var group=new DrawingGroup();
            group.Children.Add(new ImageDrawing(icons[pair.Left],new System.Windows.Rect(0,0,22,22)));
            group.Children.Add(new ImageDrawing(icons[pair.Right],new System.Windows.Rect(26,0,22,22)));
            group.Children.Add(new GeometryDrawing(null,new Pen(Brushes.MediumPurple,2),Geometry.Parse("M 15,27 L 33,27 M 15,24 L 12,27 L 15,30 M 33,24 L 36,27 L 33,30")));
            var image=new DrawingImage(group);image.Freeze();icons[pair.Servo]=image;
        }
        // Audio belongs to the blue Head & Audio control family. Play is normally
        // export-only, but still has an icon wherever a command list exposes it.
        var audio = new DrawingImage(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(121, 169, 225)), null,
            Geometry.Parse("M 13,4 L 27,1 L 27,23 C 27,29 18,31 18,26 C 18,22 22,20 24,21 L 24,9 L 16,11 L 16,27 C 16,33 7,35 7,30 C 7,26 11,24 13,25 Z")));
        audio.Freeze(); icons[ServoNames.Play] = audio;
        return icons;
    }

    private static ImageSource Load(string fileName)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(
            $"pack://application:,,,/AnimationEditorPlayer;component/Assets/ControlIcons/{fileName}",
            UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
