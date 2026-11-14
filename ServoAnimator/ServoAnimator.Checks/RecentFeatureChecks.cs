using ServoAnimator;
using System.IO;
using System.Text.Json;

internal static partial class Program
{
    private static void RecentFeatureChecks()
    {
        void Near(double actual,double expected,string message) => Check(Math.Abs(actual-expected)<.00001,message+$" ({actual} vs {expected})");
        void Reject(Action action,string message) { bool rejected=false; try { action(); } catch(ArgumentException){rejected=true;} catch(InvalidDataException){rejected=true;} Check(rejected,message); }
        var doubled=CommandGroupOperations.ProportionalTimes(new[]{5d,7,11,7},2);
        Near(doubled[5],5,"Proportional anchor stays fixed"); Near(doubled[7],9,"Double first gap"); Near(doubled[11],17,"Double nonuniform second gap");
        Near(CommandGroupOperations.ProportionalTimes(new[]{5d,7,11},.5)[11],8,"Halve spacing");
        foreach(double bad in new[]{0d,-1,double.NaN,double.PositiveInfinity}) Reject(()=>CommandGroupOperations.ProportionalTimes(new[]{1d,2},bad),"Reject invalid multiplier");
        foreach(var kind in new[]{ControllerKind.Xbox,ControllerKind.Steam})
        {
            var p=ControllerProfile.Empty(kind);
            var b=new ControllerBinding { Target="Servo:NeckTurn", Mode=ControllerMapMode.Absolute, DeadZone=.1, Low=-100, High=100, Expo=50 };
            p.Layers[0]["LeftX"]=b;
            var engine=new ControllerInputEngine();
            ControllerSample Sample(double value)=>new(){Connected=true,DeviceId=1,Values=new(){["LeftX"]=value}};
            engine.Evaluate(p,Sample(0),.025,_=>0);
            Near(engine.Evaluate(p,Sample(.55),.025,_=>0).Single().Value,31.25,"Expo half input softened after dead zone");
            Near(engine.Evaluate(p,Sample(-.55),.025,_=>0).Single().Value,-31.25,"Expo symmetric negative input");
            Near(engine.Evaluate(p,Sample(1),.025,_=>0).Single().Value,100,"Expo keeps full travel");
            b.Target="Servo:TrackForward"; b.Low=-200;b.High=200;
            engine.Reset();engine.Evaluate(p,Sample(0),.025,_=>0);
            Near(engine.Evaluate(p,Sample(.55),.025,_=>0).Single().Value,62.5,"TrackForward uses expo");
            Near(engine.Evaluate(p,Sample(0),.025,_=>0).Single().Value,0,"TrackForward neutral stops");
        }
        var paired=BodyControlCatalog.PairedPicklistControls.ToArray();
        Check(paired.Select(c=>c.Servo).Distinct().Count()==BodyControlCatalog.Controls.Length,"Paired list includes every control once");
        for(int i=0;i<paired.Length;i++) if(paired[i].Joint.EndsWith("Left")) Check(paired[i+1].Joint==paired[i].Joint[..^4]+"Right","Matching body controls adjacent");
        var map=new HeadroomTimeline {Format="johnny5-headroom-timeline",Version=1,SampleRate=44100,OriginalDuration=4,OutputDuration=6,TotalAddedSeconds=2,
            TimeMap=new(){
                new(){Kind="original",OriginalStart=0,OriginalEnd=1,OutputStart=0,OutputEnd=1},
                new(){Kind="inserted",OriginalStart=1,OriginalEnd=1,OutputStart=1,OutputEnd=2},
                new(){Kind="original",OriginalStart=1,OriginalEnd=2,OutputStart=2,OutputEnd=3},
                new(){Kind="stretch",OriginalStart=2,OriginalEnd=3,OutputStart=3,OutputEnd=5},
                new(){Kind="original",OriginalStart=3,OriginalEnd=4,OutputStart=5,OutputEnd=6}},
            Events=new(){new(){Id=1,Type="stutter",OriginalStart=1,OriginalEnd=1.5,OutputStart=1,OutputEnd=2,Repeats=2,SliceSeconds=.5,
                RepeatList=new(){new(){Index=1,OutputStart=1,OutputEnd=1.5,Duration=.5},new(){Index=2,OutputStart=1.5,OutputEnd=2,Duration=.5}}},
                new(){Id=2,Type="stretch",OriginalStart=2,OriginalEnd=3,OutputStart=3,OutputEnd=5,Factor=2}},
            Words=new(){new(){AlignConfidence=.2}}};
        map.Validate();Near(map.Map(0),0,"Map origin");Near(map.Map(1),2,"Insertion boundary lands after repeats");Near(map.Map(2.5),4,"Stretch midpoint");Near(map.Map(4),6,"Map end");Near(map.Map(7),9,"Shift later content");
        double last=-1;for(int i=0;i<=400;i++){double t=map.Map(i/100d);Check(t>=last,"Monotone time map");last=t;}
        map.Version=2;Reject(map.Validate,"Reject newer format");map.Version=1;
        map.TimeMap[2].OutputStart=2.1;Reject(map.Validate,"Reject discontinuity");map.TimeMap[2].OutputStart=2;
        var audio=new ServoCommand{Servo=ServoNames.Play,OffsetSeconds=10,TextValue="original.wav"};
        var doc=new AnimationDocument {Commands=new(){new(){Servo=ServoNames.B_ElbowLeft,OffsetSeconds=2,NumericValue=20},audio,
            new(){Servo=ServoNames.NeckTurn,OffsetSeconds=10,NumericValue=0},new(){Servo=ServoNames.NeckTurn,OffsetSeconds=11,NumericValue=10},
            new(){Servo=ServoNames.NeckTurn,OffsetSeconds=11.25,NumericValue=30},new(){Servo=ServoNames.NeckTurn,OffsetSeconds=11.5,NumericValue=50},
            new(){Servo=ServoNames.NeckTurn,OffsetSeconds=12.5,NumericValue=60},new(){Servo=ServoNames.Play,OffsetSeconds=15,TextValue="later.wav"}}};
        var cfg=UrdfConfiguration.CreateDefault();var servos=ServoConfiguration.CreateDefault();
        HeadroomEditResult Apply(bool motion=false,bool repeat=true)=>HeadroomTimelineEditor.Apply(doc,map,10,audio,true,repeat,motion,1,cfg,servos,new(),new(),new HashSet<ServoNames>(),50);
        var result=Apply();
        Check(doc.Commands[3].OffsetSeconds==11 && doc.Commands.Count==8,"Transform does not mutate source");
        Check(result.Commands.Any(c=>c.Servo==ServoNames.B_ElbowLeft&&c.OffsetSeconds==2&&c.NumericValue==20),"Pre-audio command untouched");
        Check(result.Commands.Any(c=>c.Servo==ServoNames.Play&&c.TextValue=="original.wav"&&c.OffsetSeconds==10),"Selected audio stays anchored");
        Check(result.Commands.Any(c=>c.Servo==ServoNames.Play&&c.TextValue=="later.wav"&&c.OffsetSeconds==17),"Subsequent audio shifted");
        foreach(double t in new[]{11.25,11.75,12.25}) Check(result.Commands.Any(c=>c.Servo==ServoNames.NeckTurn&&c.OffsetSeconds==t&&c.NumericValue==30),"Repeated original key value and timing");
        Check(result.Warnings.Any(w=>w.Contains("low-confidence")),"Alignment warning reported");
        Check(CommandConflicts.Find(result.Commands).Count==0,"No duplicate target/time commands");
        var moving=Apply(true);var again=Apply(true);
        Check(moving.Commands.Count==again.Commands.Count && moving.Commands.Zip(again.Commands).All(p=>CommandConflicts.SameContent(p.First,p.Second)),"Motion deterministic");
        Check(moving.Commands.Where(c=>!c.IsTextServo).All(c=>c.NumericValue>=ServoCommand.RangeFor(c.Servo).Min&&c.NumericValue<=ServoCommand.RangeFor(c.Servo).Max),"Motion clamped to ranges");
        Check(CommandConflicts.Find(moving.Commands).Count==0,"Motion has no duplicate commands");
        var front = TrackForwardDirection.OnFloor(0, 0, 0);
        Near(front.X, 1, "Forward follows robot front"); Near(front.Y, 0, "Forward has no lateral drift");
        front = TrackForwardDirection.OnFloor(30, 60, 0);
        Near(front.X, 0, "Stage heading and robot spin combine"); Near(front.Y, 1, "Turned robot travels along its front");
        front = TrackForwardDirection.OnFloor(0, 90, 90);
        Near(front.X, 0, "Vertical front has no floor direction X"); Near(front.Y, 0, "Vertical front has no floor direction Y");
        Near(CasterSwivel.Target(0, 0, 10, .3), -90, "Stationary turn swivels casters sideways");
        Near(CasterSwivel.Target(-90, 0, 0, .3), -90, "Stopped casters hold position");
        Near(CasterSwivel.Target(-90, 1, 0, .3), 0, "Forward straightens casters");
        Near(CasterSwivel.Target(-90, -1, 0, .3), 0, "Reverse straightens casters");
        Check(CasterSwivel.Target(0, 1, 90, .3) is < 0 and > -90, "Combined travel and turn blends caster angle");
        Near(CasterSwivel.Advance(0, -90, .1), -18, "Caster swivel is gradual");
        Near(CasterSwivel.Advance(-80, -90, 1), -90, "Caster swivel does not overshoot");        audio.AudioTrim=new(){SkipCommitted=true,SkipSeconds=.2};Reject(()=>Apply(),"Reject trimmed take rather than corrupt archive");
    }
}

