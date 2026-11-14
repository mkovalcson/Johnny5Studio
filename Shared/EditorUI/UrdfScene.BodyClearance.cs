using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>
/// Body collision warnings for the full-body model without dedicated collision meshes.
/// Each arm (upper arm through fingertips) is checked against the head (everything under
/// base_link, including the eyes and flaps), the other arm, the torso (chest and neck lift),
/// the waist and pelvis, and the tracks. The geometry
/// is a set of surface points sampled every <see cref="BodySampleSpacing"/> from the
/// links' own visual meshes, placed with the same numeric link transforms as the head
/// collision session. A contact is reported when two surfaces come within
/// <see cref="BodyContactDistance"/>. Models without the arm joints have no body checks.
/// </summary>
internal sealed partial class UrdfScene
{
    internal const double BodyContactDistance = 0.005;   // metres
    internal const double BodySampleSpacing = 0.005;     // metres between surface samples
    internal const string BodyPairPrefix = "body:";

    private BodyClearanceModel _bodyClearance;
    private bool _bodyClearanceBuilt;

    private sealed class BodyLinkCloud
    {
        public string Link;
        public Point3D[] Local;
        public Point3D[] World;
        public Rect3D LocalBounds;
        public Matrix3D Pose;
        public bool WorldCurrent;
        public Rect3D Bounds;
    }

    private sealed record BodyGroup(string Name, BodyLinkCloud[] Links);
    private sealed record BodyClearanceModel(BodyGroup[] Groups, (int A, int B)[] Pairs);

    /// <summary>One link-to-link contact between two checked body groups.</summary>
    internal readonly record struct BodyHit(string Key, string GroupA, string LinkA, string GroupB, string LinkB);

    /// <summary>True when the loaded model has both arms and a head to check.</summary>
    internal bool BodyClearanceAvailable => GetBodyClearanceModel() != null;

    /// <summary>The checked groups with their link and sample counts (for checks and diagnostics).</summary>
    internal (string Group, int Links, int Samples)[] BodyClearanceGroups() =>
        GetBodyClearanceModel()?.Groups.Select(g => (g.Name, g.Links.Length, g.Links.Sum(c => c.Local.Length))).ToArray()
        ?? Array.Empty<(string, int, int)>();

    /// <summary>Smallest sample distance between two checked groups in the current pose (metres), and the
    /// links where it occurs. Brute force over samples near the other group; for checks and diagnostics.</summary>
    internal (double Distance, string LinkA, string LinkB) BodyGroupClearance(string groupA, string groupB, double search = 0.05)
    {
        var model = GetBodyClearanceModel();
        var result = (double.PositiveInfinity, (string)null, (string)null);
        if (model == null) return result;
        _warningCollisionSession ??= new CollisionSession(this);
        CaptureCollisionPose(_warningCollisionSession);
        DetectBodyHits(_warningCollisionSession, model, new List<BodyHit>());
        var ga = model.Groups.First(g => g.Name == groupA); var gb = model.Groups.First(g => g.Name == groupB);
        double best = search * search;
        foreach (var a in ga.Links)
        foreach (var b in gb.Links)
        {
            if (a.Bounds.IsEmpty || b.Bounds.IsEmpty || !Near(a.Bounds, b.Bounds, search)) continue;
            EnsureWorld(a); EnsureWorld(b);
            foreach (var p in a.World)
            {
                if (!Near(new Rect3D(p, new Size3D()), b.Bounds, search)) continue;
                foreach (var q in b.World)
                {
                    double d2 = (p - q).LengthSquared;
                    if (d2 < best) { best = d2; result = (Math.Sqrt(d2), a.Link, b.Link); }
                }
            }
        }
        return result;
    }

    /// <summary>Forget the sampled body geometry (call after the scene's meshes change).</summary>
    internal void ResetBodyClearance() { _bodyClearance = null; _bodyClearanceBuilt = false; }

