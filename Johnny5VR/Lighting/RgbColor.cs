namespace Johnny5VR.Lighting;

// Platform-neutral equivalents for the editor emulator's color/command inputs.
public readonly record struct RgbColor(byte R,byte G,byte B)
{
    public static RgbColor FromRgb(byte r,byte g,byte b)=>new(r,g,b);
}
internal static class RgbColors {internal static readonly RgbColor Black=new(0,0,0);}
internal sealed record RgbCommand(double OffsetSeconds,string TextValue,bool Disable=false);
