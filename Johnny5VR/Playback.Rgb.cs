using Johnny5VR.Lighting;

namespace Johnny5VR;

internal sealed partial class PlaybackPlayer
{
    private readonly ArduinoRgbTimelineSimulator rgb=new();
    private PlaybackSequence? rgbSequence;
    private RgbRingFrame? carriedRgb;
    private int rgbEpoch=-1;
    internal RgbRingFrame RgbFrame=>RgbAt(Math.Min(Position,Current?.Duration??0));
    private RgbRingFrame RgbAt(double time)
    {
        if(rgbSequence!=Current||rgbEpoch!=Epoch)
        {
            rgbSequence=Current;rgbEpoch=Epoch;rgb.SetInitialFrame(carriedRgb!);
        }
        return rgb.Evaluate(Current?.RgbCommands??[],time);
    }
    private void CaptureRgb(double time)=>carriedRgb=RgbAt(time);
    private void ResetRgb(){carriedRgb=null;rgbSequence=null;rgbEpoch=-1;rgb.SetInitialFrame(null!);}
}
