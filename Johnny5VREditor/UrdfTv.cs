using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>Shared JSON handling for the TV commands' text values.</summary>
internal static class TvJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static T Read<T>(string text) where T : new() =>
        string.IsNullOrWhiteSpace(text) ? new T() : JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("TV settings must be an object.");
}

/// <summary>URDF TV Front: glide the camera past the robot to face the backdrop TV squarely.</summary>
public sealed class TvFrontCommand
{
    internal const string DefaultScreen = "backdrop_tv_screen_link";
    public double Seconds { get; set; } = 3;
    /// <summary>Fraction of the view the screen fills in its limiting direction (0.2-1).</summary>
    public double Fill { get; set; } = .85;
    /// <summary>Backdrop link whose box visual is the screen.</summary>
    public string Screen { get; set; } = DefaultScreen;
    public static TvFrontCommand Read(string text) { var c = TvJson.Read<TvFrontCommand>(text); c.Validate(); return c; }
    public void Validate()
    {
        if (!double.IsFinite(Seconds) || Seconds < 0 || Seconds > 30) throw new InvalidDataException("URDF TV Front seconds must be between 0 and 30.");
        if (!double.IsFinite(Fill) || Fill < .2 || Fill > 1) throw new InvalidDataException("URDF TV Front fill must be between 0.2 and 1.");
        if (string.IsNullOrWhiteSpace(Screen)) throw new InvalidDataException("URDF TV Front needs a screen link.");
    }
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
}

/// <summary>URDF TV Return: glide back to the timeline's normal camera.</summary>
public sealed class TvReturnCommand
{
    public double Seconds { get; set; } = 3;
    public static TvReturnCommand Read(string text) { var c = TvJson.Read<TvReturnCommand>(text); c.Validate(); return c; }
    public void Validate()
    {
        if (!double.IsFinite(Seconds) || Seconds < 0 || Seconds > 30) throw new InvalidDataException("URDF TV Return seconds must be between 0 and 30.");
    }
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
}

public enum TvFit { Contain, Cover, Stretch }

/// <summary>TV Show: an image or video drawn over the TV screen. An empty path blanks the screen.</summary>
public sealed class TvShowCommand
{
    internal static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    internal static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".asf" };
    /// <summary>Media path relative to Configuration (or absolute).</summary>
    public string Path { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TvFit Fit { get; set; } = TvFit.Contain;
    /// <summary>Video position shown at the command time.</summary>
    public double StartSeconds { get; set; }
    public bool Loop { get; set; }
    /// <summary>Video volume, 0-100.</summary>
    public double Volume { get; set; } = 100;
    [JsonIgnore] public bool IsBlank => string.IsNullOrWhiteSpace(Path);
    [JsonIgnore] public bool IsVideo => VideoExtensions.Contains(System.IO.Path.GetExtension(Path ?? "").ToLowerInvariant());
    public static TvShowCommand Read(string text) { var c = TvJson.Read<TvShowCommand>(text); c.Validate(); return c; }
    public void Validate()
    {
        if (!double.IsFinite(StartSeconds) || StartSeconds < 0) throw new InvalidDataException("TV Show startSeconds must be zero or more.");
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 100) throw new InvalidDataException("TV Show volume must be between 0 and 100.");
        if (!IsBlank)
        {
            string ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if (!ImageExtensions.Contains(ext) && !VideoExtensions.Contains(ext))
                throw new InvalidDataException("TV Show supports images (" + string.Join(" ", ImageExtensions) + ") and videos (" + string.Join(" ", VideoExtensions) + ").");
        }
    }
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
    internal string FullPath(string configRoot) => System.IO.Path.IsPathRooted(Path) ? Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(configRoot ?? "", Path));

    /// <summary>Copy media into Configuration\TvMedia (never overwriting) and return its portable relative path.</summary>
    internal static string Import(string configRoot, string source)
    {
        string full = System.IO.Path.GetFullPath(source);
        if (!File.Exists(full)) throw new FileNotFoundException("TV media not found.", full);
        var probe = new TvShowCommand { Path = full }; probe.Validate();
        if (ConfigPathService.IsWithin(configRoot, full)) return System.IO.Path.GetRelativePath(configRoot, full);
        string folder = System.IO.Path.Combine(configRoot, "TvMedia"); Directory.CreateDirectory(folder);
        string destination = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(full) + "-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(full));
        File.Copy(full, destination);
        return System.IO.Path.GetRelativePath(configRoot, destination);
    }
}

/// <summary>What the TV feature shows at one timeline instant.</summary>
internal sealed record TvState(double Weight, TvFrontCommand Front, bool Arrived, TvShowCommand Show, double MediaSeconds, double OverlayOpacity)
{
    internal static readonly TvState Inactive = new(0, null, false, null, 0, 0);
    internal bool Active => Weight > 0 && Front != null;
}

