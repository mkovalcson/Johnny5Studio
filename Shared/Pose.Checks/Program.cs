using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using ServoAnimator;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static void Near(double a, double b, string message, double tolerance = 1e-9)
        => Check(double.IsFinite(a) && Math.Abs(a-b) <= tolerance * Math.Max(1,Math.Abs(b)), $"{message}: {a:R} vs {b:R}");
    private sealed class Curve : IPoseCurve
    {
        public double[] T {get;set;} = [0, .37, 1.23, 2.6];
        public double[] V {get;set;} = [20, 70, -30, 10];
        public double[] M {get;set;}
        public double Min => -100;
        public double Max => 100;
        public bool[] BreakAfter {get;set;} = [false,false,true,false];
        public Curve() => M=SplineUtil.Tangents(T,V,BreakAfter);
    }
    private static ServoCommand C(ServoNames s, double time, int value, RobotControls? child=null, bool disabled=false)
        => new() {Servo=s,OffsetSeconds=time,NumericValue=value,Control=child,Disable=disabled};
    private static Dictionary<string,double[]> Samples(Assembly old = null)
    {
        var result=new Dictionary<string,double[]>();
        var config=ServoConfiguration.CreateDefault();
        var neck=old?.GetType("ServoAnimator.NeckMix",true);
        for(int mask=0;mask<16;mask++)
        {
            int bit=0;
            foreach(var parent in new[]{ServoNames.NeckNodUp,ServoNames.NeckTiltRight})
                foreach(var child in new[]{RobotControls.NeckTiltLeft,RobotControls.NeckTiltRight})
                    config.SetGangReversed(parent,child,(mask & (1<<bit++))!=0);
            foreach(double nod in new[]{-100d,-40,0,25,100}) foreach(double tilt in new[]{-100d,-30,0,45,100})
            {
                var pair=old==null?NeckMix.Pistons(config,nod,tilt):((double Left,double Right))neck.GetMethod("Pistons",All).Invoke(null,[config,nod,tilt]);
                var back=old==null?NeckMix.Decompose(config,pair.Left,pair.Right):((double Nod,double Tilt))neck.GetMethod("Decompose",All).Invoke(null,[config,pair.Left,pair.Right]);
                result[$"neck/{mask}/{nod}/{tilt}"]=[pair.Left,pair.Right,back.Nod,back.Tilt];
            }
        }
        var swivel=old?.GetType("ServoAnimator.CasterSwivel",true);
        foreach(double angle in new[]{-179d,-90,0,72,179}) foreach(double forward in new[]{-.8d,0,.4}) foreach(double turn in new[]{-120d,0,75})
        {
            double roll=old==null?CasterSwivel.Roll(angle,forward,turn,.45,.095):(double)swivel.GetMethod("Roll",All).Invoke(null,[angle,forward,turn,.45,.095]);
            result[$"caster/{angle}/{forward}/{turn}"]=[roll];
        }
        var commands=new[]{C(ServoNames.TrackForward,0,20),C(ServoNames.TrackForward,.37,70),C(ServoNames.TrackForward,1.23,-30),C(ServoNames.TrackForward,2.6,10),
            C(ServoNames.TrackForward,3.13,0,disabled:true),C(ServoNames.URDFSpin,.43,45),C(ServoNames.URDFSpin,1.17,-30),
            C(ServoNames.URDFStageHeading,.77,80),C(ServoNames.URDFRotate,1.55,40)};
        foreach(bool spline in new[]{false,true}) foreach(double trail in new[]{0d,.095}) foreach(double origin in new[]{0d,.25})
        {
            var curve=new Curve(); object oldCurve=null;
            if(old!=null && spline)
            {
                var type=old.GetType("ServoAnimator.SplineCurve",true);oldCurve=Activator.CreateInstance(type);
                foreach(var name in new[]{"T","V","M","Min","Max","BreakAfter"})type.GetField(name).SetValue(oldCurve,typeof(Curve).GetProperty(name).GetValue(curve));
            }
            object path=old==null?new TimelineTravelPath(commands,origin,15,spline?curve:null,null,0,0,null,null,.45,trail)
                :Activator.CreateInstance(old.GetType("ServoAnimator.TrackForwardPath",true),All,null,[commands,origin,15d,oldCurve,null,0d,0d,null,null,.45,trail],null);
            // Deliberately seek both forwards and backwards through command boundaries.
            foreach(double time in new[]{0d,.02,.25,.37,.43,.77,1.17,1.23,1.55,2.6,3.13,4,.11,1.04,4.2})
            {
                var t=path.GetType(); var xy=((double X,double Y))t.GetMethod("At",All).Invoke(path,[time]);
                var caster=((double Angle,double Target,double Left,double Right))t.GetMethod("CasterAt",All).Invoke(path,[time]);
                double forward=(double)t.GetMethod("ForwardDistanceAt",All).Invoke(path,[time]);
                result[$"path/{spline}/{trail}/{origin}/{time}"]=[xy.X,xy.Y,forward,caster.Angle,caster.Target,caster.Left,caster.Right];
            }
        }
        return result;
    }
    private static Dictionary<string,double[]> VrNeckSamples(Assembly old = null)
    {
        var result=new Dictionary<string,double[]>();
        var type=old?.GetType("Johnny5VR.AnimationRig",true);
        object rig=type==null?null:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        for(int mask=0;mask<16;mask++)
        {
            var config=ServoConfiguration.CreateDefault();var reversed=new Dictionary<string,bool>();int bit=0;
            foreach(var parent in new[]{ServoNames.NeckNodUp,ServoNames.NeckTiltRight})
                foreach(var child in new[]{RobotControls.NeckTiltLeft,RobotControls.NeckTiltRight})
                {bool reverse=(mask&(1<<bit++))!=0;config.SetGangReversed(parent,child,reverse);reversed[parent+"/"+child]=reverse;}
            type?.GetField("reversed",All).SetValue(rig,reversed);
            foreach(double nod in new[]{-100d,0,40,100}) foreach(double tilt in new[]{-100d,0,30,100})
                foreach(double stamp in new[]{-1d,0,1}) foreach(string source in new[]{"NeckNodUp","NeckTiltRight"})
            {
                var pose=new Dictionary<string,(double Value,double Stamp,string Servo)>
                { ["servo:NeckNodUp"]=(nod,0,"NeckNodUp"),["servo:NeckTiltRight"]=(tilt,0,"NeckTiltRight"),["control:NeckTiltLeft"]=(35,stamp,source) };
                var value=type==null?NeckMix.Inputs(config,pose):((double Nod,double Tilt))type.GetMethod("NeckInputs",All).Invoke(rig,[pose]);
                result[$"{mask}/{nod}/{tilt}/{stamp}/{source}"]=[value.Nod,value.Tilt];
            }
        }
        return result;
    }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            if(args.Length>0 && args[0] is "--capture" or "--capture-vr")
            {
                string assembly=Path.GetFullPath(args[1]),folder=Path.GetDirectoryName(assembly);
                AssemblyLoadContext.Default.Resolving+=(_,name)=>File.Exists(Path.Combine(folder,name.Name+".dll"))?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(folder,name.Name+".dll")):null;
                var old=AssemblyLoadContext.Default.LoadFromAssemblyPath(assembly);
                File.WriteAllText(args[2],JsonSerializer.Serialize(args[0]=="--capture-vr"?VrNeckSamples(old):Samples(old),new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("Captured pre-extraction pose and travel baseline.");return 0;
            }
            var expected=JsonSerializer.Deserialize<Dictionary<string,double[]>>(File.ReadAllText(args[0]));
            var actual=Samples();Check(actual.Count==expected.Count,"Baseline cases retained");
            foreach(var (key,values) in expected)for(int i=0;i<values.Length;i++)Near(actual[key][i],values[i],key+"/"+i);
            var vrExpected=JsonSerializer.Deserialize<Dictionary<string,double[]>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"vr-neck-baseline.json")));
            var vrActual=VrNeckSamples();Check(vrActual.Count==vrExpected.Count,"VR baseline cases retained");
            foreach(var (key,values) in vrExpected)for(int i=0;i<values.Length;i++)Near(vrActual[key][i],values[i],"VR neck/"+key+"/"+i);
            Behavior();Console.WriteLine($"PASS: {checks} shared pose, neck, continuity, limits, ground-travel and caster assertions.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Behavior()
    {
        foreach(var pair in PairedBodyControls.All)
        {
            var calibration=new UrdfConfiguration();var l=BodyControlCatalog.Find(pair.Left);var r=BodyControlCatalog.Find(pair.Right);
            var left=calibration.BodySetting(l);var right=calibration.BodySetting(r);
            foreach(bool reverse in new[]{false,true})
            {
                right.Reverse=reverse;
                foreach(double value in new[]{-100d,-35,0,40,100})
                {
                    var pairedCommands=PairedBodyControls.Resolve(pair,calibration,value);
                    double la=left.Map(pairedCommands[0].Value,l),ra=right.Map(pairedCommands[1].Value,r);
                    Near(la,ra,pair.Name+" paired physical symmetry including reversal");
                    Check(pairedCommands.All(c=>c.Value>=-100&&c.Value<=100),"Paired commands respect native limits");
                }
            }
            right.MaxDegrees*=.6;right.MinDegrees*=.7;
            var limited=PairedBodyControls.Resolve(pair,calibration,100);
            Near(left.Map(limited[0].Value,l),right.Map(limited[1].Value,r),"Unequal calibrated ranges keep symmetric paired angles");
        }
        var config=ServoConfiguration.CreateDefault();
        var empty=new Dictionary<ServoNames,IPoseCurve>();
        var commands=new[]{C(ServoNames.IrisClose,0,15),C(ServoNames.IrisClose,1,35,RobotControls.LeftIris),C(ServoNames.IrisClose,2,70),
            C(ServoNames.IrisClose,2,90,RobotControls.LeftIris),C(ServoNames.NeckNodUp,0,100),C(ServoNames.NeckTiltRight,0,50)};
        var evaluator=new PhysicalPoseEvaluator(commands,empty,config);
        ServoCommand At(double time,RobotControls control)=>evaluator.Evaluate(time).Single(c=>c.Control==control);
        Check(At(1.5,RobotControls.LeftIris).NumericValue==35 && At(1.5,RobotControls.RightIris).NumericValue==15,"A child changes only its own channel");
        Check(At(2,RobotControls.LeftIris).NumericValue==70,"Editor gang wins exact-time tie");
        Check(evaluator.Evaluate(1).All(c=>c.NumericValue>=ServoCommand.RangeFor(c.Servo).Min && c.NumericValue<=ServoCommand.RangeFor(c.Servo).Max),"All physical targets stay in native limits");
        var saturated=NeckMix.Pistons(config,100,50);
        Check(Math.Abs(saturated.Left)<=100 && Math.Abs(saturated.Right)<=100,"Combined neck commands saturate each physical piston");
        var pose=new Dictionary<string,(double Value,double Stamp,string Servo)>();
        MoviePoseContinuity.Carry(pose,"IrisClose","LeftIris",22);
        Check(MoviePoseContinuity.TryChannel(pose,"IrisClose","LeftIris",out var carried)&&carried==22,"Individual channel carries between sequences");
        pose["servo:IrisClose"]=(50,0,"IrisClose");
        Check(MoviePoseContinuity.TryChannel(pose,"IrisClose","LeftIris",out var current)&&current==50,"New sequence parent supersedes carried child");
        pose["control:LeftIris"]=(80,0,"IrisClose");
        Check(MoviePoseContinuity.TryChannel(pose,"IrisClose","LeftIris",out var tied)&&tied==80,"VR child wins exact-time tie");
        Check(MoviePoseContinuity.TryChannel(pose,"IrisClose","LeftIris",out var parent,false)&&parent==50,"Explicit editor tie policy");
        pose.Clear();pose["servo:NeckNodUp"]=(100,0,"NeckNodUp");pose["servo:NeckTiltRight"]=(50,0,"NeckTiltRight");
        var mixed=NeckMix.Inputs(config,pose);Near(mixed.Nod,75,"VR shared neck saturation");Near(mixed.Tilt,25,"VR shared tilt saturation");
        pose["control:NeckTiltLeft"]=(-20,1,"NeckNodUp");
        var overridden=NeckMix.Inputs(config,pose);var decomposed=NeckMix.Decompose(config,-20,saturated.Right);
        Near(overridden.Nod,decomposed.Nod,"Direct piston override nod");Near(overridden.Tilt,decomposed.Tilt,"Direct piston override tilt");
        var carryEvaluator=new PhysicalPoseEvaluator([],empty,config,
            new Dictionary<ServoNames,double>{{ServoNames.LeftEyePop,321},{ServoNames.RightEyePop,456}},
            new Dictionary<(ServoNames,RobotControls),double>{{(ServoNames.IrisClose,RobotControls.LeftIris),22}});
        var carryPose=carryEvaluator.Evaluate(0);
        Check(carryPose.Single(c=>c.Control==RobotControls.LeftEyePop).NumericValue==321 && carryPose.Single(c=>c.Control==RobotControls.RightEyePop).NumericValue==456,"Untouched both-eye alias preserves individual carried values");
        Check(carryPose.Single(c=>c.Control==RobotControls.LeftIris).NumericValue==22,"Untouched gang retains carried child");
        var changes=new TimelineHardwareChanges();changes.Take(evaluator.Evaluate(0));Check(changes.Take(evaluator.Evaluate(0)).Length==0,"Repeated physical pose emits no duplicate target");
        foreach(double forward in new[]{-2d,0,3}) foreach(double turn in new[]{-90d,0,120})
        {var belts=DifferentialDrive.BeltTravel(forward,turn,.4);Near((belts.Left+belts.Right)/2,forward,"Track centre distance");Near(belts.Right-belts.Left,turn*Math.PI/180*.4,"Differential turn distance");}
        // The two host adapters must agree when supplied the same prepared values.
        var travelCommands=new[]{C(ServoNames.TrackForward,0,40),C(ServoNames.TrackForward,.73,-20),C(ServoNames.URDFSpin,1.12,45)};
        var timeline=new TimelineTravelPath(travelCommands,0,0,null,null,0,0,null,null,.45,.095);
        double Value(ServoNames servo,double time)=>travelCommands.LastOrDefault(c=>c.Servo==servo&&c.OffsetSeconds<=time)?.NumericValue??0;
        var prepared=new GroundTravelPath(Value,travelCommands.Select(c=>c.OffsetSeconds),0,0,.45,.095,boundaryTolerance:1e-9);
        foreach(double time in new[]{.1,.73,1.12,1.7,2,.3})
        {var a=timeline.At(time);var b=prepared.At(time);var wheels=timeline.CasterAt(time);Near(a.X,b.X,"Host ground X");Near(a.Y,b.Y,"Host ground Y");Near(wheels.Left,b.WheelLeft,"Host caster left");Near(wheels.Right,b.WheelRight,"Host caster right");}
        var isolated=new GroundTravelPath(Value,[0,.73,1.12],0,0,.45,.095);var before=isolated.At(.3);isolated.At(10);Check(isolated.At(.3)==before,"Backward seek restores exact caster and travel state");
        var curve=new Curve();var live=new PhysicalPoseEvaluator([C(ServoNames.NeckTurn,0,0),C(ServoNames.NeckTurn,2,50)],new Dictionary<ServoNames,IPoseCurve>{{ServoNames.NeckTurn,curve}},config);
        var first=live.Evaluate(.2).Single(c=>c.Control==RobotControls.NeckTurn).NumericValue;curve.V=[-90,-90,-90,-90];curve.M=[0,0,0,0];
        Check(live.Evaluate(.2).Single(c=>c.Control==RobotControls.NeckTurn).NumericValue!=first,"Curve adapters expose live edited samples without stale copies");
        Check(typeof(NeckMix).Assembly.GetReferencedAssemblies().All(a=>a.Name is not ("PresentationFramework" or "PresentationCore" or "SharpDX.Direct3D11" or "NAudio")),"Pose library owns no renderer, UI or audio-device dependency");
    }
}