    private BodyClearanceModel GetBodyClearanceModel()
    {
        if (_bodyClearanceBuilt) return _bodyClearance;
        _bodyClearanceBuilt = true;
        if (!_links.ContainsKey("base_link") ||
            !_joints.TryGetValue("ArmTwistLeft", out var left) || !_joints.TryGetValue("ArmTwistRight", out var right))
            return _bodyClearance = null;

        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var joint in _joints.Values)
        {
            if (!children.TryGetValue(joint.ParentLink, out var list)) children[joint.ParentLink] = list = new();
            list.Add(joint.ChildLink);
        }
        List<string> Subtree(string root)
        {
            var result = new List<string>();
            var stack = new Stack<string>(); stack.Push(root);
            while (stack.Count > 0)
            {
                string link = stack.Pop(); result.Add(link);
                if (children.TryGetValue(link, out var list)) foreach (string c in list) stack.Push(c);
            }
            return result;
        }
        var backs = new HashSet<GeometryModel3D>(_openBackVisuals.Values);
        BodyGroup Group(string name, IEnumerable<string> links) =>
            new(name, links.Select(l => SampleLink(l, backs)).Where(c => c != null).ToArray());

        var head = Subtree("base_link");
        var leftArm = Subtree(left.ChildLink);
        var rightArm = Subtree(right.ChildLink);
        // The shoulders (pivot, bracket, housing and hub) carry the arms, so they are in no group:
        // they always touch the upper arm and the chest.
        var shoulders = new HashSet<string>(StringComparer.Ordinal);
        foreach (string hug in new[] { "ShoulderHugLeft", "ShoulderHugRight" })
            if (_joints.TryGetValue(hug, out var joint)) shoulders.UnionWith(Subtree(joint.ChildLink));
        var excluded = new HashSet<string>(head, StringComparer.Ordinal);
        excluded.UnionWith(leftArm); excluded.UnionWith(rightArm); excluded.UnionWith(shoulders);

        // Torso: the chest and the neck lift mechanism. Waist: everything that turns and leans with
        // the waist below the chest, plus the pelvis. Tracks: the tracks, treads, shrouds and caster.
        var torso = new List<string>(); var waist = new List<string>(); var tracks = new List<string>();
        string chest = _joints.TryGetValue("ChestTilt", out var chestTilt) ? chestTilt.ChildLink : null;
        var chestLinks = chest == null ? new HashSet<string>() : new HashSet<string>(Subtree(chest), StringComparer.Ordinal);
        string root = _links.Keys.FirstOrDefault(l => !_joints.Values.Any(j => j.ChildLink == l));
        if (root != null)
            foreach (string link in Subtree(root))
            {
                if (excluded.Contains(link)) continue;
                if (chestLinks.Contains(link)) torso.Add(link);
                else if (IsTrackLink(link)) tracks.Add(link);
                else waist.Add(link);
            }

