using System.Numerics;
using System.Text.Json;
namespace ServoAnimator;

public sealed record BodyTrackingFrame(Matrix4x4 Head,Matrix4x4 Left,Matrix4x4 Right,float[] LeftFingers,float[] RightFingers,bool Valid,string FingerSource)
{
    public bool TrackingValid=>Valid&&Finite(Head)&&Finite(Left)&&Finite(Right);
    private static bool Finite(Matrix4x4 m)=>new[]{m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33,m.M41,m.M42,m.M43}.All(float.IsFinite);
    public static float Yaw(Matrix4x4 pose){var f=Vector3.TransformNormal(-Vector3.UnitZ,pose);return MathF.Atan2(-f.X,-f.Z);}
    public static float Wrap(float a)=>MathF.Atan2(MathF.Sin(a),MathF.Cos(a));
    // The headset sits about 11 cm above and 9 cm in front of the neck pivot
    // (head-local axes, -Z forward). Looking down or tilting the head swings the
    // headset around that pivot while the shoulders stay put.
    public static readonly Vector3 NeckFromEyes=new(0,-.11f,.09f);
    public static Vector3 Neck(Matrix4x4 head)=>head.Translation+Vector3.TransformNormal(NeckFromEyes,head);
    // Body-frame position measured from the neck pivot, expressed as if the head
    // were level and facing the body. For a level head this equals the original
    // head-relative position, so saved shoulder calibrations keep their meaning.
    public static Vector3 BodyLocal(Vector3 position,Matrix4x4 head,float bodyYaw)
        =>Vector3.Transform(position-Neck(head),Quaternion.CreateFromAxisAngle(Vector3.UnitY,-bodyYaw))+NeckFromEyes;
}
public sealed class BodyTrackingCalibration
{
    public int Version {get;set;}=2;
    public DateTimeOffset Saved {get;set;}
    public float LeftFitDeviation {get;set;} public float RightFitDeviation {get;set;}
    public Vector3 LeftShoulder {get;set;}
    public Vector3 RightShoulder {get;set;}
    public float LeftUpper {get;set;} public float LeftForearm {get;set;} public float RightUpper {get;set;} public float RightForearm {get;set;}
    public int ArmCalibrationVersion {get;set;}
    public Vector3 LeftPitchAxis {get;set;}=Vector3.UnitX;
    public Vector3 RightPitchAxis {get;set;}=Vector3.UnitX;
    public Vector3 LeftRollAxis {get;set;}=-Vector3.UnitZ;
    public Vector3 RightRollAxis {get;set;}=-Vector3.UnitZ;
    public float LeftTwistNeutral {get;set;} public float RightTwistNeutral {get;set;}
    // Where your wrist pivot sits relative to the controller's own origin (controller-local
    // metres). Learned from the wrist bend/roll steps: the point that stays still while the
    // controller rotates. Zero (older profiles) means the controller origin is used as the hand.
    public Vector3 LeftWristPivot {get;set;} public Vector3 RightWristPivot {get;set;}
    public float LeftWristPivotFit {get;set;} public float RightWristPivotFit {get;set;}
    public const float WristPivotMaximum=.25f,WristPivotFitMaximum=.03f,AssumedGripRadius=.17f,ShoulderFitMaximum=.22f;
    public bool HasWristPivot(bool left)=>(left?LeftWristPivot:RightWristPivot)!=Vector3.Zero;
    public Vector3 Wrist(Matrix4x4 controller,bool left)=>controller.Translation+Vector3.TransformNormal(left?LeftWristPivot:RightWristPivot,controller);
    // Least squares for the controller-local point that moves least across the poses, each
    // pose given in one common frame. Needs rotations about at least two different axes.
    public static (Vector3 Pivot,float Residual) FitWristPivot(IReadOnlyList<Matrix4x4> poses)
    {
        if(poses.Count<3)throw new InvalidDataException("Too few wrist poses for a pivot fit.");
        static Matrix4x4 Rot(Matrix4x4 m)=>new(m.M11,m.M12,m.M13,0,m.M21,m.M22,m.M23,0,m.M31,m.M32,m.M33,0,0,0,0,1);
        var meanP=Vector3.Zero;foreach(var m in poses)meanP+=m.Translation;meanP/=poses.Count;
        var sumR=new Matrix4x4();var sumRtP=Vector3.Zero;
        foreach(var m in poses){var r=Rot(m);sumR+=r;sumRtP+=Vector3.TransformNormal(m.Translation-meanP,Matrix4x4.Transpose(r));}
        var meanR=sumR*(1f/poses.Count);
        // Row vectors: c A = b with A = n(I - meanR meanR^T) and b = -sum (p_i - mean p) R_i^T
        var a=(Matrix4x4.Identity-meanR*Matrix4x4.Transpose(meanR))*poses.Count;
        a.M14=a.M24=a.M34=a.M41=a.M42=a.M43=0;a.M44=1;
        if(Math.Abs(a.GetDeterminant())<1e-4f||!Matrix4x4.Invert(a,out var inverse))throw new InvalidDataException("Wrist poses rotate too little, or all about one axis, for a pivot fit.");
        var pivot=Vector3.TransformNormal(-sumRtP,inverse);
        var points=poses.Select(m=>m.Translation+Vector3.TransformNormal(pivot,m)).ToArray();var centre=points.Aggregate(Vector3.Zero,(v,p)=>v+p)/points.Length;
        float residual=MathF.Sqrt(points.Average(p=>Vector3.DistanceSquared(p,centre)));
        if(!float.IsFinite(pivot.LengthSquared())||!float.IsFinite(residual))throw new InvalidDataException("Wrist pivot fit failed.");
        return(pivot,residual);
    }
    public float[] LeftTwist {get;set;}=[-.7f,.7f];
    public float[] RightTwist {get;set;}=[-.7f,.7f];
    public Quaternion LeftNeutral {get;set;}=Quaternion.Identity;
    public Quaternion RightNeutral {get;set;}=Quaternion.Identity;
    public float[] LeftRoll {get;set;}=[-1,1];
    public float[] RightRoll {get;set;}=[-1,1];
    public float[] LeftPitch {get;set;}=[-.7f,.7f];
    public float[] RightPitch {get;set;}=[-.7f,.7f];
    public float[] LeftOpen {get;set;}=new float[5];
    public float[] LeftClosed {get;set;}=[1,1,1,1,1];
    public float[] RightOpen {get;set;}=new float[5];
    public float[] RightClosed {get;set;}=[1,1,1,1,1];
    public float StandingHeight {get;set;} public float CrouchDrop {get;set;} public float BowDrop {get;set;} public float BowForward {get;set;} public float ReferenceYaw {get;set;} public float TurnAgreementRadians {get;set;}=.45f;
    public string FingerSource {get;set;}="Unavailable";
    // Measurements are stored per physical finger, so changing which robot finger
    // each one drives never invalidates a saved calibration (see Load).
    public const string CurrentFingerMapping="Thumb=Thumb;Index=UpperFinger;Middle=LowerFinger";
    public const string PreviousFingerMapping="Thumb=Thumb;Index=LowerFinger;Middle=UpperFinger";
    public string FingerMapping {get;set;}=CurrentFingerMapping;
    public float TurnLeft {get;set;} public float TurnRight {get;set;} public float WalkDistance {get;set;}
    public float MaximumJointDegreesPerSecond {get;set;}=60;
    public int MeasuredFingers(bool left)
    {var open=left?LeftOpen:RightOpen;var closed=left?LeftClosed:RightClosed;return open?.Length==5&&closed?.Length==5?Enumerable.Range(0,3).Count(i=>Math.Abs(closed[i]-open[i])>.1f):0;}
    public BodyTrackingCalibration DisplaySnapshot()=>(BodyTrackingCalibration)MemberwiseClone();
    public bool Valid()=>CalibratedArmMotion.Valid(this)&&((Version==2&&ArmCalibrationVersion==0)||(Version==3&&ArmCalibrationVersion==1))&&FingerMapping==CurrentFingerMapping&&new[]{LeftUpper,LeftForearm,RightUpper,RightForearm}.All(v=>float.IsFinite(v)&&v>.1f&&v<.7f)
        &&StandingHeight>.7f&&StandingHeight<2.5f&&CrouchDrop>.07f&&CrouchDrop<1.3f&&BowDrop>.02f&&BowForward>.02f
        &&new[]{ReferenceYaw,BowDrop,BowForward,TurnLeft,TurnRight,WalkDistance,TurnAgreementRadians}.All(float.IsFinite)
        &&TurnAgreementRadians>=.3f&&TurnAgreementRadians<=.8f
        &&Finite(LeftShoulder)&&Finite(RightShoulder)&&LeftShoulder.X<RightShoulder.X
        &&new[]{LeftOpen,LeftClosed,RightOpen,RightClosed}.All(v=>v?.Length==5&&v.All(float.IsFinite))
        &&new[]{LeftRoll,RightRoll,LeftPitch,RightPitch}.All(v=>v?.Length==2&&v.All(float.IsFinite)&&v[0]<-.1f&&v[1]>.1f)
        &&new[]{LeftNeutral,RightNeutral}.All(q=>float.IsFinite(q.LengthSquared())&&Math.Abs(q.LengthSquared()-1)<.05f)
        &&float.IsFinite(MaximumJointDegreesPerSecond)&&MaximumJointDegreesPerSecond>0&&MaximumJointDegreesPerSecond<=360
        &&Finite(LeftWristPivot)&&Finite(RightWristPivot)&&LeftWristPivot.Length()<=WristPivotMaximum&&RightWristPivot.Length()<=WristPivotMaximum&&float.IsFinite(LeftWristPivotFit)&&float.IsFinite(RightWristPivotFit);
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X+v.Y+v.Z);
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,IncludeFields=true};
    public void Save(string path)
    {
        if(!Valid())throw new InvalidDataException("Calibration is incomplete or invalid.");
        Saved=DateTimeOffset.Now;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,Json));File.Move(path+".tmp",path,true);
    }
    public static BodyTrackingCalibration Load(string path)
    {
        var profile=JsonSerializer.Deserialize<BodyTrackingCalibration>(File.ReadAllText(path),Json);
        if(profile?.FingerMapping==PreviousFingerMapping)profile.FingerMapping=CurrentFingerMapping;
        return profile?.Valid()==true?profile:throw new InvalidDataException("Motion calibration is incomplete or incompatible. Calibrate again.");
    }
    public static (Vector3 Center,float Radius,float Error) FitShoulder(Vector3 down,Vector3 up,Vector3 forward)
    {
        var a=up-down;var b=forward-down;var n=Vector3.Cross(a,b);float denominator=2*n.LengthSquared();
        if(denominator<.002f)throw new InvalidDataException("Arm poses are too similar. Keep the arm straight in three distinct directions.");
        var center=down+(a.LengthSquared()*Vector3.Cross(b,n)+b.LengthSquared()*Vector3.Cross(n,a))/denominator;
        float radius=Vector3.Distance(center,down),error=Math.Abs(Vector3.Distance(center,(down+up)/2));
        // Before the wrist pivot is known the three points carry the controller grip offset
        // (13-17 cm, rotating with the controller), so the sphere fit is allowed ShoulderFitMaximum of error.
        if(radius<.3f||radius>1.1f||error>ShoulderFitMaximum)throw new InvalidDataException($"Shoulder fit is inconsistent (reach {radius*100:F0} cm, fit error {error*100:F0} cm, limit {ShoulderFitMaximum*100:F0}). Repeat with straight arms and keep the shoulders relaxed.");
        return(center,radius,error);
    }
    public static (float Upper,float Forearm) SplitArm(float reach,float bentReach)
    {
        float discriminant=2*bentReach*bentReach-reach*reach;
        if(discriminant<-.015f||bentReach>reach*.94f)throw new InvalidDataException("Elbow measurement is not near 90 degrees. Hold upper arms forward and forearms upright.");
        float upper=(reach-MathF.Sqrt(Math.Max(0,discriminant)))/2,forearm=reach-upper;
        if(upper<.1f||forearm<.1f)throw new InvalidDataException("Arm segment estimate is outside the supported range.");
        return(upper,forearm);
    }
    public static float Twist(Quaternion rotation,Vector3 axis)
    {
        var v=new Vector3(rotation.X,rotation.Y,rotation.Z);return BodyTrackingFrame.Wrap(2*MathF.Atan2(Vector3.Dot(v,axis),rotation.W));
    }
}

