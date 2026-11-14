using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>Text value of a FallApart timeline command: which way to play the
/// baked clip and how fast. Forwards the robot falls apart and stays in pieces;
/// reversed it starts in pieces and reassembles, then joint control resumes.</summary>
public sealed class FallApartCommand
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public bool Reverse { get; set; }
    public double Speed { get; set; } = 1;

    public static FallApartCommand Read(string text)
    {
        var command = string.IsNullOrWhiteSpace(text) ? new FallApartCommand()
            : JsonSerializer.Deserialize<FallApartCommand>(text, Options) ?? throw new InvalidDataException("Disassemble needs a settings object.");
        command.Validate(); return command;
    }
    public void Validate()
    {
        if (!double.IsFinite(Speed) || Speed < .1 || Speed > 10) throw new InvalidDataException("Disassemble speed must be between 0.1 and 10.");
    }
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    /// <summary>Accepts the "describe" label that 1.37.1 wrote by mistake; never written.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("describe")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string LegacyDescribe { get => null; set { } }
    public string Label() => Reverse ? "Reassemble" : "Disassemble";
}

/// <summary>Baked rigid-body clip written by FullBodyModel/Body/bake_fall_apart.py:
/// per link, the link frame in root-link coordinates for every frame.</summary>
internal sealed class FallApartClip
{
    internal const string RelativePath = @"Meshes\Body\FallApart\fall_apart.json";
    private readonly Dictionary<string, float[]> _poses;
    internal int Fps { get; }
    internal int FrameCount { get; }
    internal int ShudderFrames { get; }
    internal IReadOnlyCollection<string> Links => _poses.Keys;
    internal double DurationSeconds => (FrameCount - 1) / (double)Fps;

    private FallApartClip(int fps, int frames, int shudder, Dictionary<string, float[]> poses)
    { Fps = fps; FrameCount = frames; ShudderFrames = shudder; _poses = poses; }

    /// <summary>The clip beside a model, or null when it is absent or invalid (the effect is then unavailable).</summary>
    internal static FallApartClip Load(string modelDirectory)
    {
        string path = Path.Combine(modelDirectory ?? "", RelativePath);
        if (!File.Exists(path)) return null;
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Fall apart clip unavailable: " + ex.Message); return null; }
    }

    internal static FallApartClip Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported fall-apart clip version.");
        int fps = root.GetProperty("fps").GetInt32(), frames = root.GetProperty("frameCount").GetInt32();
        int shudder = root.TryGetProperty("shudderFrames", out var s) ? s.GetInt32() : 0;
        if (fps < 1 || fps > 240 || frames < 2 || frames > 100000 || shudder < 0 || shudder >= frames) throw new InvalidDataException("Invalid fall-apart timing.");
        var poses = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var link in root.GetProperty("links").EnumerateArray())
        {
            string name = link.GetProperty("link").GetString();
            byte[] bytes = Convert.FromBase64String(link.GetProperty("poses").GetString());
            if (string.IsNullOrEmpty(name) || bytes.Length != frames * 7 * sizeof(float)) throw new InvalidDataException("Invalid fall-apart poses: " + name);
            var values = new float[frames * 7];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException();
            if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Non-finite fall-apart pose: " + name);
            poses[name] = values;
        }
        if (poses.Count == 0) throw new InvalidDataException("The fall-apart clip has no links.");
        return new FallApartClip(fps, frames, shudder, poses);
    }

    internal bool HasLink(string link) => _poses.ContainsKey(link);

    /// <summary>Link frame to root-link coordinates at a fractional frame (clamped),
    /// interpolating position linearly and rotation spherically.</summary>
    internal Matrix3D Sample(string link, double frame)
    {
        var p = _poses[link];
        frame = Math.Clamp(double.IsFinite(frame) ? frame : 0, 0, FrameCount - 1);
        int a = (int)Math.Floor(frame), b = Math.Min(a + 1, FrameCount - 1); double t = frame - a;
        Vector3D Position(int f) => new(p[f * 7], p[f * 7 + 1], p[f * 7 + 2]);
        Quaternion Rotation(int f) => new(p[f * 7 + 3], p[f * 7 + 4], p[f * 7 + 5], p[f * 7 + 6]);
        var q = t == 0 ? Rotation(a) : Quaternion.Slerp(Rotation(a), Rotation(b), t);
        return FallApartMath.Compose(Position(a) + (Position(b) - Position(a)) * t, q);
    }
}

