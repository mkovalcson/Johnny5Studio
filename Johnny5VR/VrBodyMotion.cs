using System.Numerics;
using System.Runtime.InteropServices;
using ServoAnimator;
using Johnny5.VrBridge;
using Valve.VR;
namespace Johnny5VR;

internal sealed partial class VrBodyMotion:IDisposable
{
    private readonly IPanelOverlay overlay;
    internal readonly FollowSpeedSettings Speeds=new();
    internal readonly FollowNeutralSettings Neutrals=new();
    internal readonly FollowNeutralMapper NeutralMapper=new();
    internal readonly FollowInputSettings InputTuning=new();
    internal readonly FollowHands Hands=new();
    internal bool TuningSpeeds {get;set;}
    private ulong panel;
    private long nextPaint;
    private Task<(byte[] Pixels,int Width,int Height)>? painting;
    private bool panelReady,panelLarge;
    private Matrix4x4? calibrationWorld;
    private Matrix4x4 viewerOrigin=Matrix4x4.Identity;
    private Vector3 virtualViewer,overheadCenter;
    private bool hasRobotView,mirrorFacing=true;
    internal bool MirrorFacing=>mirrorFacing;
    internal void UpdateView(Matrix4x4 head,Matrix4x4 origin,VrPoseState? pose,float floor,float height)
    {
        viewerOrigin=origin;virtualViewer=(head*origin).Translation;
        var left=pose?.Handles.FirstOrDefault(h=>h.Key=="Servo:B_ShoulderRotateLeft");
        var right=pose?.Handles.FirstOrDefault(h=>h.Key=="Servo:B_ShoulderRotateRight");
        if(left==null||right==null)return;
        var center=(left.Anchor+right.Anchor)*.5f;var side=left.Anchor-right.Anchor;side.Y=0;
        if(side.LengthSquared()<.0001f)return;
        // Mirroring is explicitly selected with the left A button, independent of viewpoint.
        var headPoints=pose!.Handles.Where(h=>h.Key.StartsWith("Child:",StringComparison.Ordinal)||(!h.Key.StartsWith("Servo:B_",StringComparison.Ordinal)&&!h.Key.StartsWith("Servo:URDF",StringComparison.Ordinal))).ToArray();
        float top=headPoints.Length>0?headPoints.Max(h=>h.Anchor.Y)+height/18:floor+height;
        overheadCenter=new(center.X,top+.12f+(.95f*330/1400)/2,center.Z);hasRobotView=true;
    }
    internal FollowMovementTarget MovementTarget {get;private set;}=FollowMovementTarget.Robot;
    internal bool MoveMe=>MovementTarget==FollowMovementTarget.Viewer;
    private bool leftAHeld;
    internal Action<FollowToggle>? ToggleFeedback;
    internal bool SelectionsOpen {get;set;}
    private float? laserLowerTarget;
    internal bool CanFireLaser=>Following&&!SelectionsOpen&&CanFireLaserAt(LatestPose)&&(!laserLowerTarget.HasValue||laserLowerTarget.Value>=(float)BodyControlCatalog.LaserLowerMaximum-.001f);
    internal static bool CanFireLaserAt(VrPoseState? pose)
    {
        var h=pose?.Handles.FirstOrDefault(h=>h.Key=="Servo:B_LaserLower");
        return pose?.Accessory==1&&h!=null&&h.Maximum>h.Minimum&&float.IsFinite(h.Value)&&h.Value>=h.Maximum-Math.Max(.1f,(h.Maximum-h.Minimum)*.02f);
    }
    internal void ToggleLaser()
    {
        if(!Following||LatestPose is not {Accessory:1} pose)return;
        var control=pose.Handles.FirstOrDefault(h=>h.Key=="Servo:B_LaserLower");if(control==null)return;
        float maximum=(float)ServoAnimator.BodyControlCatalog.LaserLowerMaximum;
        bool lowered=laserLowerTarget.HasValue?laserLowerTarget.Value>maximum/2:control.Value>(control.Minimum+control.Maximum)/2;
        laserLowerTarget=lowered?0:maximum;
    }
    internal void FollowButtons(ControllerReading input,bool enabled)
    {
        if(enabled)
        {
            if(input.LeftA&&!leftAHeld){mirrorFacing=!mirrorFacing;InputTuning.Filter.Reset();ToggleFeedback?.Invoke(mirrorFacing?FollowToggle.Mirrored:FollowToggle.Unmirrored);}

        }
        leftAHeld=input.LeftA;
    }
    internal void ToggleHandMode()
    {
        Hands.ToggleMode(result?.Targets,mirrorFacing);
        ToggleFeedback?.Invoke(Hands.Manual?FollowToggle.Hand:FollowToggle.Movement);
    }
    private string config="",path="",token="";
    private readonly HashSet<string> sentIds=[];
    private readonly Queue<string> sentOrder=new();
    private BodyTrackingCalibration? profile;
    private BodyCalibrationWizard? wizard;
    private BodyTrackingSolver? solver;
    private BodyTrackingResult? result;
    private BodyTrackingFrame? current;
    private Task? save;
    private float sendElapsed,invalidElapsed;
    private bool backDown,awaitRelease,waitingStart;
    private long showUntil,startDeadline,editorWaitDeadline;
    // Right after the VR Editor opens it can briefly say it can't be posed (a loading
    // window, playback finishing). Follow waits this long at the start instead of stopping.
    internal const int EditorWaitMs=10000;
    private bool editorWaitLogged;
    // Raw tracking is recorded while following so a session can be replayed offline.
    private TrackingRecorder? recorder;
    private int loggedStep;private string loggedRejection="";
    internal string? RecordingPath=>recorder?.Path;
    // A Follow problem (tracking interruption or a stop you didn't ask for) shows the
    // overhead status panel even when "Show overhead Follow status" is off.
    internal const int ProblemShowMs=8000;
    private long problemUntil,dropoutStart;
    private bool inDropout;
    private string dropoutDetail="";
    internal string ProblemText {get;private set;}="";
    internal bool ProblemShowing=>Environment.TickCount64<problemUntil;
    private void Problem(string text)
    {
        ProblemText=DateTime.Now.ToString("HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)+" · "+text;
        problemUntil=Environment.TickCount64+ProblemShowMs;nextPaint=0;
    }
    internal Action<VrMotionBatch>? Send;
    internal Action<VrRecordingRequest>? SendRecording;
    internal VrRecordingTracks RecordingTracks {get;set;}=VrRecordingTracks.Body;
    private VrRecordingAction? startRecording;
    internal Func<long> CountdownClock=()=>Environment.TickCount64;
    private long countdownUntil;
    private int countdownLength;
    internal bool RecordingPending=>startRecording.HasValue||recordingRequested;
    internal int Countdown=>countdownUntil==0?0:(int)Math.Max(0,(countdownUntil-CountdownClock()+999)/1000);
    private string recordingRequest="";
    private bool recordingRequested,recordingActive;
    private long recordingDeadline;
    internal VrPoseState? LatestPose {get;private set;}
    internal void StartRecording(bool overdub,int countdownSeconds=0)
    {
        if(SendRecording==null)throw new InvalidOperationException("Open the matching updated VR Editor first.");
        if(LatestPose?.HasTake==true)throw new InvalidOperationException("Keep or discard the previous take first.");
        if(RecordingTracks==0)throw new InvalidOperationException("Arm at least one control group.");
        StartFollowing(preserveHands:Following);countdownLength=Math.Clamp(countdownSeconds,0,10);countdownUntil=0;startRecording=overdub?VrRecordingAction.Overdub:VrRecordingAction.Record;
        // All three neck axes stay off together for overdub unless explicitly armed.
        Status="Stand upright, arms down; recording starts after tracking is acquired and the trigger is released.";
    }
    internal void RecordingAction(VrRecordingAction action)
    {
        var pose=LatestPose??throw new InvalidOperationException("Waiting for the editor state.");
        SendRecording?.Invoke(new(Guid.NewGuid().ToString(),pose.Token,action,RecordingTracks));
    }
    internal bool ShowFollowingStatus {get;set;}
    // When off (the default) the tracked waist swivel is not sent, so the editor keeps the
    // timeline's waist swivel and a kept recording never replaces it.
    internal bool TrackWaistSwivel {get;set;}
    internal bool Following {get;private set;}
    internal bool Active=>Following||wizard!=null;
    internal bool HasCalibration=>profile?.Valid()==true;
    internal string TriggerStatus {get;private set;}="Waiting for controller input";
    internal string Status {get;private set;}="Motion following is off";
    internal BodyCalibrationWizard? Wizard=>wizard;
    internal VrBodyMotion(int adapter):this(new SteamVrPanelOverlay(adapter)){}
    internal VrBodyMotion(IPanelOverlay overlay){this.overlay=overlay;}
    internal void Configure(string folder)
    {
        if(config==folder)return;Stop("Config folder changed");config=folder;Speeds.Load(folder);Neutrals.Load(folder);InputTuning.Load(folder);path=Path.Combine(config,"VR","BodyTrackingCalibration.json");profile=null;
        if(File.Exists(path))try{profile=BodyTrackingCalibration.Load(path);Status=profile.ArmCalibrationVersion==0?"Saved calibration loaded — recalibrate for separate wrist axes / arm twist":"Saved body calibration loaded";}catch(Exception ex){Status=ex.Message;}
    }
    internal void Calibrate()
    {
        if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Open the VR Editor and select a Config folder first.");
        if(save is {IsCompleted:false})throw new InvalidOperationException("Calibration is still saving.");
        calibrationWorld=null;
        Following=false;solver=null;wizard=new();result=null;save=null;awaitRelease=true;Status="Body calibration · measured hands, estimated elbows";
        // Calibration attempts are recorded and every accepted or rejected step is logged, so a
        // calibration that won't pass can be looked at afterwards.
        recorder?.Dispose();recorder=null;try{recorder=new(Diagnostics.Folder,"tracking-calibration");}catch(Exception ex){Diagnostics.Log(ex);}
        loggedStep=0;loggedRejection="";BridgeTiming.Event("Calibration started");
    }
    private void LogCalibration(BodyCalibrationWizard w)
    {
        if(w.Step!=loggedStep)
        {
            if(w.Step>loggedStep)BridgeTiming.Event($"Calibration step {loggedStep+1} accepted: {w.Feedback}");
            else BridgeTiming.Event($"Calibration back to step {w.Step+1}");
            loggedStep=w.Step;loggedRejection="";
        }
        if(w.LastRejection.Length>0&&w.LastRejection!=loggedRejection)
        {
            loggedRejection=w.LastRejection;
            BridgeTiming.Event($"Calibration step {w.Step+1} rejected: {w.LastRejection}"+(w.LiveGauge!=null?" · "+w.LiveGauge.Summary():""));
        }
        if(w.Complete&&recorder!=null){BridgeTiming.Event("Calibration complete");recorder.Dispose();recorder=null;}
    }
    internal void Previous(){if(save is {IsCompleted:false})throw new InvalidOperationException("Wait for the calibration to finish saving.");wizard?.Back();save=null;awaitRelease=true;}
    internal void StartFollowing()=>StartFollowing(preserveHands:false);
    private void StartFollowing(bool preserveHands)
    {
        if(!HasCalibration)throw new InvalidOperationException("Complete body calibration first.");
        if(save is {IsCompleted:false})throw new InvalidOperationException("Wait for calibration to finish saving.");
        startRecording=null;recordingRequested=recordingActive=false;laserLowerTarget=null;SelectionsOpen=false;
        sentIds.Clear();sentOrder.Clear();if(!preserveHands)Hands.Reset();wizard=null;solver=new(profile!);Following=true;waitingStart=true;startDeadline=Environment.TickCount64+5000;editorWaitDeadline=Environment.TickCount64+EditorWaitMs;editorWaitLogged=false;invalidElapsed=0;awaitRelease=true;
        InputTuning.Filter.Reset();inDropout=false;BridgeTiming.FollowStarted();
        recorder?.Dispose();recorder=null;try{recorder=new(Diagnostics.Folder);}catch(Exception ex){Diagnostics.Log(ex);}
        Status="Stand upright, arms relaxed down, facing forward; acquiring tracking and current robot placement…";
    }
    // byUser: B button, transport Stop or inserting a pose. Any other stop while
    // following or recording is a problem and pops up the overhead panel.
    internal void Stop(string message="Motion following stopped",bool byUser=false)
    {
        if(Following)BridgeTiming.FollowStopped(message);
        if((Following||recordingActive||recordingRequested)&&!byUser)Problem("Follow stopped: "+message);
        if(recordingActive||recordingRequested){RecordingAction(VrRecordingAction.Stop);recordingActive=recordingRequested=false;}
        startRecording=null;countdownUntil=0;Following=false;wizard=null;solver=null;waitingStart=false;result=null;Status=message;showUntil=Environment.TickCount64+4000;
        recorder?.Dispose();recorder=null;
    }
    internal string TrackingDetail {get;set;}="Controller/headset tracking unavailable";
    internal void TrackingLost(float seconds)
    {
        invalidElapsed+=Math.Clamp(seconds,0,.05f);
        if(!Following)return;
        if(!inDropout){inDropout=true;dropoutStart=Environment.TickCount64;dropoutDetail=TrackingDetail;Problem("Tracking interrupted — robot pose held · "+TrackingDetail);}
        Status="Tracking interrupted — robot pose held · "+TrackingDetail;
        if(invalidElapsed>.15f)Stop("Tracking lost — pose held. "+TrackingDetail+". Re-center and start following again.");
    }
    internal void Step(BodyTrackingFrame frame,VrPoseState? pose,ControllerReading input,bool allowed,float seconds,string triggerSource="controller",string inputBlockReason="")
    {
        if(wizard!=null)wizard.TrackingHint=TrackingDetail;
        Speeds.Tick();Neutrals.Tick();InputTuning.Tick();current=frame;LatestPose=pose;
        if((Following||wizard is {Complete:false})&&recorder!=null)try{recorder.Record(frame,seconds);}catch(IOException ex){Diagnostics.Log(ex);recorder.Dispose();recorder=null;}TriggerStatus=$"Trigger: {(input.Select?"pressed":"released")} · {triggerSource}"+(!allowed?" · Paused: "+(inputBlockReason.Length>0?inputBlockReason:"menu / input focus"):awaitRelease?" · Release trigger first":"");
        bool back=input.Back&&!backDown;backDown=input.Back;
        if(back&&Active){Stop("B button pressed — body capture stopped, robot pose held",byUser:true);return;}
        if(awaitRelease){if(!input.Select)awaitRelease=false;}
        if(!frame.TrackingValid){TrackingLost(seconds);if(wizard!=null)wizard.Update(frame,seconds,false);return;}invalidElapsed=0;
        if(inDropout)
        {
            InputTuning.Filter.Reset();inDropout=false;long ms=Environment.TickCount64-dropoutStart;
            Problem((Following?"Tracking back after ":"Tracking back (Follow had stopped) after ")+ms+" ms · was: "+dropoutDetail);
        }
        if(wizard!=null)
        {
            if(allowed)wizard.Update(frame,seconds,input.Select&&!awaitRelease);else wizard.Observe(frame);
            LogCalibration(wizard);
            if(wizard.Complete&&save==null)
            {
                profile=wizard.Profile.DisplaySnapshot();var candidate=profile;
                save=Task.Run(()=>candidate.Save(path));Status="Saving body calibration…";
            }
            if(save?.IsCompleted==true)Status=save.IsCompletedSuccessfully?"Calibration saved. Finger ranges: L "+profile!.MeasuredFingers(true)+"/3, R "+profile.MeasuredFingers(false)+"/3. A: Follow my movements. B: close.":"Calibration could not save: "+save.Exception?.GetBaseException().Message;
            return;
        }
        if(!Following)return;
        if(pose!=null&&sentIds.Contains(pose.LastRequest)&&pose.Message.StartsWith("Body capture error:",StringComparison.Ordinal)){Stop(pose.Message);return;}
        if(recordingRequested)
        {
            if(pose?.Recording==true){recordingRequested=false;recordingActive=true;token=pose.Token;}
            else if(pose?.LastRequest==recordingRequest&&pose.RecordingMessage.StartsWith("Recording error:",StringComparison.Ordinal)){recordingRequested=false;Stop(pose.RecordingMessage);return;}
            else if(Environment.TickCount64>recordingDeadline){Stop("Recording did not start. Check the editor, then try again.");return;}
            else{Status="Starting recording…";return;}
        }
        if(recordingActive&&pose?.Recording!=true){recordingActive=false;Stop(pose?.RecordingMessage??"Recording stopped. Review the take.");return;}
        if(pose is not {Editable:true})
        {
            if(waitingStart&&Environment.TickCount64<editorWaitDeadline)
            {
                if(!editorWaitLogged){editorWaitLogged=true;BridgeTiming.Event("Follow waiting for the editor (playback or a dialog)");}
                Status="Starting follow — waiting for the editor. Pause playback and close its dialogs.";return;
            }
            Stop("Following paused: pause playback and close dialogs, then start again.");return;
        }
        if(!allowed)
        {
            if(SelectionsOpen){Status="Following held · Selections open · close with Left B to resume";return;}
            // The menu reports a consumed interaction on the same frame that its
            // Follow button closes it. Do not treat that startup frame as a pause.
            if(waitingStart&&Environment.TickCount64<startDeadline)
            {Status="Starting follow — waiting for the menu to close / VR input focus. Release the trigger.";return;}
            Stop("Following paused: "+(inputBlockReason.Length>0?inputBlockReason:"menu/dashboard")+". Stand upright and choose Follow / re-center to resume.");return;
        }
        if(awaitRelease){Status="Starting follow — release the trigger, standing upright with arms down.";return;}
        if(waitingStart)
        {
            if(!pose.Handles.Any(h=>h.Key=="Servo:B_ShoulderRotateLeft")||!pose.Handles.Any(h=>h.Key=="Servo:B_ShoulderRotateRight")){Stop("Choose a full-body robot URDF before following body movements.");return;}
            float Value(string name)=>pose.Handles.FirstOrDefault(h=>h.Key=="Servo:"+name)?.Value??0;
            solver!.Recenter(frame,Value("URDFStageX"),Value("URDFStageY"),Value("URDFStageHeading"));NeutralMapper.Capture(solver.Solve(frame,0));token=pose.Token;waitingStart=false;sendElapsed=0;
        }
        if(startRecording is {} action)
        {
            if(token!=pose.Token){Stop("Timeline changed during recording setup. Start again.");return;}
            if(countdownLength>0)
            {
                if(countdownUntil==0)countdownUntil=CountdownClock()+countdownLength*1000;
                if(Countdown>0){Status=$"Recording starts in {Countdown} · Right B cancels";return;}
            }
            countdownUntil=0;startRecording=null;recordingRequest=Guid.NewGuid().ToString();recordingRequested=true;recordingDeadline=Environment.TickCount64+5000;
            SendRecording?.Invoke(new(recordingRequest,pose.Token,action,RecordingTracks));return;
        }
        if(token!=pose.Token){Stop("Timeline/cursor changed — following stopped to protect the new editing context.");return;}
        var drive=TuningSpeeds||Hands.Manual?(Forward:0f,Turn:0f):FollowStickRouting.Robot(input);
        solver!.Drive(Locomotion.DeadZone(drive.Forward)*Speeds.Get("URDFStageTravel"),Locomotion.DeadZone(drive.Turn)*Speeds.Get("URDFStageHeading"),seconds);
        solver!.MirrorFacing=mirrorFacing;
        // Suppress tracked curls and their automatic point/wrist assist in manual or held mode.
        var solveFrame=Hands.OverrideFingers?frame with{LeftFingers=null,RightFingers=null}:frame;
        result=NeutralMapper.Apply(solver.Solve(solveFrame,seconds,InputTuning.Filter),mirrorFacing,Neutrals.Values,InputTuning.Filter,seconds);
        Hands.Apply(input,!TuningSpeeds,mirrorFacing,seconds,result.Targets,
            control=>Neutrals.Values.GetValueOrDefault(control.Key,control.Initial));
        if(!TrackWaistSwivel)result.Targets.Remove(nameof(ServoNames.B_WaistSwivel));
        if(pose.Accessory!=1)laserLowerTarget=null;
        if(laserLowerTarget is {} laser)result.Targets["B_LaserLower"]=laser;
        Status=recordingActive?pose.RecordingMessage:"Following · "+(mirrorFacing?"Mirrored":"Unmirrored")+" · "+(Hands.Manual?"Hand":"Movement")+" · "+result.Confidence+" · B: stop";
        // Offer the latest targets every frame. The bridge keeps only the newest and sends
        // it just before each scene request, so every scene moves by one even step.
        sendElapsed+=Math.Clamp(seconds,0,.05f);
        if(Send!=null)
        {
            string id=Guid.NewGuid().ToString();sentIds.Add(id);sentOrder.Enqueue(id);
            while(sentOrder.Count>512)sentIds.Remove(sentOrder.Dequeue());
            Send(new(id,token,sendElapsed,profile!.MaximumJointDegreesPerSecond,result.Targets){Speeds=new(Speeds.Values)});sendElapsed=0;
        }
    }
    internal void Draw(Matrix4x4 head,bool visible=true)
    {
        bool problem=ProblemShowing&&wizard==null;
        if(!visible||!problem&&((wizard==null&&!ShowFollowingStatus&&!recordingActive&&!recordingRequested)||(!Active&&Environment.TickCount64>showUntil))){if(panel!=0)overlay.HideOverlay(panel);return;}
        if(panel==0)
        {
            Check(overlay.CreateOverlay("local.johnny5vr.body."+Environment.ProcessId,"Body calibration and following",ref panel));
            Check(overlay.SetOverlayInputMethod(panel,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(panel,8));
        }
        bool large=wizard!=null;
        if(painting is {IsCompleted:true})
        {
            var pending=painting;painting=null;var image=pending.GetAwaiter().GetResult();
            var pin=GCHandle.Alloc(image.Pixels,GCHandleType.Pinned);
            try{Check(overlay.SetOverlayRaw(panel,pin.AddrOfPinnedObject(),(uint)image.Width,(uint)image.Height,4));}finally{pin.Free();}
            panelReady=true;panelLarge=image.Height>400;
        }
        if(painting==null&&Environment.TickCount64>=nextPaint)
        {
            nextPaint=Environment.TickCount64+100;
            var displayWizard=wizard?.DisplaySnapshot();var displayProfile=profile?.DisplaySnapshot();var displayCurrent=current;var displayResult=result;string status=Status,triggerStatus=TriggerStatus,problemText=problem?ProblemText:"";
            painting=Task.Run(()=>
            {
                using var bitmap=DrawPanel(displayWizard,displayProfile,displayCurrent,displayResult,status,triggerStatus,problemText);
                var data=bitmap.LockBits(new(0,0,bitmap.Width,bitmap.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] pixels=new byte[bitmap.Width*bitmap.Height*4];
                try{for(int y=0;y<bitmap.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,pixels,y*bitmap.Width*4,bitmap.Width*4);}finally{bitmap.UnlockBits(data);}
                for(int i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);
                return(pixels,bitmap.Width,bitmap.Height);
            });
        }
        if(!panelReady)return;
        large=panelLarge;Check(overlay.SetOverlayWidthInMeters(panel,large?1.25f:.95f));
        var world=DisplayWorld(head,large);
        var native=EditorVrPanel.Native(world);Check(overlay.SetOverlayTransformAbsolute(panel,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(panel));
    }
    internal Matrix4x4 DisplayWorld(Matrix4x4 head,bool large)
    {
        if(!large)
        {
            if(!hasRobotView)return PanelWorld(head,false);
            var direction=overheadCenter-virtualViewer;direction.Y=0;if(direction.LengthSquared()<.001f)direction=-Vector3.UnitZ;
            var world=Matrix4x4.CreateWorld(overheadCenter,Vector3.Normalize(direction),Vector3.UnitY);
            return Matrix4x4.Invert(viewerOrigin,out var inverse)?world*inverse:world;
        }
        calibrationWorld??=PanelWorld(head,true);
        return calibrationWorld.Value;
    }
    internal static Matrix4x4 PanelWorld(Matrix4x4 head,bool large)
    {
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,head);forward.Y=0;if(forward.LengthSquared()<.001f)forward=-Vector3.UnitZ;forward=Vector3.Normalize(forward);
        float down=large?.05f+(1.25f*940/1400)/3:.45f;
        return Matrix4x4.CreateWorld(head.Translation+forward*(large?1.65f:1.1f)-Vector3.UnitY*down,forward,Vector3.UnitY);
    }
    internal static Bitmap DrawPanel(BodyCalibrationWizard? wizard,BodyTrackingCalibration? profile,BodyTrackingFrame? current,BodyTrackingResult? result,string status,string triggerStatus="",string problem="")
    {
        if(wizard!=null)return DrawCalibrationAlignmentPanel(wizard,current,status,triggerStatus);
        var bitmap=new Bitmap(1400,330);using var g=Graphics.FromImage(bitmap);g.Clear(Color.FromArgb(248,17,25,37));
        using var title=new Font("Segoe UI",42,FontStyle.Bold,GraphicsUnit.Pixel);using var text=new Font("Segoe UI",28,FontStyle.Regular,GraphicsUnit.Pixel);using var small=new Font("Segoe UI",24,FontStyle.Regular,GraphicsUnit.Pixel);
        if(problem.Length>0)
        {
            g.Clear(Color.FromArgb(250,110,16,16));using var border=new Pen(Color.Gold,8);g.DrawRectangle(border,4,4,1392,322);
            g.DrawString("⚠ Follow problem",title,Brushes.White,new RectangleF(30,20,1340,70));g.DrawString(problem,text,Brushes.Gold,new RectangleF(30,90,1340,150));
            if(!problem.Contains(status,StringComparison.Ordinal))g.DrawString("Now: "+status,small,Brushes.White,new RectangleF(30,245,1340,70));return bitmap;
        }
        g.DrawString("Johnny 5 · Body following",title,Brushes.White,new RectangleF(30,20,1340,110));g.DrawString(status,text,Brushes.LightCyan,new RectangleF(30,125,1340,105));
        g.DrawString(result==null?"Stand upright before re-centering. Physical robot output is not enabled.":$"Elbows estimated: L {result.Left.ElbowDegrees:F0}° · R {result.Right.ElbowDegrees:F0}°   Crouch {result.Crouch:P0} · Lean {result.Lean:P0}"+(result.Left.Pointing||result.Right.Pointing?$"   Pointing: {(result.Left.Pointing&&result.Right.Pointing?"both hands":result.Left.Pointing?"your left hand":"your right hand")}":""),small,Brushes.LightGray,30,245);
        return bitmap;
    }
    private static void Check(EVROverlayError error){if(error!=EVROverlayError.None)throw new InvalidOperationException("Body capture overlay: "+error);}
    public void Dispose(){recorder?.Dispose();recorder=null;Speeds.Flush();Neutrals.Flush();InputTuning.Flush();if(panel!=0)overlay.DestroyOverlay(panel);panel=0;overlay.Dispose();}
}
