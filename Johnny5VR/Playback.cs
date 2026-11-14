using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using Johnny5VR.Lighting;

namespace Johnny5VR;

internal static class LibraryJson
{
    internal static string Truncate(this string text,int length)=>text.Length<=length?text:text[..length]+"… (full details in Diagnostics/playback-report.txt)";
    internal static readonly JsonDocumentOptions Options = ServoAnimator.AnimationJson.Options;
    internal static JsonElement Get(this JsonElement e,string name) => ServoAnimator.AnimationJson.Get(e,name);
    internal static string Text(this JsonElement e,string name,string fallback="") => ServoAnimator.AnimationJson.Text(e,name,fallback);
    internal static double Number(this JsonElement e,string name,double fallback=0) => ServoAnimator.AnimationJson.Number(e,name,fallback);
    internal static bool Flag(this JsonElement e,string name) => ServoAnimator.AnimationJson.Flag(e,name);
    internal static IEnumerable<JsonElement> Items(this JsonElement e) => ServoAnimator.AnimationJson.Items(e);
    internal static string Resolve(string config,string reference) => ServoAnimator.ConfigurationPaths.ResolveAsset(config, reference);

}

internal sealed record LibraryEntry(string File,string Name,bool Movie,string Description)
{
    public override string ToString()=>Name;
}
internal sealed record AudioCue(string File,double Start,double Skip,double Length)
{
    internal AudioEnvelope Envelope {get;init;}=AudioEnvelope.Empty;
}
internal sealed record KeyPoint(double Time,double Value,bool Break);
internal sealed class MotionTrack(string servo,string control,KeyPoint[] points,bool spline)
{
    internal string Servo=>servo;
    internal string Control=>control;
    internal KeyPoint[] Points=>points;
    private readonly ServoAnimator.KeyframeTrack track = new(points.Select(p => new ServoAnimator.TimelineKeyframe(p.Time,p.Value,p.Break)), spline);
    internal (double Value,double Stamp)? At(double time) => track.At(time);
}
internal sealed record PlaybackSequence(string File,string Name,double Duration,MotionTrack[] Tracks,AudioCue[] Audio,bool Loop,string Trigger,string[] Warnings)
{
    internal string SceneReference {get;init;}="";
    internal RgbCommand[] RgbCommands {get;init;}=[];
    internal EffectCue[] Effects {get;init;}=[];
    internal AudioCue? AudioAt(double time)=>Audio.LastOrDefault(a=>a.Start<=time&&a.Start+a.Length>time);
}
internal sealed record PlaybackShow(string Name,PlaybackSequence[] Sequences,string[] Warnings);

