using NAudio.Wave;
using System.Runtime.InteropServices;
namespace Johnny5VR;
internal sealed record ArcadeClip(byte[] Bytes,WaveFormat Format);
internal sealed class ArcadeEventAudio:IDisposable
{
    private readonly Dictionary<ArcadeCue,ArcadeClip> clips;
    private readonly List<(WaveOutEvent Output,RawSourceWaveStream Source,ArcadeCue Cue,ArcadeClip Clip)> active=[];
    internal ArcadeEventAudio(Dictionary<ArcadeCue,ArcadeClip> clips){this.clips=clips;}
    internal void Play(ArcadeCue cue)
    {
        try
        {
            foreach(var p in active.Where(p=>p.Output.PlaybackState==PlaybackState.Stopped).ToArray()){p.Output.Dispose();p.Source.Dispose();active.Remove(p);}
            if(active.Count>=4){var p=active[0];p.Output.Dispose();p.Source.Dispose();active.RemoveAt(0);}
            var clip=clips[cue];var source=new RawSourceWaveStream(new MemoryStream(clip.Bytes,false),clip.Format);var output=new WaveOutEvent{DesiredLatency=60,Volume=.65f};
            try{output.Init(source);output.Play();active.Add((output,source,cue,clip));}catch{output.Dispose();source.Dispose();throw;}
        }
        catch(Exception ex){Diagnostics.Log(ex);}
    }
    internal double MouthAmplitude
    {
        get
        {
            double peak=0;foreach(var item in active.Where(p=>p.Cue!=ArcadeCue.Dalek&&p.Output.PlaybackState==PlaybackState.Playing))
            {
                try{int position=(int)Math.Min(item.Clip.Bytes.Length,item.Output.GetPosition());int step=item.Clip.Format.BitsPerSample/8;double sum=0;int n=0;
                    for(int i=position;i+step<=item.Clip.Bytes.Length&&n<128;i+=step,n++){double value=step==4?BitConverter.ToSingle(item.Clip.Bytes,i):BitConverter.ToInt16(item.Clip.Bytes,i)/32768.0;sum+=value*value;}
                    if(n>0)peak=Math.Max(peak,Math.Sqrt(sum/n)*5);
                }catch{}
            }return Math.Clamp(peak,0,1);
        }
    }
    internal static Task<Dictionary<ArcadeCue,ArcadeClip>> Load(ArcadeSettings settings,string config)
    {
        var completion=new TaskCompletionSource<Dictionary<ArcadeCue,ArcadeClip>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>
        {
            var result=new Dictionary<ArcadeCue,ArcadeClip>();
            foreach(var cue in Enum.GetValues<ArcadeCue>())
            {
                try
                {
                    string reference=settings.Audio(cue);if(cue==ArcadeCue.Hit&&reference.Length==0){result[cue]=new(ArcadeAudio.Tone(true),new WaveFormat(24000,16,1));continue;}
                    string file=reference.Length>0?LibraryJson.Resolve(config,reference):DefaultVoice(cue);
                    if(file.Length==0)throw new FileNotFoundException("No generated voice available.");
                    using var reader=new AudioFileReader(file);using var bytes=new MemoryStream();reader.CopyTo(bytes);result[cue]=new(bytes.ToArray(),reader.WaveFormat);
                }
                catch(Exception ex){Diagnostics.Log(ex);result[cue]=new(ArcadeAudio.Tone(cue is ArcadeCue.Disassemble or ArcadeCue.Hit),new WaveFormat(24000,16,1));}
            }
            completion.SetResult(result);
        }){IsBackground=true,Name="Game audio preload"};worker.SetApartmentState(ApartmentState.STA);worker.Start();return completion.Task;
    }
    private static string DefaultVoice(ArcadeCue cue)
    {
        if(cue==ArcadeCue.Hit)return "";
        string bundled=Path.Combine(AppContext.BaseDirectory,"Audio","LaserRange",cue+"-v1.wav");if(File.Exists(bundled))return bundled;
        string folder=Path.Combine(ViewerSettings.Home,"Settings","LaserRangeAudio");Directory.CreateDirectory(folder);string file=Path.Combine(folder,cue+"-v1.wav");if(File.Exists(file))return file;
        object? voice=null,stream=null;
        try
        {
            voice=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);stream=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream")!);
            dynamic output=stream!;output.Open(file+".tmp",3,false);dynamic speaker=voice!;speaker.AudioOutputStream=output;speaker.Rate=cue==ArcadeCue.Dalek?-2:1;
            speaker.Speak(cue switch{ArcadeCue.Disassemble=>"Critical damage. Systems failing.",ArcadeCue.Reassemble=>"Johnny Five is alive!",_=>"Target acquired. Exterminate!"},0);output.Close();File.Move(file+".tmp",file,true);return file;
        }
        finally{if(stream!=null&&Marshal.IsComObject(stream))Marshal.FinalReleaseComObject(stream);if(voice!=null&&Marshal.IsComObject(voice))Marshal.FinalReleaseComObject(voice);}
    }
    public void Dispose(){foreach(var p in active){p.Output.Dispose();p.Source.Dispose();}active.Clear();}
}
