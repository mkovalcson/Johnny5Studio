using System.Numerics;
namespace ServoAnimator;

public sealed record TrackedArm(Vector3 Shoulder,Vector3 Elbow,Vector3 Hand,float ElbowDegrees,float WristPitchDegrees,float WristRollDegrees,bool Pointing=false);
public sealed record BodyTrackingResult(Dictionary<string,float> Targets,TrackedArm Left,TrackedArm Right,float Crouch,float Lean,float YawDegrees,float Travel,string Confidence);
public sealed class BodyTrackingSolver
{
    private readonly BodyTrackingCalibration profile;
    private Vector3 lastHead,standingAnchor;
    private float referenceYaw,bodyYaw,rootHeading,rootX,rootY,initialHeight,lastLean;
    // Point gesture (per hand, in your hand's own terms; mirroring is applied afterwards).
    // Starts when your index is nearly straight while your middle finger and thumb are
    // nearly closed, then holds Johnny's upper finger straight and the rest closed until
    // you bend your index past PointReleaseCurl. Curls are 0 open .. 1 closed, calibrated.
    public const float PointStartIndexCurl=.3f,PointStartClosedCurl=.6f,PointReleaseCurl=.6f;
    // Current full-body URDF: align upper-arm -> forearm -> hand axes.
    // Joint zero is the CAD rest pose, not mechanical straightness.
    public const float PointElbowRadians=-.75903717f,PointWristPitchRadians=-.7628464f;
    private bool leftPointing,rightPointing;
    public bool MirrorFacing {get;set;}
    public BodyTrackingSolver(BodyTrackingCalibration profile){if(!profile.Valid())throw new ArgumentException("Invalid body calibration.");this.profile=profile;}
    public void Recenter(BodyTrackingFrame sample,float x,float y,float heading)
    {
        lastHead=standingAnchor=sample.Head.Translation;referenceYaw=bodyYaw=BodyTrackingFrame.Yaw(sample.Head);initialHeight=sample.Head.Translation.Y;
        rootX=x;rootY=y;rootHeading=heading;lastLean=0;leftPointing=rightPointing=false;
    }
    // Updates one hand's point state from its calibrated curls; NaN means not measured.
    public void Drive(float forward,float turn,float seconds)
    {
        float dt=Math.Clamp(seconds,0,.05f);
        // URDF positive Z yaw turns left; stick-right must turn the robot right.
        rootHeading-=turn*90*dt;
        float heading=rootHeading*MathF.PI/180;
        rootX+=forward*80*dt*MathF.Cos(heading);rootY+=forward*80*dt*MathF.Sin(heading);
    }
    public static bool UpdatePointing(bool pointing,float thumb,float index,float middle)
    {
        if(!float.IsFinite(index))return false;
        if(pointing)return index<PointReleaseCurl;
        return index<=PointStartIndexCurl&&float.IsFinite(middle)&&middle>=PointStartClosedCurl&&(!float.IsFinite(thumb)||thumb>=PointStartClosedCurl);
    }
    // Near full reach the bend estimate is very sensitive: a hand 2 cm short of a
    // 62 cm reach already reads as about 28 degrees. Those last few centimetres are
    // mostly tracking and shoulder-estimate error, so treat the hand as fully
    // reaching at StraightReach of the arm length, blending in from StraightBlendStart
    // (below that, and at a right angle, the estimate is unchanged).
    public const float StraightReach=.97f,StraightBlendStart=.8f;
    // Fraction of the calibrated crouch depth used to lower the neck before the body bends.
    public const float NeckCrouchShare=.35f;
    public static Vector3 ElbowTarget(Vector3 shoulder,Vector3 hand,float reach)
    {
        var offset=hand-shoulder;float distance=offset.Length();
        if(reach<=0||distance<1e-5f)return hand;
        float ratio=distance/reach;if(ratio<=StraightBlendStart)return hand;
        float adjusted=Math.Min(1,StraightBlendStart+(ratio-StraightBlendStart)*(1-StraightBlendStart)/(StraightReach-StraightBlendStart));
        return shoulder+offset*(adjusted/ratio);
    }
    public static (Vector3 Elbow,float Bend) SolveElbow(Vector3 shoulder,Vector3 hand,float upper,float forearm,Vector3 hint)
    {
        var offset=hand-shoulder;float distance=Math.Clamp(offset.Length(),Math.Max(.001f,Math.Abs(upper-forearm)+.0001f),upper+forearm-.0001f);
        var direction=offset.LengthSquared()<1e-8f?-Vector3.UnitY:Vector3.Normalize(offset);
        var perpendicular=hint-direction*Vector3.Dot(hint,direction);if(perpendicular.LengthSquared()<1e-5f)perpendicular=Vector3.Cross(direction,Vector3.UnitX);
        if(perpendicular.LengthSquared()<1e-5f)perpendicular=Vector3.UnitZ;
        perpendicular=Vector3.Normalize(perpendicular);
        float along=(upper*upper-forearm*forearm+distance*distance)/(2*distance);along=Math.Clamp(along,-upper,upper);
        var elbow=shoulder+direction*along+perpendicular*MathF.Sqrt(Math.Max(0,upper*upper-along*along));
        float bend=MathF.PI-MathF.Acos(Math.Clamp((upper*upper+forearm*forearm-distance*distance)/(2*upper*forearm),-1,1));
        // The 0.1 mm clamp above otherwise leaves a fully reached arm reading about 2 degrees bent.
        if(offset.Length()>=upper+forearm-.0001f){elbow=shoulder+direction*upper;bend=0;}
        return(elbow,bend);
    }
    public static double FollowingSpeedMultiplier(ServoNames servo)=>FollowSpeedCatalog.Value(null,servo.ToString());
    public static BodyTrackingResult MapPresentation(BodyTrackingResult source,bool mirror)
    {
        var targets=new Dictionary<string,float>();
        foreach(var item in source.Targets)
        {
            string key=item.Key;float value=item.Value;
            if(mirror&&key.StartsWith("B_",StringComparison.Ordinal))
            {
                if(key.EndsWith("Left",StringComparison.Ordinal))key=key[..^4]+"Right";
                else if(key.EndsWith("Right",StringComparison.Ordinal))key=key[..^5]+"Left";
            }
            if(!mirror&&(key is "NeckTurn" or "NeckTiltRight"))value=-value;
            targets[key]=value;
        }
        return source with{Targets=targets};
    }
    public static Quaternion ForearmRotation(Vector3 shoulder,Vector3 elbow,Vector3 hand)
    {
        var direction=hand-elbow;if(direction.LengthSquared()<1e-8f)direction=-Vector3.UnitY;direction=Vector3.Normalize(direction);
        var normal=Vector3.Cross(elbow-shoulder,direction);
        if(normal.LengthSquared()<1e-8f)normal=Vector3.Cross(Vector3.UnitY,direction);
        if(normal.LengthSquared()<1e-8f)normal=Vector3.UnitX;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero,-direction,Vector3.Normalize(normal))));
    }
    private static float Fraction(float value,float[] range)=>Math.Clamp(value/(value<0?-range[0]:range[1]),-1,1);
    public BodyTrackingResult Solve(BodyTrackingFrame sample,float seconds,FollowInputFilter input=null)
    {
        if(!sample.TrackingValid)throw new InvalidOperationException("Tracking lost.");
        float dt=Math.Clamp(seconds,0,.05f),headYaw=BodyTrackingFrame.Yaw(sample.Head);
        var handLine=sample.Right.Translation-sample.Left.Translation;
        float handYaw=MathF.Atan2(-handLine.Z,handLine.X);
        float handTurn=BodyTrackingFrame.Wrap(handYaw-bodyYaw),headTurn=BodyTrackingFrame.Wrap(headYaw-bodyYaw);
        bool turnAgreement=handLine.Length()>.20f&&Math.Abs(BodyTrackingFrame.Wrap(handYaw-headYaw))<profile.TurnAgreementRadians
            &&(Math.Abs(handTurn)<.025f||Math.Sign(handTurn)==Math.Sign(headTurn));
        float previousYaw=bodyYaw;
        if(turnAgreement&&Math.Abs(handTurn)>.025f&&Math.Abs(headTurn)>.025f)bodyYaw+=handTurn*Math.Clamp(dt*4,0,1);
        var headDelta=sample.Head.Translation-lastHead;
        float drop=Math.Max(0,initialHeight-sample.Head.Translation.Y);
        float headPitch=MathF.Asin(Math.Clamp(Vector3.TransformNormal(-Vector3.UnitZ,sample.Head).Y,-1,1));
        if(drop<.03f)standingAnchor=sample.Head.Translation;
        float forwardLean=-Vector3.Transform(sample.Head.Translation-standingAnchor,Quaternion.CreateFromAxisAngle(Vector3.UnitY,-bodyYaw)).Z;
        float lean=Math.Max(Math.Clamp(-headPitch/.8f,0,1),Math.Clamp(forwardLean/Math.Max(.04f,profile.BowForward),0,1));
        // Standing-head lowering alone cannot separate a squat from a bow. Pitch
        // contributes evidence; expose the estimate rather than claiming torso tracking.
        float crouch=Math.Clamp(drop/Math.Max(.08f,profile.CrouchDrop),0,1);
        float bow=Math.Clamp(drop/Math.Max(.04f,profile.BowDrop),0,1)*lean;
        // Crouching and forward bending lower the neck first, then bend the torso. The neck
        // ignores the first 3 cm (normal standing sway) and is fully down at
        // NeckCrouchShare of the calibrated crouch; the body bends over the rest.
        float standing=Math.Min(.5f*NeckCrouchShare,.03f/Math.Max(.08f,profile.CrouchDrop));
        // Shape the shared input before splitting neck and torso movement.
        float lowerCrouch=input?.Shape("crouch",crouch,seconds,"B_NeckLift")??crouch;
        float lowerBow=input?.Shape("bow",bow,seconds,"B_NeckLift")??bow;
        float neckLower=Math.Clamp((Math.Max(lowerCrouch,lowerBow)-standing)/(NeckCrouchShare-standing),0,1);
        float bodyCrouch=Math.Clamp((lowerCrouch-NeckCrouchShare)/(1-NeckCrouchShare),0,1);
        float bodyBow=Math.Clamp((lowerBow-NeckCrouchShare)/(1-NeckCrouchShare),0,1);
        float bend=Math.Clamp(bodyCrouch*.45f+bodyBow*.55f,0,1)*.65f;
        var targets=new Dictionary<string,float>();
        targets[ServoNames.NeckTurn.ToString()]=Math.Clamp(BodyTrackingFrame.Wrap(headYaw-bodyYaw)/(MathF.PI/3)*100,-100,100);
        targets[ServoNames.NeckNodUp.ToString()]=Math.Clamp(headPitch/(MathF.PI/6)*100,-100,100);
        var headUp=Vector3.TransformNormal(Vector3.UnitY,sample.Head);
        targets[ServoNames.NeckTiltRight.ToString()]=Math.Clamp(MathF.Atan2(headUp.X,headUp.Y)/(MathF.PI/9)*100,-100,100);
        void Set(ServoNames servo,float radians)=>targets[servo.ToString()]=radians;
        var waist=BodyControlCatalog.Find(ServoNames.B_WaistSwivel);
        Set(ServoNames.B_WaistSwivel,(float)Math.Clamp((MirrorFacing?-1:1)*BodyTrackingFrame.Wrap(bodyYaw-referenceYaw),waist.MinRadians,waist.MaxRadians));
        Set(ServoNames.B_ChestTilt,bend*.45f);Set(ServoNames.B_WaistLean,bend*.55f);
        Set(ServoNames.B_NeckLift,neckLower*(float)BodyControlCatalog.Find(ServoNames.B_NeckLift).MinRadians);
        Quaternion bodyRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,bodyYaw);
        Vector3 Local(Vector3 position)=>BodyTrackingFrame.BodyLocal(position,sample.Head,bodyYaw);
        Quaternion Rotation(Matrix4x4 pose)=>Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(pose));
        TrackedArm Arm(bool left)
        {
            string side=left?"Left":"Right";var handPose=left?sample.Left:sample.Right;
            var shoulder=left?profile.LeftShoulder:profile.RightShoulder;var hand=Local(profile.Wrist(handPose,left));
            float upper=left?profile.LeftUpper:profile.RightUpper,forearm=left?profile.LeftForearm:profile.RightForearm;
            var elbow=SolveElbow(shoulder,ElbowTarget(shoulder,hand,upper+forearm),upper,forearm,new Vector3(left?-.25f:.25f,-1,.15f));
            var upperDirection=Vector3.Normalize(elbow.Elbow-shoulder);
            float horizontal=MathF.Sqrt(upperDirection.X*upperDirection.X+upperDirection.Z*upperDirection.Z);
            float elevation=MathF.Atan2(horizontal,-upperDirection.Y);
            if(upperDirection.Z>.1f&&Math.Abs(upperDirection.Z)>Math.Abs(upperDirection.X)&&elevation<MathF.PI*.75f)elevation=-elevation;
            float lateral=horizontal>.05f?MathF.Atan2((left?-1:1)*upperDirection.X,Math.Max(.001f,-upperDirection.Z)):0;
            var forearmRotation=ForearmRotation(shoulder,elbow.Elbow,hand);
            Quaternion handBody=Quaternion.Inverse(bodyRotation)*Rotation(handPose);
            var neutral=left?profile.LeftNeutral:profile.RightNeutral;
            var wrist=Quaternion.Normalize(Quaternion.Inverse(neutral)*Quaternion.Inverse(forearmRotation)*handBody);
            var measuredWrist=CalibratedArmMotion.Wrist(profile,left,wrist);float roll=measuredWrist.Roll,pitch=measuredWrist.Pitch;
            void Member(string name,float value)=>targets["B_"+name+side]=value;
            Member("ShoulderRotate",elevation+25*MathF.PI/180);
            Member("Elbow",elbow.Bend+PointElbowRadians);Member("ArmTwist",CalibratedArmMotion.ArmTwist(profile,left,hand,elbow.Bend));
            Member("WristPitch",Fraction(pitch,left?profile.LeftPitch:profile.RightPitch));
            Member("WristRoll",Fraction(roll,left?profile.LeftRoll:profile.RightRoll)*MathF.PI);
            var fingers=left?sample.LeftFingers:sample.RightFingers;
            var open=left?profile.LeftOpen:profile.RightOpen;var closed=left?profile.LeftClosed:profile.RightClosed;
            bool pointing=false;
            if(fingers?.Length==5&&open.Length==5&&closed.Length==5)
            {
                float Curl(int index)=>Math.Abs(closed[index]-open[index])>.1f?Math.Clamp((fingers[index]-open[index])/(closed[index]-open[index]),0,1):float.NaN;
                // Your index finger drives the robot's upper finger; your middle finger its lower one.
                float thumb=Curl(0),upperFinger=Curl(1),lowerFinger=Curl(2);
                pointing=UpdatePointing(left?leftPointing:rightPointing,thumb,upperFinger,lowerFinger);
                if(left)leftPointing=pointing;else rightPointing=pointing;
                if(pointing){thumb=1;lowerFinger=1;}
                if(float.IsFinite(thumb)){Member("ThumbCurl",-.3f+thumb*((float)HandClosingLimits.ThumbMaximum+.3f));Member("ThumbTipCurl",-.3f+thumb*((float)HandClosingLimits.ThumbTipMaximum+.3f));}
                void Finger(string finger,float value)
                {
                    if(!float.IsFinite(value))return;
                    targets["B_FingerSpread"+finger+side]=.6f-.8f*value;
                    targets["B_FingerCurl"+finger+side]=-.3f+value*((float)HandClosingLimits.BaseMaximum+.3f);
                    targets["B_FingerTipCurl"+finger+side]= -.3f+value*((float)HandClosingLimits.TipMaximum+.3f);
                    targets["B_FingerEndCurl"+finger+side]= -.3f+value*((float)HandClosingLimits.EndMaximum+.3f);
                }
                if(pointing)
                {
                    // Pointing aligns the hand only. Elbow and arm twist keep following
                    // the tracked arm, including the normal mechanical elbow offset.
                    Member("WristPitch",PointWristPitchRadians);Member("WristRoll",0);
                    // Pointing finger: spread axle centred and every joint straight.
                    targets["B_FingerSpreadUpper"+side]=0;targets["B_FingerCurlUpper"+side]=0;
                    targets["B_FingerTipCurlUpper"+side]=0;targets["B_FingerEndCurlUpper"+side]=0;
                }
                else Finger("Upper",upperFinger);
                Finger("Lower",lowerFinger);
            }
            else if(left)leftPointing=false;else rightPointing=false;
            return new(shoulder,elbow.Elbow,hand,elbow.Bend*180/MathF.PI,pitch*180/MathF.PI,roll*180/MathF.PI,pointing);
        }
        var l=Arm(true);var r=Arm(false);
        float yawDelta=BodyTrackingFrame.Wrap(bodyYaw-previousYaw);
        // Root movement is exclusively joystick-driven via Drive; tracked yaw only estimates arm/head alignment.
        var localDelta=Vector3.Transform(headDelta,Quaternion.Inverse(bodyRotation));
        float forward=-localDelta.Z-(bow-lastLean)*profile.BowForward;
        if(Math.Abs(forward)<.0008f)forward=0;
        forward=Math.Clamp(forward,-.8f*dt,.8f*dt);
        targets[ServoNames.URDFStageX.ToString()]=rootX;targets[ServoNames.URDFStageY.ToString()]=rootY;targets[ServoNames.URDFStageHeading.ToString()]=rootHeading;
        lastHead=sample.Head.Translation;lastLean=bow;
        return new(targets,l,r,crouch,bow,BodyTrackingFrame.Wrap(bodyYaw-referenceYaw)*180/MathF.PI,forward,
            turnAgreement?"Hands tracked · elbows/body estimated":"Body yaw held: head/hand turn disagree · elbows estimated");
    }
}
