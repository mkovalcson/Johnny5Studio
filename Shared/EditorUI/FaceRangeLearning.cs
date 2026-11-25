namespace ServoAnimator;

internal sealed class FaceRangeLearning
{
    private readonly List<double> values=new();
    private readonly string signal;
    private readonly double neutral;
    private long? lastStamp;
    internal int Count=>values.Count;
    internal FaceRangeLearning(FaceMapping mapping){signal=mapping.Signal;neutral=mapping.Neutral;}

    internal void Observe(long timestamp,double input)
    {
        if(lastStamp==timestamp||!double.IsFinite(input)||!double.IsFinite(neutral))return;
        lastStamp=timestamp;
        if(values.Count<1800)values.Add(input-neutral);
    }

    internal bool TryRange(out double minimum,out double maximum,out string reason)
    {
        minimum=maximum=0;
        if(values.Count<20){reason="Need at least 20 different tracking frames. Keep your face visible and try again.";return false;}
        var sorted=values.Order().ToArray();
        double low=sorted[(int)((sorted.Length-1)*.02)];
        double high=sorted[(int)((sorted.Length-1)*.98)];
        minimum=Math.Min(0,low);
        maximum=Math.Max(0,high);
        double span=signal is "headYaw" or "headPitch" or "headRoll"?2:.025;
        if(high-low<span)
        {reason="Too little movement measured. Move through a wider comfortable range and retry.";return false;}
        reason="Range measured. Accept it to update this mapping's draft, or Retry.";
        return true;
    }
}
