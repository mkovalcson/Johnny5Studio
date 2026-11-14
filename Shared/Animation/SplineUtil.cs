namespace ServoAnimator;

/// <summary>Cubic Hermite interpolation shared by timeline display, export and playback.</summary>
public static class SplineUtil
{
    /// <summary>
    /// Catmull-Rom style tangents by finite differences:
    ///   interior: m[i] = (v[i+1] - v[i-1]) / (t[i+1] - t[i-1])
    ///   ends:     one-sided slope
    /// </summary>
    public static double[] Tangents(double[] t, double[] v, bool[] breakAfter = null)
    {
        int n = t.Length;
        var m = new double[n];
        if (n <= 1) return m;   // no segments -> no tangents (0 or 1 point)

        for (int i = 0; i < n; i++)
        {
            bool left = i > 0 && !(breakAfter?.Length == n && breakAfter[i - 1]);
            bool right = i < n - 1 && !(breakAfter?.Length == n && breakAfter[i]);
            int lo = left ? i - 1 : i;
            int hi = right ? i + 1 : i;
            m[i] = (v[hi] - v[lo]) / Math.Max(1e-9, t[hi] - t[lo]);
        }
        return m;
    }

    /// <summary>
    /// Evaluate the Hermite spline at time x. Outside the control-point
    /// range the curve holds its end values. Uses the standard Hermite
    /// basis on the segment containing x.
    /// </summary>
    public static double Eval(double[] t, double[] v, double[] m, double x, bool[] breakAfter = null)
    {
        int n = t.Length;
        if (n == 0) return 0;
        if (n == 1 || x <= t[0]) return v[0];
        if (x >= t[n - 1]) return v[n - 1];

        // Binary search for the segment [t[i], t[i+1]] containing x.
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (t[mid] <= x) lo = mid; else hi = mid;
        }

        // A break holds the outgoing value until the next authored point.
        if (breakAfter?.Length == n && breakAfter[lo]) return v[lo];

        double h = t[lo + 1] - t[lo];
        double s = (x - t[lo]) / h;
        return Hermite(v[lo], v[lo + 1], m[lo], m[lo + 1], h, s);
    }
    /// <summary>One cubic Hermite segment. Hosts choose endpoint/hold behavior before evaluation.</summary>
    public static double Hermite(double from, double to, double fromTangent, double toTangent, double duration, double fraction)
    {
        double s = fraction, s2 = s * s, s3 = s2 * s;
        double h00 = 2 * s3 - 3 * s2 + 1;
        double h10 = s3 - 2 * s2 + s;
        double h01 = -2 * s3 + 3 * s2;
        double h11 = s3 - s2;
        return h00 * from + h10 * duration * fromTangent + h01 * to + h11 * duration * toTangent;
    }
}
