using System.Numerics;
using ServoAnimator;
namespace Johnny5VR;
internal static class BodyMotionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var handControls=Enum.GetValues<ServoNames>().Where(s=>s.ToString().StartsWith("B_",StringComparison.Ordinal)&&(s.ToString().Contains("Wrist")||s.ToString().Contains("Finger")||s.ToString().Contains("Thumb"))&&(s.ToString().EndsWith("Left")||s.ToString().EndsWith("Right"))).ToArray();
        check(handControls.Length==24&&handControls.All(s=>BodyTrackingSolver.FollowingSpeedMultiplier(s)==2),"Both wrists, thumbs and all finger joints have twice their previous Follow speed limit.");
        check(BodyTrackingSolver.FollowingSpeedMultiplier(ServoNames.B_ShoulderRotateLeft)==3&&BodyTrackingSolver.FollowingSpeedMultiplier(ServoNames.B_ElbowRight)==3&&BodyTrackingSolver.FollowingSpeedMultiplier(ServoNames.B_WaistLean)==1,"Faster hands preserve the shoulder/elbow boost and the remaining body speed limits.");
        Matrix4x4 Pose(Vector3 p,Quaternion? q=null)=>Matrix4x4.CreateFromQuaternion(q??Quaternion.Identity)*Matrix4x4.CreateTranslation(p);
        var panelHead=Matrix4x4.CreateTranslation(0,1.7f,0);var panelWorld=VrBodyMotion.PanelWorld(panelHead,true);
        check(Math.Abs(panelWorld.Translation.Z+1.65f)<.001f&&Math.Abs(panelWorld.Translation.Y-(1.7f-.05f-(1.25f*940/1400)/3))<.001f,"Calibration display sits farther away and one-third of its physical height lower.");
        var head=new Vector3(0,1.7f,0);var leftShoulder=new Vector3(-.2f,1.4f,0);var rightShoulder=new Vector3(.2f,1.4f,0);
        var leftDown=leftShoulder-Vector3.UnitY*.65f;var rightDown=rightShoulder-Vector3.UnitY*.65f;
        BodyTrackingFrame Frame(Vector3 l,Vector3 r,Vector3? h=null,Quaternion? hq=null,Quaternion? lq=null,Quaternion? rq=null,float curl=0)
            =>new(Pose(h??head,hq),Pose(l,lq),Pose(r,rq),Enumerable.Repeat(curl,5).ToArray(),Enumerable.Repeat(curl,5).ToArray(),true,"SteamVR skeletal finger curls");
        var down=Frame(leftDown,rightDown);
        var up=Frame(leftShoulder+Vector3.UnitY*.65f,rightShoulder+Vector3.UnitY*.65f);
        var forward=Frame(leftShoulder-Vector3.UnitZ*.65f,rightShoulder-Vector3.UnitZ*.65f);
        var bent=Frame(leftShoulder-Vector3.UnitZ*.3f+Vector3.UnitY*.35f,rightShoulder-Vector3.UnitZ*.3f+Vector3.UnitY*.35f);
        var fit=BodyTrackingCalibration.FitShoulder(leftDown-head,up.Left.Translation-head,forward.Left.Translation-head);
        check(Vector3.Distance(fit.Center,leftShoulder-head)<.0001f&&Math.Abs(fit.Radius-.65f)<.0001f,"Three straight-arm poses recover a known shoulder center and reach.");
        var split=BodyTrackingCalibration.SplitArm(.65f,MathF.Sqrt(.3f*.3f+.35f*.35f));
        check(Math.Abs(split.Upper-.3f)<.001f&&Math.Abs(split.Forearm-.35f)<.001f,"A known 90-degree pose recovers estimated segment lengths.");
        bool rejected=false;try{BodyTrackingCalibration.FitShoulder(Vector3.Zero,Vector3.UnitY,Vector3.UnitY*2);}catch(InvalidDataException){rejected=true;}
        check(rejected,"Degenerate shoulder calibration is rejected.");
        // A grip offset that rotates with the controller spoils the sphere fit by up to ~20 cm before the pivot is known.
        float skewedError=0;bool skewedAccepted=false;
        for(float k=.02f;k<=.4f&&skewedError<=.15f;k+=.01f)
        {
            try{var skewedFit=BodyTrackingCalibration.FitShoulder(leftDown-head+new Vector3(0,0,k),up.Left.Translation-head+new Vector3(0,0,k),forward.Left.Translation-head+new Vector3(0,k,0));skewedError=skewedFit.Error;skewedAccepted=true;}
            catch(InvalidDataException){skewedAccepted=false;break;}
        }
        check(skewedAccepted&&skewedError>.15f&&skewedError<=.22f,$"A shoulder fit skewed by a rotating grip offset ({skewedError*100:F0} cm error) is accepted up to 22 cm.");
        string badFitMessage="";try{BodyTrackingCalibration.FitShoulder(leftDown-head,leftDown-head+new Vector3(0,2.4f,0),leftDown-head+new Vector3(0,1.2f,1.2f));}catch(InvalidDataException ex){badFitMessage=ex.Message;}bool badFit=badFitMessage.Contains("fit error")&&badFitMessage.Contains("limit 22");
        check(badFit,"An inconsistent shoulder fit reports its reach, error and limit in centimetres: "+badFitMessage);
        var wizard=new BodyCalibrationWizard();
        BodyTrackingFrame Turn(float yaw)
        {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            return Frame(Vector3.Transform(leftDown-head,q)+head,Vector3.Transform(rightDown-head,q)+head,hq:q,lq:q,rq:q);
        }
        BodyTrackingFrame[] stages=[down,up,forward,bent,bent,
            bent with{Left=Pose(bent.Left.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.7f)),Right=Pose(bent.Right.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,-.7f))},
            bent with{Left=Pose(bent.Left.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,-.7f)),Right=Pose(bent.Right.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.7f))},
            bent with{Left=Pose(bent.Left.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitX,.5f)),Right=Pose(bent.Right.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitX,.5f))},
            bent with{Left=Pose(bent.Left.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitX,-.5f)),Right=Pose(bent.Right.Translation,Quaternion.CreateFromAxisAngle(Vector3.UnitX,-.5f))},
            down,down with{LeftFingers=[1,1,1,1,1],RightFingers=[1,1,1,1,1]},
            Frame(leftDown-Vector3.UnitY*.4f,rightDown-Vector3.UnitY*.4f,head-Vector3.UnitY*.4f),
            Frame(leftDown-Vector3.UnitZ*.3f-Vector3.UnitY*.25f,rightDown-Vector3.UnitZ*.3f-Vector3.UnitY*.25f,head-Vector3.UnitZ*.3f-Vector3.UnitY*.25f,Quaternion.CreateFromAxisAngle(Vector3.UnitX,-.5f)),
            Turn(.785f),Turn(-.785f),Frame(leftDown-Vector3.UnitZ*.3f,rightDown-Vector3.UnitZ*.3f,head-Vector3.UnitZ*.3f)];
        var armNeutral=Frame(leftShoulder-Vector3.UnitY*.3f-Vector3.UnitZ*.35f,rightShoulder-Vector3.UnitY*.3f-Vector3.UnitZ*.35f);
        var lp=Vector3.Normalize(new Vector3(1,1,.2f));var lr=Vector3.Normalize(new Vector3(.1f,0,1));
        var rp=Vector3.Normalize(new Vector3(-.3f,1,.1f));var rr=Vector3.Normalize(new Vector3(.2f,.1f,1));
        BodyTrackingFrame WristPose(bool left,Vector3 axis,float angle)
        {var source=left?armNeutral.Left:armNeutral.Right;var rotated=Pose(source.Translation,Quaternion.CreateFromAxisAngle(axis,angle));return left?armNeutral with{Left=rotated}:armNeutral with{Right=rotated};}
        BodyTrackingFrame TwistPose(bool left,float outward)
        {
            var shoulder=left?leftShoulder:rightShoulder;var pose=left?armNeutral.Left:armNeutral.Right;
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(left?1:-1)*outward);
            var moved=Pose(shoulder+Vector3.Transform(pose.Translation-shoulder,rotation),rotation);
            return left?armNeutral with{Left=moved}:armNeutral with{Right=moved};
        }
        BodyTrackingFrame[] calibrationStages=[down,up,forward,bent,armNeutral,
            WristPose(true,lp,.5f),WristPose(true,lp,-.5f),WristPose(true,lr,.7f),WristPose(true,lr,-.7f),
            WristPose(false,rp,.5f),WristPose(false,rp,-.5f),WristPose(false,rr,.7f),WristPose(false,rr,-.7f),
            TwistPose(true,-.6f),TwistPose(true,.6f),TwistPose(false,-.6f),TwistPose(false,.6f),stages[9],stages[10],stages[11],stages[12]];
        for(int step=0;step<BodyCalibrationWizard.Steps.Length;step++)
        {
            var alignment=wizard.AssessAlignment(calibrationStages[step]);
            check(alignment.Ready&&alignment.Checks.Length>=2,"Step "+step+" exposes ready alignment requirements before sampling: "+alignment.Blocker);
            var untracked=wizard.AssessAlignment(calibrationStages[step] with{Valid=false});
            check(!untracked.Ready&&!untracked.PartReady("Left")&&!untracked.PartReady("Right"),"Step "+step+" clearly blocks and colors tracking loss.");
            if(step==5)
            {
                var misplaced=calibrationStages[step];var badHand=misplaced.Left;badHand.Translation+=Vector3.UnitX*.3f;
                var blocked=wizard.AssessAlignment(misplaced with{Left=badHand});
                check(!blocked.Ready&&!blocked.PartReady("Left")&&blocked.PartReady("Right")&&blocked.Blocker.Contains("cm"),"Wrist alignment names drift in centimetres and identifies only the misplaced side.");
                var wristGripFrame=calibrationStages[step];var grip=wristGripFrame.Left;grip.Translation+=Vector3.UnitY*.1f;
                check(wizard.AssessAlignment(wristGripFrame with{Left=grip}).Ready,"Wrist bending allows plausible controller grip travel instead of rejecting every movement over 8 cm.");
                // A 17 cm grip lever: a 0.5 rad (29°) bend legitimately moves the controller origin about 8 cm, plus 8 cm of slack.
                var farGrip=wristGripFrame.Left;farGrip.Translation+=Vector3.UnitY*.13f;
                check(wizard.AssessAlignment(wristGripFrame with{Left=farGrip}).Ready,"A 29° wrist bend allows 13 cm of controller travel (measured grips are 13–17 cm from the origin).");
                var tooFar=wristGripFrame.Left;tooFar.Translation+=Vector3.UnitY*.2f;
                check(!wizard.AssessAlignment(wristGripFrame with{Left=tooFar}).Ready,"20 cm of controller travel during a 29° bend is still a moved forearm.");
                var neutralWrist=wizard.AssessAlignment(armNeutral);
                check(!neutralWrist.Ready&&neutralWrist.Blocker.Contains("10°"),"A wrist that has not bent yet reports the minimum movement before accepting an endpoint.");
                wizard.Update(misplaced with{Left=badHand},.02f,true);
                for(int i=0;i<50;i++)wizard.Update(misplaced with{Left=badHand},.02f,false);
                check(wizard.Step==step&&wizard.Collecting&&wizard.Progress==0&&wizard.LastRejection.Contains("cm"),"Misaligned armed capture stays on its step, explains why and waits for correction without requiring another trigger pull.");
                wizard.Observe(misplaced with{Left=badHand});
                using(var preview=VrBodyMotion.DrawPanel(wizard,wizard.Profile,misplaced with{Left=badHand},null,"Alignment help"))preview.Save(Path.Combine(Diagnostics.Folder,"calibration-alignment-adjust.png"));
            }
            if(step==6)
            {
                var sameSide=wizard.AssessAlignment(calibrationStages[5]);
                check(!sameSide.Ready&&sameSide.Checks.Any(c=>c.Label=="Opposite endpoint"&&!c.Good),"Second wrist endpoint explains wrong-side movement before capture.");
            }
            if(step==17)
            {
                var missing=wizard.AssessAlignment(calibrationStages[step] with{FingerSource="Unavailable",LeftFingers=null,RightFingers=null});
                check(missing.Ready&&missing.Checks.Any(c=>!c.Required&&!c.Good),"Missing finger sensors are an explicit optional warning rather than a silent calibration failure.");
            }
            wizard.Update(calibrationStages[step],.02f,true);
            if(BodyCalibrationWizard.IsFingerStep(step))
            {
                for(int held=0;held<50;held++)wizard.Update(calibrationStages[step],.02f,true);
                check(wizard.Step==step&&wizard.CountdownRemaining==3,"Finger countdown waits for trigger release before consuming its preparation time.");
            }
            for(int i=0;i<(BodyCalibrationWizard.IsFingerStep(step)?230:110);i++)
            {
                wizard.Update(calibrationStages[step],.02f,false);
                if((BodyCalibrationWizard.IsFingerStep(step))&&i==60)check(wizard.Step==step&&wizard.CountdownRemaining>1.5f,"Finger measurement waits through a three-second preparation countdown.");
            }
            check(wizard.Step==step+1,"Guided calibration accepts stable valid stage "+step+": "+wizard.Feedback);
            if(wizard.Step is 5 or 7 or 13)
            {
                string kind=BodyCalibrationWizard.Motion(wizard.Step);
                using var preview=VrBodyMotion.DrawPanel(wizard,wizard.Profile,calibrationStages[wizard.Step],null,"Measuring "+kind);
                preview.Save(Path.Combine(Diagnostics.Folder,"calibration-"+kind.ToLowerInvariant()+"-panel.png"));
            }
        }
        check(wizard.Complete&&wizard.Profile.Valid(),"The full guided sequence produces a validated profile.");
        string path=Path.Combine(Diagnostics.Folder,"body-calibration-check","Profile.json");wizard.Profile.Save(path);
        var profile=BodyTrackingCalibration.Load(path);
        check(Vector3.Distance(profile.LeftShoulder,wizard.Profile.LeftShoulder)<.00001f&&profile.LeftPitch.SequenceEqual(wizard.Profile.LeftPitch),"Calibration round-trip preserves vector, orientation and wrist range data.");
        var legacyPath=Path.Combine(Diagnostics.Folder,"body-calibration-check","LegacyProfile.json");
        File.WriteAllText(legacyPath,File.ReadAllText(path).Replace(BodyTrackingCalibration.CurrentFingerMapping,BodyTrackingCalibration.PreviousFingerMapping));
        var legacy=BodyTrackingCalibration.Load(legacyPath);
        check(legacy.FingerMapping==BodyTrackingCalibration.CurrentFingerMapping&&legacy.LeftOpen.SequenceEqual(wizard.Profile.LeftOpen)&&legacy.RightClosed.SequenceEqual(wizard.Profile.RightClosed),"Existing saved calibrations load with the new finger mapping while retaining measured input ranges.");
        var elbow=BodyTrackingSolver.SolveElbow(leftShoulder,bent.Left.Translation,.3f,.35f,-Vector3.UnitY);
        check(Math.Abs(elbow.Bend-MathF.PI/2)<.001f&&Math.Abs(Vector3.Distance(leftShoulder,elbow.Elbow)-.3f)<.001f,"Two-bone IK preserves upper-arm length and a right-angle bend.");
        foreach(bool left in new[]{true,false})
        {
            var pitchAxis=left?profile.LeftPitchAxis:profile.RightPitchAxis;var rollAxis=left?profile.LeftRollAxis:profile.RightRollAxis;
            var pureRoll=CalibratedArmMotion.Wrist(profile,left,Quaternion.CreateFromAxisAngle(rollAxis,.5f));
            var purePitch=CalibratedArmMotion.Wrist(profile,left,Quaternion.CreateFromAxisAngle(pitchAxis,.3f));
            check(Math.Abs(pureRoll.Pitch)<.001f&&Math.Abs(pureRoll.Roll-.5f)<.001f,"Calibrated axial rotation does not bend the "+(left?"left":"right")+" wrist, including nonstandard controller grip axes.");
            check(Math.Abs(purePitch.Roll)<.001f&&Math.Abs(purePitch.Pitch-.3f)<.001f,"Calibrated bending does not roll the "+(left?"left":"right")+" wrist.");
            var pose=TwistPose(left,.4f);var local=wizard.Local((left?pose.Left:pose.Right).Translation,pose.Head);
            check(CalibratedArmMotion.ArmTwist(profile,left,local,1)>0,"Bent-arm outward sweep drives independently calibrated "+(left?"left":"right")+" arm twist.");
            check(CalibratedArmMotion.ArmTwist(profile,left,local,0)==0,"Straight-arm ambiguity does not invent upper-arm twist.");
        }
        bool similarRejected=false;try{CalibratedArmMotion.Learn(Quaternion.CreateFromAxisAngle(lp,.5f),Quaternion.CreateFromAxisAngle(lp,.6f));}catch(InvalidDataException){similarRejected=true;}
        check(similarRejected,"Calibration rejects two endpoints on the same side of neutral.");
        {
            // Relaxed wrist gates: endpoints need only be about 105° apart in direction, and a
            // roll axis may be up to 80% like the bend axis.
            var tilted110=Vector3.Normalize(Vector3.Transform(lp,Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(lp,Vector3.UnitZ)),70*MathF.PI/180)));
            var apart110=CalibratedArmMotion.Learn(Quaternion.CreateFromAxisAngle(lp,.5f),Quaternion.CreateFromAxisAngle(tilted110,-.5f));
            check(apart110.Range[0]<-.1f&&apart110.Range[1]>.1f,"Wrist endpoints whose directions are 110° apart are accepted as opposite.");
            var tilted100=Vector3.Normalize(Vector3.Transform(lp,Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(lp,Vector3.UnitZ)),100*MathF.PI/180)));
            bool apart80Rejected=false;try{CalibratedArmMotion.Learn(Quaternion.CreateFromAxisAngle(lp,.5f),Quaternion.CreateFromAxisAngle(tilted100,-.5f));}catch(InvalidDataException){apart80Rejected=true;}
            check(apart80Rejected,"Wrist endpoints only 80° apart in direction are still rejected.");
            var rollish=Vector3.Normalize(lp*.75f+Vector3.Normalize(Vector3.Cross(lp,Vector3.UnitY))*MathF.Sqrt(1-.75f*.75f));
            var tooAlike=Vector3.Normalize(lp*.85f+Vector3.Normalize(Vector3.Cross(lp,Vector3.UnitY))*MathF.Sqrt(1-.85f*.85f));
            check(CalibratedArmMotion.Valid(profile.DisplaySnapshot() is var p1&&(p1.LeftRollAxis=rollish)==rollish?p1:p1)&&!CalibratedArmMotion.Valid(profile.DisplaySnapshot() is var p2&&(p2.LeftRollAxis=tooAlike)==tooAlike?p2:p2),"A roll axis 75% like the bend axis is accepted; 85% is rejected.");
            // Live gauges behind the gates.
            var gaugeWizard=new BodyCalibrationWizard();
            for(int step=0;step<7;step++){gaugeWizard.Update(calibrationStages[step],.02f,true);for(int i=0;i<110;i++)gaugeWizard.Update(calibrationStages[step],.02f,false);}
            check(gaugeWizard.Step==7,"Gauge probe reaches the first left roll endpoint.");
            gaugeWizard.Observe(calibrationStages[7]);var g1=gaugeWizard.LiveGauge;
            check(g1!=null&&g1.Motion=="Roll"&&Math.Abs(g1.Angle-40.1f)<1.5f&&g1.AngleOk&&float.IsNaN(g1.Opposite)&&!float.IsNaN(g1.Similarity)&&g1.SimilarityOk,"The first roll endpoint gauge shows the angle moved and how roll-like it is: "+g1?.Summary());
            gaugeWizard.Observe(armNeutral);var g0=gaugeWizard.LiveGauge;
            check(g0!=null&&g0.Angle<1&&!g0.AngleOk,"A straight wrist shows the gauge under its minimum.");
            gaugeWizard.Update(calibrationStages[7],.02f,true);for(int i=0;i<110;i++)gaugeWizard.Update(calibrationStages[7],.02f,false);
            gaugeWizard.Observe(calibrationStages[8]);var g2=gaugeWizard.LiveGauge;
            check(g2!=null&&g2.Opposite>170&&g2.OppositeOk&&g2.Similarity<.4f,"The second roll endpoint gauge shows the two endpoints opposite and the roll axis unlike bending: "+g2?.Summary());
            gaugeWizard.Observe(calibrationStages[7]);var g3=gaugeWizard.LiveGauge;
            check(g3!=null&&g3.Opposite<1&&!g3.OppositeOk,"Returning to the first endpoint shows the opposite gauge at zero.");
            check(gaugeWizard.DisplaySnapshot().LiveGauge==g3,"The display snapshot carries the live gauge.");
            // The guide animates and draws gauges.
            using var a=new Bitmap(310,235);using var b=new Bitmap(310,235);using var c=new Bitmap(310,118);
            using(var ga=Graphics.FromImage(a))CalibrationPoseGuide.Draw(ga,new(0,0,310,235),7,0,0f,true);
            using(var gb=Graphics.FromImage(b))CalibrationPoseGuide.Draw(gb,new(0,0,310,235),7,0,1.2f,true);
            using(var gc=Graphics.FromImage(c))CalibrationPoseGuide.DrawGauges(gc,new(0,0,310,118),g2);
            bool differs=false;for(int y=0;y<235&&!differs;y+=3)for(int x=0;x<310;x+=3)if(a.GetPixel(x,y)!=b.GetPixel(x,y)){differs=true;break;}
            check(differs,"The pose guide animates the required motion over time.");
            using(var strip=new Bitmap(1300,400))using(var sg=Graphics.FromImage(strip))
            {
                sg.Clear(Color.FromArgb(17,25,37));
                for(int i=0;i<4;i++)CalibrationPoseGuide.Draw(sg,new(10+i*320,10,310,235),i<2?7:14,0,i%2==0?0f:1.2f,true);
                CalibrationPoseGuide.DrawGauges(sg,new(10,260,310,118),g1);CalibrationPoseGuide.DrawGauges(sg,new(330,260,310,118),g2);CalibrationPoseGuide.DrawGauges(sg,new(650,260,310,118),g0);
                strip.Save(Path.Combine(Diagnostics.Folder,"calibration-guide-animation.png"));
            }
        }
        var oldProfile=new BodyTrackingCalibration{ArmCalibrationVersion=0};var oldAngles=CalibratedArmMotion.Wrist(oldProfile,true,Quaternion.CreateFromAxisAngle(Vector3.UnitX,.4f));
        check(Math.Abs(oldAngles.Pitch-.4f)<.001f,"Existing profiles retain their previous wrist interpretation until recalibrated.");
        var oldJson=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach(string name in new[]{"ArmCalibrationVersion","LeftPitchAxis","RightPitchAxis","LeftRollAxis","RightRollAxis","LeftTwistNeutral","RightTwistNeutral","LeftTwist","RightTwist"})oldJson.Remove(name);
        oldJson["Version"]=2;
        var oldSaved=System.Text.Json.JsonSerializer.Deserialize<BodyTrackingCalibration>(oldJson.ToJsonString(),BodyTrackingCalibration.Json)!;
        check(oldSaved.Valid()&&oldSaved.ArmCalibrationVersion==0,"Profiles saved before separate wrist-axis calibration remain readable without inventing measured axes.");
        // Wrist pivot: the controller's origin is in the handle, not at the wrist. The wrist
        // steps learn where the wrist is relative to the controller and the arm is measured
        // from there, so bending the wrist no longer moves the estimated arm.
        {
            var truePivot=new Vector3(.03f,-.04f,.08f);var centre=new Vector3(.2f,1.1f,-.3f);var fitPoses=new List<Matrix4x4>();
            foreach(var q in new[]{Quaternion.Identity,Quaternion.CreateFromAxisAngle(Vector3.UnitX,.5f),Quaternion.CreateFromAxisAngle(Vector3.UnitX,-.6f),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.7f),Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(.3f,1,.2f)),-.5f)})
            {var r=Matrix4x4.CreateFromQuaternion(q);fitPoses.Add(r*Matrix4x4.CreateTranslation(centre-Vector3.TransformNormal(truePivot,r)));}
            var pivotFit=BodyTrackingCalibration.FitWristPivot(fitPoses);
            check(Vector3.Distance(pivotFit.Pivot,truePivot)<.001f&&pivotFit.Residual<.001f,"Rotating a controller about a fixed wrist recovers the controller-local wrist pivot.");
            bool oneAxisRejected=false;try{BodyTrackingCalibration.FitWristPivot(fitPoses.Take(3).ToList());}catch(InvalidDataException){oneAxisRejected=true;}
            check(oneAxisRejected,"Wrist poses that only rotate about one axis cannot fix the pivot along that axis and are rejected.");
            // Walk the whole calibration with the controller origin displaced from the wrist.
            BodyTrackingFrame Grip(BodyTrackingFrame f)
            {
                Matrix4x4 Shift(Matrix4x4 m){m.Translation-=Vector3.TransformNormal(truePivot,m);return m;}
                return f with{Left=Shift(f.Left),Right=Shift(f.Right)};
            }
            var gripWizard=new BodyCalibrationWizard();
            for(int step=0;step<BodyCalibrationWizard.Steps.Length;step++)
            {
                var stage=Grip(calibrationStages[step]);gripWizard.Update(stage,.02f,true);
                for(int i=0;i<(BodyCalibrationWizard.IsFingerStep(step)?230:110);i++)gripWizard.Update(stage,.02f,false);
                check(gripWizard.Step==step+1,"Calibration with a displaced controller origin accepts stage "+step+": "+gripWizard.Feedback);
                if(step==8)check(gripWizard.Feedback.Contains("wrist pivot")&&gripWizard.Feedback.Contains("cm from grip"),"Finishing the left wrist steps reports the learned wrist pivot: "+gripWizard.Feedback);
            }
            var gp=gripWizard.Profile;
            check(gripWizard.Complete&&gp.Valid()&&Vector3.Distance(gp.LeftWristPivot,truePivot)<.005f&&Vector3.Distance(gp.RightWristPivot,truePivot)<.005f,"Both wrist pivots are learned from the wrist bend and roll endpoints.");
            check(Math.Abs(gp.LeftUpper+gp.LeftForearm-.65f)<.01f&&Math.Abs(gp.RightUpper+gp.RightForearm-.65f)<.01f&&Vector3.Distance(gp.LeftShoulder,profile.LeftShoulder)<.01f&&Vector3.Distance(gp.RightShoulder,profile.RightShoulder)<.01f,
                "Arm lengths and shoulder positions are re-measured to the wrist, matching a grip-at-wrist calibration.");
            var pivotSolver=new BodyTrackingSolver(gp);var gripNeutral=Grip(armNeutral);pivotSolver.Recenter(gripNeutral,0,0,0);
            var still=pivotSolver.Solve(gripNeutral,.02f);
            var bentWrist=Grip(WristPose(true,Vector3.UnitX,.5f));var moved=pivotSolver.Solve(bentWrist,.02f);
            check(Math.Abs(moved.Targets["B_ElbowLeft"]-still.Targets["B_ElbowLeft"])<.02f&&Math.Abs(moved.Targets["B_ShoulderRotateLeft"]-still.Targets["B_ShoulderRotateLeft"])<.02f,
                "Bending only the wrist leaves the estimated elbow and shoulder unchanged once the wrist pivot is known.");
            var originSolver=new BodyTrackingSolver(profile);originSolver.Recenter(gripNeutral,0,0,0);
            var originStill=originSolver.Solve(gripNeutral,.02f);var originMoved=originSolver.Solve(bentWrist,.02f);
            check(Math.Abs(originMoved.Targets["B_ElbowLeft"]-originStill.Targets["B_ElbowLeft"])>.05f,"Without a wrist pivot the same wrist bend reads as elbow movement (the problem being fixed).");
            string gripPath=Path.Combine(Diagnostics.Folder,"body-calibration-check","GripProfile.json");gp.Save(gripPath);
            var gripLoaded=BodyTrackingCalibration.Load(gripPath);
            check(gripLoaded.LeftWristPivot==gp.LeftWristPivot&&gripLoaded.RightWristPivotFit==gp.RightWristPivotFit,"Wrist pivots round-trip through the saved profile.");
            var noPivotJson=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(gripPath))!.AsObject();
            foreach(string name in new[]{"LeftWristPivot","RightWristPivot","LeftWristPivotFit","RightWristPivotFit"})noPivotJson.Remove(name);
            var noPivot=System.Text.Json.JsonSerializer.Deserialize<BodyTrackingCalibration>(noPivotJson.ToJsonString(),BodyTrackingCalibration.Json)!;
            check(noPivot.Valid()&&!noPivot.HasWristPivot(true)&&noPivot.Wrist(bentWrist.Left,true)==bentWrist.Left.Translation,"Profiles saved before wrist pivots load and keep using the controller origin.");
            // Tracking recorder round trip.
            string recordingFolder=Path.Combine(Diagnostics.Folder,"body-calibration-check","recording");
            if(Directory.Exists(recordingFolder))Directory.Delete(recordingFolder,true);
            string recordingPath;
            using(var recorder=new TrackingRecorder(recordingFolder))
            {
                recordingPath=recorder.Path;
                recorder.Record(gripNeutral,.0133f);recorder.Record(bentWrist with{Valid=false,LeftFingers=null},.0134f);recorder.Record(bentWrist,.0135f);
            }
            var played=TrackingRecorder.Read(recordingPath).ToArray();
            check(played.Length==3&&played[0].Frame.Left==gripNeutral.Left&&!played[1].Frame.Valid&&played[1].Frame.LeftFingers==null&&played[2].Frame.Right==bentWrist.Right&&played[2].Frame.FingerSource==bentWrist.FingerSource&&Math.Abs(played[2].Seconds-.0135f)<1e-6f,
                "Tracking recordings replay the same poses, finger data, validity and timing.");
            string replaySummary=TrackingRecorder.Replay(recordingPath,gripPath,Path.ChangeExtension(recordingPath,".csv"));
            var csv=File.ReadAllLines(Path.ChangeExtension(recordingPath,".csv"));
            check(csv.Length==4&&csv[0].StartsWith("seconds,valid,confidence,")&&csv[0].Contains("B_ElbowLeft")&&csv[2].StartsWith("0.0267,0,")&&replaySummary.Contains("1 dropouts"),"Replaying a recording writes one solver row per frame and counts dropouts: "+replaySummary);
        }
        var solver=new BodyTrackingSolver(profile);solver.Recenter(armNeutral,0,0,0);
        foreach(bool left in new[]{true,false})
        {
            string side=left?"Left":"Right";var rollPose=solver.Solve(WristPose(left,left?lr:rr,.5f),.02f);var pitchPose=solver.Solve(WristPose(left,left?lp:rp,.3f),.02f);
            check(Math.Abs(rollPose.Targets["B_WristPitch"+side])<.005f&&rollPose.Targets["B_WristRoll"+side]>1,"Real solver routes calibrated "+side+" rotation into roll without wrist bending.");
            check(Math.Abs(pitchPose.Targets["B_WristRoll"+side])<.005f&&pitchPose.Targets["B_WristPitch"+side]>.4f,"Real solver routes calibrated "+side+" bending into pitch without roll.");
        }
        solver.Recenter(down,0,0,0);
        var neutral=solver.Solve(down,.016f);
        check(neutral.Targets.Values.All(float.IsFinite),"Neutral body mapping produces finite targets.");
        check(neutral.Left.ElbowDegrees<1&&neutral.Right.ElbowDegrees<1,$"Relaxed straight arms at the sides read as straight elbows (L {neutral.Left.ElbowDegrees:F1}°, R {neutral.Right.ElbowDegrees:F1}°; reach L {profile.LeftUpper+profile.LeftForearm:F3} shoulder {profile.LeftShoulder}).");
        // Look down 30 degrees: the headset swings around the neck pivot while shoulders and hands stay put.
        var neckPivot=head+BodyTrackingFrame.NeckFromEyes;var lookDown=Quaternion.CreateFromAxisAngle(Vector3.UnitX,-30*MathF.PI/180);
        var lookingDown=solver.Solve(down with{Head=Pose(neckPivot-Vector3.Transform(BodyTrackingFrame.NeckFromEyes,lookDown),lookDown)},.02f);
        check(lookingDown.Left.ElbowDegrees<1&&lookingDown.Right.ElbowDegrees<1,$"Looking down does not bend relaxed straight arms (L {lookingDown.Left.ElbowDegrees:F1}°, R {lookingDown.Right.ElbowDegrees:F1}°).");
        var tilt=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,20*MathF.PI/180);
        var tilted=solver.Solve(down with{Head=Pose(neckPivot-Vector3.Transform(BodyTrackingFrame.NeckFromEyes,tilt),tilt)},.02f);
        check(tilted.Left.ElbowDegrees<1&&tilted.Right.ElbowDegrees<1,"Tilting the head sideways does not bend relaxed straight arms.");
        solver.Recenter(down,0,0,0);
        var nearlyStraight=BodyTrackingSolver.SolveElbow(Vector3.Zero,BodyTrackingSolver.ElbowTarget(Vector3.Zero,-Vector3.UnitY*.65f*.975f,.65f),.3f,.35f,-Vector3.UnitZ);
        check(nearlyStraight.Bend<.001f,"A hand within the last few centimetres of full reach counts as a straight arm.");
        var rightAngle=BodyTrackingSolver.SolveElbow(leftShoulder,BodyTrackingSolver.ElbowTarget(leftShoulder,bent.Left.Translation,.65f),.3f,.35f,-Vector3.UnitY);
        check(Math.Abs(rightAngle.Bend-MathF.PI/2)<.001f,"Straight-arm handling leaves a right-angle elbow unchanged.");
        float previousBend=float.MaxValue;bool monotonic=true;
        for(float ratio=.5f;ratio<=1.0001f;ratio+=.01f)
        {
            float b=BodyTrackingSolver.SolveElbow(Vector3.Zero,BodyTrackingSolver.ElbowTarget(Vector3.Zero,-Vector3.UnitY*.65f*ratio,.65f),.3f,.35f,-Vector3.UnitZ).Bend;
            if(b>previousBend+1e-4f)monotonic=false;previousBend=b;
        }
        check(monotonic,"Elbow bend decreases smoothly as the hand moves out to full reach.");
        var headOnly=solver.Solve(down with{Head=Pose(head,Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f))},.02f);
        check(Math.Abs(headOnly.Targets["URDFStageHeading"])<.001f&&headOnly.Targets["NeckTurn"]>30,"Head-only turning moves the neck without spinning the treads.");
        solver.Recenter(down,0,0,0);var smallHeadOnly=solver.Solve(down with{Head=Pose(head,Quaternion.CreateFromAxisAngle(Vector3.UnitY,.15f))},.02f);
        check(Math.Abs(smallHeadOnly.Targets["URDFStageHeading"])<.001f,"Small head-only looks do not masquerade as whole-body turns.");
        solver.Recenter(down,0,0,0);var walked=solver.Solve(Frame(leftDown-Vector3.UnitZ*.01f,rightDown-Vector3.UnitZ*.01f,head-Vector3.UnitZ*.01f),.02f);
        check(walked.Targets["URDFStageX"]==0&&Math.Abs(walked.Targets["URDFStageY"])<.001f,"Physical walking does not move the robot base.");
        var reversed=solver.Solve(down,.02f);check(reversed.Targets["URDFStageX"]==walked.Targets["URDFStageX"],"Physical backing up leaves the robot base in place.");
        solver.Recenter(down,0,0,0);var rotated=solver.Solve(Turn(.5f),.02f);
        check(rotated.Targets["URDFStageHeading"]==0&&rotated.Targets["B_WaistSwivel"]>0,"Coherent head-and-hand turning swivels the waist without turning the base.");
        solver.Recenter(down,0,0,0);var crouched=solver.Solve(stages[11],.02f);
        check(crouched.Targets["B_ChestTilt"]>0&&crouched.Targets["B_WaistLean"]>0,"Crouch lowers the robot through combined chest and waist bending.");
        float neckDown=(float)BodyControlCatalog.Find(ServoNames.B_NeckLift).MinRadians;
        BodyTrackingResult Crouch(float metres){solver.Recenter(down,0,0,0);return solver.Solve(Frame(leftDown,rightDown,head-Vector3.UnitY*metres),.02f);}
        var standingStill=Crouch(0);var sway=Crouch(.02f);
        check(standingStill.Targets["B_NeckLift"]==0&&sway.Targets["B_NeckLift"]==0&&sway.Targets["B_ChestTilt"]==0,"Standing and small head dips leave the neck lift and body upright.");
        var shallow=Crouch(profile.CrouchDrop*BodyTrackingSolver.NeckCrouchShare*.6f);
        check(shallow.Targets["B_NeckLift"]<0&&shallow.Targets["B_NeckLift"]>neckDown&&shallow.Targets["B_ChestTilt"]==0&&shallow.Targets["B_WaistLean"]==0,"A shallow crouch lowers the neck only.");
        var neckOnly=Crouch(profile.CrouchDrop*BodyTrackingSolver.NeckCrouchShare);
        check(Math.Abs(neckOnly.Targets["B_NeckLift"]-neckDown)<.001f&&neckOnly.Targets["B_ChestTilt"]<.001f,"The neck is fully lowered before the chest and waist start bending.");
        var deeper=Crouch(profile.CrouchDrop*.7f);
        check(Math.Abs(deeper.Targets["B_NeckLift"]-neckDown)<.001f&&deeper.Targets["B_ChestTilt"]>0&&deeper.Targets["B_ChestTilt"]<.65f*.45f*.45f,"Deeper crouching keeps the neck down and then bends the body.");
        var full=Crouch(profile.CrouchDrop);
        check(Math.Abs(full.Targets["B_NeckLift"]-neckDown)<.001f&&Math.Abs(full.Targets["B_ChestTilt"]-.65f*.45f*.45f)<.001f&&Math.Abs(full.Targets["B_WaistLean"]-.65f*.45f*.55f)<.001f,"A full crouch reaches the same chest and waist bend as before, with the neck lowered.");
        check(Johnny5.VrBridge.VrRecordingSelection.Owns(Johnny5.VrBridge.VrRecordingTracks.Torso,"B_NeckLift")&&!Johnny5.VrBridge.VrRecordingSelection.Owns(Johnny5.VrBridge.VrRecordingTracks.Neck,"B_NeckLift"),"Recording the torso group captures the crouch neck lift.");
        solver.Recenter(down,0,0,0);var bowed=solver.Solve(stages[12],.02f);
        check(Math.Abs(bowed.Targets["URDFStageX"])<.001f,"Calibrated forward-bend displacement is removed from walking travel.");
        BodyTrackingFrame StraightWrists(BodyTrackingFrame frame)
        {
            Matrix4x4 Hand(bool left)
            {
                var source=left?frame.Left:frame.Right;var shoulder=left?profile.LeftShoulder:profile.RightShoulder;
                var hand=source.Translation-frame.Head.Translation;
                var bend=BodyTrackingSolver.SolveElbow(shoulder,hand,left?profile.LeftUpper:profile.RightUpper,left?profile.LeftForearm:profile.RightForearm,new(left?-.25f:.25f,-1,.15f));
                var basis=BodyTrackingSolver.ForearmRotation(shoulder,bend.Elbow,hand);
                return Pose(source.Translation,basis*(left?profile.LeftNeutral:profile.RightNeutral));
            }
            return frame with{Left=Hand(true),Right=Hand(false)};
        }
        solver.Recenter(bent,0,0,0);var straightBent=solver.Solve(StraightWrists(bent),.02f);
        var straightDown=solver.Solve(StraightWrists(down),.02f);
        check(Math.Abs(straightBent.Targets["B_WristPitchLeft"])<.001f&&Math.Abs(straightBent.Targets["B_WristRollRight"])<.001f,"A neutral calibrated wrist maps to neutral robot wrist targets.");
        check(Math.Abs(straightDown.Targets["B_WristPitchLeft"])<.001f&&Math.Abs(straightDown.Targets["B_WristRollRight"])<.001f,"Changing forearm direction does not masquerade as wrist flex or roll.");
        var closed=solver.Solve(down with{LeftFingers=[1,1,1,1,1],RightFingers=[1,1,1,1,1]},.02f);
        check(closed.Targets["B_ThumbCurlLeft"]>0&&closed.Targets["B_FingerCurlUpperRight"]>0,"Index finger curls drive robot thumb and finger targets.");
        var mapped=solver.Solve(down with{LeftFingers=[1,0,.5f,0,0],RightFingers=[0,1,0,1,1]},.02f);
        // Left: thumb curled, middle half curled (not a point), index open. Right: only index curled.
        check(mapped.Targets["B_ThumbCurlLeft"]>0&&mapped.Targets["B_FingerCurlUpperLeft"]<0&&mapped.Targets["B_FingerCurlLowerLeft"]>0,"Left thumb, index and middle independently drive thumb, upper and lower fingers.");
        check(mapped.Targets["B_ThumbCurlRight"]<0&&mapped.Targets["B_FingerCurlUpperRight"]>0&&mapped.Targets["B_FingerCurlLowerRight"]<0,"Right thumb, index and middle independently drive thumb, upper and lower fingers.");
        var ignored=solver.Solve(down with{LeftFingers=[1,0,.5f,1,1],RightFingers=[0,1,0,0,0]},.02f);
        check(mapped.Targets.Where(p=>p.Key.Contains("Finger")||p.Key.Contains("Thumb")).All(p=>ignored.Targets[p.Key]==p.Value),"Ring and little fingers have no effect on robot hand controls.");
        check(!mapped.Targets.Keys.Any(k=>k.Contains("ShoulderHug")),"Shoulder Hug is excluded from tracked motion.");
        // Point gesture: thumb and middle nearly closed, index nearly straight.
        var pointer=new BodyTrackingSolver(profile);pointer.Recenter(down,0,0,0);
        BodyTrackingResult Hand(float thumb,float index,float middle)=>pointer.Solve(down with{LeftFingers=[thumb,index,middle,0,0],RightFingers=[0,0,0,0,0]},.02f);
        float baseClosed=(float)HandClosingLimits.BaseMaximum,thumbClosed=(float)HandClosingLimits.ThumbMaximum;
        bool UpperStraight(BodyTrackingResult r)=>r.Targets["B_FingerSpreadUpperLeft"]==0&&r.Targets["B_FingerCurlUpperLeft"]==0&&r.Targets["B_FingerTipCurlUpperLeft"]==0&&r.Targets["B_FingerEndCurlUpperLeft"]==0;
        bool OthersClosed(BodyTrackingResult r)=>Math.Abs(r.Targets["B_FingerCurlLowerLeft"]-baseClosed)<.001f&&Math.Abs(r.Targets["B_ThumbCurlLeft"]-thumbClosed)<.001f;
        var notYet=Hand(.9f,.1f,.4f);
        check(!notYet.Left.Pointing&&notYet.Targets["B_FingerSpreadUpperLeft"]>.5f,"An open middle finger does not start a point.");
        var bentIndex=Hand(.9f,.5f,.9f);
        check(!bentIndex.Left.Pointing,"A half-bent index does not start a point.");
        var point=Hand(.8f,.2f,.8f);
        check(point.Left.Pointing&&!point.Right.Pointing&&UpperStraight(point)&&OthersClosed(point),"Pointing straightens the upper finger, centres its spread and closes the other finger and thumb.");
        var relaxed=Hand(0,.45f,0);
        check(relaxed.Left.Pointing&&UpperStraight(relaxed)&&OthersClosed(relaxed),"The point holds while the index bends a little and the other fingers relax.");
        var released=Hand(0,.65f,0);
        check(!released.Left.Pointing&&released.Targets["B_FingerCurlUpperLeft"]>0&&released.Targets["B_FingerCurlLowerLeft"]<0,"Bending the index well past the hold releases the point and fingers follow again.");
        var again=Hand(0,.2f,0);
        check(!again.Left.Pointing,"After release, straightening the index alone does not re-start the point.");
        Hand(.8f,.2f,.8f);pointer.Recenter(down,0,0,0);
        check(!Hand(0,.4f,0).Left.Pointing,"Re-centering clears a held point.");
        var mirroredPoint=BodyTrackingSolver.MapPresentation(Hand(.8f,.2f,.8f),true);
        check(mirroredPoint.Targets["B_FingerSpreadUpperRight"]==0&&Math.Abs(mirroredPoint.Targets["B_FingerCurlLowerRight"]-baseClosed)<.001f,"Front-facing mirroring moves a point to Johnny's matching hand.");
        var mirrored=BodyTrackingSolver.MapPresentation(mapped,true);var same=BodyTrackingSolver.MapPresentation(mapped,false);
        check(mirrored.Targets["B_ThumbCurlRight"]==mapped.Targets["B_ThumbCurlLeft"]&&same.Targets["B_ThumbCurlLeft"]==mapped.Targets["B_ThumbCurlLeft"],"Front mirror swaps anatomical arm/hand sides; side/rear mapping preserves them.");
        var turned=neutral with{Targets=new(){{"NeckTurn",25},{"NeckTiltRight",15},{"NeckNodUp",10},{"B_WristRollLeft",.5f}}};
        check(BodyTrackingSolver.MapPresentation(turned,true).Targets["NeckTurn"]==25&&BodyTrackingSolver.MapPresentation(turned,false).Targets["NeckTurn"]==-25&&BodyTrackingSolver.MapPresentation(turned,false).Targets["NeckTiltRight"]==-15,"Front head mirroring is retained, while side/rear head turn and tilt follow the same direction.");
        check(BodyTrackingSolver.MapPresentation(turned,true).Targets["B_WristRollRight"]==.5f,"Mirrored wrist roll preserves symmetric robot joint angles when exchanging sides.");
        solver.MirrorFacing=true;solver.Recenter(down,0,0,0);check(solver.Solve(Turn(.5f),.02f).Targets["B_WaistSwivel"]<0,"Mirroring reverses tracked waist swivel, not the base.");
        solver.MirrorFacing=false;
        check(BodyCalibrationWizard.Steps.Length==21,"Calibration separates left/right wrist pitch, roll and arm twist without walking/turning steps.");
        solver.Recenter(down,0,0,0);solver.Drive(1,0,.05f);var driven=solver.Solve(down,.02f);
        check(Math.Abs(driven.Targets["URDFStageX"]-4)<.001f,"Joystick forward drives robot-native travel at 80 cm/s.");
        solver.Drive(0,1,.05f);check(Math.Abs(solver.Solve(down,.02f).Targets["URDFStageHeading"]+4.5f)<.001f,"Stick-right turns the robot right smoothly at 90 degrees/s.");

        solver.Recenter(down,12,34,20);var noTravel=solver.Solve(stages[15],.02f);
        check(noTravel.Targets["URDFStageX"]==12&&noTravel.Targets["URDFStageY"]==34,"Physical travel cannot change a nonzero robot placement.");
        CalibrationPoseGuide.SavePreview(Path.Combine(Diagnostics.Folder,"calibration-pose-guides.png"));
        using(var panel=VrBodyMotion.DrawPanel(wizard,profile,bent,null,"Calibration saved"))panel.Save(Path.Combine(Diagnostics.Folder,"body-calibration-panel.png"));
        string config=Path.Combine(Diagnostics.Folder,"body-follow-check","Config");profile.Save(Path.Combine(config,"VR","BodyTrackingCalibration.json"));
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            motion.Configure(config);check(motion.HasCalibration,"Following mode reloads the saved Config/VR profile.");
            using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var state=Johnny5.VrBridge.PoseWire.Read(reader)!;
            var batches=new List<Johnny5.VrBridge.VrMotionBatch>();motion.Send=batches.Add;
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");
            motion.StartFollowing();motion.Step(down,state,input with{Select=true},false,.04f);
            check(motion.Following&&batches.Count==0&&motion.Status.StartsWith("Starting follow"),"Follow survives the menu-selection frame that closes the menu while its interaction flag remains set.");
            motion.Step(down,state,input with{Select=true},true,.04f);
            check(motion.Following&&batches.Count==0,"Follow waits for the menu-selection trigger to be released before recentering.");
            motion.Step(down,state,input,true,.04f);
            check(batches.Count==1&&batches[0].Targets.Count>20,"Following sends one complete pose batch instead of dropping individual joint updates.");
            check(!motion.TrackWaistSwivel&&!batches[0].Targets.ContainsKey("B_WaistSwivel")&&batches[0].Targets.ContainsKey("B_WaistLean")&&new ViewerSettings().TrackWaistSwivel==false,"Waist swivel tracking is off by default: Follow leaves the timeline's waist swivel alone while still bending the waist.");
            motion.TrackWaistSwivel=true;motion.Step(down,state,input,true,.04f);
            check(batches[^1].Targets.ContainsKey("B_WaistSwivel"),"Turning waist swivel tracking on sends the tracked waist swivel again.");
            motion.TrackWaistSwivel=false;motion.Step(down,state,input,true,.04f);batches.RemoveRange(1,batches.Count-1);
            for(int i=0;i<4;i++)motion.Step(down with{Valid=false},state,input,true,.05f);
            check(!motion.Following&&batches.Count==1,"Tracking loss holds the pose and stops new batches.");
            motion.StartFollowing();motion.Step(down,state,input,true,.04f);int beforeContext=batches.Count;
            motion.Step(down,state with{Token="different"},input,true,.04f);
            check(!motion.Following&&batches.Count==beforeContext,"Changing the timeline context stops motion following.");
            motion.StartFollowing();motion.Step(down,state,input,true,.04f);motion.Step(down,state,input,false,.04f);
            check(!motion.Following,"Menus/dashboard pause following and require a deliberate re-center.");
            motion.StartFollowing();motion.Step(down,state,input,true,.04f);motion.Step(down,state,input with{Back=true},true,.04f);
            check(!motion.Following,"The B button stops motion following.");
        }
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            motion.Configure(config);
            using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var pose=Johnny5.VrBridge.PoseWire.Read(reader)!;
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");var batches=new List<Johnny5.VrBridge.VrMotionBatch>();motion.Send=batches.Add;
            motion.StartFollowing();motion.Step(down,pose,input,true,.04f);batches.Clear();
            var lost=down with{Valid=false};motion.TrackingDetail="Left controller: invalid pose";
            motion.Step(lost,pose,input,true,.04f);
            check(motion.Following&&batches.Count==0&&motion.Status.Contains("pose held"),"Tracking interruption holds the robot and sends no motion.");
            motion.Step(down,pose,input,true,.04f);
            check(motion.Following&&batches.Count==1,"Valid tracking recovers within the existing grace interval.");
            for(int i=0;i<4;i++)motion.Step(lost,pose,input,true,.04f);
            check(!motion.Following&&motion.Status.Contains("Left controller"),"Existing 150 ms tracking-loss timeout remains unchanged and identifies the controller.");
        }
        var problemOverlay=new VisibleOverlay();
        using(var motion=new VrBodyMotion(problemOverlay))
        {
            motion.Configure(config);motion.ShowFollowingStatus=false;
            using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var pose=Johnny5.VrBridge.PoseWire.Read(reader)!;
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");motion.Send=_=>{};
            bool PanelShown(){for(int i=0;i<100;i++){motion.Draw(panelHead);if(problemOverlay.Visible)return true;Thread.Sleep(20);}return false;}
            motion.StartFollowing();motion.Step(down,pose,input,true,.04f);motion.Step(down,pose,input,true,.04f);
            motion.Draw(panelHead);check(!problemOverlay.Visible&&!motion.ProblemShowing,"With overhead status off, normal following shows no overhead panel.");
            motion.TrackingDetail="Left: device 2, connected=True, valid=False, Running_OutOfRange";
            motion.Step(down with{Valid=false},pose,input,true,.04f);
            check(motion.Following&&motion.ProblemShowing&&motion.ProblemText.Contains("Left: device 2"),"A short tracking interruption raises a problem naming the device.");
            check(PanelShown(),"A tracking problem shows the overhead status panel even with overhead status turned off.");
            motion.Step(down,pose,input,true,.04f);
            check(motion.Following&&motion.ProblemText.Contains("Tracking back after")&&motion.ProblemText.Contains(" ms"),"Recovery reports how long tracking was lost.");
            for(int i=0;i<4;i++)motion.Step(down with{Valid=false},pose,input,true,.05f);
            check(!motion.Following&&motion.ProblemText.Contains("Follow stopped: Tracking lost"),"A tracking-loss stop is shown as a problem.");
            using(var problemPanel=VrBodyMotion.DrawPanel(null,null,null,null,motion.Status,"",motion.ProblemText))problemPanel.Save(Path.Combine(Diagnostics.Folder,"follow-problem-panel.png"));
        }
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            motion.Configure(config);
            using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var pose=Johnny5.VrBridge.PoseWire.Read(reader)!;
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");motion.Send=_=>{};
            motion.StartFollowing();motion.Step(down,pose,input,true,.04f);motion.Step(down,pose,input with{Back=true},true,.04f);
            check(!motion.Following&&!motion.ProblemShowing,"Stopping with B is not reported as a problem.");
            motion.StartFollowing();motion.Step(down,pose,input,true,.04f);motion.Stop("Stopped from transport",byUser:true);
            check(!motion.ProblemShowing,"Stopping from the transport is not reported as a problem.");
            motion.StartFollowing();motion.Step(down,pose,input,true,.04f);motion.Step(down,pose with{Token="different"},input,true,.04f);
            check(motion.ProblemShowing&&motion.ProblemText.Contains("Timeline/cursor changed"),"An unrequested stop (context change) is reported as a problem.");
        }
        BridgeTiming.Tracking(true,true,true,true,true,"");
        BridgeTiming.Tracking(true,false,true,true,true,"Left: device 2, connected=True, valid=False, Running_OutOfRange");
        check(BridgeTiming.LastProblem.Contains("Left controller lost")&&BridgeTiming.LastProblem.Contains("Running_OutOfRange"),"The problem log records which controller dropped and SteamVR's reason.");
        BridgeTiming.Tracking(true,true,true,true,true,"");
        check(BridgeTiming.LastProblem.Contains("Left controller back after")&&BridgeTiming.ProblemFilePath!=null,"The problem log records how long the controller was gone.");
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            motion.Configure(config);
            using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var pose=Johnny5.VrBridge.PoseWire.Read(reader)!;
            var requests=new List<Johnny5.VrBridge.VrRecordingRequest>();var batches=new List<Johnny5.VrBridge.VrMotionBatch>();motion.SendRecording=requests.Add;motion.Send=batches.Add;
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");
            motion.Step(down,pose,input,true,.04f);motion.StartRecording(true);
            motion.Step(down,pose,input with{Select=true},false,.04f);
            check(motion.Following,"Recording start also survives the closing-menu interaction frame.");
            check(requests.Count==0,"Recording waits for trigger release before starting.");
            motion.Step(down,pose,input,true,.04f);
            check(requests.Count==1&&requests[0].Action==Johnny5.VrBridge.VrRecordingAction.Overdub&&(requests[0].Tracks&Johnny5.VrBridge.VrRecordingTracks.Neck)==0,"Overdub requests body tracks with the complete neck group unarmed by default.");
            check(batches.Count==0,"No motion batches are sent until the editor acknowledges recording.");
            var live=pose with{Token="recording-session",Recording=true,Cursor=pose.Cursor+.1,RecordingMessage="Recording"};
            motion.Step(down,live,input,true,.04f);motion.Step(down,live with{Cursor=pose.Cursor+.2},input,true,.04f);
            check(motion.Following&&batches.Count==2&&batches.All(b=>b.Token==live.Token),"Following remains active during overdub playback using the stable recording token.");
            motion.Step(down,live,input with{Back=true},true,.04f);
            check(!motion.Following&&requests.Count(r=>r.Action==Johnny5.VrBridge.VrRecordingAction.Stop)==1,"B sends one Stop request and retains the take for review.");
        }
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            motion.Configure(config);motion.Calibrate();
            check(motion.RecordingPath!=null&&Path.GetFileName(motion.RecordingPath).StartsWith("tracking-calibration-")&&File.Exists(motion.RecordingPath)&&BridgeTiming.LastProblem.Contains("Calibration started"),"Starting calibration records tracking and logs the attempt.");
            var anchored=motion.DisplayWorld(panelHead,true);
            var movedHead=Matrix4x4.CreateRotationY(.7f)*Matrix4x4.CreateTranslation(1,1.4f,2);
            check(motion.DisplayWorld(movedHead,true)==anchored,"Calibration display remains fixed in the room as the user turns, crouches or walks.");
            motion.Previous();check(motion.DisplayWorld(movedHead,true)==anchored,"Returning to a calibration step preserves the panel's room position.");
            motion.Calibrate();check(motion.DisplayWorld(movedHead,true)!=anchored,"Starting a new calibration places its panel in front of the current user position.");
            check(VrSession.BodyInputAllowed(true,false,true,true),"Calibration owns trigger input even when another editor interaction reports a hit.");
            check(!VrSession.BodyInputAllowed(true,true,false,true)&&!VrSession.BodyInputAllowed(false,false,false,true),"An open menu or SteamVR focus loss still pauses calibration input.");
            var input=new ControllerReading(Vector2.Zero,Vector2.Zero,"","",true,"");
            motion.Step(down,null,input with{Select=true},false,.02f,"SteamVR binding","SteamVR dashboard / input focus");
            check(motion.TriggerStatus.Contains("Paused:")&&!motion.Wizard!.Collecting,"The calibration readout distinguishes detected triggers from focus-blocked capture.");
            motion.Step(down,null,input with{Select=true},true,.02f,"SteamVR binding");
            motion.Step(down,null,input,true,.02f,"SteamVR binding");
            motion.Step(down,null,input with{Select=true},true,.02f,"SteamVR binding");
            check(motion.Wizard!.Collecting&&motion.TriggerStatus.Contains("pressed"),"A bound trigger reaches the real calibration wrapper after releasing the menu-selection pull.");
            for(int i=0;i<110;i++)motion.Step(down,null,input,true,.02f,"SteamVR binding");
            check(motion.Wizard.Step==1,"Trigger press/release advances the first calibration step through the VR body capture wrapper.");
            motion.Step(down,null,input with{Back=true},false,.02f);
            check(!motion.Active,"B exits calibration even when its capture input is paused by focus/menu gating.");
        }
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            var viewPose=new Johnny5.VrBridge.VrPoseState("view",0,true,0,"","",[
                new("Servo:B_ShoulderRotateLeft","L",new(.3f,1.3f,-3),-100,100,0,false),
                new("Servo:B_ShoulderRotateRight","R",new(-.3f,1.3f,-3),-100,100,0,false),
                new("Servo:NeckTurn","Neck",new(0,1.7f,-3),-100,100,0,false)]);
            motion.UpdateView(panelHead,Matrix4x4.Identity,viewPose,0,1.8f);var above=motion.DisplayWorld(panelHead,false).Translation;
            check(motion.MirrorFacing&&above.Y-.95f*330/1400/2>1.8f&&Math.Abs(above.Z+3)<.001f,"Following status is wholly above the robot instead of in front of its body.");
            var raised=viewPose with{Handles=viewPose.Handles.Concat(new[]{new Johnny5.VrBridge.VrPoseHandle("Servo:B_WristPitchLeft","Raised wrist",new(.3f,2.6f,-3),-100,100,0,false)}).ToArray()};
            motion.UpdateView(panelHead,Matrix4x4.Identity,raised,0,1.8f);
            check(Vector3.Distance(above,motion.DisplayWorld(panelHead,false).Translation)<.001f,"Raising an arm above the head does not push the following status away from the head.");
            var origin=Matrix4x4.CreateTranslation(4,0,-3);motion.UpdateView(panelHead,origin,viewPose,0,1.8f);
            var moved=(motion.DisplayWorld(panelHead,false)*origin).Translation;
            check(motion.MirrorFacing&&Vector3.Distance(above,moved)<.001f,"Viewpoint movement never changes the selected mirror mode.");
        }
        using(var motion=new VrBodyMotion(new FakeOverlay()))
        {
            var input=ControllerReading.Failed("");
            var confirmations=new List<FollowToggle>();motion.ToggleFeedback=confirmations.Add;
            check(motion.MirrorFacing&&!motion.MoveMe,"Follow defaults to Mirrored and Move Robot.");
            motion.FollowButtons(input with{LeftA=true,LeftB=true},true);
            check(!motion.MirrorFacing&&!motion.MoveMe,"Left A toggles mirror; Left B no longer toggles movement target.");
            motion.FollowButtons(input with{LeftA=true,LeftB=true},true);
            check(!motion.MirrorFacing&&!motion.MoveMe,"Holding a toggle does not repeat it.");
            check(confirmations.SequenceEqual(new[]{FollowToggle.Unmirrored}),"Only mirror changes emit toggle confirmations; Left B is reserved for Selections.");
            motion.FollowButtons(input,false);motion.FollowButtons(input with{LeftA=true,LeftB=true},false);
            check(confirmations.Count==1,"Disabled/focus-blocked toggles do not make confirmation sounds.");
            var routed=(input with{Menu=true,Back=true,LeftA=true,LeftB=true}).ForFollowing();
            check(!routed.Menu&&!routed.Back,"Left A/B cannot activate menu or stop while following.");
            routed=(input with{RightA=true,RightB=true}).ForFollowing();check(routed.Menu&&routed.Back,"Right A/B retain menu and stop.");
            var controller=Matrix4x4.CreateTranslation(0,1,-.5f);
            check(FollowControllerLabels.Anchor(controller,true).Z>FollowControllerLabels.Anchor(controller,false).Z,"A plates are closer to the viewer than B plates on forward-pointed controllers.");
        }
        var route=new ControllerInputRoute();route.EnableTracking(true);
        var rawButtons=ControllerReading.Failed("") with{LeftA=true,LeftB=true,RightA=true,RightB=true};
        var boundButtons=ControllerReading.Failed("") with{LeftA=true,BoundLeftA=true,BoundLeftB=true,BoundRightA=true,BoundRightB=true};
        var handRoute=route.Read(false,()=>rawButtons,()=>boundButtons);
        check(handRoute.LeftA&&!handRoute.LeftB&&!handRoute.RightA&&!handRoute.RightB&&!handRoute.ForFollowing().Menu,"Released per-hand bound buttons override stale legacy presses without crossing hands.");
        var smooth=new Locomotion();var pivot=panelHead.Translation;
        for(int i=0;i<60;i++)smooth.Step(panelHead,Vector2.Zero,1,1f/60,1,30,true,true);
        check(Vector3.Distance((panelHead*smooth.Origin).Translation,pivot)<.001f&&Vector3.TransformNormal(-Vector3.UnitZ,smooth.Origin).X>.99f,"Smooth turning holds headset position and integrates across frames.");
        var uncertain=new BodyCalibrationWizard();uncertain.Update(down,.02f,true);
        for(int i=0;i<100;i++)uncertain.Update(down with{Valid=false},.02f,false);
        check(uncertain.Step==0&&uncertain.Progress==0,"Tracking loss never advances calibration.");
    }
    private sealed class FakeOverlay:IPanelOverlay
    {
        public Valve.VR.EVROverlayError CreateOverlay(string key,string name,ref ulong h){h=1;return 0;}
        public Valve.VR.EVROverlayError SetOverlayWidthInMeters(ulong h,float width)=>0;
        public Valve.VR.EVROverlayError SetOverlayInputMethod(ulong h,Valve.VR.VROverlayInputMethod method)=>0;
        public Valve.VR.EVROverlayError SetOverlaySortOrder(ulong h,uint order)=>0;
        public Valve.VR.EVROverlayError SetOverlayRaw(ulong h,IntPtr data,uint width,uint height,uint depth)=>0;
        public Valve.VR.EVROverlayError SetOverlayTransformAbsolute(ulong h,Valve.VR.ETrackingUniverseOrigin origin,ref Valve.VR.HmdMatrix34_t pose)=>0;
        public Valve.VR.EVROverlayError ShowOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError HideOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError DestroyOverlay(ulong h)=>0;
        public void Dispose(){}
    }
    // Records whether the overhead panel is currently shown.
    private sealed class VisibleOverlay:IPanelOverlay
    {
        internal bool Visible;
        public Valve.VR.EVROverlayError CreateOverlay(string key,string name,ref ulong h){h=1;return 0;}
        public Valve.VR.EVROverlayError SetOverlayWidthInMeters(ulong h,float width)=>0;
        public Valve.VR.EVROverlayError SetOverlayInputMethod(ulong h,Valve.VR.VROverlayInputMethod method)=>0;
        public Valve.VR.EVROverlayError SetOverlaySortOrder(ulong h,uint order)=>0;
        public Valve.VR.EVROverlayError SetOverlayRaw(ulong h,IntPtr data,uint width,uint height,uint depth)=>0;
        public Valve.VR.EVROverlayError SetOverlayTransformAbsolute(ulong h,Valve.VR.ETrackingUniverseOrigin origin,ref Valve.VR.HmdMatrix34_t pose)=>0;
        public Valve.VR.EVROverlayError ShowOverlay(ulong h){Visible=true;return 0;}
        public Valve.VR.EVROverlayError HideOverlay(ulong h){Visible=false;return 0;}
        public Valve.VR.EVROverlayError DestroyOverlay(ulong h)=>0;
        public void Dispose(){}
    }
}
