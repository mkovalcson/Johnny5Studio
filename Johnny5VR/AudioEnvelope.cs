using NAudio.Wave;

namespace Johnny5VR;

// Timeline-sampled peaks, like the editor's ±15 ms mouth envelope. Decoding
// happens while loading, never on the headset's rendering or audio thread.
internal sealed class AudioEnvelope(float[] peaks)
{
    internal static readonly AudioEnvelope Empty=new([]);
    internal static AudioEnvelope Read(AudioFileReader source)
    {
        var values=new List<float>();var buffer=new float[8192];long sample=0;
        int read,channels=source.WaveFormat.Channels,rate=source.WaveFormat.SampleRate;
        while((read=source.Read(buffer,0,buffer.Length))>0)
            for(int i=0;i<read;i++,sample++)
            {
                int bucket=(int)((sample/channels)*1000/rate);
                while(values.Count<=bucket)values.Add(0);
                values[bucket]=Math.Max(values[bucket],Math.Abs(buffer[i]));
            }
        return new(values.ToArray());
    }
    internal double At(double seconds)
    {
        if(peaks.Length==0||seconds<0||!double.IsFinite(seconds))return 0;
        int lo=Math.Clamp((int)((seconds-.015)*1000),0,peaks.Length-1),hi=Math.Clamp((int)((seconds+.015)*1000),lo,peaks.Length-1);
        float value=0;for(int i=lo;i<=hi;i++)value=Math.Max(value,peaks[i]);return Math.Clamp(value,0,1);
    }
}
