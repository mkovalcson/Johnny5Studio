using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.Wave;
namespace Johnny5VR;

internal enum FeedbackConfirmation { Sounds, Voice, Off }
internal enum FollowToggle { Mirrored, Unmirrored, MoveMe, MoveRobot, Movement, Hand }

// Audio is independent of voice playback/mouth illumination and never runs on the render thread.
internal sealed class ControllerFeedback:IDisposable
{
    private readonly BlockingCollection<(FollowToggle Toggle,FeedbackConfirmation Mode,float Volume,int Epoch,int Serial)> queue=new(8);
    private Thread? worker;
    private readonly object gate=new();
    private volatile bool stopping;
    private int epoch;
    private readonly int[] serials=new int[3];
    internal static int ToggleGroup(FollowToggle toggle)=>toggle is FollowToggle.Mirrored or FollowToggle.Unmirrored?0:toggle is FollowToggle.Hand or FollowToggle.Movement?2:1;
    internal void Cancel(){Interlocked.Increment(ref epoch);while(queue.TryTake(out _)){} }
    internal static string Label(FollowToggle toggle)=>toggle switch{FollowToggle.MoveMe=>"Move me",FollowToggle.MoveRobot=>"Move robot",_=>toggle.ToString()};
    internal void Play(FollowToggle toggle,FeedbackConfirmation mode,float volume)
    {
        volume=Math.Clamp(float.IsFinite(volume)?volume:.4f,0,1);
        if(mode==FeedbackConfirmation.Off||volume<=0||stopping)return;
        lock(gate)
        {
            if(stopping)return;
            if(worker==null){worker=new Thread(Run){IsBackground=true,Name="Controller confirmations"};worker.SetApartmentState(ApartmentState.STA);worker.Start();}
            int serial=Interlocked.Increment(ref serials[ToggleGroup(toggle)]);
            queue.TryAdd((toggle,mode,volume,Volatile.Read(ref epoch),serial));
        }
    }
    private void Run()
    {
        object? speech=null;
        try
        {
            foreach(var item in queue.GetConsumingEnumerable())
            {
                if(stopping)break;
                if(item.Epoch!=Volatile.Read(ref epoch)||item.Serial!=Volatile.Read(ref serials[ToggleGroup(item.Toggle)]))continue;
                try
                {
                    if(item.Mode==FeedbackConfirmation.Voice)
                    {
                        speech??=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")??throw new InvalidOperationException("Windows speech voice unavailable."));
                        dynamic voice=speech!;voice.Volume=(int)Math.Round(item.Volume*100);voice.Rate=3;voice.Speak(Label(item.Toggle),1);
                        while(!stopping&&item.Epoch==Volatile.Read(ref epoch)&&item.Serial==Volatile.Read(ref serials[ToggleGroup(item.Toggle)])&&(int)voice.Status.RunningState==2)Thread.Sleep(10);
                        if(stopping||item.Epoch!=Volatile.Read(ref epoch)||item.Serial!=Volatile.Read(ref serials[ToggleGroup(item.Toggle)]))voice.Speak("",3);
                    }
                    else
                    {
                        var pcm=Sound(item.Toggle);using var stream=new RawSourceWaveStream(new MemoryStream(pcm),new WaveFormat(24000,16,1));
                        using var output=new WaveOutEvent{DesiredLatency=40,Volume=item.Volume};output.Init(stream);output.Play();
                        while(!stopping&&item.Epoch==Volatile.Read(ref epoch)&&item.Serial==Volatile.Read(ref serials[ToggleGroup(item.Toggle)])&&output.PlaybackState==PlaybackState.Playing)Thread.Sleep(10);
                        output.Stop();
                    }
                }
                catch(Exception ex){Diagnostics.Log(new InvalidOperationException("Controller confirmation audio failed",ex));}
            }
        }
        finally{if(speech!=null&&Marshal.IsComObject(speech))Marshal.FinalReleaseComObject(speech);}
    }
    // Fixed, short cues: rising/falling clicks, pneumatic hiss-click, low servo-clunk.
    internal static byte[] Sound(FollowToggle toggle)
    {
        const int rate=24000;double duration=toggle is FollowToggle.Mirrored or FollowToggle.Unmirrored?.18:.26;
        var pcm=new byte[(int)(rate*duration)*2];var noise=new Random(195+((int)toggle));
        for(int i=0;i<pcm.Length/2;i++)
        {
            double t=(double)i/rate,n=noise.NextDouble()*2-1,v=0;
            if(toggle is FollowToggle.Mirrored or FollowToggle.Unmirrored)
            {
                double pulse=t<.08?t:t-.095;bool first=t<.08;
                double frequency=(toggle==FollowToggle.Mirrored)==first?850:1450;
                if(pulse>=0&&pulse<.055)v=(Math.Sin(2*Math.PI*frequency*pulse)*.65+n*.18)*Math.Exp(-pulse*75)*Math.Min(1,pulse/.002);
            }
            else if(toggle==FollowToggle.MoveMe)
            {
                if(t<.16)v=n*.3*Math.Sin(Math.PI*t/.16);
                else {double p=t-.18;if(p>=0)v=(Math.Sin(2*Math.PI*1200*p)*.6+n*.15)*Math.Exp(-p*80)*Math.Min(1,p/.002);}
            }
            else
            {
                v=(Math.Sin(2*Math.PI*(150*t+190*t*t))*.22+Math.Sin(2*Math.PI*300*t)*.06)*Math.Sin(Math.PI*Math.Min(t/.22,1));
                double p=t-.19;if(p>=0)v+=(Math.Sin(2*Math.PI*95*p)*.7+n*.12)*Math.Exp(-p*55)*Math.Min(1,p/.003);
            }
            v*=Math.Min(1,(duration-t)/.012);short sample=(short)(Math.Clamp(v,-.95,.95)*short.MaxValue);
            pcm[i*2]=(byte)sample;pcm[i*2+1]=(byte)(sample>>8);
        }
        return pcm;
    }
    public void Dispose(){lock(gate){stopping=true;Cancel();queue.CompleteAdding();}}
}
