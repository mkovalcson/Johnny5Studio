using System.Numerics;
namespace ServoAnimator;
public sealed record CalibrationCheck(string Part,string Label,bool Good,string Detail,bool Required=true);
public sealed record CalibrationTarget(Vector3 Position,float Radius);
// Live numbers behind the wrist and arm-twist gates, for the gauge under the pose guide.
// Angle: degrees moved from neutral (signed toward the required direction for Twist).
// Opposite: degrees between this endpoint's rotation and the first one's (NaN on a first endpoint).
// Similarity: how alike the roll axis and the bend axis are, 0..1 (NaN outside roll steps).
public sealed record CalibrationGauge(string Motion,float Angle,float AngleNeeded,float Opposite,float OppositeNeeded,float Similarity,float SimilarityLimit)
{
    public bool AngleOk=>Angle>=AngleNeeded;
    public bool OppositeOk=>float.IsNaN(Opposite)||Opposite>=OppositeNeeded;
    public bool SimilarityOk=>float.IsNaN(Similarity)||Similarity<=SimilarityLimit;
    public string Summary()=>$"moved {Angle:F0}°/{AngleNeeded:F0}°"+(float.IsNaN(Opposite)?"":$", opposite {Opposite:F0}°/{OppositeNeeded:F0}°")+(float.IsNaN(Similarity)?"":$", like bending {Similarity:P0}/{SimilarityLimit:P0}");
}
public sealed record CalibrationAlignment(CalibrationCheck[] Checks,CalibrationTarget LeftTarget=null,CalibrationTarget RightTarget=null)
{
    public bool Ready=>Checks.All(c=>!c.Required||c.Good);
    public string Blocker=>Checks.FirstOrDefault(c=>c.Required&&!c.Good)?.Detail??"Ready";
    public bool PartReady(string part)=>Checks.Where(c=>c.Required&&(c.Part==part||c.Part=="Tracking")).All(c=>c.Good);
}
public sealed partial class BodyCalibrationWizard
{
    public CalibrationAlignment Alignment {get;private set;}=new([]);
    public string LastRejection {get;private set;}="";
    public string TrackingHint {get;set;}="";
    public CalibrationGauge LiveGauge {get;private set;}
    public void Observe(BodyTrackingFrame frame){Current=frame;Alignment=AssessAlignment(frame);LiveGauge=Gauge(frame);}
    public CalibrationGauge Gauge(BodyTrackingFrame f)
    {
        if(Complete||!f.TrackingValid||Step is <5 or >16||captured.Count<5)return null;
        bool left=ActiveLeft(Step)==true;string motion=Motion(Step);
        if(motion=="Twist")
        {
            var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;
            float angle=BodyTrackingFrame.Wrap(CalibratedArmMotion.ArmAzimuth(shoulder,Hand(f,left),left)-(left?Profile.LeftTwistNeutral:Profile.RightTwistNeutral))*180/MathF.PI;
            bool outward=Step is 14 or 16;
            return new(motion,outward?angle:-angle,9,float.NaN,float.NaN,float.NaN,float.NaN);
        }
        var neutral=left?Profile.LeftNeutral:Profile.RightNeutral;var pitchAxis=left?Profile.LeftPitchAxis:Profile.RightPitchAxis;
        var v=CalibratedArmMotion.RotationVector(Quaternion.Inverse(neutral)*WristOrientation(f,left));float angle2=v.Length()*180/MathF.PI;
        float opposite=float.NaN,similarity=float.NaN;Vector3 axis=v;
        if(Step is 6 or 8 or 10 or 12)
        {
            var previous=CalibratedArmMotion.RotationVector(Quaternion.Inverse(neutral)*WristOrientation(captured[Step-1],left));
            if(previous.Length()>1e-4f&&v.Length()>1e-4f)opposite=MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(previous),Vector3.Normalize(v)),-1,1))*180/MathF.PI;
            axis=previous-v;
        }
        if(motion=="Roll"&&axis.Length()>1e-4f&&angle2>=3)similarity=Math.Abs(Vector3.Dot(pitchAxis,Vector3.Normalize(axis)));
        return new(motion,angle2,CalibratedArmMotion.MinimumEndpointRadians*180/MathF.PI,opposite,CalibratedArmMotion.OppositeMinimumDegrees,similarity,CalibratedArmMotion.AxisSimilarityMaximum);
    }
    public static bool FingerDataAvailable(BodyTrackingFrame f)=>f.FingerSource.Contains("skeletal",StringComparison.OrdinalIgnoreCase)&&new[]{f.LeftFingers,f.RightFingers}.All(a=>a?.Length==5&&a.All(float.IsFinite));
    private float WristDriftAllowance(BodyTrackingFrame frame,bool left)
    {
        float angle=CalibratedArmMotion.RotationVector(Quaternion.Inverse(left?Profile.LeftNeutral:Profile.RightNeutral)*WristOrientation(frame,left)).Length();

        // Controller grip is offset from the wrist pivot. A bent wrist legitimately translates it:
        // measured pivots are 13-17 cm from the controller origin, so allow that lever arm.
        return .08f+Math.Min(.14f,2*BodyTrackingCalibration.AssumedGripRadius*MathF.Sin(Math.Min(MathF.PI,angle)/2));
    }
    public CalibrationAlignment AssessAlignment(BodyTrackingFrame f)
    {
        var checks=new List<CalibrationCheck>();CalibrationTarget lt=null,rt=null;
        void Add(string part,string label,bool good,string detail,bool required=true)=>checks.Add(new(part,label,good,detail,required));
        if(Complete)return new([new("Body","Calibration saved",true,"All measurements complete.")]);
        Add("Tracking","Headset + controllers",f.TrackingValid,f.TrackingValid?"Tracking is valid.":TrackingHint.Length>0?TrackingHint:"Restore headset and both controller tracking; measurement is paused.");
        if(!f.TrackingValid)return new(checks.ToArray());
        Vector3 Hand(bool left)=>this.Hand(f,left);
        string Side(bool left)=>left?"Left":"Right";
        void Target(bool left,Vector3 position,float radius=.08f){if(left)lt=new(position,radius);else rt=new(position,radius);}
        string Move(Vector3 from,Vector3 to,float tolerance)
        {
            var d=to-from;var parts=new List<string>();
            if(Math.Abs(d.Y)>tolerance)parts.Add($"{(d.Y>0?"raise":"lower")} {Math.Abs(d.Y)*100:F0} cm");
            if(Math.Abs(d.Z)>tolerance)parts.Add($"move {(d.Z<0?"forward":"back")} {Math.Abs(d.Z)*100:F0} cm");
            if(Math.Abs(d.X)>tolerance)parts.Add($"move to your {(d.X>0?"right":"left")} {Math.Abs(d.X)*100:F0} cm");
            return parts.Count>0?string.Join("; ",parts):"Return closer to the reference ring.";
        }
        if(Step==0)
        {
            Add("Head","Stand upright",f.Head.Translation.Y>=.7f,$"Head height {f.Head.Translation.Y*100:F0} cm; minimum 70 cm.");
            foreach(bool left in new[]{true,false}){float below=f.Head.Translation.Y-(left?f.Left:f.Right).Translation.Y;Add(Side(left),Side(left)+" hand down",below>=.3f,below>=.3f?$"{below*100:F0} cm below head (need 30 cm).":$"Lower {Side(left).ToLower()} hand another {(.3f-below)*100:F0} cm.");Target(left,new(left?-.2f:.2f,-.7f,0),.15f);}
        }
        if(Step==1)foreach(bool left in new[]{true,false})
        {float above=(left?f.Left:f.Right).Translation.Y-f.Head.Translation.Y;Add(Side(left),Side(left)+" hand overhead",above>=.05f,above>=.05f?$"{above*100:F0} cm above head (need 5 cm).":$"Raise {Side(left).ToLower()} hand another {(.05f-above)*100:F0} cm.");Target(left,new(left?-.2f:.2f,.25f,0),.12f);}
        if(Step==2&&captured.Count>=2)foreach(bool left in new[]{true,false})
        {
            Vector3 At(BodyTrackingFrame frame)=>this.Hand(frame,left);
            var center=(At(captured[0])+At(captured[1]))*.5f;float reach=Vector3.Distance(At(captured[0]),At(captured[1]))*.5f;
            Target(left,center-Vector3.UnitZ*reach,.15f);
            try{var fit=BodyTrackingCalibration.FitShoulder(At(captured[0]),At(captured[1]),Hand(left));bool side=left?fit.Center.X<-.03f:fit.Center.X>.03f;Add(Side(left),Side(left)+" shoulder fit",side,side?$"Reach {fit.Radius*100:F0} cm; fit error {fit.Error*100:F1} cm (limit {BodyTrackingCalibration.ShoulderFitMaximum*100:F0}).":"Face the original direction; check controller hand assignment.");}
            catch(InvalidDataException ex){Add(Side(left),Side(left)+" straight forward",false,ex.Message+" "+Move(Hand(left),center-Vector3.UnitZ*reach,.08f));}
        }
        if(Step==3)foreach(bool left in new[]{true,false})
        {
            var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;float reach=left?Profile.LeftUpper+Profile.LeftForearm:Profile.RightUpper+Profile.RightForearm;float distance=Vector3.Distance(Hand(left),shoulder);
            Target(left,shoulder+new Vector3(0,reach*.5f,-reach*.5f),.12f);
            try{BodyTrackingCalibration.SplitArm(reach,distance);Add(Side(left),Side(left)+" 90° arm pose",true,$"Bent reach {distance*100:F0} cm / straight reach {reach*100:F0} cm; consistent.");}
            catch(InvalidDataException){Add(Side(left),Side(left)+" 90° arm pose",false,$"Bent reach {distance*100:F0} cm. {(distance>reach*.94f?"Bend elbow more; move hand closer to shoulder.":"Open elbow slightly; move hand farther from shoulder.")}");}
        }
        if(Step==4)foreach(bool left in new[]{true,false})
        {
            var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;float upper=left?Profile.LeftUpper:Profile.RightUpper,fore=left?Profile.LeftForearm:Profile.RightForearm;var target=shoulder+new Vector3(0,-upper,-fore);var hand=Hand(left);Target(left,target,.12f);
            float height=hand.Y-target.Y,forward=shoulder.Z-hand.Z;
            Add(Side(left),Side(left)+" forearm level",Math.Abs(height)<=.12f,Math.Abs(height)<=.12f?$"Height offset {height*100:F0} cm (allow ±12 cm).":$"{(height>0?"Lower":"Raise")} {Side(left).ToLower()} hand {Math.Abs(height)*100:F0} cm; elbow down beside ribs.");
            Add(Side(left),Side(left)+" forearm forward",forward>=.1f,forward>=.1f?$"{forward*100:F0} cm forward of shoulder (need 10).":$"Move {Side(left).ToLower()} hand forward another {(.1f-forward)*100:F0} cm.");
        }
        if(Step is >=5 and <=12&&captured.Count>4)
        {
            bool left=ActiveLeft(Step)==true;string side=Side(left);var hand=Hand(left);var target=this.Hand(captured[4],left);float allowance=WristDriftAllowance(f,left),drift=Vector3.Distance(hand,target);Target(left,target,allowance);
            Add(side,side+" arm position",drift<=allowance,drift<=allowance?$"Controller drift {drift*100:F1} / {allowance*100:F1} cm allowed (grip estimate).":$"Drift {drift*100:F1} / {allowance*100:F1} cm allowed. "+Move(hand,target,.02f));
            var neutral=left?Profile.LeftNeutral:Profile.RightNeutral;var delta=Quaternion.Inverse(neutral)*WristOrientation(f,left);float angle=CalibratedArmMotion.RotationVector(delta).Length()*180/MathF.PI;
            Add(side,side+" wrist movement",angle>=10,$"Moved {angle:F1}°; need at least 10°. "+(angle<10?$"{(Motion(Step)=="Pitch"?"Bend":"Rotate")} farther from straight.":"Hold this endpoint still."));
            if(Step is 6 or 8 or 10 or 12)
            {
                var previous=Quaternion.Inverse(neutral)*WristOrientation(captured[Step-1],left);
                float previousAngle=CalibratedArmMotion.RotationVector(previous).Length()*180/MathF.PI;
                try
                {
                    var learned=CalibratedArmMotion.Learn(previous,delta);Add(side,"Opposite endpoint",true,$"First {previousAngle:F0}°, now {angle:F0}°; opposite directions.");
                    if(Motion(Step)=="Roll"){float similarity=Math.Abs(Vector3.Dot(left?Profile.LeftPitchAxis:Profile.RightPitchAxis,learned.Axis));float limit=CalibratedArmMotion.AxisSimilarityMaximum;Add(side,"Roll differs from bending",similarity<=limit,similarity<=limit?$"Distinct rotation axis measured ({similarity:P0} like bending; limit {limit:P0}).":$"Axes {similarity:P0} similar (limit {limit:P0}). Keep wrist straight; rotate palm. Back repeats first roll endpoint.");}
                }
                catch(InvalidDataException){float apart=previous.LengthSquared()>1e-8f&&delta.LengthSquared()>1e-8f?MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(CalibratedArmMotion.RotationVector(previous)),Vector3.Normalize(CalibratedArmMotion.RotationVector(delta))),-1,1))*180/MathF.PI:0;Add(side,"Opposite endpoint",false,previousAngle<10?$"First endpoint only {previousAngle:F1}°. Use A menu > Repeat previous step.":$"First {previousAngle:F0}°, now {angle:F0}°, directions {apart:F0}° apart (need {CalibratedArmMotion.OppositeMinimumDegrees:F0}°). Move past straight to the opposite side; use Back to redo the first endpoint.");}
            }
        }
        if(Step is >=13 and <=16&&captured.Count>4)
        {
            bool left=ActiveLeft(Step)==true;string side=Side(left);var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;var initial=this.Hand(captured[4],left);var hand=Hand(left);
            float height=Math.Abs(hand.Y-initial.Y),reach=new Vector2(hand.X-shoulder.X,hand.Z-shoulder.Z).Length();float angle=BodyTrackingFrame.Wrap(CalibratedArmMotion.ArmAzimuth(shoulder,hand,left)-(left?Profile.LeftTwistNeutral:Profile.RightTwistNeutral));bool outward=Step is 14 or 16;
            var direction=Vector3.Transform(initial-shoulder,Quaternion.CreateFromAxisAngle(Vector3.UnitY,(left?1:-1)*(outward?.5f:-.5f)));Target(left,shoulder+direction,.1f);
            Add(side,side+" forearm level",height<=.08f&&reach>=.1f,$"Height drift {height*100:F0} cm (limit 8); reach {reach*100:F0} cm (need 10). "+(height>.08f?Move(hand,new(hand.X,initial.Y,hand.Z),.02f):"Keep elbow beside ribs."));
            // The first endpoint had no standalone constraint before; show its direction before arming it.
            Add(side,side+" arm twist",outward?angle>=.15f:angle<=-.15f,$"Twist {angle*180/MathF.PI:F0}°; need {(outward?"outward +":"inward −")}9° or more.");
        }
        if(IsFingerStep(Step))
        {
            bool available=FingerDataAvailable(f);
            Add("Body","Finger sensors",available,available?"Thumb / index / middle sensor data available.":"No reliable finger data. You may continue; finger following will be disabled.",false);
            if(available)foreach(bool left in new[]{true,false})
            {
                var values=left?f.LeftFingers:f.RightFingers;float mean=values.Take(3).Average();
                if(Step==HandsOpenStep)Add(Side(left),Side(left)+" fingers open",true,$"Average curl {mean:P0}. Open as comfortably as possible; release trigger.");
                else{var open=left?Profile.LeftOpen:Profile.RightOpen;float difference=Enumerable.Range(0,3).Average(i=>Math.Abs(values[i]-open[i]));Add(Side(left),Side(left)+" fingers closed",difference>=.12f,$"Open/closed difference {difference:P0}; need 12%. "+(difference<.12f?"Close fingers more, or Back to repeat fully-open pose.":"Range measured."));}
            }
        }
        if(Step==CrouchStep){float drop=Profile.StandingHeight-f.Head.Translation.Y;Add("Head","Crouch depth",drop>=.08f,$"Head lowered {drop*100:F0} cm; need 8 cm. "+(drop<.08f?$"Crouch another {(.08f-drop)*100:F0} cm.":"Hold upright crouch."));}
        if(Step==BowStep)
        {
            float drop=Profile.StandingHeight-f.Head.Translation.Y,forward=-Vector3.Transform(f.Head.Translation-captured[0].Head.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitY,-yaw)).Z;
            Add("Head","Head lowering",drop>=.04f,$"Lowered {drop*100:F0} cm; need 4 cm. "+(drop<.04f?"Bend farther at the waist.":"Depth measured."));
            Add("Body","Forward bend",forward>=.04f,$"Forward travel {forward*100:F0} cm; need 4 cm. "+(forward<.04f?"Lean forward from the waist.":"Forward bend measured."));
        }
        // The real validation is also probed on a detached profile, ensuring Ready never hides a rejection.
        var probe=new BodyCalibrationWizard{Step=Step,Profile=Profile.DisplaySnapshot(),yaw=yaw};probe.captured.AddRange(captured);
        try{probe.Validate(f);}catch(InvalidDataException ex){if(checks.All(c=>!c.Required||c.Good))Add("Body","Measurement requirement",false,ex.Message);}
        if(collecting&&samples.Count>1)foreach(bool left in new[]{true,false})
        {
            Vector3 Position(BodyTrackingFrame frame)=>(left?frame.Left:frame.Right).Translation;
            var mean=samples.Aggregate(Vector3.Zero,(sum,frame)=>sum+Position(frame))/samples.Count;
            float movement=Math.Max(Vector3.Distance(Position(f),mean),samples.Max(frame=>Vector3.Distance(Position(frame),mean)));
            var current=Q(left?f.Left:f.Right);float rotation=samples.Max(frame=>1-Math.Abs(Quaternion.Dot(Q(left?frame.Left:frame.Right),current)));
            bool stable=movement<=.025f&&rotation<=.005f;
            Add(Side(left),Side(left)+" hand steady",stable,$"Movement {movement*100:F1}/2.5 cm; rotation {2*MathF.Acos(Math.Clamp(1-rotation,0,1))*180/MathF.PI:F1}/11.5°. "+(stable?"Hold still.":"Stop moving this hand; capture restarts automatically."));
        }
        return new(checks.ToArray(),lt,rt);
    }
}
