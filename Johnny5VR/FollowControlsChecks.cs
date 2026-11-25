using System.Numerics;
using ServoAnimator;
using Johnny5.VrBridge;
namespace Johnny5VR;
internal static class FollowControlsChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool good,string name){if(!good)throw new InvalidOperationException(name);count++;}
        var input=ControllerReading.Failed("") with{Move=new(.7f,.9f),Right=new(-.4f,-.6f)};
        var robot=FollowStickRouting.Robot(input);var viewer=FollowStickRouting.Viewer(input,true);
        Check(robot==(.9f,.7f),"Left Y drives robot, Left X turns robot.");
        Check(viewer.Move==new Vector2(0,-.6f)&&viewer.Turn==-.4f,"Right Y moves viewer, Right X turns viewer, with no strafe.");
        Check(FollowStickRouting.Viewer(input,false).Move==input.Move,"Non-Follow navigation stays unchanged.");
        var hands=new FollowHands();
        var fingers=new Dictionary<string,float>();
        void Apply(ControllerReading sticks,bool mirror=false)=>hands.Apply(sticks,true,mirror,.02f,fingers,c=>c.Initial);
        Check(hands.Tracking&&!hands.Manual,"Follow starts in Movement with finger tracking on.");
        hands.ToggleTracking();Apply(ControllerReading.Failed(""));
        Check(fingers["B_ThumbCurlLeft"]==-.3f,"Tracking off returns to defaults.");
        hands.ToggleMode();
        var gesture=ControllerReading.Failed("") with{Move=new(-.9f,0),Right=new(.9f,0)};
        Apply(gesture);
        Check(fingers["B_FingerCurlUpperLeft"]==0&&fingers["B_FingerCurlLowerLeft"]>0&&fingers["B_FingerCurlUpperRight"]>0,"Left presets point and right presets make a fist.");
        Apply(gesture,true);
        Check(fingers["B_FingerCurlUpperRight"]==0,"Presets follow mirrored routing.");
        Apply(gesture with{Move=new(0,.9f)});
        Check(fingers["B_FingerCurlUpperLeft"]==0,"Forward does not release a point.");
        Apply(gesture with{Move=new(0,-.9f)});
        Apply(gesture with{Move=Vector2.Zero});
        Check(fingers["B_FingerCurlUpperLeft"]==0,"Releasing a point then centering holds the point.");
        Apply(gesture with{Move=new(0,.9f)});
        float curl=fingers["B_FingerCurlLowerLeft"];
        Check(curl<(float)HandClosingLimits.BaseMaximum,"Forward opens the hand after preset release.");
        hands.ToggleMode();Apply(gesture with{Move=new(0,.9f)});
        Check(fingers["B_FingerCurlLowerLeft"]==curl,"Returning to Movement holds the last pose with tracking off.");
        hands.ToggleTracking();fingers.Clear();Apply(gesture);
        Check(fingers.Count==0,"Movement with tracking on leaves tracked fingers in control.");
        hands.ToggleMode(new Dictionary<string,float>{{"B_ThumbCurlLeft",.35f}});
        Apply(ControllerReading.Failed(""));
        Check(fingers["B_ThumbCurlLeft"]==.35f,"Entering Hand with centered sticks holds the last tracked pose.");
        var route=new ControllerInputRoute();route.EnableTracking(true);
        var combined=route.Read(false,()=>input with{LeftStickClick=true},()=>ControllerReading.Failed("") with{BoundLeftStickClick=true,LeftStickClick=false});
        Check(!combined.LeftStickClick,"Bound left-stick release overrides stale raw click.");
        var right=route.Read(false,()=>input with{RightStickClick=true,RightTrigger=true},
            ()=>ControllerReading.Failed("") with{BoundRightStickClick=true,BoundRightTrigger=true});
        Check(!right.RightStickClick&&!right.RightTrigger,"Bound right-hand release overrides stale raw input.");
        combined=route.Read(false,()=>input,()=>ControllerReading.Failed("") with{BoundLeftStickClick=true,LeftStickClick=true});
        Check(combined.LeftStickClick,"Bound left-stick click is preserved.");
        Check(!(input with{LeftB=true}).ForFollowing().Back,"Left B never becomes Follow stop.");
        Check((input with{RightB=true}).ForFollowing().Back,"Right B remains Follow stop.");
        var profile=new BodyTrackingCalibration{LeftShoulder=new(-.2f,-.3f,0),RightShoulder=new(.2f,-.3f,0),LeftUpper=.3f,RightUpper=.3f,LeftForearm=.35f,RightForearm=.35f,StandingHeight=1.7f,CrouchDrop=.4f,BowDrop=.25f,BowForward=.3f};
        Check(profile.Valid(),"Focused fixture is a valid calibration.");
        var frame=new BodyTrackingFrame(Matrix4x4.CreateTranslation(0,1.7f,0),Matrix4x4.CreateTranslation(-.2f,1.05f,-.3f),Matrix4x4.CreateTranslation(.2f,1.05f,-.3f),[1,0,1,1,1],[1,0,1,1,1],true,"skeletal");
        var solver=new BodyTrackingSolver(profile);solver.Recenter(frame,0,0,0);var point=solver.Solve(frame,.02f);
        foreach(string side in new[]{"Left","Right"})foreach(string joint in new[]{"WristRoll","FingerSpreadUpper","FingerCurlUpper","FingerTipCurlUpper","FingerEndCurlUpper"})Check(point.Targets["B_"+joint+side]==0,"Pointing straightens "+joint+side);
        foreach(string side in new[]{"Left","Right"})
        {

            Check(point.Targets["B_WristPitch"+side]==BodyTrackingSolver.PointWristPitchRadians,"Point wrist pitch aligns hand with forearm.");
        }
        Check(point.Left.Pointing&&point.Right.Pointing,"Both hands enter assisted point.");
        var released=solver.Solve(frame with{LeftFingers=[1,1,1,1,1],RightFingers=[1,1,1,1,1]},.02f);
        Check(!released.Left.Pointing&&!released.Right.Pointing&&released.Targets["B_ElbowLeft"]>0,"Bending index releases point and restores arm tracking.");
        foreach(string side in new[]{"Left","Right"})foreach(string joint in new[]{"Elbow","ArmTwist"})
            Check(Math.Abs(point.Targets["B_"+joint+side]-released.Targets["B_"+joint+side])<.0001f,"Pointing leaves normal "+joint+side+" tracking unchanged.");
        foreach(float reach in new[]{1f,.98f})
        {
            var straight=frame with{Left=Matrix4x4.CreateTranslation(-.2f,1.4f,-.65f*reach),Right=Matrix4x4.CreateTranslation(.2f,1.4f,-.65f*reach),LeftFingers=[0,0,0,0,0],RightFingers=[0,0,0,0,0]};
            var full=solver.Solve(straight,.02f);
            foreach(string side in new[]{"Left","Right"})Check(Math.Abs(full.Targets["B_Elbow"+side]-BodyTrackingSolver.PointElbowRadians)<.0001f,"Normal Follow reaches mechanical straightness at "+reach+" reach for "+side);
        }
        var bentPose=frame with{Left=Matrix4x4.CreateTranslation(-.2f,1.1f,-.35f),Right=Matrix4x4.CreateTranslation(.2f,1.1f,-.35f),LeftFingers=[0,0,0,0,0],RightFingers=[0,0,0,0,0]};
        var bent=solver.Solve(bentPose,.02f);
        foreach(string side in new[]{"Left","Right"})Check(Math.Abs(bent.Targets["B_Elbow"+side]-(MathF.PI/2+BodyTrackingSolver.PointElbowRadians))<.001f,"Normal 90-degree elbow is offset consistently for "+side);
        using var motion=new VrBodyMotion(new NullOverlay());
        string folder=Path.Combine(Diagnostics.Folder,"follow-controls-check");profile.Save(Path.Combine(folder,"VR","BodyTrackingCalibration.json"));motion.Configure(folder);
        VrPoseHandle H(string key,float value=0)=>new("Servo:"+key,key,Vector3.Zero,0,100,value,false);
        var pose=new VrPoseState("focused",0,true,0,"","",[H("B_ShoulderRotateLeft"),H("B_ShoulderRotateRight"),H("B_LaserLower")]){Accessory=1};
        var batches=new List<VrMotionBatch>();motion.Send=batches.Add;motion.StartFollowing();motion.Step(frame,pose,ControllerReading.Failed(""),true,.02f);
        motion.ToggleLaser();motion.Step(frame,pose,ControllerReading.Failed(""),true,.02f);
        Check(batches.Last().Targets["B_LaserLower"]==(float)BodyControlCatalog.LaserLowerMaximum,"First visible-laser toggle lowers laser through motion batch.");
        motion.ToggleLaser();motion.Step(frame,pose,ControllerReading.Failed(""),true,.02f);Check(batches.Last().Targets["B_LaserLower"]==0,"Next click raises laser.");
        motion.SelectionsOpen=true;int before=batches.Count;motion.Step(frame,pose,input,false,.02f);
        Check(motion.Following&&batches.Count==before,"Selections hold Follow without stopping or driving.");
        motion.SelectionsOpen=false;motion.Step(frame,pose,input,true,.02f);Check(motion.Following&&batches.Count>before,"Closing Selections resumes Follow without re-centering.");
        motion.Step(frame,pose with{Accessory=0},input,true,.02f);motion.ToggleLaser();motion.Step(frame,pose with{Accessory=0},input,true,.02f);
        Check(!batches.Last().Targets.ContainsKey("B_LaserLower"),"Hidden laser is not driven by stick clicks.");
        Check(VrBodyMotion.CanFireLaserAt(pose with{Handles=[H("B_LaserLower",100)]}),"Lowered visible laser can fire.");
        Check(!VrBodyMotion.CanFireLaserAt(pose with{Handles=[H("B_LaserLower",50)]}),"Moving laser cannot fire before reaching lowered position.");
        Check(!VrBodyMotion.CanFireLaserAt(pose with{Accessory=0,Handles=[H("B_LaserLower",100)]}),"Hidden laser cannot fire.");
        long now=100;motion.CountdownClock=()=>now;
        var requests=new List<VrRecordingRequest>();motion.SendRecording=requests.Add;
        var idle=ControllerReading.Failed("");
        motion.StartRecording(true,3);
        motion.Step(frame,pose,idle with{Select=true},true,.02f);
        Check(motion.Countdown==0&&requests.Count==0,"Countdown waits for trigger release.");
        motion.Step(frame,pose,idle,true,.02f);
        Check(motion.Countdown==3&&requests.Count==0,"Countdown starts once tracking and trigger release are ready.");
        now+=2999;motion.Step(frame,pose,idle,true,.02f);
        Check(motion.Countdown==1&&requests.Count==0,"Overdub does not send Start before three seconds.");
        motion.Step(frame,pose,idle with{Back=true},true,.02f);
        now+=2;motion.Step(frame,pose,idle,true,.02f);
        Check(!motion.RecordingPending&&requests.Count==0,"Cancel during countdown sends no editor request.");
        motion.StartRecording(true,3);motion.Step(frame,pose,idle,true,.02f);
        now+=3000;motion.Step(frame,pose,idle,true,.02f);motion.Step(frame,pose,idle,true,.02f);
        Check(requests.Count==1&&requests[0].Action==VrRecordingAction.Overdub,"Countdown sends exactly one armed overdub request.");
        var leftTrigger=route.Read(false,()=>input with{LeftTrigger=true},()=>ControllerReading.Failed("") with{BoundLeftTrigger=true,LeftTrigger=false});
        Check(!leftTrigger.LeftTrigger,"Bound left-trigger release overrides stale raw trigger.");
        Check(!(input with{RightA=true}).ForFollowing().LeftTrigger,"Right A cannot become a left trigger.");
        using(var menu=new VrMenu(new NullOverlay()))
        {
            int restored=0,back=0;
            menu.RestoreRegularMenu=()=>{restored++;menu.Set("J5 Studio · Home",[new("Follow / Record · Body movements",()=>{})]);menu.GoBack=()=>back++;};
            menu.Set("Follow · Selections",[new("Laser",()=>{})]);menu.FollowSelections=true;menu.Navigation.Open();
            menu.Step(ControllerReading.Failed(""),true,frame.Head);
            menu.Step((input with{RightA=true}).ForFollowing(),true,frame.Head);
            Check(restored==1&&!menu.FollowSelections&&menu.Navigation.Visible&&menu.Rows[0].Label.Contains("Record"),"Right A replaces visible Selections with the regular recording menu.");
            menu.Step(ControllerReading.Failed(""),true,frame.Head);menu.Step((input with{RightB=true}).ForFollowing(),true,frame.Head);
            Check(back==1,"Right B retains regular menu Back.");
            menu.Set("Follow · Selections",[new("Laser",()=>{})]);menu.FollowSelections=true;menu.Navigation.Close();
            menu.Step(ControllerReading.Failed(""),true,frame.Head);menu.Step((input with{RightA=true}).ForFollowing(),true,frame.Head);
            Check(restored==2&&menu.Navigation.Visible&&!menu.FollowSelections,"Right A restores the regular menu even after Selections was closed.");
        }
        foreach(float dpi in new[]{96f,144f,192f})
        {
            const string help="Stand upright before starting/re-centering. Headset and two controllers estimate elbows and body; they do not measure them directly. Right A opens the main menu and Right B stops or goes Back. Left A toggles mirroring and Left B opens Selections. Left stick drives and turns the robot; right stick moves and turns you. Point or use a stick to browse. Pull the trigger to select a row. The menu holds motion until closed. Choose Record to create a new body take, or Overdub to hear existing audio and add body movements. Keep the take to commit it to the timeline, then save your sequence.";
            using var image=VrMenu.Draw("Follow / Record · Calibration, body movements and overdubbing",help,Enumerable.Range(1,8).Select(i=>"Control "+i).ToArray(),7,0,null,dpi);
            using var graphics=Graphics.FromImage(image);
            using var titleFont=VrTextureText.FitFont(graphics,"Follow / Record · Calibration, body movements and overdubbing",new(1030,66),38,FontStyle.Bold);
            using var helpFont=VrTextureText.FitFont(graphics,help,new(1030,148),19);
            Check(titleFont.Unit==GraphicsUnit.Pixel&&graphics.MeasureString("Follow / Record · Calibration, body movements and overdubbing",titleFont,1030).Height<=66,"Menu title fits at "+dpi+" DPI.");
            Check(helpFont.Unit==GraphicsUnit.Pixel&&graphics.MeasureString(help,helpFont,1030).Height<=148,"Full help text fits at "+dpi+" DPI.");
            image.Save(Path.Combine(Diagnostics.Folder,"vr-menu-safe-text-"+(int)dpi+".png"));
        }
        Check(VrMenu.RowAt(new(.5f,(150+7*62+27)/900f),0,8)==7,"Bottom menu row hit testing matches its new position.");
        using var lcd=FollowControllerLabels.Display(true,true,FollowMovementTarget.Robot,"10:30 AM");lcd.Save(Path.Combine(Diagnostics.Folder,"follow-selections-glove.png"));
        Console.WriteLine($"PASS: {count} focused Follow controls checks. Full suite not run.");
    }
    internal sealed class NullOverlay:IPanelOverlay
    {
        public Valve.VR.EVROverlayError CreateOverlay(string k,string n,ref ulong h){h=1;return 0;}
        public Valve.VR.EVROverlayError DestroyOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError SetOverlayWidthInMeters(ulong h,float w)=>0;
        public Valve.VR.EVROverlayError SetOverlayInputMethod(ulong h,Valve.VR.VROverlayInputMethod m)=>0;
        public Valve.VR.EVROverlayError SetOverlaySortOrder(ulong h,uint v)=>0;
        public Valve.VR.EVROverlayError SetOverlayTransformAbsolute(ulong h,Valve.VR.ETrackingUniverseOrigin o,ref Valve.VR.HmdMatrix34_t m)=>0;
        public Valve.VR.EVROverlayError SetOverlayRaw(ulong h,IntPtr p,uint w,uint t,uint d)=>0;
        public Valve.VR.EVROverlayError ShowOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError HideOverlay(ulong h)=>0;
        public void Dispose(){}
    }
}
