using System.Numerics;
using System.Text.Json;

namespace Johnny5VR;

internal sealed record EffectCue(string Servo,double Time,string Text);
internal sealed record LaserSettings(int Shots,double Interval,double Speed,double Range,double Length,Vector4 Color,string Sound,double Volume)
{
    internal static LaserSettings Read(string text)
    {
        using var doc=JsonDocument.Parse(string.IsNullOrWhiteSpace(text)?"{}":text);var r=doc.RootElement;
        var c=ColorTranslator.FromHtml(r.Text("color","#FF2A2A"));
        return new((int)Math.Clamp(r.Number("shots",3),1,20),Math.Clamp(r.Number("interval",.25),.05,2),Math.Clamp(r.Number("speed",15),1,60),Math.Clamp(r.Number("range",10),.5,30),Math.Clamp(r.Number("length",.4),.05,2),new(c.R/255f,c.G/255f,c.B/255f,1),r.Text("sound","default"),Math.Clamp(r.Number("volume",100),0,100));
    }
    internal double Duration=>(Shots-1)*Interval+Range/Speed;
}
internal sealed record FlyingBolt(Vector3 Tail,Vector3 Tip,Vector4 Color,float Flash,Vector3 Muzzle);
internal sealed class BakedFallApart
{
    internal const string RelativePath="Meshes/Body/FallApart/fall_apart.json";
    internal int Fps,Frames,Shudder;
    private readonly Dictionary<string,float[]> poses=[];
    internal static BakedFallApart? Load(string model)
    {
        string file=Path.Combine(Path.GetDirectoryName(model)!,RelativePath);if(!File.Exists(file))return null;
        using var doc=JsonDocument.Parse(File.ReadAllText(file));var r=doc.RootElement;
        var clip=new BakedFallApart{Fps=(int)r.Number("fps"),Frames=(int)r.Number("frameCount"),Shudder=(int)r.Number("shudderFrames")};
        if(r.Number("version")!=1||clip.Fps<1||clip.Fps>240||clip.Frames<2||clip.Frames>100000||clip.Shudder<0||clip.Shudder>=clip.Frames)throw new InvalidDataException("Invalid fall-apart clip timing.");
        foreach(var l in r.Get("links").Items())
        {
            var bytes=Convert.FromBase64String(l.Text("poses"));if(bytes.Length!=clip.Frames*28)throw new InvalidDataException("Invalid fall-apart clip poses.");
            var values=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
            if(values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Non-finite fall-apart pose.");clip.poses.Add(l.Text("link"),values);
        }
        return clip;
    }
    internal double Duration=>(Frames-1)/(double)Fps;
    internal double? FrameAt(EffectCue[] effects,double time)
    {
        var command=effects.LastOrDefault(c=>c.Servo=="FallApart"&&c.Time<=time);if(command==null)return null;
        using var doc=JsonDocument.Parse(command.Text);var r=doc.RootElement;double played=Math.Clamp((time-command.Time)*Math.Clamp(r.Number("speed",1),.1,10)*Fps,0,Frames-1);
        if(!r.Flag("reverse"))return played;double f=Frames-1-played;return f<=0?null:f;
    }
    internal bool Has(string link)=>poses.ContainsKey(link);
    internal Matrix4x4 Sample(string link,double frame)
    {
        var p=poses[link];frame=Math.Clamp(frame,0,Frames-1);int a=(int)frame,b=Math.Min(a+1,Frames-1);float t=(float)(frame-a);
        Vector3 Position(int f)=>new(p[f*7],p[f*7+1],p[f*7+2]);
        Quaternion Rotation(int f)=>Quaternion.Normalize(new(p[f*7+3],p[f*7+4],p[f*7+5],p[f*7+6]));
        return Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(Rotation(a),Rotation(b),t))*Matrix4x4.CreateTranslation(Vector3.Lerp(Position(a),Position(b),t));
    }
    internal float Weight(double frame){float t=Shudder==0?1:(float)Math.Clamp(frame/Shudder,0,1);return t*t*(3-2*t);}
    internal static Matrix4x4 Blend(Matrix4x4 a,Matrix4x4 b,float t)
    {
        if(t<=0)return a;if(t>=1)return b;
        Matrix4x4.Decompose(a,out _,out var qa,out var pa);Matrix4x4.Decompose(b,out _,out var qb,out var pb);
        return Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(qa,qb,t))*Matrix4x4.CreateTranslation(Vector3.Lerp(pa,pb,t));
    }
}
