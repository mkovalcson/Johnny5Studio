using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
namespace ServoAnimator;

/// <summary>
/// TipToe: both track assemblies (track, tread and shroud parts) rotate about the pelvis drum axis,
/// front down and rear up, by up to 61.27 degrees (the front-top tread is then vertical to the body).
/// The caster is the CAD four-bar linkage (marker links body_caster_linkage_*):
///   A upper brace body hinge, B upper brace head hinge (centre of its 45 degree chamfer, over the column),
///   C lower brace body hinge, D lower brace head hinge (collar ring).
/// The one-piece upper brace (body_caster_head_link with the swivel top housing, plus the hinge's rear leaf)
/// swings nose-down about A in step with the tracks, up to 41.7 degrees (CAD). The lower brace (collar braces)
/// follows about C and the column (collar, bellows, fork, wheels) is the coupler B-D, so it leans slightly.
/// The whole robot then pitches back about the drum, keeping the front rollers planted, until the caster wheel
/// is on the floor again; the robot rises so the treads touch the floor and the caster wheels roll in.
///
/// Built once per URDF from the model's own visual geometry (body_root_link frame, metres, +X
/// forward, +Z up), so the Desktop editor, VR Editor and VR Player all use identical numbers.
/// </summary>
public sealed class TipToeGeometry
{
    public const double MaximumRadians = 61.27 * Math.PI / 180;
    /// <summary>Upper brace swing at full TipToe (CAD rest and tiptoe side views).</summary>
    public const double UpperBraceMaximumRadians = 41.7 * Math.PI / 180;
    public const string PivotLink = "body_pelvis_drum_link";
    public const string AnchorLink = "body_track_front_hub_left_link";
    public const string FrontRollerLink = "body_track_front_roller_left_link", CasterWheelLink = "body_caster_wheel_l_link", CasterAxleLink = "body_caster_axle_link";
    public const string UpperBodyHinge = "body_caster_linkage_upper_body_link", UpperHeadHinge = "body_caster_linkage_upper_head_link",
        LowerBodyHinge = "body_caster_linkage_lower_body_link", LowerHeadHinge = "body_caster_linkage_lower_head_link";
    public enum Part { Body, Track, UpperBrace, LowerBrace, Column }
    public static bool IsTrackLink(string link) => link.StartsWith("body_track_", StringComparison.Ordinal)
        || link.StartsWith("body_tread_", StringComparison.Ordinal) || link.StartsWith("body_shroud_", StringComparison.Ordinal);
    public static bool IsCasterLink(string link) => link.StartsWith("body_caster_", StringComparison.Ordinal);
    private static readonly string[] columnPrefixes = { "body_caster_collar", "body_caster_spring", "body_caster_yoke", "body_caster_fork",
        "body_caster_axle", "body_caster_wheel", "body_caster_hub", "body_caster_top_plate", "body_caster_detail_collar_pin", LowerHeadHinge };
    /// <summary>Which rigid TipToe group a direct child of the root link moves with.</summary>
    public static Part PartOf(string link)
    {
        if (IsTrackLink(link)) return Part.Track;
        if (link is "body_caster_head_link" or "body_caster_detail_hinge_rear_leaf_link" or UpperHeadHinge) return Part.UpperBrace;
        if (link.StartsWith("body_caster_detail_collar_brace", StringComparison.Ordinal)) return Part.LowerBrace;
        foreach (var prefix in columnPrefixes) if (link.StartsWith(prefix, StringComparison.Ordinal)) return Part.Column;
        return Part.Body;
    }

    /// <summary>The pose at one TipToe angle. Lift/Forward move the drum (metres); Pitch tips the whole robot back about
    /// the drum (radians, positive = front up); UpperBrace/LowerBrace are the brace swings (radians, positive = head end
    /// down); ColumnLean is the column's forward lean (radians); ColumnShift moves the collar hinge D (x, z metres, body
    /// frame); CasterRoll is the floor distance a wheel of <see cref="CasterWheelRadius"/> rolls for the rotation it needs
    /// on top of the column's and the robot's own rotation.</summary>
    public readonly record struct Pose(float Lift, float Forward, float Pitch, float UpperBrace, float LowerBrace, float ColumnLean, Vector2 ColumnShift, float CasterRoll);

