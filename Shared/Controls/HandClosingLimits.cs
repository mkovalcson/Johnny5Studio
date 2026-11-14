namespace ServoAnimator;

public static class HandClosingLimits
{
    public const double BaseMaximum = .8;
    public const double TipMaximum = 1.0;
    public const double EndMaximum = .8;
    public const double ThumbMaximum = .8;
    public const double ThumbTipMaximum = 1.3;
    // The former shared closing-plane stops, kept so saved stock calibrations can be upgraded.
    public const double FormerBaseMaximum = .002424, FormerTipMaximum = .003995, FormerEndMaximum = .007600,
        FormerThumbMaximum = .002437, FormerThumbTipMaximum = .006650;

    public static (double Base, double Tip) Constrain(double proximal, double distal)
    {
        var limited = ConstrainFinger(proximal, distal, 0);
        return (limited.Base, limited.Tip);
    }

    public static (double Base, double Tip, double End) ConstrainFinger(double proximal, double distal, double end)
        => (Math.Clamp(proximal, -.3, BaseMaximum), Math.Clamp(distal, -.3, TipMaximum), Math.Clamp(end, -.3, EndMaximum));

    public static (double Base, double Tip) ConstrainThumb(double proximal, double distal)
        => (Math.Clamp(proximal, -.3, ThumbMaximum), Math.Clamp(distal, -.3, ThumbTipMaximum));
}
