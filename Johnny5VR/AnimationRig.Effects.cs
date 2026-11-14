using System.Numerics;
using System.Text.Json;
namespace Johnny5VR;

internal sealed partial class AnimationRig
{
    internal BakedFallApart? FallApart {get;private set;}
    internal double? FallFrame {get;private set;}
    internal bool LaserVisible {get;private set;}
    internal bool ToolboxVisible {get;private set;}
    internal List<FlyingBolt> Bolts {get;}=[];
    private EffectCue[] effects=[];
    private double effectTime;
    private string accessory="None";
    private readonly Dictionary<(double,int),(Vector3 Muzzle,Vector3 Aim)> shotAims=[];
    private Dictionary<string,Matrix4x4> lastWorlds=[];
    private Matrix4x4 lastScene=Matrix4x4.Identity;
    internal void SetEffects(EffectCue[] commands,double time)
    {
        if(!ReferenceEquals(effects,commands)||time<effectTime)shotAims.Clear();
        effects=commands;effectTime=time;FallFrame=FallApart?.FrameAt(commands,time);
        bool? Flag(string servo,string property)
        {
            var command=commands.LastOrDefault(c=>c.Servo==servo&&c.Time<=time);if(command==null)return null;
            using var doc=JsonDocument.Parse(command.Text);return doc.RootElement.Get(property).ValueKind!=JsonValueKind.False;
        }
        bool swap=Flag("ToolboxSwap","swap")??accessory.Equals("Toolbox",StringComparison.OrdinalIgnoreCase);
        bool hide=Flag("HideLaser","hide")??(!commands.Any(c=>c.Servo=="ToolboxSwap"&&c.Time<=time)&&accessory.Equals("None",StringComparison.OrdinalIgnoreCase));
        LaserVisible=!hide&&!swap&&indices.ContainsKey("body_laser_head_link");ToolboxVisible=!hide&&swap;
    }
    private void InitializeEffects(string file,string config)
    {
        FallApart = BakedFallApart.Load(file);
        accessory = rigConfiguration.Rendering.DefaultAccessory.ToString();
        SetEffects([],0);
    }
    private bool HiddenAccessory(string link)=>link.StartsWith("body_laser_",StringComparison.Ordinal)&&
        (link=="body_laser_toolbox_link"?!ToolboxVisible:link=="body_laser_head_link"?!LaserVisible:!LaserVisible&&!ToolboxVisible);
    private Dictionary<string,Matrix4x4> EffectWorlds(Dictionary<string,Matrix4x4> live)
    {
        if(FallFrame is not {} frame||FallApart==null)return live;
        var shown=new Dictionary<string,Matrix4x4>();float weight=FallApart.Weight(frame);
        Matrix4x4 Show(string l)
        {
            if(shown.TryGetValue(l,out var existing))return existing;
            Matrix4x4 world;
            if(FallApart.Has(l))world=BakedFallApart.Blend(live[l],FallApart.Sample(l,frame),weight);
            else if(joints.TryGetValue(l,out var joint)){Matrix4x4.Invert(live[joint.Parent],out var inverse);world=live[l]*inverse*Show(joint.Parent);}
            else world=live[l];
            return shown[l]=world;
        }
        foreach(var link in links)Show(link);return shown;
    }
    private void UpdateBolts()
    {
        Bolts.Clear();var active=new HashSet<(double,int)>();
        if(LaserVisible&&lastWorlds.TryGetValue("body_laser_head_link",out var laser))
        {
            var transform=laser*lastScene*placement;var muzzle=Vector3.Transform(new(.3355f,0,.0545f),transform);
            var aim=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,transform));
            float scale=Vector3.TransformNormal(Vector3.UnitX,placement).Length();
            foreach(var command in effects.Where(c=>c.Servo=="FireLaser"&&c.Time<=effectTime))
            {
                var fire=LaserSettings.Read(command.Text);if(effectTime-command.Time>=fire.Duration)continue;
                for(int i=0;i<fire.Shots;i++)
                {
                    double age=effectTime-command.Time-i*fire.Interval;if(age<0||age>=fire.Range/fire.Speed)continue;
                    var key=(command.Time,i);active.Add(key);
                    if(!shotAims.TryGetValue(key,out var origin))shotAims[key]=origin=(muzzle,aim);
                    double front=Math.Min(age*fire.Speed,fire.Range),tail=Math.Max(0,front-fire.Length);
                    float fade=1-(float)Math.Clamp((front/fire.Range-.85)/.15,0,1);
                    if(fade>.01)Bolts.Add(new(origin.Muzzle+origin.Aim*(float)tail*scale,origin.Muzzle+origin.Aim*(float)front*scale,fire.Color with{W=fade},(float)Math.Max(0,1-age/.08),origin.Muzzle));
                }
            }
        }
        foreach(var key in shotAims.Keys.Where(k=>!active.Contains(k)).ToArray())shotAims.Remove(key);
    }
}