internal static class PlaybackLibrary
{
    internal static LibraryEntry[] Scan(string config,List<string>? errors=null)
    {
        var result=new List<LibraryEntry>();
        foreach(string directory in new[]{"Projects","Library/Animation"})
        {
            string folder=Path.Combine(config,directory);if(!Directory.Exists(folder))continue;
            foreach(string file in Directory.EnumerateFiles(folder,"*.json",SearchOption.AllDirectories))
            {
                try
                {
                    using var doc=ServoAnimator.AnimationJson.ReadFile(file);var r=doc.RootElement;
                    bool movie=r.Get("sequences").ValueKind==JsonValueKind.Array;
                    if(!movie&&r.Get("commands").ValueKind!=JsonValueKind.Array)continue;
                    result.Add(new(Path.GetRelativePath(config,file),Path.ChangeExtension(Path.GetRelativePath(Path.Combine(config,directory),file),null).Replace('\\','/'),movie,r.Text("description")));
                }
                catch(Exception ex) when(ex is JsonException or IOException){errors?.Add(Path.GetRelativePath(config,file)+": "+ex.Message);}
            }
        }
        return result.OrderByDescending(x=>x.Movie).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static PlaybackShow Load(string config,LibraryEntry entry)
    {
        var warnings=new List<string>();var sequences=new List<PlaybackSequence>();var envelopes=new Dictionary<string,AudioEnvelope>(StringComparer.OrdinalIgnoreCase);
        if(entry.Movie)
        {
            var movie = ServoAnimator.MovieDocument.Load(LibraryJson.Resolve(config, entry.File));
            for (int i = 0; i < movie.Sequences.Count; i++)
            {
                if (movie.Sequences[i] == null) throw new InvalidDataException("Movie sequence path must be text.");
                sequences.Add(Read(config, movie.Sequences[i], i < movie.SequenceLoops.Count && movie.SequenceLoops[i],
                    i < movie.SequenceTriggers.Count ? movie.SequenceTriggers[i] ?? "" : "", envelopes));
            }
        }
        else sequences.Add(Read(config,entry.File,false,"",envelopes));
        if(sequences.Count==0)throw new InvalidDataException("This movie has no sequences.");
        warnings.AddRange(sequences.SelectMany(s=>s.Warnings));
        return new(entry.Name,sequences.ToArray(),warnings.Distinct().ToArray());
    }
    private static PlaybackSequence Read(string config,string file,bool loop,string trigger,Dictionary<string,AudioEnvelope> envelopes)
    {
        string path=LibraryJson.Resolve(config,file);
        using var source = ServoAnimator.SequenceSource.Load(path); var r = source.Root;
        var warnings=new List<string>();var audio=new List<AudioCue>();var points=new Dictionary<(string Servo,string Control),List<KeyPoint>>();var rgb=new List<RgbCommand>();var effects=new List<EffectCue>();
        var splines=source.SplineServos.ToHashSet();
        double duration=source.DurationSeconds;bool scaled=source.ScaleValues;
        foreach(var c in source.Commands)
        {
            string servo=c.Text("servo"),control=c.Text("control");double time=Math.Max(0,c.Number("offsetSeconds"));duration=Math.Max(duration,time);
            if(c.Get("value").ValueKind==JsonValueKind.Number)
            {
                double value=c.Number("value")*(scaled?100:1);
                if(!points.TryGetValue((servo,control),out var list))points[(servo,control)]=list=[];
                list.Add(new(time,value,c.Flag("breakSpline")));
            }
            else if(servo=="Play")AddAudio(c.Text("value"),time,c.Get("audioTrim"));
            else if(servo=="RGBCommand")rgb.Add(new(time,c.Text("value"),c.Text("value")=="Disable"));
            else if(servo is "FallApart" or "FireLaser" or "HideLaser" or "ToolboxSwap"&&c.Text("value")!="Disable")
            {
                string text=c.Text("value","{}");using var effect=JsonDocument.Parse(text);effects.Add(new(servo,time,text));
                if(servo=="FireLaser")
                {
                    var fire=LaserSettings.Read(text);duration=Math.Max(duration,time+fire.Duration);
                }
                if(servo=="FallApart")
                {
                    var settings=ViewerSettings.Load();settings.ConfigFolder=config;
                    var resolved=Scene(config,r.Text("urdfBackgroundFile"),settings.Robot,settings.Backdrop);
                    var clip=BakedFallApart.Load(LibraryJson.Resolve(config,resolved.Robot));
                    if(clip!=null)duration=Math.Max(duration,time+clip.Duration/Math.Clamp(effect.RootElement.Number("speed",1),.1,10));
                    else warnings.Add("The selected robot has no baked disassembly clip.");
                }
            }
            else if(c.Text("value")!="Disable")warnings.Add($"{Path.GetFileName(file)}: {servo} effects are not supported by this VR player yet.");
        }
        string primary=source.PrimaryAudio;
        // Primary is considered first. An inserted Play clip wins an equal-start tie.
        if(!string.IsNullOrWhiteSpace(primary))
        {
            AddAudio(primary,source.AudioStartOffsetSeconds,default);
            var primaryCue=audio[^1];audio.RemoveAt(audio.Count-1);audio.Insert(0,primaryCue);
        }
        void AddAudio(string reference,double time,JsonElement trim)
        {
            try
            {
                string a=LibraryJson.Resolve(config,reference);using var reader=new AudioFileReader(a);
                double skip=trim.Flag("skipCommitted")?Math.Max(0,trim.Number("skipSeconds")):0;
                double end=trim.Flag("endCommitted")?Math.Clamp(trim.Number("endSeconds",reader.TotalTime.TotalSeconds),skip,Math.Max(skip,reader.TotalTime.TotalSeconds)):reader.TotalTime.TotalSeconds;
                if(skip>=reader.TotalTime.TotalSeconds)throw new InvalidDataException("Audio trim starts after the file ends.");
                if(!envelopes.TryGetValue(a,out var envelope)){reader.Position=0;envelopes[a]=envelope=AudioEnvelope.Read(reader);}
                double length=Math.Max(0,end-skip);audio.Add(new(a,time,skip,length){Envelope=envelope});duration=Math.Max(duration,time+length);
            }
            catch(Exception ex) when(ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException or InvalidDataException){throw new InvalidDataException("Cannot load audio '"+reference+"': "+ex.Message,ex);}
        }
        return new(file,Path.GetFileNameWithoutExtension(file),Math.Max(.1,duration),points.Select(k=>new MotionTrack(k.Key.Servo,k.Key.Control,k.Value.GroupBy(p=>p.Time).Select(g=>g.Last()).OrderBy(p=>p.Time).ToArray(),splines.Contains(k.Key.Servo))).ToArray(),audio.OrderBy(c=>c.Start).ToArray(),loop,trigger,warnings.ToArray()){SceneReference=source.SceneReference,RgbCommands=rgb.ToArray(),Effects=effects.OrderBy(c=>c.Time).ToArray()};
    }
    internal static (string Robot,string Backdrop,string[] Warnings,ServoAnimator.UrdfStageSettings? Stage) Scene(string config,string reference,string robot,string backdrop)
    {
        var warnings=new List<string>();
        ServoAnimator.UrdfStageSettings? stage=null;
        if (File.Exists(Path.Combine(config, "UrdfModelSettings.json")))
        {
            var models = ServoAnimator.UrdfModelSettings.Load(config);
            robot = ServoAnimator.ConfigurationPaths.Reference(config, models.ModelFile);
            if (reference == "") reference = models.ActiveSceneFile;
        }
        if (reference != "")
        {
            var scene = ServoAnimator.ConfigurationStore.ReadScene(config, reference);
            if (!string.IsNullOrWhiteSpace(scene.RobotModelFile)) robot = scene.RobotModelFile;
            var background = scene.Settings?.Background ?? throw new InvalidDataException("Saved scene has no Background settings: " + reference);
            if (!string.IsNullOrWhiteSpace(background.Stage?.ModelFile)) backdrop = background.Stage.ModelFile;
            if (background.BackgroundMode==ServoAnimator.UrdfBackgroundMode.Urdf3D) stage=background.Stage;
            if (background.BackgroundMode is not (ServoAnimator.UrdfBackgroundMode.Studio or ServoAnimator.UrdfBackgroundMode.Urdf3D))
                warnings.Add("The saved scene's 2D image/video background is not a VR environment; its 3D stage is used.");
        }
        else if (File.Exists(Path.Combine(config, "URDFconfig.json")))
        {
            var configuration = ServoAnimator.ConfigurationStore.LoadUrdf(config, LibraryJson.Resolve(config, robot));
            if (!string.IsNullOrWhiteSpace(configuration.Presentation?.Stage?.ModelFile)) backdrop = configuration.Presentation.Stage.ModelFile;
            if (configuration.Presentation?.BackgroundMode==ServoAnimator.UrdfBackgroundMode.Urdf3D) stage=configuration.Presentation.Stage;
        }
        return(robot.Replace('\\','/'),backdrop.Replace('\\','/'),warnings.ToArray(),stage);
    }
}

internal sealed partial class PlaybackPlayer:IDisposable
{
    private readonly PlaybackClock clock;
    internal PlaybackPlayer(Func<double>? timeSource=null)=>clock=new(timeSource);
    private readonly List<(AudioCue Cue,AudioFileReader Reader,WaveOutEvent Output)> playing=[];
    private readonly Dictionary<string,(double Value,double Stamp,string Servo)> carried=new();
    private readonly Dictionary<string,EffectCue> carriedEffects=[];
    private EffectCue[]? effectCache;
    internal EffectCue[] Effects=>effectCache??=carriedEffects.Values.Concat(Current?.Effects??[]).OrderBy(c=>c.Time).ToArray();
    internal PlaybackShow? Show {get;private set;}
    internal int Index {get;private set;}
    internal int Epoch {get;private set;}
    internal bool Running=>clock.IsRunning;
    internal double Position=>clock.Elapsed;
    internal double MouthAmplitude=>Running&&Current?.AudioAt(Position) is {} cue?cue.Envelope.At(cue.Skip+Position-cue.Start):0;
    internal PlaybackSequence? Current=>Show?.Sequences[Index];
    internal string Status=>Current==null?"Choose a movie or sequence":$"{(Running?"Playing":Position>0?"Paused":"Ready")} · {Current.Name} · {Math.Min(Position,Current.Duration):F1} / {Current.Duration:F1}s · {Index+1}/{Show!.Sequences.Length}"+(Current.Loop?" · Loop (Next advances)":"")+(!string.IsNullOrEmpty(Current.Trigger)?" · trigger: Next advances":"");
    internal void Load(PlaybackShow show){Stop();Show=show;Index=0;}
    internal void Unload(){Stop();Show=null;}
    internal void Toggle()
    {
        if(Current==null)return;
        if(!Running&&Index==Show!.Sequences.Length-1&&Position>=Current.Duration)Stop();
        if(Running){clock.Stop();foreach(var p in playing)p.Output.Pause();}
        else{clock.Start();foreach(var p in playing)p.Output.Play();}
    }
    internal void Stop(){clock.Reset();StopAudio();carried.Clear();carriedEffects.Clear();effectCache=null;ResetRgb();Index=0;Epoch++;}
    private void StopAudio(){foreach(var p in playing){p.Output.Stop();p.Output.Dispose();p.Reader.Dispose();}playing.Clear();}
    internal void Next()
    {
        if(Show==null)return;
        if(Index+1>=Show.Sequences.Length){clock.Stop();StopAudio();return;}
        Capture(Current!.Duration);StopAudio();clock.Reset();Index++;Epoch++;effectCache=null;clock.Start();
    }
    internal void Tick()
    {
        if(!Running||Current==null)return;
        if(Position>=Current.Duration)
        {
            if(Current.Loop){Capture(Current.Duration);StopAudio();Epoch++;effectCache=null;clock.Restart();}
            else if(Index+1<Show!.Sequences.Length&&string.IsNullOrEmpty(Show.Sequences[Index+1].Trigger)){Next();}
            else{clock.Stop();StopAudio();return;}
        }
        var cue=Current.AudioAt(Position);
        if(cue!=null&&(playing.Count==0||playing[0].Cue!=cue))
        {
            StopAudio();double offset=Position-cue.Start;
            var reader=new AudioFileReader(cue.File);WaveOutEvent? output=null;
            try{reader.CurrentTime=TimeSpan.FromSeconds(cue.Skip+offset);output=new WaveOutEvent{DesiredLatency=60};output.Init(new TrimmedAudio(reader,cue.Skip+cue.Length));playing.Add((cue,reader,output));output.Play();}
            catch{output?.Dispose();reader.Dispose();throw;}
        }
        if(cue==null)StopAudio();
    }
    private void Capture(double time)
    {
        if(Current==null)return;
        CaptureRgb(time);
        foreach(string servo in new[]{"FallApart","HideLaser","ToolboxSwap"})
            if(Effects.LastOrDefault(c=>c.Servo==servo&&c.Time<=time) is {} cue)carriedEffects[servo]=cue with{Time=cue.Time-time};
        // Body gangs overlap independent joints. Preserve their relative ages when
        // moving to the next sequence, while keeping every carried stamp before t=0.
        foreach(var key in carried.Keys.Where(k=>k.StartsWith("servo:B_")).ToArray())
        {var c=carried[key];carried[key]=(c.Value,c.Stamp-time-1,c.Servo);}
        foreach(var t in Current.Tracks)if(t.At(time) is {} value)
        {
            if(t.Servo.StartsWith("B_"))carried[Key(t)]=(value.Value,value.Stamp-time-1,t.Servo);
            else ServoAnimator.MoviePoseContinuity.Carry(carried,t.Servo,t.Control,value.Value);
        }
    }
    private static string Key(MotionTrack t)=>ServoAnimator.MoviePoseContinuity.Key(t.Servo,t.Control);
    internal Dictionary<string,(double Value,double Stamp,string Servo)> Pose()
    {
        var pose=new Dictionary<string,(double Value,double Stamp,string Servo)>(carried);
        if(Current!=null)foreach(var t in Current.Tracks)if(t.At(Math.Min(Position,Current.Duration)) is {} value)pose[Key(t)]=(value.Value,value.Stamp,t.Servo);
        return pose;
    }
    public void Dispose(){StopAudio();clock.Stop();}
    private sealed class TrimmedAudio(AudioFileReader source,double end):IWaveProvider
    {
        public WaveFormat WaveFormat=>source.WaveFormat;
        public int Read(byte[] buffer,int offset,int count)
        {
            long remaining=(long)Math.Max(0,(end-source.CurrentTime.TotalSeconds)*WaveFormat.AverageBytesPerSecond);
            count=(int)Math.Min(count,remaining/WaveFormat.BlockAlign*WaveFormat.BlockAlign);
            return count==0?0:source.Read(buffer,offset,count);
        }
    }
}

internal sealed class PlaybackClock(Func<double>? timeSource=null)
{
    private readonly ServoAnimator.TransportClock clock = new(timeSource);
    internal bool IsRunning => clock.IsRunning;
    internal double Elapsed => clock.Elapsed;
    internal void Start() => clock.Start();
    internal void Stop() => clock.Stop();
    internal void Reset() => clock.Reset();
    internal void Restart() => clock.Restart();
}
