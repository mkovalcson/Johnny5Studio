namespace ServoAnimator;
/// <summary>Live sampling data, independent of graph visibility and rendering.</summary>
public interface IPoseCurve
{
    double[] T { get; }
    double[] V { get; }
    double[] M { get; }
    double Min { get; }
    double Max { get; }
    bool[] BreakAfter { get; }
}
