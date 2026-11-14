using System.IO;
using System.Text.Json.Serialization;

namespace ServoAnimator;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum PistonClipMode { Independent, PreserveTilt }

internal sealed class PistonSignalSettings
{
    public double PitchRange { get; set; } = 20;
    public double RollRange { get; set; } = 20;
    public double PitchGain { get; set; } = 1;
    public double RollAuthority { get; set; } = .5;
    public bool ReversePitch { get; set; }
    public bool ReverseRoll { get; set; }
    public PistonClipMode ClipMode { get; set; } = PistonClipMode.PreserveTilt;
    public double PitchNeutral { get; set; }
    public double RollNeutral { get; set; }

    public void Validate()
    {
        if (!new[] { PitchRange, RollRange, PitchGain, RollAuthority, PitchNeutral, RollNeutral }.All(double.IsFinite)
            || PitchRange <= 0 || RollRange <= 0 || PitchGain < 0 || PitchGain > 1
            || RollAuthority < 0 || RollAuthority > 1 || !Enum.IsDefined(ClipMode))
            throw new InvalidDataException("Piston ranges must be positive; pitch gain and roll authority must be 0–1.");
    }

    public (double Left, double Right, bool Clipped) Evaluate(double pitchDeg, double rollDeg)
    {
        double p = Math.Clamp(pitchDeg / PitchRange, -1, 1) * (ReversePitch ? -1 : 1);
        double r = Math.Clamp(rollDeg / RollRange, -1, 1) * (ReverseRoll ? -1 : 1);
        double left = PitchGain * p - RollAuthority * r;
        double right = PitchGain * p + RollAuthority * r;
        bool clipped = Math.Abs(left) > 1 || Math.Abs(right) > 1;
        if (clipped && ClipMode == PistonClipMode.PreserveTilt)
        {
            double shift = Math.Max(left, right) > 1 ? 1 - Math.Max(left, right) : -1 - Math.Min(left, right);
            double shiftedLeft = left + shift, shiftedRight = right + shift;
            if (shiftedLeft >= -1 && shiftedLeft <= 1 && shiftedRight >= -1 && shiftedRight <= 1)
            { left = shiftedLeft; right = shiftedRight; }
        }
        return (Math.Clamp(left, -1, 1), Math.Clamp(right, -1, 1),
            clipped || Math.Abs(pitchDeg) > PitchRange || Math.Abs(rollDeg) > RollRange);
    }
}
