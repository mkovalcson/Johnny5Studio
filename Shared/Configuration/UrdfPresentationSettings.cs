using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfBackgroundMode { Studio, Color, Image, MovingImage, Urdf3D }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfImageLayout { Stretch, Cover, Tile }

public sealed class UrdfPresentationSettings
{
    public const string ImageFolder = "Backdrops";
    public UrdfStageSettings Stage { get; set; } = new();
    public UrdfBackgroundMode BackgroundMode { get; set; } = UrdfBackgroundMode.Studio;
    public string BackgroundColor { get; set; } = "#00FF00";
    public string BackgroundImage { get; set; } = "";
    public UrdfImageLayout ImageLayout { get; set; } = UrdfImageLayout.Cover;
    public bool CleanUndockedView { get; set; }
    public double BackgroundMotionSpeed { get; set; } = 1;
    public int BackgroundFrameRate { get; set; } = 15;
    public double BackgroundZoomMinimum { get; set; } = 110;
    public double BackgroundZoomMaximum { get; set; } = 150;
    public double BackgroundPanAmount { get; set; } = 75;
    public double BackgroundTargetSeconds { get; set; } = 8;
    public int BackgroundSeed { get; set; } = 1;
    public bool BackgroundRotation { get; set; }
    public bool BackgroundHalfTurns { get; set; }
    public UrdfPresentationSettings Clone()
    {
        var copy = (UrdfPresentationSettings)MemberwiseClone(); copy.Stage = (Stage ?? new()).Clone(); return copy;
    }
    public Brush CreateBrush(string root, Brush studio)
    {
        if (BackgroundMode is UrdfBackgroundMode.Studio or UrdfBackgroundMode.Urdf3D) return studio;
        if (BackgroundMode == UrdfBackgroundMode.Color) return new SolidColorBrush((Color)ColorConverter.ConvertFromString(BackgroundColor));
        string path = Path.GetFullPath(Path.Combine(root, BackgroundImage));
        if (string.IsNullOrWhiteSpace(BackgroundImage) || !ConfigurationPaths.IsWithin(root, path)) throw new InvalidDataException("Choose a background image imported into Config.");
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
        // Static backdrops always show the whole picture. Keep ImageLayout readable
        // for older scenes, but do not let legacy crop/stretch settings distort them.
        var brush = new ImageBrush(bitmap)
        {
            Stretch = BackgroundMode == UrdfBackgroundMode.MovingImage ? Stretch.UniformToFill : Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        brush.Freeze(); return brush;
    }
    public static string ImportImage(string root, string source)
    {
        source = ImageSource(root, source);
        // Keep the source stable while validating and copying; original images are never changed.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        if (ConfigurationPaths.IsWithin(Path.Combine(root, ImageFolder), source))
        {
            string existing = Path.GetRelativePath(root, source); ConfigurationAssets.Destination(root, existing); return existing;
        }
        input.Position = 0; string hash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        string relative = ImageReference(root, source, hash);
        string destination = ConfigurationAssets.Destination(root, relative);
        bool Matches() => File.Exists(destination) && new FileInfo(destination).Length == input.Length && ConfigurationAssets.HashFile(destination) == hash;
        if (Matches()) return relative;
        if (File.Exists(destination))
        {
            relative = Path.Combine(ImageFolder, Path.GetFileNameWithoutExtension(relative) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(relative));
            destination = ConfigurationAssets.Destination(root, relative);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
            try { File.Move(temporary, destination); }
            catch (IOException) when (Matches()) { } // Another importer may have installed these exact bytes.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return relative;
    }
    private static string ImageSource(string root, string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Choose a backdrop image.");
        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) source = new Uri(source).LocalPath;
        return Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(root, source));
    }
    public static string ImageReference(string root, string source, string hash = null)
    {
        source = ImageSource(root, source);
        if (ConfigurationPaths.IsWithin(Path.Combine(root, ImageFolder), source)) return Path.GetRelativePath(root, source);
        hash ??= ConfigurationAssets.HashFile(source);
        string stem = Path.GetFileNameWithoutExtension(source);
        if (stem.Length > 90) stem = stem[..90];
        return Path.Combine(ImageFolder, stem + "-" + hash[..16].ToLowerInvariant() + Path.GetExtension(source).ToLowerInvariant());
    }
    public void EnsureLocalImage(string root)
    {
        if (BackgroundMode is not (UrdfBackgroundMode.Image or UrdfBackgroundMode.MovingImage)) return;
        string source = ImageSource(root, BackgroundImage);
        if (ConfigurationPaths.IsWithin(Path.Combine(root, ImageFolder), source))
        {
            string relative = Path.GetRelativePath(root, source); ConfigurationAssets.Destination(root, relative); BackgroundImage = relative;
        }
        else BackgroundImage = ImportImage(root, source);
    }
}
