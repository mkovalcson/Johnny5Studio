namespace ServoAnimator;

public sealed record TimelineKeyframe(double Time, double Value, bool Break);

/// <summary>Prepared ordered timeline points with explicit stepped/spline and authored-stamp semantics.</summary>
public sealed class KeyframeTrack
{
    private readonly TimelineKeyframe[] points;
    private readonly double[] tangents;
    private readonly bool spline;
    public KeyframeTrack(IEnumerable<TimelineKeyframe> source, bool spline)
    {
        points = source.ToArray(); this.spline = spline;
        tangents = SplineUtil.Tangents(points.Select(p => p.Time).ToArray(), points.Select(p => p.Value).ToArray(), points.Select(p => p.Break).ToArray());
    }
    public (double Value, double Stamp)? At(double time)
    {
        if (points.Length == 0 || time < points[0].Time) return null;
        int lo = 0, hi = points.Length;
        while (lo + 1 < hi) { int mid = (lo + hi) / 2; if (points[mid].Time <= time) lo = mid; else hi = mid; }
        var p = points[lo];
        if (!spline || lo == points.Length - 1 || p.Break) return (p.Value, p.Time);
        var next = points[lo + 1];
        double h = next.Time - p.Time, s = Math.Clamp((time - p.Time) / h, 0, 1);
        return (SplineUtil.Hermite(p.Value, next.Value, tangents[lo], tangents[lo + 1], h, s), p.Time);
    }
}
