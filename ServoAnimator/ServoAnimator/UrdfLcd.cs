using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

/// <summary>LCD Show: what the waist LCD shows (it is revealed by opening the waist panels with
/// B_PanelsOpen). A picture when Image is set, otherwise Text. Without any LCD Show command the
/// screen shows <see cref="DefaultText"/>.</summary>
public sealed class LcdShowCommand
{
    internal const string DefaultText = "If you can read this, you should have asked me out on a date first!";
    internal const string DefaultColor = "#7CFFB2";
    internal const int MaxTextLength = 2000;
    /// <summary>Message drawn when there is no picture. Empty text leaves the screen dark.</summary>
    public string Text { get; set; } = "";
    /// <summary>Picture path relative to Configuration (or absolute); empty for text.</summary>
    public string Image { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TvFit Fit { get; set; } = TvFit.Contain;
    /// <summary>Text colour as #RRGGBB.</summary>
    public string Color { get; set; } = DefaultColor;
    [JsonIgnore] public bool HasImage => !string.IsNullOrWhiteSpace(Image);

    internal static LcdShowCommand Default => new() { Text = DefaultText };

    public static LcdShowCommand Read(string text) { var c = TvJson.Read<LcdShowCommand>(text); c.Validate(); return c; }

    public void Validate()
    {
        if ((Text ?? "").Length > MaxTextLength) throw new InvalidDataException("LCD Show text is limited to " + MaxTextLength + " characters.");
        if (HasImage && !TvShowCommand.ImageExtensions.Contains(Path.GetExtension(Image).ToLowerInvariant()))
            throw new InvalidDataException("LCD Show supports pictures (" + string.Join(" ", TvShowCommand.ImageExtensions) + ").");
        if (!TryParseColor(Color, out _)) throw new InvalidDataException("LCD Show colour must look like #7CFFB2.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);

    internal string Label() => HasImage ? "LCD Show " + Path.GetFileName(Image)
        : string.IsNullOrWhiteSpace(Text) ? "LCD Show (dark)" : "LCD Show \"" + (Text.Length > 30 ? Text[..30] + "..." : Text) + "\"";

    internal string FullImagePath(string configRoot) => Path.IsPathRooted(Image) ? Image : Path.GetFullPath(Path.Combine(configRoot ?? "", Image));

    internal static bool TryParseColor(string text, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(text) || text.Length != 7 || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>Copy a picture into Configuration\LcdMedia (never overwriting) and return its portable relative path.</summary>
    internal static string Import(string configRoot, string source)
    {
        string full = Path.GetFullPath(source);
        if (!File.Exists(full)) throw new FileNotFoundException("LCD picture not found.", full);
        new LcdShowCommand { Image = full }.Validate();
        if (ConfigPathService.IsWithin(configRoot, full)) return Path.GetRelativePath(configRoot, full);
        string folder = Path.Combine(configRoot, "LcdMedia"); Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(full) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(full));
        File.Copy(full, destination);
        return Path.GetRelativePath(configRoot, destination);
    }
}

internal static class LcdTimeline
{
    /// <summary>The LCD content at the cursor: the latest enabled LCD Show at or before it,
    /// or the default message.</summary>
    internal static LcdShowCommand StateAt(IEnumerable<ServoCommand> commands, double cursor)
    {
        var command = commands.Where(c => c.Servo == ServoNames.LcdShow && !c.Disable && c.OffsetSeconds <= cursor + 1e-9)
            .OrderBy(c => c.OffsetSeconds).LastOrDefault();
        if (command == null) return LcdShowCommand.Default;
        try { return LcdShowCommand.Read(command.TextValue); } catch { return LcdShowCommand.Default; }
    }
}

/// <summary>Draws LCD content to a bitmap shaped like the screen.</summary>
internal static class LcdRenderer
{
    private const int LongSide = 720;
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);
    internal static void ClearCache() => Cache.Clear();

    internal static ImageSource Render(LcdShowCommand show, string configRoot, double aspect)
    {
        aspect = double.IsFinite(aspect) && aspect > .05 && aspect < 20 ? aspect : .5625;
        int width = aspect >= 1 ? LongSide : Math.Max(16, (int)Math.Round(LongSide * aspect));
        int height = aspect >= 1 ? Math.Max(16, (int)Math.Round(LongSide / aspect)) : LongSide;
        string key = show.ToJson() + "|" + configRoot + "|" + width + "x" + height;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(4, 10, 8)), null, new Rect(0, 0, width, height));
            if (show.HasImage) DrawPicture(dc, show, configRoot, width, height);
            else if (!string.IsNullOrWhiteSpace(show.Text)) DrawText(dc, show, width, height);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        if (Cache.Count > 32) Cache.Clear();
        return Cache[key] = bitmap;
    }

    private static void DrawPicture(DrawingContext dc, LcdShowCommand show, string configRoot, int width, int height)
    {
        BitmapImage image;
        try
        {
            image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; image.UriSource = new Uri(show.FullImagePath(configRoot)); image.EndInit(); image.Freeze();
        }
        catch (Exception ex)
        {
            DrawText(dc, new LcdShowCommand { Text = "LCD picture not found:\n" + Path.GetFileName(show.Image) + "\n" + ex.GetType().Name, Color = "#FF8080" }, width, height);
            return;
        }
        double sx = width / image.Width, sy = height / image.Height;
        double scale = show.Fit == TvFit.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
        var target = show.Fit == TvFit.Stretch ? new Rect(0, 0, width, height)
            : new Rect((width - image.Width * scale) / 2, (height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        dc.DrawImage(image, target);
        dc.Pop();
    }

    private static void DrawText(DrawingContext dc, LcdShowCommand show, int width, int height)
    {
        LcdShowCommand.TryParseColor(show.Color, out var color);
        var brush = new SolidColorBrush(color); brush.Freeze();
        var face = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double margin = Math.Min(width, height) * .08, maxWidth = width - 2 * margin, maxHeight = height - 2 * margin;
        FormattedText text = null;
        for (double size = Math.Min(width, height) * .22; size >= 6; size *= .92)
        {
            text = new FormattedText(show.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0)
            { MaxTextWidth = maxWidth, TextAlignment = TextAlignment.Center, Trimming = TextTrimming.None };
            if (text.Height <= maxHeight && text.Width <= maxWidth + .5 && !BreaksWords(show.Text, face, size, maxWidth)) break;
        }
        dc.DrawText(text, new Point(margin, (height - text.Height) / 2));
    }

    /// <summary>True when one word alone is wider than the line, so wrapping would split it.</summary>
    private static bool BreaksWords(string text, Typeface face, double size, double maxWidth) =>
        text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Any(word =>
            new FormattedText(word, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.White, 1.0).WidthIncludingTrailingWhitespace > maxWidth);
}