internal static class FallApartTimeline
{
    /// <summary>Clip frame shown at the cursor, or null when the robot is whole
    /// and normal joint control applies. The latest enabled FallApart command at or
    /// before the cursor wins. Forwards holds the final frame; reversed releases the
    /// override once the clip is back at frame 0.</summary>
    internal static double? FrameAt(IEnumerable<ServoCommand> commands, double cursor, int fps, int frameCount)
    {
        var command = commands.Where(c => c.Servo == ServoNames.FallApart && !c.Disable && c.OffsetSeconds <= cursor + 1e-9)
            .OrderBy(c => c.OffsetSeconds).LastOrDefault();
        if (command == null || fps < 1 || frameCount < 2) return null;
        FallApartCommand settings;
        try { settings = FallApartCommand.Read(command.TextValue); } catch { return null; }
        double last = frameCount - 1;
        double played = Math.Clamp((cursor - command.OffsetSeconds) * settings.Speed * fps, 0, last);
        if (!settings.Reverse) return played;
        double frame = last - played;
        return frame <= 0 ? null : frame;
    }

    /// <summary>How far each part has moved from the live (servo-driven) pose toward
    /// the baked pose: eases in over the shudder, so a posed robot settles into the
    /// clip instead of snapping, and reassembly hands back to the live pose.</summary>
    internal static double BlendWeight(double frame, int shudderFrames)
    {
        if (shudderFrames <= 0) return 1;
        double t = Math.Clamp(frame / shudderFrames, 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal static double DurationSeconds(FallApartClip clip, FallApartCommand settings) => clip.DurationSeconds / settings.Speed;
}

internal static class FallApartMath
{
    internal static Matrix3D Compose(Vector3D position, Quaternion rotation)
    {
        var m = Matrix3D.Identity;
        if (!rotation.IsIdentity) { rotation.Normalize(); m.Rotate(rotation); }
        m.Translate(position); return m;
    }

    /// <summary>Rigid (rotation + translation) interpolation between two transforms.</summary>
    internal static Matrix3D Blend(Matrix3D from, Matrix3D to, double weight)
    {
        if (weight >= 1) return to;
        if (weight <= 0) return from;
        var a = new Vector3D(from.OffsetX, from.OffsetY, from.OffsetZ); var b = new Vector3D(to.OffsetX, to.OffsetY, to.OffsetZ);
        return Compose(a + (b - a) * weight, Quaternion.Slerp(Rotation(from), Rotation(to), weight));
    }

    /// <summary>Rotation of a rigid WPF matrix (row-vector convention).</summary>
    internal static Quaternion Rotation(Matrix3D m)
    {
        // WPF stores the rotation transposed relative to column-vector formulas.
        double r00 = m.M11, r01 = m.M21, r02 = m.M31, r10 = m.M12, r11 = m.M22, r12 = m.M32, r20 = m.M13, r21 = m.M23, r22 = m.M33;
        double trace = r00 + r11 + r22, x, y, z, w;
        if (trace > 0)
        {
            double s = Math.Sqrt(trace + 1) * 2; w = .25 * s; x = (r21 - r12) / s; y = (r02 - r20) / s; z = (r10 - r01) / s;
        }
        else if (r00 > r11 && r00 > r22)
        {
            double s = Math.Sqrt(1 + r00 - r11 - r22) * 2; w = (r21 - r12) / s; x = .25 * s; y = (r01 + r10) / s; z = (r02 + r20) / s;
        }
        else if (r11 > r22)
        {
            double s = Math.Sqrt(1 + r11 - r00 - r22) * 2; w = (r02 - r20) / s; x = (r01 + r10) / s; y = .25 * s; z = (r12 + r21) / s;
        }
        else
        {
            double s = Math.Sqrt(1 + r22 - r00 - r11) * 2; w = (r10 - r01) / s; x = (r02 + r20) / s; y = (r12 + r21) / s; z = .25 * s;
        }
        var q = new Quaternion(x, y, z, w); q.Normalize(); return q;
    }
}