    public Vector3 Pivot { get; }
    public float CasterWheelRadius { get; }
    public Vector2 HingeA { get; }
    public Vector2 HingeB { get; }
    public Vector2 HingeC { get; }
    public Vector2 HingeD { get; }
    private const int Steps = 128;
    private readonly float[] lift, forward, pitch, roll;
    private readonly float upper, lower, column;
    private TipToeGeometry(Vector3 pivot, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float wheelRadius, float[] lift, float[] forward, float[] pitch, float[] roll)
    {
        Pivot = pivot; HingeA = a; HingeB = b; HingeC = c; HingeD = d; CasterWheelRadius = wheelRadius;
        this.lift = lift; this.forward = forward; this.pitch = pitch; this.roll = roll;
        upper = Vector2.Distance(a, b); lower = Vector2.Distance(c, d); column = Vector2.Distance(b, d);
    }
    public float UpperBraceLength => upper;
    public float LowerBraceLength => lower;

    private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);
    /// <summary>Rotates an (x, z) point about centre by radians, positive moving +X down (as <see cref="RotationAbout"/>).</summary>
    private static Vector2 Rotate(Vector2 p, Vector2 centre, float radians)
    {
        var v = p - centre; float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return centre + new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
    }
    /// <summary>Four-bar: upper brace swing (head end down) to the lower brace swing, column lean and the hinges B and D.</summary>
    public (float Lower, float Lean, Vector2 B, Vector2 D) Linkage(float upperSwing)
    {
        var b = Rotate(HingeB, HingeA, -upperSwing);
        float distance = Vector2.Distance(HingeC, b), along = (lower * lower - column * column + distance * distance) / (2 * distance);
        float h = MathF.Sqrt(MathF.Max(0, lower * lower - along * along));
        var mid = HingeC + along * (b - HingeC) / distance; var normal = new Vector2(-(b - HingeC).Y, (b - HingeC).X) / distance;
        var guess = HingeD + (b - HingeB);
        var d1 = mid + h * normal; var d2 = mid - h * normal;
        var d = Vector2.Distance(d1, guess) <= Vector2.Distance(d2, guess) ? d1 : d2;
        // Swing: the brace's head end goes down (its angle grows, the brace points backward). Lean: the column top goes forward.
        static float Wrap(float a) => MathF.IEEERemainder(a, 2 * MathF.PI);
        return (Wrap(Angle(d - HingeC) - Angle(HingeD - HingeC)), Wrap(Angle(HingeB - HingeD) - Angle(b - d)), b, d);
    }

    public Pose At(double radians)
    {
        double clamped = Math.Clamp(radians, 0, MaximumRadians);
        if (!(clamped > 0)) return default;
        double position = clamped / MaximumRadians * Steps;
        int i = Math.Min(Steps - 1, (int)Math.Floor(position)); float t = (float)(position - i);
        float L(float[] v) => v[i] + (v[i + 1] - v[i]) * t;
        float swing = (float)(clamped / MaximumRadians * UpperBraceMaximumRadians);
        var (lowerSwing, lean, _, d) = Linkage(swing);
        return new(L(lift), L(forward), L(pitch), swing, lowerSwing, lean, d - HingeD, L(roll));
    }
    /// <summary>Row-vector rotation about the drum axis (+Y): positive angles move +X (front) down.</summary>
    public static Matrix4x4 RotationAbout(Vector3 pivot, float radians) =>
        Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationY(radians) * Matrix4x4.CreateTranslation(pivot);
    private static Vector3 Xz(Vector2 p) => new(p.X, 0, p.Y);
    /// <summary>Motion of a part in the body frame (drum fixed), before the robot pitches back and rises.</summary>
    private Matrix4x4 PartMotion(Part part, double radians, Pose pose) => part switch
    {
        Part.Track => RotationAbout(Pivot, (float)Math.Clamp(radians, 0, MaximumRadians)),
        Part.UpperBrace => RotationAbout(Xz(HingeA), -pose.UpperBrace),
        Part.LowerBrace => RotationAbout(Xz(HingeC), -pose.LowerBrace),
        Part.Column => RotationAbout(Xz(HingeD), pose.ColumnLean) * Matrix4x4.CreateTranslation(pose.ColumnShift.X, 0, pose.ColumnShift.Y),
        _ => Matrix4x4.Identity,
    };
    /// <summary>The robot pitches back about the drum, then the drum rises and moves forward.</summary>
    public static Matrix4x4 Placement(Vector3 pivot, Pose pose) => RotationAbout(pivot, -pose.Pitch) * Matrix4x4.CreateTranslation(pose.Forward, 0, pose.Lift);
    /// <summary>Extra transform (in the body_root_link frame) for a direct child of the root link.</summary>
    public Matrix4x4 RootChildMotion(string link, double radians)
    {
        var pose = At(radians);
        if (pose == default) return Matrix4x4.Identity;
        return PartMotion(PartOf(link), radians, pose) * Placement(Pivot, pose);
    }

    /// <summary>Points are (x, z) in the root frame; only their outline matters for floor contact (every rotation is about +Y).</summary>
    public static TipToeGeometry Build(Vector3 pivot, Vector2 a, Vector2 b, Vector2 c, Vector2 d, IReadOnlyList<Vector2> track, IReadOnlyList<Vector2> column,
        Vector3 frontRoller, Vector3 casterWheel, float casterWheelRadius = .075f)
    {
        if (track.Count == 0 || column.Count == 0) throw new InvalidDataException("TipToe needs track and caster geometry.");
        var trackHull = Hull(track); var columnHull = Hull(column);
        var lift = new float[Steps + 1]; var forward = new float[Steps + 1]; var pitch = new float[Steps + 1]; var roll = new float[Steps + 1];
        var geometry = new TipToeGeometry(pivot, a, b, c, d, casterWheelRadius, lift, forward, pitch, roll);
        var drum = new Vector2(pivot.X, pivot.Z); var roller = new Vector2(frontRoller.X, frontRoller.Z); var axle = new Vector2(casterWheel.X, casterWheel.Z);
        static float MinZ(Vector2[] points, Func<Vector2, Vector2> move) { float min = float.MaxValue; foreach (var p in points) min = Math.Min(min, move(p).Y); return min; }
        float ground = MinZ(trackHull, p => p), casterOffset = MinZ(columnHull, p => p) - ground;
        for (int i = 1; i <= Steps; i++)
        {
            float theta = (float)(MaximumRadians * i / Steps), swing = (float)(UpperBraceMaximumRadians * i / Steps);
            var (_, lean, _, dNow) = geometry.Linkage(swing); var shift = dNow - d;
            Vector2 Track(Vector2 p) => Rotate(p, drum, theta);
            Vector2 Column(Vector2 p) => Rotate(p, d, lean) + shift;
            // For a pitch-back angle: lift (treads on the floor), forward shift (front rollers planted) and the caster gap.
            (float Lift, float Forward, float Gap) Place(float beta)
            {
                float rise = ground - MinZ(trackHull, p => Rotate(Track(p), drum, -beta));
                float ahead = roller.X - Rotate(Track(roller), drum, -beta).X;
                return (rise, ahead, MinZ(columnHull, p => Rotate(Column(p), drum, -beta)) + rise - ground - casterOffset);
            }
            float lo = (float)(-30 * Math.PI / 180), hi = (float)(30 * Math.PI / 180);
            for (int k = 0; k < 40; k++) { float mid = (lo + hi) / 2; if (Place(mid).Gap > 0) lo = mid; else hi = mid; }
            float pitchBack = (lo + hi) / 2; var (riseNow, aheadNow, _) = Place(pitchBack);
            lift[i] = riseNow; forward[i] = aheadNow; pitch[i] = pitchBack;
            // Rolling without slipping: moving distance s forward turns a wheel s/r about +Y. The column lean and the
            // robot's pitch already turn it by (lean - pitch), so the wheel itself turns s/r - (lean - pitch).
            float travel = Rotate(Column(axle), drum, -pitchBack).X + aheadNow - axle.X;
            roll[i] = travel - (lean - pitchBack) * casterWheelRadius;
        }
        return geometry;
    }
    /// <summary>Convex hull (monotone chain) of (x, z) points.</summary>
    private static Vector2[] Hull(IReadOnlyList<Vector2> points)
    {
        var p = points.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToArray();
        if (p.Length < 3) return p;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new Vector2[p.Length * 2]; int k = 0;
        foreach (var v in p) { while (k >= 2 && Cross(hull[k - 2], hull[k - 1], v) <= 0) k--; hull[k++] = v; }
        for (int i = p.Length - 2, t = k + 1; i >= 0; i--) { while (k >= t && Cross(hull[k - 2], hull[k - 1], p[i]) <= 0) k--; hull[k++] = p[i]; }
        return hull[..(k - 1)];
    }

    private static readonly Dictionary<string, (DateTime Stamp, TipToeGeometry? Geometry)> cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Reads the URDF's track, shroud and caster column visuals and the four-bar hinge markers (meshes and primitives). Null when the model has no tracks.</summary>
    public static TipToeGeometry? FromUrdf(string urdfPath)
    {
        string full = Path.GetFullPath(urdfPath); var stamp = File.GetLastWriteTimeUtc(full);
        lock (cache) if (cache.TryGetValue(full, out var hit) && hit.Stamp == stamp) return hit.Geometry;
        TipToeGeometry? geometry = null;
        try { geometry = Load(full); } catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException) { geometry = null; }
        lock (cache) cache[full] = (stamp, geometry);
        return geometry;
    }

    private static TipToeGeometry? Load(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Empty URDF.");
        string folder = Path.GetDirectoryName(path)!;
        var joints = root.Elements("joint").ToDictionary(j => (string)j.Element("child")!.Attribute("link")!, j => ((string)j.Element("parent")!.Attribute("link")!, Origin(j.Element("origin"))));
        var worlds = new Dictionary<string, Matrix4x4>();
        Matrix4x4 World(string link)
        {
            if (worlds.TryGetValue(link, out var w)) return w;
            w = joints.TryGetValue(link, out var j) ? j.Item2 * World(j.Item1) : Matrix4x4.Identity;
            return worlds[link] = w;
        }
        string[] markers = { UpperBodyHinge, UpperHeadHinge, LowerBodyHinge, LowerHeadHinge };
        if (!joints.ContainsKey(PivotLink) || !joints.ContainsKey(FrontRollerLink) || !joints.ContainsKey(CasterAxleLink) || markers.Any(m => !joints.ContainsKey(m))) return null;
        Vector2 Marker(string link) { var t = World(link).Translation; return new(t.X, t.Z); }
        var pivot = World(PivotLink).Translation;
        var frontRoller = World(FrontRollerLink).Translation; var casterWheel = World(CasterAxleLink).Translation;
        float wheelRadius = .075f;
        var wheelCylinder = root.Elements("link").FirstOrDefault(l => (string?)l.Attribute("name") == CasterWheelLink)?.Element("visual")?.Element("geometry")?.Element("cylinder");
        if (wheelCylinder != null && float.TryParse((string?)wheelCylinder.Attribute("radius"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) && r > .01f) wheelRadius = r;
        // Links move with their root-child ancestor's part (the collar pins hang below the collar).
        string RootChild(string link) { while (joints.TryGetValue(link, out var j) && joints.ContainsKey(j.Item1)) link = j.Item1; return link; }
        var track = new HashSet<Vector2>(); var column = new HashSet<Vector2>();
        foreach (var link in root.Elements("link"))
        {
            string name = (string)link.Attribute("name")!;
            var part = PartOf(RootChild(name));
            if (part is not (Part.Track or Part.Column)) continue;
            var set = part == Part.Track ? track : column;
            foreach (var visual in link.Elements("visual"))
            {
                var m = Origin(visual.Element("origin")) * World(name);
                foreach (var p in Points(visual.Element("geometry")?.Elements().FirstOrDefault(), folder))
                {
                    var q = Vector3.Transform(p, m);
                    set.Add(new(MathF.Round(q.X, 4), MathF.Round(q.Z, 4)));
                }
            }
        }
        return track.Count == 0 || column.Count == 0 ? null : Build(pivot, Marker(UpperBodyHinge), Marker(UpperHeadHinge), Marker(LowerBodyHinge), Marker(LowerHeadHinge),
            track.ToArray(), column.ToArray(), frontRoller, casterWheel, wheelRadius);
    }

    private static IEnumerable<Vector3> Points(XElement? geometry, string folder)
    {
        if (geometry == null) yield break;
        float F(string attribute) => float.Parse((string)geometry.Attribute(attribute)!, CultureInfo.InvariantCulture);
        switch (geometry.Name.LocalName)
        {
            case "cylinder":
                {
                    float r = F("radius"), h = F("length") / 2;
                    for (int i = 0; i < 48; i++) { float a = i * MathF.PI / 24; yield return new(r * MathF.Cos(a), r * MathF.Sin(a), h); yield return new(r * MathF.Cos(a), r * MathF.Sin(a), -h); }
                    break;
                }
            case "box":
                {
                    var s = ((string)geometry.Attribute("size")!).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture) / 2).ToArray();
                    foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 }) yield return new(x * s[0], y * s[1], z * s[2]);
                    break;
                }
            case "sphere":
                {
                    float r = F("radius");
                    for (int i = 0; i <= 12; i++) for (int j = 0; j < 24; j++)
                        {
                            float t = i * MathF.PI / 12, p = j * MathF.PI / 12;
                            yield return new(r * MathF.Sin(t) * MathF.Cos(p), r * MathF.Sin(t) * MathF.Sin(p), r * MathF.Cos(t));
                        }
                    break;
                }
            case "mesh":
                {
                    string file = Path.Combine(folder, (string)geometry.Attribute("filename")!);
                    var scale = ((string?)geometry.Attribute("scale") ?? "1 1 1").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    if (!File.Exists(file)) yield break;
                    foreach (var v in StlVertices(file)) yield return v * new Vector3(scale[0], scale[1], scale[2]);
                    break;
                }
        }
    }
    private static IEnumerable<Vector3> StlVertices(string file)
    {
        var data = File.ReadAllBytes(file);
        bool ascii = data.Length > 5 && System.Text.Encoding.ASCII.GetString(data, 0, 5) == "solid" && System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(400, data.Length)).Contains("facet");
        if (ascii)
        {
            foreach (var line in System.Text.Encoding.ASCII.GetString(data).Split('\n'))
            {
                var t = line.Trim();
                if (!t.StartsWith("vertex", StringComparison.Ordinal)) continue;
                var p = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                yield return new(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture));
            }
            yield break;
        }
        int count = BitConverter.ToInt32(data, 80);
        for (int i = 0; i < count; i++)
        {
            int o = 84 + i * 50 + 12;
            for (int v = 0; v < 3; v++, o += 12)
                yield return new(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8));
        }
    }
    private static Matrix4x4 Origin(XElement? origin)
    {
        float[] V(string? text) => (text ?? "0 0 0").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var xyz = V((string?)origin?.Attribute("xyz")); var rpy = V((string?)origin?.Attribute("rpy"));
        // URDF fixed-axis roll, pitch, yaw: R = Rz(yaw) Ry(pitch) Rx(roll), as row vectors X, then Y, then Z.
        return Matrix4x4.CreateRotationX(rpy[0]) * Matrix4x4.CreateRotationY(rpy[1]) * Matrix4x4.CreateRotationZ(rpy[2]) * Matrix4x4.CreateTranslation(xyz[0], xyz[1], xyz[2]);
    }
}
