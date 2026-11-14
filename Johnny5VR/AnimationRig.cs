using ServoAnimator;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace Johnny5VR;

// Vertices retain the importer rest pose. Only the small per-link palette is
// uploaded each frame; a million-triangle model is never rebuilt on the CPU.
internal sealed partial class AnimationRig
{
    private sealed record Joint(string Name,string Parent,string Type,Vector3 Axis,Matrix4x4 Origin,double Lower,double Upper,string Mimic,double Multiplier,double Offset);
    private sealed record Calibration(string Servo,string Control,double Min,double Zero,double Max,string Unit,bool Reverse);
    private readonly UrdfConfiguration rigConfiguration;
    private readonly ServoConfiguration servoConfiguration;
    private readonly string[] links;
    private readonly Dictionary<string,int> indices;
    private readonly Dictionary<string,Joint> joints;
    private readonly Dictionary<string,Matrix4x4> rest=new();
    // TipToe (B_TipToe) has no URDF joint: direct children of the root link get an extra
    // root-frame transform from Shared/Pose/TipToeGeometry, exactly as in the editors.
    private readonly TipToeGeometry? tipToe;
    private readonly string[] rootChildren=[];
    internal double TipToeRadians {get;private set;}
    private readonly Dictionary<string,Calibration> calibration=new();
    private readonly Dictionary<string,(double Min,double Max,bool Reverse)> body=new();
    private readonly Dictionary<string,bool> reversed=new();
    private readonly HashSet<string> supportedJoints;
    private Matrix4x4 placement,inversePlacement;
    internal Matrix4x4[] Palette {get;}
    internal Vector4 Apertures {get;private set;}
    internal AnimationRig(string file,string config)
    {
        var root=XDocument.Load(file).Root!;
        links=root.Elements("link").Select(l=>(string)l.Attribute("name")!).ToArray();
        indices=links.Select((s,i)=>(s,i)).ToDictionary(x=>x.s,x=>x.i+1);
        joints=root.Elements("joint").ToDictionary(j=>(string)j.Element("child")!.Attribute("link")!,j=>new Joint(
            (string)j.Attribute("name")!,(string)j.Element("parent")!.Attribute("link")!,(string?)j.Attribute("type")??"fixed",
            UrdfReader.Vec((string?)j.Element("axis")?.Attribute("xyz"),Vector3.UnitX),UrdfReader.Origin(j.Element("origin")),
            double.TryParse((string?)j.Element("limit")?.Attribute("lower"),System.Globalization.CultureInfo.InvariantCulture,out var lo)?lo:-Math.PI,
            double.TryParse((string?)j.Element("limit")?.Attribute("upper"),System.Globalization.CultureInfo.InvariantCulture,out var hi)?hi:Math.PI,
            (string?)j.Element("mimic")?.Attribute("joint")??"",
            double.TryParse((string?)j.Element("mimic")?.Attribute("multiplier"),System.Globalization.CultureInfo.InvariantCulture,out var mult)?mult:1,
            double.TryParse((string?)j.Element("mimic")?.Attribute("offset"),System.Globalization.CultureInfo.InvariantCulture,out var offset)?offset:0));
        supportedJoints=joints.Values.Select(j=>j.Name).ToHashSet();
        tipToe=TipToeGeometry.FromUrdf(file);
        if(tipToe!=null)
        {
            string rootLink=links.FirstOrDefault(l=>!joints.ContainsKey(l))??"";
            rootChildren=joints.Where(j=>j.Value.Parent==rootLink).Select(j=>j.Key).ToArray();
        }
        servoConfiguration = ConfigurationStore.LoadServos(config);
        rigConfiguration = ConfigurationStore.LoadUrdf(config, file);
        foreach (var channel in servoConfiguration.Servos)
            reversed[channel.Control.ToString()] = channel.Reversed;
        foreach (var gang in servoConfiguration.GangDirections)
            reversed[gang.Servo + "/" + gang.Control] = gang.Reversed;
        foreach (var motion in rigConfiguration.Motions)
        {
            string servo = motion.Servo.ToString(), control = motion.Control.ToString();
            calibration[servo + "/" + control] = new(servo, control, motion.MinExtent, motion.ZeroExtent,
                motion.MaxExtent, motion.Unit, motion.ReverseOverride ?? InheritedReverse(servo, control));
        }
        foreach (var entry in rigConfiguration.BodyMotions.ToArray())
            body[entry.Key] = (entry.Value.MinDegrees * Math.PI / 180, entry.Value.MaxDegrees * Math.PI / 180, entry.Value.Reverse);
        foreach (var definition in BodyControlCatalog.Controls)
        {
            // Migrate authored keys and former stock stops while retaining custom URDF limits for uncalibrated joints.
            if (!rigConfiguration.BodyMotions.Keys.Any(key => BodyName(key) == definition.Joint)) continue;
            var setting = rigConfiguration.BodySetting(definition);
            body[definition.Joint] = (setting.MinDegrees * Math.PI / 180, setting.MaxDegrees * Math.PI / 180, setting.Reverse);
        }
        var visiting=new HashSet<string>();
        Matrix4x4 World(string link)
        {
            if(rest.TryGetValue(link,out var w))return w;
            if(!indices.ContainsKey(link)||!visiting.Add(link))throw new InvalidDataException("Invalid/cyclic URDF rig link: "+link);
            w=joints.TryGetValue(link,out var j)?j.Origin*World(j.Parent):Matrix4x4.Identity;
            visiting.Remove(link);return rest[link]=w;
        }
        foreach(string l in links)World(l);
        InitializeSecondary(file,config);
        InitializeEffects(file,config);
        Palette=Enumerable.Repeat(Matrix4x4.Identity,links.Length+1).ToArray();SetPlacement(Matrix4x4.Identity);
    }
    internal int Bone(string link)=>indices.GetValueOrDefault(link);
    internal Vector3 RestPoint(string link,Vector3 local)=>Vector3.Transform(Vector3.Transform(local,rest[link]),placement);
    private bool InheritedReverse(string servo,string control)=>Enum.TryParse<ServoNames>(servo, out var name) && ControlDefinitions.UsesGangDirection(name)?reversed.GetValueOrDefault(servo+"/"+control):reversed.GetValueOrDefault(control);
    internal void SetPlacement(Matrix4x4 world){placement=world;Matrix4x4.Invert(world,out inversePlacement);}
    internal void Reset(){SetEffects([],0);Array.Fill(Palette,Matrix4x4.Identity);foreach(var link in links)if(HiddenAccessory(link))Palette[indices[link]]=default;Bolts.Clear();Apertures=Vector4.Zero;Array.Clear(TreadPhases);Array.Clear(TreadDistances);}
    internal string[] Unsupported(PlaybackShow show)=>show.Sequences.SelectMany(s=>s.Tracks).Where(t=>!Supports(t.Servo,t.Control)).Select(t=>t.Servo+(t.Control!=""?"/"+t.Control:"")).Distinct().ToArray();
    private bool Supports(string servo,string control)=>calibration.Values.Any(c=>c.Servo==servo&&(control==""||control==c.Control))||servo=="BothEyePop"||servo is "TrackForward" or "URDFSpin" or "URDFRotate" or "URDFStageX" or "URDFStageY" or "URDFStageHeading"||servo=="B_TipToe"&&tipToe!=null||servo.StartsWith("B_")&&(supportedJoints.Contains(BodyName(servo[2..]))||servo is "B_OpenLeftHand" or "B_OpenRightHand"||Enum.TryParse<ServoNames>(servo,out var paired)&&BodyControlCatalog.IsPairedGang(paired));
    internal static string BodyName(string name) =>
        Enum.TryParse<ServoNames>("B_" + name, out var servo) && BodyControlCatalog.IsBody(servo)
            ? BodyControlCatalog.Canonical(servo).ToString()[2..]
            : name.Replace("ShoulderPitch", "ShoulderRotate").Replace("ShoulderPivot", "ShoulderHug").Replace("ShoulderRaise", "ShoulderHug");
    internal void Apply(Dictionary<string,(double Value,double Stamp,string Servo)> pose,TravelState travel=default)
    {
        var values=new Dictionary<string,double>();
        Apertures=Vector4.Zero;
        foreach(var c in calibration.Values)
        {
            if(!MoviePoseContinuity.TryChannel(pose,c.Servo,c.Control,out double input))continue;
            double amount=Map(c,input),angle=amount*Math.PI/180;
            if(c.Control.Contains("Iris"))
            {
                double percent=Math.Clamp(amount,10,100),inches=percent>=55?.9+(percent-55)/45*.9:.3+(percent-10)/45*.6;
                float delta=(float)(inches*.0254/2-.01143);
                if(c.Control.StartsWith("Left"))Apertures=new(delta,Apertures.Y,0,0);else Apertures=new(Apertures.X,delta,0,0);
                continue;
            }
            if(c.Control=="NeckTiltLeft"||c.Control=="NeckTiltRight")continue;
            if(c.Control.StartsWith("Brow")&&c.Control.EndsWith("TopOpen"))angle=-angle;
            if(c.Control=="NeckTurn")angle=-angle;
            values[c.Control]=c.Unit=="mm"?amount/1000:angle;
            if(c.Control.EndsWith("EyeVent"))for(int i=1;i<=5;i++)values[c.Control+"Fin"+i]=angle;
        }
        if(pose.Keys.Any(k=>k is "servo:NeckNodUp" or "servo:NeckTiltRight" or "control:NeckTiltLeft" or "control:NeckTiltRight"))
        {
            var (nod,tilt)=NeckInputs(pose);
            double Extent(string servo,string control,double input)=>calibration.TryGetValue(servo+"/"+control,out var c)?Map(c,input)*Math.PI/180:0;
            values["NeckNodUp"]=-(Extent("NeckNodUp","NeckTiltLeft",nod)-Extent("NeckNodUp","NeckTiltRight",nod))*.5;
            values["NeckTiltRight"]=(Extent("NeckTiltRight","NeckTiltLeft",tilt)+Extent("NeckTiltRight","NeckTiltRight",tilt))*.5;
        }
        foreach(var p in pose.Where(p=>p.Key.StartsWith("servo:B_")).OrderBy(p=>p.Value.Stamp)
            .ThenBy(p=>Enum.TryParse<ServoNames>(p.Key[6..],out var s)&&BodyControlCatalog.IsGang(s)?0:1))
        {
            if(Enum.TryParse<ServoNames>(p.Key[6..],out var pairedServo)&&PairedBodyControls.Find(pairedServo) is {} pair)
            {
                foreach(var member in PairedBodyControls.Resolve(pair,rigConfiguration,p.Value.Value))
                {
                    var definition=BodyControlCatalog.Find(member.Servo);
                    values[definition.Joint]=rigConfiguration.BodySetting(definition).Map(member.Value,definition);
                }
                continue;
            }
            string name=BodyName(p.Key[8..]);
            if(name is "OpenLeftHand" or "OpenRightHand")
            {
                string side=name=="OpenLeftHand"?"Left":"Right";double open=Math.Clamp(p.Value.Value/100,0,1);
                foreach(var finger in joints.Values.Where(j=>j.Name.EndsWith(side)&&(j.Name.StartsWith("Finger")||j.Name.StartsWith("Thumb"))))
                {
                    var limits=body.GetValueOrDefault(finger.Name,(Min:finger.Lower,Max:finger.Upper,Reverse:false));
                    values[finger.Name]=finger.Name.StartsWith("FingerSpread")?limits.Min+(limits.Max-limits.Min)*open:limits.Max-(limits.Max-limits.Min)*open;
                }
                continue;
            }
            if(name=="TipToe")
            {
                var definition=BodyControlCatalog.Find(ServoNames.B_TipToe);
                if(tipToe!=null)values["TipToe"]=rigConfiguration.BodySetting(definition).Map(p.Value.Value,definition);
                continue;
            }
            var j=joints.Values.FirstOrDefault(j=>j.Name==name);if(j==null)continue;
            var range=body.GetValueOrDefault(name,(Min:j.Lower,Max:j.Upper,Reverse:false));double zero=Math.Clamp(0,range.Min,range.Max),v=Math.Clamp((range.Reverse?-1:1)*p.Value.Value/100,-1,1);
            values[name]=zero+(v<0?-v*(range.Min-zero):v*(range.Max-zero));
        }
        if(GameLaserElevation is {} elevation)values["LaserLower"]=(70-elevation)*Math.PI/180;
        foreach(var j in joints.Values.Where(j=>j.Mimic!=""))values[j.Name]=values.GetValueOrDefault(j.Mimic)*j.Multiplier+j.Offset;
        if(pose.TryGetValue("servo:BothEyePop",out var pop))foreach(string side in new[]{"Left","Right"})if(calibration.TryGetValue(side+"EyePop/"+side+"EyePop",out var c))values[side+"EyePop"]=Map(c,pop.Value)/1000;
        var worlds=new Dictionary<string,Matrix4x4>();
        var custom=new Dictionary<string,Matrix4x4>();
        TipToeRadians=tipToe==null?0:Math.Clamp(values.GetValueOrDefault("TipToe"),0,TipToeGeometry.MaximumRadians);
        if(tipToe!=null&&TipToeRadians>0)foreach(string child in rootChildren)custom[child]=tipToe.RootChildMotion(child,TipToeRadians);
        // The CAD neck cylinders pivot about their lower ball and extend toward
        // the moving upper ball, instead of remaining frozen in the rest pose.
        if(joints.ContainsKey("left_fabco_body_link"))
        {
            var pivot=new Vector3(0,0,.109029515f);
            var headMotion=Matrix4x4.CreateTranslation(-pivot)*Matrix4x4.CreateRotationX((float)values.GetValueOrDefault("NeckTiltRight"))*Matrix4x4.CreateRotationY((float)values.GetValueOrDefault("NeckNodUp"))*Matrix4x4.CreateTranslation(pivot);
            foreach(string side in new[]{"left","right"})
            {
                var lower=new Vector3(.0266785f,side=="left"?.016176236f:-.016176323f,.016069421f);
                var upper=new Vector3(.06509033f,side=="left"?.07829832f:-.07829833f,.11265779f);
                var neutral=upper-lower;var current=Vector3.Transform(upper,headMotion)-lower;
                var a=Vector3.Normalize(neutral);var b=Vector3.Normalize(current);var axis=Vector3.Cross(a,b);
                var rotation=axis.LengthSquared()<1e-12f?Matrix4x4.Identity:Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.Acos(Math.Clamp(Vector3.Dot(a,b),-1,1)));
                var swivel=Matrix4x4.CreateTranslation(-lower)*rotation*Matrix4x4.CreateTranslation(lower);
                custom[side+"_fabco_body_link"]=swivel;
                custom[side+"_fabco_piston_link"]=swivel*Matrix4x4.CreateTranslation(b*(current.Length()-neutral.Length()));
            }
        }
        Matrix4x4 World(string l)
        {
            if(worlds.TryGetValue(l,out var w))return w;
            w=Matrix4x4.Identity;
            if(joints.TryGetValue(l,out var j))
            {
                float v=(float)values.GetValueOrDefault(j.Name);var motion=j.Type=="prismatic"?Matrix4x4.CreateTranslation(j.Axis*v):j.Type is "revolute" or "continuous"?Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(j.Axis),v):Matrix4x4.Identity;
                w=motion*j.Origin*custom.GetValueOrDefault(l,Matrix4x4.Identity)*World(j.Parent);
            }
            return worlds[l]=w;
        }
        double Value(string s)=>pose.TryGetValue("servo:"+s,out var v)?v.Value:0;
        // TipToe: the caster wheels roll in from behind as the four-bar swings down and the body moves forward.
        if(tipToe!=null&&TipToeRadians>0){double roll=tipToe.At(TipToeRadians).CasterRoll;travel=travel with{CasterLeft=travel.CasterLeft+roll,CasterRight=travel.CasterRight+roll};}
        var secondary=SecondaryTransforms(Value("URDFSpin")+Value("URDFStageHeading"),travel);
        var sceneMotion=Matrix4x4.CreateRotationZ((float)(Value("URDFSpin")*Math.PI/180))*Matrix4x4.CreateRotationX((float)(-Value("URDFRotate")*Math.PI/180))*Matrix4x4.CreateRotationZ((float)(Value("URDFStageHeading")*Math.PI/180))*Matrix4x4.CreateTranslation((float)(Value("URDFStageX")/100+travel.X),(float)(Value("URDFStageY")/100+travel.Y),0);
        foreach(var l in links)World(l);
        var displayed=EffectWorlds(worlds);lastWorlds=displayed;lastScene=sceneMotion;
        foreach(var l in links){Matrix4x4.Invert(rest[l],out var inv);Palette[indices[l]]=HiddenAccessory(l)?default:inversePlacement*(FallFrame==null?secondary.GetValueOrDefault(l,Matrix4x4.Identity):Matrix4x4.Identity)*inv*displayed[l]*sceneMotion*placement;}
        UpdateBolts();
    }
    internal (double Nod,double Tilt) NeckInputs(Dictionary<string,(double Value,double Stamp,string Servo)> pose)
    {
        return NeckMix.Inputs(servoConfiguration, pose);
    }
    private double Map(Calibration c,double input)
    {
        if (Enum.TryParse<ServoNames>(c.Servo, out var servo) && Enum.TryParse<RobotControls>(c.Control, out var control)
            && rigConfiguration.Get(servo, control) != null)
            return rigConfiguration.Map(servo, control, input, servoConfiguration);
        bool positive=c.Unit=="mm"||c.Servo is "NoseBasket" or "VentsOpen";
        double zero=c.Unit=="mm"?(c.Reverse?c.Max:c.Min):Math.Clamp(c.Zero,c.Min,c.Max);
        if(positive){double v=Math.Clamp(input/(c.Servo.EndsWith("EyePop")?2000:100),0,1);return zero+v*((c.Reverse?c.Min:c.Max)-zero);}
        double x=Math.Clamp(input,-100,100)/100*(c.Reverse?-1:1)*(c.Servo=="IrisClose"?-1:1);
        return zero+(x<0?-x*(c.Min-zero):x*(c.Max-zero));
    }
}
