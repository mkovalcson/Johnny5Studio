using NAudio.Wave;
namespace Johnny5VR;
internal sealed class ArcadeAudio:IDisposable
{
    private readonly List<(WaveOutEvent Output,RawSourceWaveStream Source)> active=[];
    private static readonly byte[] Shot=Tone(false),Blast=Tone(true);
    internal void Play(bool explosion)
    {
        try
        {
            foreach(var p in active.Where(p=>p.Output.PlaybackState==PlaybackState.Stopped).ToArray()){p.Output.Dispose();p.Source.Dispose();active.Remove(p);}
            if(active.Count>=8){var old=active[0];old.Output.Dispose();old.Source.Dispose();active.RemoveAt(0);}
            var source=new RawSourceWaveStream(new MemoryStream(explosion?Blast:Shot,false),new WaveFormat(24000,16,1));var output=new WaveOutEvent{DesiredLatency=60,Volume=.35f};
            try{output.Init(source);output.Play();active.Add((output,source));}catch{output.Dispose();source.Dispose();throw;}
        }
        catch(Exception ex){Diagnostics.Log(ex);}
    }
    internal static byte[] Tone(bool explosion)
    {
        const int rate=24000;float duration=explosion?.65f:.14f;var bytes=new byte[(int)(rate*duration)*2];var random=new Random(49);double phase=0,low=0;
        for(int i=0;i<bytes.Length/2;i++)
        {
            double t=i/(double)rate,envelope=Math.Exp(-t/(explosion?.18:.045))*Math.Min(1,t/.003),noise=random.NextDouble()*2-1;
            low=low*.8+noise*.2;phase+=Math.Tau*(explosion?70:1400*Math.Exp(-t*18))/rate;
            double value=(explosion?low*1.5+Math.Sin(phase)*.25:Math.Sin(phase)*.55+noise*.07)*envelope;
            short pcm=(short)(Math.Clamp(value,-.95,.95)*short.MaxValue);bytes[i*2]=(byte)pcm;bytes[i*2+1]=(byte)(pcm>>8);
        }
        return bytes;
    }
    public void Dispose(){foreach(var p in active){p.Output.Dispose();p.Source.Dispose();}active.Clear();}
}