        var groups = new List<BodyGroup> { Group("head", head), Group("left arm", leftArm), Group("right arm", rightArm) };
        if (groups.Any(g => g.Links.Length == 0)) return _bodyClearance = null;
        var pairs = new List<(int, int)> { (1, 0), (2, 0), (1, 2) };
        foreach (var (name, links) in new[] { ("torso", torso), ("waist", waist), ("tracks", tracks) })
        {
            var group = Group(name, links);
            if (group.Links.Length == 0) continue;
            groups.Add(group);
            pairs.Add((1, groups.Count - 1)); pairs.Add((2, groups.Count - 1));
        }
        return _bodyClearance = new BodyClearanceModel(groups.ToArray(), pairs.ToArray());
    }

    private static bool IsTrackLink(string link) =>
        link.Contains("_track_", StringComparison.Ordinal) || link.Contains("_tread_", StringComparison.Ordinal) ||
        link.Contains("_shroud_", StringComparison.Ordinal) || link.Contains("_caster_", StringComparison.Ordinal);

    private BodyLinkCloud SampleLink(string link, HashSet<GeometryModel3D> openBacks)
    {
        if (!_linkVisuals.TryGetValue(link, out var visuals)) return null;
        var seen = new HashSet<(long, long, long)>();
        var points = new List<Point3D>();
        void Add(Point3D p)
        {
            var key = ((long)Math.Floor(p.X / BodySampleSpacing), (long)Math.Floor(p.Y / BodySampleSpacing), (long)Math.Floor(p.Z / BodySampleSpacing));
            if (seen.Add(key)) points.Add(p);
        }
        foreach (var visual in visuals)
        {
            if (openBacks.Contains(visual) || visual.Geometry is not MeshGeometry3D mesh) continue;
            var transform = visual.Transform?.Value ?? Matrix3D.Identity;
            var positions = mesh.Positions; var indices = mesh.TriangleIndices;
            if (positions == null || positions.Count == 0) continue;
            if (indices == null || indices.Count < 3)
            {
                foreach (var p in positions) Add(transform.Transform(p));
                continue;
            }
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                Point3D a = transform.Transform(positions[indices[i]]), b = transform.Transform(positions[indices[i + 1]]),
                        c = transform.Transform(positions[indices[i + 2]]);
                double longest = Math.Max((b - a).Length, Math.Max((c - b).Length, (a - c).Length));
                int n = Math.Clamp((int)Math.Ceiling(longest / BodySampleSpacing), 1, 60);
                for (int u = 0; u <= n; u++)
                for (int v = 0; v <= n - u; v++)
                {
                    double fu = (double)u / n, fv = (double)v / n;
                    Add(a + (b - a) * fu + (c - a) * fv);
                }
            }
        }
        if (points.Count == 0) return null;
        var local = points.ToArray();
        return new BodyLinkCloud { Link = link, Local = local, World = new Point3D[local.Length], LocalBounds = BoundsOf(local) };
    }

    /// <summary>Paint every visual of a touching body link (open-back twins are added by the caller).</summary>
    private void AddLinkHighlight(string link, HashSet<GeometryModel3D> result)
    {
        if (_linkVisuals.TryGetValue(link, out var visuals))
            foreach (var visual in visuals) result.Add(visual);
    }

    /// <summary>Body contacts for the scene's current pose (one hit per touching link pair).</summary>
    internal List<BodyHit> DetectBodyHits()
    {
        var hits = new List<BodyHit>();
        var model = GetBodyClearanceModel();
        if (model == null) return hits;
        _warningCollisionSession ??= new CollisionSession(this);
        CaptureCollisionPose(_warningCollisionSession);
        DetectBodyHits(_warningCollisionSession, model, hits);
        return hits;
    }

    /// <summary>Body contacts for an arbitrary numeric pose (used by checks and trial motion).</summary>
    internal List<BodyHit> DetectBodyHits(CollisionSession session)
    {
        var hits = new List<BodyHit>();
        var model = GetBodyClearanceModel();
        if (model != null) DetectBodyHits(session, model, hits);
        return hits;
    }

    private static void DetectBodyHits(CollisionSession session, BodyClearanceModel model, List<BodyHit> hits)
    {
        foreach (var group in model.Groups)
        foreach (var cloud in group.Links)
        {
            // Only the bounds are placed here; the samples are moved when a pair needs them.
            cloud.WorldCurrent = false;
            if (!session.TryGetWorld(cloud.Link, out var world)) { cloud.Bounds = Rect3D.Empty; continue; }
            cloud.Pose = world;
            cloud.Bounds = TransformBounds(cloud.LocalBounds, world);
        }
        const double d = BodyContactDistance;
        foreach (var (ia, ib) in model.Pairs)
        {
            var ga = model.Groups[ia]; var gb = model.Groups[ib];
            foreach (var a in ga.Links)
            {
                if (a.Bounds.IsEmpty) continue;
                foreach (var b in gb.Links)
                {
                    if (b.Bounds.IsEmpty || !Near(a.Bounds, b.Bounds, d)) continue;
                    if (!Touching(a, b, d)) continue;
                    hits.Add(new BodyHit(BodyPairPrefix + a.Link + "|" + b.Link, ga.Name, a.Link, gb.Name, b.Link));
                }
            }
        }
    }

    private static void EnsureWorld(BodyLinkCloud cloud)
    {
        if (cloud.WorldCurrent) return;
        Array.Copy(cloud.Local, cloud.World, cloud.Local.Length);
        cloud.Pose.Transform(cloud.World);                 // in place
        cloud.WorldCurrent = true;
    }

    private static Rect3D TransformBounds(Rect3D local, Matrix3D world)
    {
        var corners = new Point3D[8];
        for (int i = 0; i < 8; i++)
            corners[i] = new Point3D(local.X + ((i & 1) != 0 ? local.SizeX : 0), local.Y + ((i & 2) != 0 ? local.SizeY : 0),
                                     local.Z + ((i & 4) != 0 ? local.SizeZ : 0));
        world.Transform(corners);
        return BoundsOf(corners);
    }

    private static Rect3D BoundsOf(Point3D[] points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
            if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
            if (p.Z < z0) z0 = p.Z; if (p.Z > z1) z1 = p.Z;
        }
        return new Rect3D(x0, y0, z0, x1 - x0, y1 - y0, z1 - z0);
    }

    private static bool Near(Rect3D a, Rect3D b, double d) =>
        a.X - d <= b.X + b.SizeX && b.X - d <= a.X + a.SizeX &&
        a.Y - d <= b.Y + b.SizeY && b.Y - d <= a.Y + a.SizeY &&
        a.Z - d <= b.Z + b.SizeZ && b.Z - d <= a.Z + a.SizeZ;

    /// <summary>True when any sample of <paramref name="a"/> lies within <paramref name="d"/> of a
    /// sample of <paramref name="b"/>. Only samples inside the shared bounds are compared.</summary>
    private static bool Touching(BodyLinkCloud a, BodyLinkCloud b, double d)
    {
        EnsureWorld(a); EnsureWorld(b);
        double lx = Math.Max(a.Bounds.X, b.Bounds.X) - d, ly = Math.Max(a.Bounds.Y, b.Bounds.Y) - d, lz = Math.Max(a.Bounds.Z, b.Bounds.Z) - d;
        double hx = Math.Min(a.Bounds.X + a.Bounds.SizeX, b.Bounds.X + b.Bounds.SizeX) + d;
        double hy = Math.Min(a.Bounds.Y + a.Bounds.SizeY, b.Bounds.Y + b.Bounds.SizeY) + d;
        double hz = Math.Min(a.Bounds.Z + a.Bounds.SizeZ, b.Bounds.Z + b.Bounds.SizeZ) + d;
        bool Inside(Point3D p) => p.X >= lx && p.X <= hx && p.Y >= ly && p.Y <= hy && p.Z >= lz && p.Z <= hz;
        // Bucket the smaller side on a grid of cell size d, then probe the 27 neighbouring cells.
        var small = a.World.Length <= b.World.Length ? a.World : b.World;
        var large = ReferenceEquals(small, a.World) ? b.World : a.World;
        var grid = new Dictionary<(long, long, long), List<Point3D>>();
        foreach (var p in small)
        {
            if (!Inside(p)) continue;
            var key = ((long)Math.Floor(p.X / d), (long)Math.Floor(p.Y / d), (long)Math.Floor(p.Z / d));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Point3D>(4);
            list.Add(p);
        }
        if (grid.Count == 0) return false;
        double d2 = d * d;
        foreach (var p in large)
        {
            if (!Inside(p)) continue;
            long cx = (long)Math.Floor(p.X / d), cy = (long)Math.Floor(p.Y / d), cz = (long)Math.Floor(p.Z / d);
            for (long x = cx - 1; x <= cx + 1; x++)
            for (long y = cy - 1; y <= cy + 1; y++)
            for (long z = cz - 1; z <= cz + 1; z++)
                if (grid.TryGetValue((x, y, z), out var list))
                    foreach (var q in list)
                        if ((p - q).LengthSquared <= d2) return true;
        }
        return false;
    }
}