internal static class TvTimeline
{
    internal const double FadeSeconds = .3;
    internal static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Stateless evaluation: fold the enabled TV Front/Return commands up to the cursor into a camera
    /// blend weight (0 = normal camera, 1 = facing the TV); each glide starts from the weight in effect when
    /// it begins. The latest TV Show since the latest Front supplies the media.</summary>
    internal static TvState StateAt(IEnumerable<ServoCommand> commands, double cursor)
    {
        var list = commands.Where(c => !c.Disable && c.OffsetSeconds <= cursor + 1e-9 &&
            c.Servo is ServoNames.TvFront or ServoNames.TvReturn or ServoNames.TvShow).OrderBy(c => c.OffsetSeconds).ToList();
        double segStart = double.NegativeInfinity, segFrom = 0, segTo = 0, segSeconds = 0;
        double Weight(double at) => segSeconds <= 0 ? (at >= segStart ? segTo : segFrom) : segFrom + (segTo - segFrom) * Smooth((at - segStart) / segSeconds);
        TvFrontCommand front = null; double frontTime = double.NaN, frontArrival = double.NaN, returnTime = double.NaN, weightAtReturn = 0; bool returning = false;
        foreach (var c in list.Where(c => c.Servo is ServoNames.TvFront or ServoNames.TvReturn))
        {
            double at = c.OffsetSeconds, current = Weight(at);
            try
            {
                if (c.Servo == ServoNames.TvFront)
                {
                    var f = TvFrontCommand.Read(c.TextValue);
                    front = f; frontTime = at; returning = false;
                    segStart = at; segFrom = current; segTo = 1; segSeconds = f.Seconds * (1 - current);
                    frontArrival = at + segSeconds;
                }
                else if (front != null)
                {
                    var r = TvReturnCommand.Read(c.TextValue);
                    returning = true; returnTime = at; weightAtReturn = current;
                    segStart = at; segFrom = current; segTo = 0; segSeconds = r.Seconds * current;
                }
            }
            catch (Exception) { /* invalid settings are ignored during playback */ }
        }
        double weight = Weight(cursor);
        if (front == null || weight <= 0) return TvState.Inactive;
        double arrival = returning ? double.NaN : segStart + Math.Max(0, segSeconds);
        bool arrived = !returning && cursor >= arrival - 1e-9;
        double opacity = arrived ? Math.Clamp((cursor - arrival) / FadeSeconds, 0, 1)
            : returning && weightAtReturn >= 1 ? Math.Clamp(1 - (cursor - returnTime) / FadeSeconds, 0, 1) : 0;
        TvShowCommand show = null; double media = 0;
        var showCommand = list.LastOrDefault(c => c.Servo == ServoNames.TvShow && c.OffsetSeconds >= frontTime - 1e-9);
        if (showCommand != null)
        {
            // Media time counts from when the show is actually visible: the later of the command and the arrival.
            try { show = TvShowCommand.Read(showCommand.TextValue); media = cursor - Math.Max(showCommand.OffsetSeconds, frontArrival) + show.StartSeconds; }
            catch (Exception) { show = null; }
        }
        return new TvState(weight, front, arrived, show, media, show == null || show.IsBlank ? 0 : opacity);
    }

    /// <summary>Video position to show, given its duration (null while unknown): wraps when looping, otherwise holds the end.</summary>
    internal static double MediaPosition(double seconds, double? duration, bool loop)
    {
        seconds = Math.Max(0, seconds);
        if (duration is not > 0) return seconds;
        return loop ? seconds % duration.Value : Math.Min(seconds, duration.Value);
    }
}

/// <summary>Geometry of a flat box screen in world coordinates.</summary>
internal sealed record TvScreen(Point3D Center, Vector3D Normal, Vector3D Right, Vector3D Up, double Width, double Height)
{
    /// <summary>Screen from the 8 world corners of a box visual. The thinnest axis is the normal, oriented toward
    /// <paramref name="viewer"/> (the room); the face is the one nearest the viewer. Up is the axis closest to world +Z.</summary>
    internal static TvScreen FromBoxCorners(IReadOnlyList<Point3D> corners, Point3D viewer)
    {
        if (corners == null || corners.Count < 8) return null;
        var c0 = corners[0];
        var edges = corners.Skip(1).Select(p => p - c0).Where(v => v.Length > 1e-9).OrderBy(v => v.Length).ToList();
        // The three shortest non-zero edges from a corner of a box are its axes.
        var axes = new List<Vector3D>();
        foreach (var e in edges)
        {
            if (axes.All(a => Math.Abs(Vector3D.DotProduct(Unit(a), Unit(e))) < .2)) axes.Add(e);
            if (axes.Count == 3) break;
        }
        if (axes.Count < 3) return null;
        var center = new Point3D(corners.Average(p => p.X), corners.Average(p => p.Y), corners.Average(p => p.Z));
        var thin = axes.OrderBy(a => a.Length).First();
        var normal = Unit(thin);
        if (Vector3D.DotProduct(viewer - center, normal) < 0) normal = -normal;
        var inPlane = axes.Where(a => a != thin).ToList();
        var up = inPlane.OrderByDescending(a => Math.Abs(Unit(a).Z)).First();
        var side = inPlane.First(a => a != up);
        var upDir = Unit(up); if (upDir.Z < 0) upDir = -upDir;
        var right = Vector3D.CrossProduct(upDir, normal); right.Normalize();   // viewer's right when facing the screen
        return new TvScreen(center + normal * (thin.Length / 2), normal, right, upDir, side.Length, up.Length);
    }
    private static Vector3D Unit(Vector3D v) { v.Normalize(); return v; }

    /// <summary>Camera distance from the screen face so it fills <paramref name="fill"/> of a view with the given
    /// horizontal half-angle tangent and aspect (width / height).</summary>
    internal double FitDistance(double tanHalfHorizontal, double aspect, double fill)
    {
        double tanHalfVertical = tanHalfHorizontal / aspect;
        return Math.Max(Width / (2 * fill * tanHalfHorizontal), Height / (2 * fill * tanHalfVertical));
    }

    internal IEnumerable<Point3D> FaceCorners()
    {
        var r = Right * (Width / 2); var u = Up * (Height / 2);
        yield return Center - r + u; yield return Center + r + u; yield return Center + r - u; yield return Center - r - u;
    }
}