public sealed partial class BodyCalibrationWizard
{
    public static readonly (string Name,string Instruction)[] Steps=
    [
        ("Standing / arms down","Stand upright, face forward. Relax both straight arms at your sides."),
        ("Straight arms overhead","Raise both straight arms above your head. Keep feet planted and shoulders relaxed."),
        ("Straight arms forward","Hold both straight arms directly forward at shoulder height."),
        ("Elbows at 90 degrees","Keep upper arms pointing forward. Bend elbows so forearms point straight up."),
        ("Arm / wrist neutral","Upper arms relaxed down, elbows bent 90 degrees, forearms straight forward. Keep wrists straight and palms facing each other."),
        ("Left wrist: bend up","LEFT hand only: bend your wrist UP like a hinge. Keep your forearm and elbow still. Do not rotate your palm."),
        ("Left wrist: bend down","LEFT hand only: bend your wrist DOWN like a hinge, past neutral. Keep your forearm still; do not rotate your palm."),
        ("Left wrist: rotate inward","LEFT hand only: keep your wrist straight. Rotate the controller around your forearm toward palm DOWN. Do not bend the wrist or move the elbow."),
        ("Left wrist: rotate outward","LEFT hand only: rotate the opposite way toward palm UP. Keep your wrist straight and elbow still."),
        ("Right wrist: bend up","RIGHT hand only: bend your wrist UP like a hinge. Keep your forearm and elbow still. Do not rotate your palm."),
        ("Right wrist: bend down","RIGHT hand only: bend your wrist DOWN like a hinge, past neutral. Keep your forearm still; do not rotate your palm."),
        ("Right wrist: rotate inward","RIGHT hand only: keep your wrist straight. Rotate the controller around your forearm toward palm DOWN. Do not bend the wrist or move the elbow."),
        ("Right wrist: rotate outward","RIGHT hand only: rotate the opposite way toward palm UP. Keep your wrist straight and elbow still."),
        ("Left arm: twist inward","LEFT upper arm stays down, elbow bent 90 degrees beside your ribs. Sweep your forearm inward across your body by twisting the upper arm. Keep wrist straight; other arm neutral."),
        ("Left arm: twist outward","LEFT upper arm stays down with elbow beside your ribs. Sweep the bent forearm OUTWARD, past straight ahead. Keep wrist straight; other arm neutral."),
        ("Right arm: twist inward","RIGHT upper arm stays down, elbow bent 90 degrees beside your ribs. Sweep your forearm inward across your body by twisting the upper arm. Keep wrist straight; other arm neutral."),
        ("Right arm: twist outward","RIGHT upper arm stays down with elbow beside your ribs. Sweep the bent forearm OUTWARD, past straight ahead. Keep wrist straight; other arm neutral."),
        ("Hands open","Keep controller straps secure. Release the trigger, straighten and open your fingers."),
        ("Hands closed","Close fingers naturally around the controllers, without forcing a tight squeeze."),
        ("Crouch reference","Face forward and crouch comfortably. Keep your head and torso as upright as possible."),
        ("Bend forward reference","Stand back up, then bend forward comfortably from your waist. Keep knees fairly straight.")

    ];
    public const int HandsOpenStep=17,HandsClosedStep=18,CrouchStep=19,BowStep=20;
    public static bool IsFingerStep(int step)=>step is HandsOpenStep or HandsClosedStep;
    public static bool? ActiveLeft(int step)=>step is >=5 and <=8 or 13 or 14?true:step is >=9 and <=12 or 15 or 16?false:null;
    public static string Motion(int step)=>step is 5 or 6 or 9 or 10?"Pitch":step is 7 or 8 or 11 or 12?"Roll":step is >=13 and <=16?"Twist":"";
    public int Step {get;private set;}
    public bool Complete {get;private set;}
    public float Progress {get;private set;}
    public string Feedback {get;private set;}="Press and release a trigger to measure this pose. B cancels.";
    public BodyTrackingCalibration Profile {get;private set;}=new(){Version=3,ArmCalibrationVersion=1};
    public BodyCalibrationWizard DisplaySnapshot()=>new(){Step=Step,Complete=Complete,Progress=Progress,Feedback=Feedback,Profile=Profile.DisplaySnapshot(),yaw=yaw,warmup=warmup,collecting=collecting,Alignment=Alignment,LastRejection=LastRejection,TrackingHint=TrackingHint,LiveGauge=LiveGauge};
    public BodyTrackingFrame Current {get;private set;}
    private readonly List<BodyTrackingFrame> samples=[];
    private readonly List<BodyTrackingFrame> captured=[];
    private bool collecting,triggerDown;
    private string pivotNote="";
    private float elapsed,warmup;
    private float yaw;
    public bool Collecting=>collecting;
    public float CountdownRemaining=>collecting&&(IsFingerStep(Step))?Math.Max(0,warmup):0;
    public Vector3 Local(Vector3 p,Matrix4x4 head)=>BodyTrackingFrame.BodyLocal(p,head,yaw);
    // The hand point used everywhere: the wrist pivot once it is known for that side, else the controller origin.
    public Vector3 Hand(BodyTrackingFrame frame,bool left)=>Local(Profile.Wrist(left?frame.Left:frame.Right,left),frame.Head);
    private Matrix4x4 LocalPose(BodyTrackingFrame frame,bool left)
    {
        var pose=left?frame.Left:frame.Right;var rotation=Quaternion.Inverse(Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw))*Q(pose);
        return Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(Local(pose.Translation,frame.Head));
    }
    public string WristPivotSummary(bool left)
    {
        if(!Profile.HasWristPivot(left))return(left?"Left":"Right")+" wrist pivot: using controller origin";
        return $"{(left?"Left":"Right")} wrist pivot {(left?Profile.LeftWristPivot:Profile.RightWristPivot).Length()*100:F1} cm from grip origin (fit {(left?Profile.LeftWristPivotFit:Profile.RightWristPivotFit)*100:F1} cm)";
    }
    // After a side's wrist bend and roll endpoints, find its wrist pivot and redo that side's
    // shoulder fit, arm lengths, neutral orientation, twist neutral and pitch axis with the
    // corrected hand point (the roll axis is learned right after, by the caller). Nothing is
    // kept unless every refit succeeds, so a poor fit leaves the grip-origin measurements.
    private string ApplyWristPivot(bool left,BodyTrackingFrame rollOutward)
    {
        int first=left?5:9;var poses=new List<Matrix4x4>{LocalPose(captured[4],left)};
        for(int i=first;i<first+3;i++)poses.Add(LocalPose(captured[i],left));poses.Add(LocalPose(rollOutward,left));
        var before=Profile.DisplaySnapshot();
        try
        {
            var fit=BodyTrackingCalibration.FitWristPivot(poses);
            if(fit.Pivot.Length()>BodyTrackingCalibration.WristPivotMaximum||fit.Residual>BodyTrackingCalibration.WristPivotFitMaximum)
                throw new InvalidDataException($"pivot {fit.Pivot.Length()*100:F0} cm, fit {fit.Residual*100:F1} cm");
            if(left){Profile.LeftWristPivot=fit.Pivot;Profile.LeftWristPivotFit=fit.Residual;}else{Profile.RightWristPivot=fit.Pivot;Profile.RightWristPivotFit=fit.Residual;}
            var shoulder=BodyTrackingCalibration.FitShoulder(Hand(captured[0],left),Hand(captured[1],left),Hand(captured[2],left));
            if(left?shoulder.Center.X>=-.03f:shoulder.Center.X<=.03f)throw new InvalidDataException("shoulder side");
            var split=BodyTrackingCalibration.SplitArm(shoulder.Radius,Vector3.Distance(Hand(captured[3],left),shoulder.Center));
            if(left){Profile.LeftShoulder=shoulder.Center;Profile.LeftFitDeviation=shoulder.Error;(Profile.LeftUpper,Profile.LeftForearm)=(split.Upper,split.Forearm);}
            else{Profile.RightShoulder=shoulder.Center;Profile.RightFitDeviation=shoulder.Error;(Profile.RightUpper,Profile.RightForearm)=(split.Upper,split.Forearm);}
            var neutral=WristOrientation(captured[4],left);float twistNeutral=CalibratedArmMotion.ArmAzimuth(shoulder.Center,Hand(captured[4],left),left);
            if(left){Profile.LeftNeutral=neutral;Profile.LeftTwistNeutral=twistNeutral;}else{Profile.RightNeutral=neutral;Profile.RightTwistNeutral=twistNeutral;}
            var pitch=CalibratedArmMotion.Learn(Quaternion.Inverse(neutral)*WristOrientation(captured[first],left),Quaternion.Inverse(neutral)*WristOrientation(captured[first+1],left));
            if(left){Profile.LeftPitchAxis=pitch.Axis;Profile.LeftPitch=pitch.Range;}else{Profile.RightPitchAxis=pitch.Axis;Profile.RightPitch=pitch.Range;}
            return WristPivotSummary(left)+".";
        }
        catch(InvalidDataException ex){Profile=before;return(left?"Left":"Right")+" wrist pivot not resolved ("+ex.Message+"); using the controller origin.";}
    }
    public Quaternion WristOrientation(BodyTrackingFrame frame,bool left)
    {
        var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;var pose=left?frame.Left:frame.Right;
        var hand=Hand(frame,left);
        float upper=left?Profile.LeftUpper:Profile.RightUpper,fore=left?Profile.LeftForearm:Profile.RightForearm;
        if(upper<=0||fore<=0)return Q(pose);
        var elbow=BodyTrackingSolver.SolveElbow(shoulder,BodyTrackingSolver.ElbowTarget(shoulder,hand,upper+fore),upper,fore,new(left?-.25f:.25f,-1,.15f));
        var basis=BodyTrackingSolver.ForearmRotation(shoulder,elbow.Elbow,hand);
        var handBody=Quaternion.Inverse(Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw))*Q(pose);
        return Quaternion.Normalize(Quaternion.Inverse(basis)*handBody);
    }
    public (float Pitch,float Roll) WristAngles(BodyTrackingFrame frame,bool left)
    {
        var delta=Quaternion.Inverse(left?Profile.LeftNeutral:Profile.RightNeutral)*WristOrientation(frame,left);
        var angles=CalibratedArmMotion.Wrist(Profile,left,delta);
        return(angles.Pitch*180/MathF.PI,angles.Roll*180/MathF.PI);
    }
    private static Quaternion Q(Matrix4x4 p)=>Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(p));
    public void Back()
    {
        if(Step==0)return;Step--;captured.RemoveRange(Step,captured.Count-Step);collecting=false;Complete=false;Progress=0;samples.Clear();elapsed=0;warmup=0;LastRejection="";
        Feedback="Repeat this pose. Press and release a trigger to measure.";
    }
    public void Update(BodyTrackingFrame frame,float dt,bool trigger)
    {
        Observe(frame);if(Complete)return;
        bool pressed=trigger&&!triggerDown;triggerDown=trigger;
        if(!frame.TrackingValid){samples.Clear();elapsed=0;Progress=0;Feedback="Tracking lost: both controllers and headset must be visible. Measurement paused.";return;}
        if(pressed&&!collecting){collecting=true;samples.Clear();elapsed=0;warmup=IsFingerStep(Step)?3f:.4f;Feedback="Release trigger, then hold this pose still…";}
        if(!collecting)return;
        if(trigger){Feedback=IsFingerStep(Step)?"Release trigger to begin the 3-second finger preparation countdown…":"Release trigger, then hold this pose still…";return;}
        if(warmup>0)
        {
            warmup=Math.Max(0,warmup-Math.Clamp(dt,0,.05f));
            if(IsFingerStep(Step))Feedback=$"Prepare your fingers — measurement starts in {Math.Max(1,(int)Math.Ceiling(warmup))}…";
            return;
        }
        if(!Alignment.Ready)
        {
            samples.Clear();elapsed=0;Progress=0;LastRejection=Alignment.Blocker;
            Feedback="Waiting for alignment: "+Alignment.Blocker;return;
        }
        Feedback="Measuring… hold this pose steady for 1.2 seconds.";
        samples.Add(frame);elapsed+=Math.Clamp(dt,0,.05f);Progress=Math.Clamp(elapsed/1.2f,0,1);
        if(elapsed<1.2f||samples.Count<20)return;
        var meanHead=samples.Aggregate(Vector3.Zero,(v,s)=>v+s.Head.Translation)/samples.Count;
        var meanLeft=samples.Aggregate(Vector3.Zero,(v,s)=>v+s.Left.Translation)/samples.Count;
        var meanRight=samples.Aggregate(Vector3.Zero,(v,s)=>v+s.Right.Translation)/samples.Count;
        float spread=samples.Max(s=>Math.Max(Vector3.Distance(s.Left.Translation,meanLeft),Vector3.Distance(s.Right.Translation,meanRight)));
        float rotationSpread=samples.Max(s=>Math.Max(1-Math.Abs(Quaternion.Dot(Q(s.Left),Q(frame.Left))),1-Math.Abs(Quaternion.Dot(Q(s.Right),Q(frame.Right)))));
        if(spread>.025f||rotationSpread>.005f)
        {samples.Clear();elapsed=0;Progress=0;Feedback=$"Measurement reset: hand movement {spread*100:F1} cm (limit 2.5 cm), rotation {2*MathF.Acos(Math.Clamp(1-rotationSpread,0,1))*180/MathF.PI:F1}° (limit 11.5°). Hold still; capture will retry automatically.";LastRejection=Feedback;return;}
        var head=frame.Head;head.Translation=meanHead;var left=frame.Left;left.Translation=meanLeft;var right=frame.Right;right.Translation=meanRight;
        float[] Mean(bool l)=>Enumerable.Range(0,5).Select(i=>samples.Average(s=>{var values=l?s.LeftFingers:s.RightFingers;return values?.Length==5&&float.IsFinite(values[i])?values[i]:0;})).ToArray();
        var average=frame with{Head=head,Left=left,Right=right,LeftFingers=Mean(true),RightFingers=Mean(false),FingerSource=IsFingerStep(Step)&&!FingerDataAvailable(frame)?"Finger sensors unavailable":frame.FingerSource};
        try
        {
            pivotNote="";Validate(average);
            captured.Add(average);Step++;collecting=false;Progress=0;LastRejection="";
            Feedback=(pivotNote.Length>0?pivotNote+" ":"")+"Measured successfully. "+(Step>=Steps.Length?"Calibration complete.":"Next: "+Steps[Step].Name+". Press and release a trigger when ready.");
            if(Step==CrouchStep&&!average.FingerSource.Contains("skeletal",StringComparison.OrdinalIgnoreCase))Feedback="Finger data unavailable: finger following is disabled. Arm/body calibration can continue. Next: "+Steps[Step].Name;
            if(Step==Steps.Length){Complete=true;Progress=1;Profile.Saved=DateTimeOffset.Now;}
            Observe(frame);
        }
        catch(InvalidDataException ex){collecting=false;Progress=0;LastRejection=ex.Message;Feedback=ex.Message+" Press trigger to retry, or use Back in the calibration menu.";}
    }
    private void Validate(BodyTrackingFrame f)
    {
        if(Step==0)
        {
            if(f.Head.Translation.Y<.7f||Math.Max(f.Left.Translation.Y,f.Right.Translation.Y)>f.Head.Translation.Y-.3f)throw new InvalidDataException("Stand upright with both hands down.");
            yaw=BodyTrackingFrame.Yaw(f.Head);Profile.ReferenceYaw=yaw;Profile.StandingHeight=f.Head.Translation.Y;Profile.FingerSource=f.FingerSource;
        }
        if(Step==1&&Math.Min(f.Left.Translation.Y,f.Right.Translation.Y)<f.Head.Translation.Y+.05f)throw new InvalidDataException("Both straight arms need to be above the headset.");
        if(Step==2)
        {
            Vector3 At(BodyTrackingFrame p,bool left)=>Hand(p,left);
            var l=BodyTrackingCalibration.FitShoulder(At(captured[0],true),At(captured[1],true),At(f,true));
            var r=BodyTrackingCalibration.FitShoulder(At(captured[0],false),At(captured[1],false),At(f,false));
            if(l.Center.X>=-.03f||r.Center.X<=.03f)throw new InvalidDataException("Shoulder sides could not be identified. Face the original direction with controllers in the correct hands.");
            Profile.LeftShoulder=l.Center;Profile.RightShoulder=r.Center;Profile.LeftFitDeviation=l.Error;Profile.RightFitDeviation=r.Error;Profile.LeftUpper=l.Radius/2;Profile.LeftForearm=l.Radius/2;Profile.RightUpper=r.Radius/2;Profile.RightForearm=r.Radius/2;
        }
        if(Step==3)
        {
            var l=BodyTrackingCalibration.SplitArm(Profile.LeftUpper+Profile.LeftForearm,Vector3.Distance(Hand(f,true),Profile.LeftShoulder));
            var r=BodyTrackingCalibration.SplitArm(Profile.RightUpper+Profile.RightForearm,Vector3.Distance(Hand(f,false),Profile.RightShoulder));
            (Profile.LeftUpper,Profile.LeftForearm)=(l.Upper,l.Forearm);(Profile.RightUpper,Profile.RightForearm)=(r.Upper,r.Forearm);
        }
        if(Step==4)
        {
            foreach(bool left in new[]{true,false})
            {
                var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;var hand=Hand(f,left);
                if(hand.Z>shoulder.Z-.1f||Math.Abs(hand.Y-(shoulder.Y-(left?Profile.LeftUpper:Profile.RightUpper)))>.12f)
                    throw new InvalidDataException("Keep upper arms DOWN, elbows bent 90 degrees beside your ribs, and forearms pointing forward.");
            }
            Profile.LeftNeutral=WristOrientation(f,true);Profile.RightNeutral=WristOrientation(f,false);
            Profile.LeftTwistNeutral=CalibratedArmMotion.ArmAzimuth(Profile.LeftShoulder,Hand(f,true),true);
            Profile.RightTwistNeutral=CalibratedArmMotion.ArmAzimuth(Profile.RightShoulder,Hand(f,false),false);
        }
        if(Step is >=5 and <=12)
        {
            bool left=ActiveLeft(Step)==true;var pose=left?f.Left:f.Right;var neutralPose=left?captured[4].Left:captured[4].Right;
            float drift=Vector3.Distance(Hand(f,left),Hand(captured[4],left));
            float allowance=WristDriftAllowance(f,left);
            if(drift>allowance)throw new InvalidDataException($"{(left?"Left":"Right")} controller moved {drift*100:F1} cm from neutral; allowance {allowance*100:F1} cm including estimated grip travel. Bring the forearm back toward neutral.");
            float rotation=CalibratedArmMotion.RotationVector(Quaternion.Inverse(left?Profile.LeftNeutral:Profile.RightNeutral)*WristOrientation(f,left)).Length();
            if(rotation<10*MathF.PI/180)throw new InvalidDataException($"{(left?"Left":"Right")} wrist moved only {rotation*180/MathF.PI:F1}°. { (Motion(Step)=="Pitch"?"Bend":"Rotate") } at least 10° from straight before measuring.");
            if(Step is 6 or 8 or 10 or 12)
            {
                if(Step is 8 or 12)pivotNote=ApplyWristPivot(left,f);
                var neutral=left?Profile.LeftNeutral:Profile.RightNeutral;
                var a=Quaternion.Inverse(neutral)*WristOrientation(captured[Step-1],left);
                var b=Quaternion.Inverse(neutral)*WristOrientation(f,left);
                var learned=CalibratedArmMotion.Learn(a,b);
                if(Motion(Step)=="Pitch")
                {if(left){Profile.LeftPitchAxis=learned.Axis;Profile.LeftPitch=learned.Range;}else{Profile.RightPitchAxis=learned.Axis;Profile.RightPitch=learned.Range;}}
                else
                {
                    var pitchAxis=left?Profile.LeftPitchAxis:Profile.RightPitchAxis;
                    if(Math.Abs(Vector3.Dot(pitchAxis,learned.Axis))>CalibratedArmMotion.AxisSimilarityMaximum)
                        throw new InvalidDataException($"That rotation is {Math.Abs(Vector3.Dot(pitchAxis,learned.Axis)):P0} similar to wrist bending (limit {CalibratedArmMotion.AxisSimilarityMaximum:P0}). Keep wrist straight and rotate the palm about your forearm. Repeat the two rotation steps using Back if needed.");
                    if(left){Profile.LeftRollAxis=learned.Axis;Profile.LeftRoll=learned.Range;}else{Profile.RightRollAxis=learned.Axis;Profile.RightRoll=learned.Range;}
                }
            }
        }
        if(Step is 14 or 16)
        {
            bool left=Step==14;var shoulder=left?Profile.LeftShoulder:Profile.RightShoulder;float neutral=left?Profile.LeftTwistNeutral:Profile.RightTwistNeutral;
            float Angle(BodyTrackingFrame frame)
            {
                var hand=Hand(frame,left);var initial=Hand(captured[4],left);
                if(Math.Abs(hand.Y-initial.Y)>.08f||new Vector2(hand.X-shoulder.X,hand.Z-shoulder.Z).Length()<.1f)
                    throw new InvalidDataException("Keep elbow beside your ribs and forearm level. Sweep the forearm inward/outward without lifting the upper arm.");
                return BodyTrackingFrame.Wrap(CalibratedArmMotion.ArmAzimuth(shoulder,hand,left)-neutral);
            }
            float inward=Angle(captured[Step-1]),outward=Angle(f);
            if(inward>-.15f||outward<.15f)throw new InvalidDataException("Twist inward then outward past neutral, at least 10 degrees each way. Use Back to repeat the inward pose if needed.");
            if(left)Profile.LeftTwist=[inward,outward];else Profile.RightTwist=[inward,outward];
        }
        if(Step==HandsOpenStep){Profile.LeftOpen=f.LeftFingers;Profile.RightOpen=f.RightFingers;}
        if(Step==HandsClosedStep)
        {
            Profile.LeftClosed=f.LeftFingers;Profile.RightClosed=f.RightFingers;Profile.FingerSource=f.FingerSource;
            if(!f.FingerSource.Contains("skeletal",StringComparison.OrdinalIgnoreCase))
            {Profile.LeftOpen=new float[5];Profile.LeftClosed=new float[5];Profile.RightOpen=new float[5];Profile.RightClosed=new float[5];}
            if(f.FingerSource.Contains("skeletal",StringComparison.OrdinalIgnoreCase)&&
                (Enumerable.Range(0,3).Average(i=>Math.Abs(Profile.LeftClosed[i]-Profile.LeftOpen[i]))<.12||Enumerable.Range(0,3).Average(i=>Math.Abs(Profile.RightClosed[i]-Profile.RightOpen[i]))<.12))
                throw new InvalidDataException("Finger opening and closing were too similar. Repeat with fingers fully open, then closed naturally.");
        }
        if(Step==CrouchStep){Profile.CrouchDrop=Profile.StandingHeight-f.Head.Translation.Y;if(Profile.CrouchDrop<.08f)throw new InvalidDataException("Crouch enough to lower the headset at least 8 cm.");}
        if(Step==BowStep)
        {
            Profile.BowDrop=Profile.StandingHeight-f.Head.Translation.Y;
            Profile.BowForward=-Vector3.Transform(f.Head.Translation-captured[0].Head.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitY,-yaw)).Z;
            if(Profile.BowDrop<.04f||Profile.BowForward<.04f)throw new InvalidDataException("Bend forward enough to measure head lowering and forward travel (at least 4 cm).");
        }
        if(Step==BowStep&&!Profile.Valid())throw new InvalidDataException("Calibration validation failed. Repeat the calibration before following.");
    }
}
