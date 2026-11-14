using System.Numerics;
using Valve.VR;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Johnny5.VrBridge;

namespace Johnny5VR;

internal sealed class VrSession:IDisposable
{
    internal CVRSystem System {get;}
    private readonly CVRCompositor compositor;
    private readonly TrackedDevicePose_t[] poses=new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly TrackedDevicePose_t[] gamePoses=new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    internal int Width {get;}
    internal int Height {get;}
    internal int Adapter {get;}
    private bool disposed;
    private readonly Locomotion locomotion=new();
    private ControllerInput? controllers;
    private ViewerSettings navigation=new();
    private long lastFrame;
    private ControllerReading lastInput=ControllerReading.Failed("waiting for controllers");
    private bool inputFocus;
    private long lastInputReport;
    private readonly Matrix4x4[] projections=new Matrix4x4[2],eyeTransforms=new Matrix4x4[2];
    private Task? reportWrite;
    private float refreshRate;
    internal float RefreshRate=>refreshRate;
    private readonly Compositor_FrameTiming[] frameTimings=new Compositor_FrameTiming[32];
    private uint lastTimedFrame;
    private long lastTimingSample;
    internal VrTransportPanel Transport {get;private set;}=null!;
    internal VrMenu? Menu {get;private set;}
    internal EditorVrPanel? EditorPanel {get;private set;}
    internal readonly RobotPoseInteraction RobotPose=new();
    private PoseHandleOverlay? poseLabels;
    private FollowControllerLabels? followLabels;
    private VrBodyMotion? bodyMotion;
    internal VrBodyMotion BodyMotion=>bodyMotion??=new(Adapter){ShowFollowingStatus=navigation.ShowFollowStatus,TrackWaistSwivel=navigation.TrackWaistSwivel};
    private readonly TrackedFingerInput trackedFingers=new();
    internal GloveHand[] GloveHands {get;private set;}=[];
    internal (Vector3 Start,Vector3 End)[] PointerRays {get;private set;}=[];
    internal Action? ToggleFollowSelections;
    internal bool ArcadeActive;
    internal ArcadeTeleport? ArcadeTeleport;
    internal Matrix4x4 ArcadeViewOrigin=>locomotion.Origin;
    internal void RestoreArcadeView(Matrix4x4 origin)=>locomotion.Restore(origin);
    internal void TeleportArcade(Matrix4x4 head,Vector3 feet,Vector3 lookAt)=>locomotion.Teleport(head,feet,lookAt);
    internal Vector3 ArcadeLookAt;
    internal Action<ControllerReading,bool,float,Matrix4x4,Matrix4x4>? ArcadeFrame;
    private bool followBHeld,followStickHeld,leftFireHeld;
    private Action<VrEditorCommand>? editorCommand;
    internal void AttachEditor(int process,Action<VrEditorCommand>? command=null,Action<VrEditorNavigation>? navigate=null,Func<EditorDesktopFrame?>? capture=null,Action<VrPoseRequest>? pose=null,Action<VrMotionBatch>? motion=null,string? config=null,Action<VrRecordingRequest>? recording=null)
    {
        EditorPanel?.Dispose();EditorPanel=new(process,new SteamVrPanelOverlay(Adapter),true){Command=command,Navigate=navigate,GrabFeedback=PanelHaptic,CaptureSource=capture};
        editorCommand=command;RobotPose.Send=pose;RobotPose.Feedback=PanelHaptic;BodyMotion.Send=motion;BodyMotion.SendRecording=recording;if(config!=null)BodyMotion.Configure(config);
    }
    private void PanelHaptic()
    {
        try
        {
            uint device=System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if(device==OpenVR.k_unTrackedDeviceIndexInvalid)device=System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
            if(device!=OpenVR.k_unTrackedDeviceIndexInvalid)System.TriggerHapticPulse(device,0,800);
        }
        catch{/* Feedback must never interrupt a grab. */}
    }
    internal void DetachEditor(){editorCommand=null;bodyMotion?.Stop("Editor disconnected");if(bodyMotion!=null)bodyMotion.Send=null;EditorPanel?.Dispose();EditorPanel=null;RobotPose.Enabled=false;RobotPose.State=null;RobotPose.Send=null;poseLabels?.Dispose();poseLabels=null;}
    internal string TimingStatus {get;private set;}="";
    internal string InputStatus {get;private set;}="Index controllers: waiting";
    internal string InputDetails=> $"{controllers?.Mode}: Left ({lastInput.Move.X:+0.00;-0.00;0.00}, {lastInput.Move.Y:+0.00;-0.00;0.00}) [{lastInput.LeftStatus}] · Right ({lastInput.Right.X:+0.00;-0.00;0.00}, {lastInput.Right.Y:+0.00;-0.00;0.00}) [{lastInput.RightStatus}]\n"+
        $"Movement {(navigation.ControllerLocomotion?"on":"off")} · Input focus {(inputFocus?"yes":"no")} · Virtual offset {locomotion.Origin.Translation.X:F2}, {locomotion.Origin.Translation.Z:F2} m"+
        (ArcadeActive?" · Laser Range: Left drive/turn · Right Y aim · Trigger fire":"")+(bodyMotion?.Following==true?" · Left stick: Robot · Right stick: Me":"")+(lastInput.Error!=""?" · "+lastInput.Error:"")+(EditorPanel?.Error is {Length:>0} error?" · Editor panel: "+error:"")+(Menu?.Error is {Length:>0} menuError?" · "+menuError:"");
    internal void Configure(ViewerSettings settings){navigation=settings;if(Transport!=null)Transport.Enabled=settings.ShowTransport;if(bodyMotion!=null){bodyMotion.ShowFollowingStatus=settings.ShowFollowStatus;bodyMotion.TrackWaistSwivel=settings.TrackWaistSwivel;}}
    internal static bool BodyInputAllowed(bool focus,bool menuVisible,bool otherInteraction,bool calibration)
        =>focus&&!menuVisible&&(calibration||!otherInteraction);
    internal void ResetPosition()=>locomotion.Reset();
    internal VrSession(ViewerSettings? initial=null,bool? useBindings=null)
    {
        navigation=initial??new();
        if(!OpenVR.IsRuntimeInstalled())throw new InvalidOperationException("SteamVR is not installed. Install SteamVR through Steam, run its room setup, then connect your headset.");
        EVRInitError error=EVRInitError.None;
        var system=OpenVR.Init(ref error,EVRApplicationType.VRApplication_Scene);
        if(error!=EVRInitError.None||system==null)throw new InvalidOperationException("SteamVR could not start: "+OpenVR.GetStringForHmdError(error));
        System=system;
        try
        {
            compositor=OpenVR.Compositor??throw new InvalidOperationException("SteamVR's compositor is unavailable. Start SteamVR and try again.");
            compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
            uint w=0,h=0;System.GetRecommendedRenderTargetSize(ref w,ref h);
            if(w==0||h==0||w>8192||h>8192)throw new InvalidOperationException("SteamVR returned an invalid render size.");
            Width=(int)w;Height=(int)h;int adapter=-1;System.GetDXGIOutputInfo(ref adapter);Adapter=Math.Max(0,adapter);
            var propertyError=ETrackedPropertyError.TrackedProp_Success;
            refreshRate=System.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,ETrackedDeviceProperty.Prop_DisplayFrequency_Float,ref propertyError);
            RefreshCameras();
            // Legacy/direct reads and action manifests cannot coexist within an
            // OpenVR session. Choose the backend before making any input calls.
            controllers=new ControllerInput(System,useBindings??navigation.UseSteamVrBindings);
            Menu=new VrMenu(Adapter);Transport=new VrTransportPanel(Adapter){Enabled=navigation.ShowTransport};
        }
        catch{OpenVR.Shutdown();throw;}
    }
    internal bool UpdatePose(out Matrix4x4 head)
    {
        PointerRays=[];GloveHands=[];
        var ev=new VREvent_t();
        bool calibrationChanged=false;
        while(System.PollNextEvent(ref ev,(uint)global::System.Runtime.InteropServices.Marshal.SizeOf<VREvent_t>()))
        {
            if((EVREventType)ev.eventType is EVREventType.VREvent_Quit or EVREventType.VREvent_ProcessQuit)
            {head=Matrix4x4.Identity;throw new InvalidOperationException("SteamVR ended the VR session.");}
            if((EVREventType)ev.eventType is EVREventType.VREvent_IpdChanged or EVREventType.VREvent_LensDistortionChanged)calibrationChanged=true;
        }
        if(calibrationChanged)RefreshCameras();
        var waitStart=BridgeTiming.Start();
        var error=compositor.WaitGetPoses(poses,gamePoses);
        BridgeTiming.Stop(BridgeTiming.Section.WaitPoses,waitStart);
        long now=Stopwatch.GetTimestamp();float seconds=lastFrame==0?0:(float)((now-lastFrame)/(double)Stopwatch.Frequency);lastFrame=now;
        head=Matrix4x4.Identity;
        if(error!=EVRCompositorError.None)throw new InvalidOperationException("SteamVR tracking: "+error);
        uint leftId=System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand),rightId=System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
        bool Valid(uint id)=>id<poses.Length&&poses[id].bPoseIsValid;
        bool hmdValid=Valid(OpenVR.k_unTrackedDeviceIndex_Hmd),leftValid=Valid(leftId),rightValid=Valid(rightId);
        bool focus=System.IsInputAvailable()&&!(OpenVR.Overlay?.IsDashboardVisible()??false);inputFocus=focus;
        string Describe(string name,uint id)=>id>=poses.Length?name+": no assigned device":$"{name}: device {id}, connected={poses[id].bDeviceIsConnected}, valid={poses[id].bPoseIsValid}, {poses[id].eTrackingResult}";
        string trackingDetail=hmdValid&&leftValid&&rightValid?"":string.Join("; ",new[]{!hmdValid?Describe("Headset",OpenVR.k_unTrackedDeviceIndex_Hmd):"",!leftValid?Describe("Left",leftId):"",!rightValid?Describe("Right",rightId):""}.Where(x=>x.Length>0));
        BridgeTiming.Tracking(hmdValid,leftValid,rightValid,focus,bodyMotion?.Following==true,trackingDetail);
        if(bodyMotion!=null)bodyMotion.TrackingDetail=trackingDetail;
        if(!hmdValid)
        {Transport.Suspend();followLabels?.Draw(head,null,null,false,true,FollowMovementTarget.Robot);bodyMotion?.TrackingLost(seconds);EditorPanel?.Suspend();RobotPose.Step(head,null,false,false);poseLabels?.Draw(RobotPose,head,Matrix4x4.Identity);locomotion.Step(head,Vector2.Zero,0,0,0,0,false);return false;}
        head=Pose(poses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
        var move=Vector2.Zero;float turn=0;
        if(controllers!=null)
        {
            try
            {
                var state=controllers.Read(bodyMotion?.Active==true||ArcadeActive);lastInput=state;
                if(focus){move=state.Move;turn=state.Right.X;}
                InputStatus=!navigation.ControllerLocomotion?"Controller movement off":!focus?"Movement paused — input focus":state.Active?"Index input active":"Index: waiting for controller data";
            }
            catch(Exception ex){InputStatus=ex.Message;lastInput=ControllerReading.Failed(ex.Message);}
        }
        Matrix4x4? pointer=null;
        var rays=new List<(Vector3 Start,Vector3 End)>();
        foreach(var role in new[]{ETrackedControllerRole.RightHand,ETrackedControllerRole.LeftHand})
        {
            uint device=System.GetTrackedDeviceIndexForControllerRole(role);
            if(device<poses.Length&&poses[device].bPoseIsValid)
            {
                var pose=Pose(poses[device].mDeviceToAbsoluteTracking);
                if(ControllerRays.FromPose(pose,locomotion.Origin) is {} ray){pointer??=pose;rays.Add(ray);}
            }
        }
        Matrix4x4? Tracked(ETrackedControllerRole role)
        {
            uint id=System.GetTrackedDeviceIndexForControllerRole(role);
            return Valid(id)?Pose(poses[id].mDeviceToAbsoluteTracking):null;
        }
        var leftHand=Tracked(ETrackedControllerRole.LeftHand);var rightHand=Tracked(ETrackedControllerRole.RightHand);
        float[]? leftCurls=null,rightCurls=null;
        bool wasFollowing=bodyMotion?.Following==true;
        bool selectionsFrame=Menu is {FollowSelections:true,Navigation.Visible:true};
        if(wasFollowing&&focus&&lastInput.LeftB&&!followBHeld&&(Menu?.Navigation.Visible!=true||selectionsFrame))ToggleFollowSelections?.Invoke();
        followBHeld=lastInput.LeftB;
        if(wasFollowing&&focus&&Menu?.Navigation.Visible!=true&&lastInput.LeftStickClick&&!followStickHeld)bodyMotion?.ToggleLaser();
        followStickHeld=lastInput.LeftStickClick;
        if(wasFollowing&&focus&&leftHand.HasValue&&Menu?.Navigation.Visible!=true&&lastInput.LeftTrigger&&!leftFireHeld&&bodyMotion?.CanFireLaser==true)
            editorCommand?.Invoke(VrEditorCommand.PreviewFireLaser);
        leftFireHeld=lastInput.LeftTrigger;
        bodyMotion?.FollowButtons(lastInput,wasFollowing&&focus&&Menu?.Navigation.Visible!=true);
        if(wasFollowing){(move,turn)=FollowStickRouting.Viewer(lastInput,true);lastInput=lastInput.ForFollowing();}
        if(ArcadeActive&&ArcadeTeleport!=null)
        {
            Matrix4x4? hand=rightHand.HasValue?rightHand.Value*locomotion.Origin:null;
            lastInput=ArcadeTeleport.Route(lastInput,focus&&rightValid,Menu?.Navigation.Visible==true,seconds,hand);
            if(ArcadeTeleport.Commit is {} destination)locomotion.Teleport(head,destination.Centre,ArcadeLookAt);
        }
        var menuStart=BridgeTiming.Start();
        bool menuActive=Menu?.Step(lastInput,focus,head,pointer)??false;
        if(ArcadeActive){move=Vector2.Zero;turn=0;}
        BridgeTiming.Stop(BridgeTiming.Section.Menu,menuStart);
        selectionsFrame|=Menu is {FollowSelections:true,Navigation.Visible:true};
        if(bodyMotion!=null){bodyMotion.SelectionsOpen=selectionsFrame;bodyMotion.TuningSpeeds=Menu is {LiveFollowing:true,Navigation.Visible:true};}
        menuActive|=Transport.Step(head,locomotion.Origin,pointer,lastInput,focus&&Menu?.Navigation.Visible!=true&&!ArcadeActive,focus&&!menuActive&&bodyMotion?.Wizard==null&&!ArcadeActive);
        if(EditorPanel!=null)
        {
            var panelStart=BridgeTiming.Start();
            menuActive|=EditorPanel.Step(head,locomotion.Origin,pointer,lastInput,focus,menuActive||RobotPose.Grabbing||bodyMotion?.Active==true,seconds);
            BridgeTiming.Stop(BridgeTiming.Section.Panel,panelStart);
        }
        var worldHead=head*locomotion.Origin;Matrix4x4? worldHand=pointer.HasValue?pointer.Value*locomotion.Origin:null;
        menuActive|=RobotPose.Step(worldHead,worldHand,lastInput.Select,focus&&!menuActive&&bodyMotion?.Active!=true,lastInput.Right,seconds);
        if(bodyMotion!=null)
        {
            try
            {
                var fingers=bodyMotion.Active?trackedFingers.Read(controllers?.ActionInput):((float[]?)null,(float[]?)null,"Finger capture inactive",(bool?)null);
                leftCurls=fingers.Item1;rightCurls=fingers.Item2;
                var tracked=new ServoAnimator.BodyTrackingFrame(head,leftHand??Matrix4x4.Identity,rightHand??Matrix4x4.Identity,fingers.Item1,fingers.Item2,leftHand.HasValue&&rightHand.HasValue,fingers.Item3);
                var captureInput=lastInput;
                bool calibration=bodyMotion.Wizard!=null;
                bool bodyAllowed=BodyInputAllowed(focus,Menu?.Navigation.Visible==true,menuActive,calibration);
                string blocked=!focus?"SteamVR dashboard / input focus":Menu?.Navigation.Visible==true?"close VR menu":"another editor interaction";
                bodyMotion.UpdateView(head,locomotion.Origin,RobotPose.State,RobotPose.FloorY,RobotPose.RobotHeight);
                bodyMotion.Step(tracked,RobotPose.State,captureInput,bodyAllowed||(focus&&bodyMotion.TuningSpeeds),seconds,lastInput.ButtonStatus.Length>0?lastInput.ButtonStatus:lastInput.Error.Length>0?"Input error: "+lastInput.Error:controllers?.Mode??"controller input",blocked);bodyMotion.Draw(head,Menu?.Navigation.Visible!=true&&!ArcadeActive);
                menuActive|=bodyMotion.Wizard!=null;
            }
            catch(Exception ex){bodyMotion.Stop("Motion capture paused: "+ex.Message);Diagnostics.Log(ex);}
        }
        try
        {
            if(RobotPose.Enabled||poseLabels!=null)
            {
                poseLabels??=new(Adapter);Matrix4x4.Invert(locomotion.Origin,out var inverseOrigin);poseLabels.Draw(RobotPose,worldHead,inverseOrigin);
            }
        }
        catch(Exception ex){RobotPose.Enabled=false;RobotPose.Step(worldHead,null,false,false);poseLabels?.Dispose();poseLabels=null;InputStatus=ex.Message;Diagnostics.Log(ex);}
        if(rays.Count>0)
        {
            if(Menu?.Pointer is {} menuRay)rays[0]=(Vector3.Transform(menuRay.Start,locomotion.Origin),Vector3.Transform(menuRay.End,locomotion.Origin));
            else if(Transport.Pointer is {} transportRay)rays[0]=transportRay;
            else if(EditorPanel?.Pointer is {} editorRay)rays[0]=editorRay;
            else if(RobotPose.Pointer is {} poseRay)rays[0]=poseRay;
        }
        rays.AddRange(RobotPose.Visible.Where(t=>t.Control.Key==RobotPose.Hovered).Select(t=>(t.Control.Anchor,t.Label)));
        PointerRays=(wasFollowing||ArcadeActive)&&Menu?.Navigation.Visible!=true&&Transport.Pointer==null?[]:rays.ToArray();
        if(ArcadeActive)ArcadeFrame?.Invoke(lastInput,focus&&leftValid&&rightValid&&!menuActive&&Menu?.Navigation.Visible!=true,seconds,head,locomotion.Origin);
        try
        {
            if(wasFollowing||followLabels!=null)
            {
                followLabels??=new(Adapter);
                followLabels.Draw(head,leftHand,rightHand,bodyMotion?.Following==true&&focus,bodyMotion?.MirrorFacing==true,bodyMotion?.MovementTarget??FollowMovementTarget.Robot);
            }
        }
        catch(Exception ex){Diagnostics.Log(ex);followLabels?.Dispose();followLabels=null;}

        locomotion.Step(head,move,turn,seconds,navigation.MoveSpeed,navigation.SnapTurnDegrees,focus&&!menuActive&&navigation.ControllerLocomotion&&controllers!=null,wasFollowing);
        if(bodyMotion?.Following==true&&focus)
        {
            var gloves=new List<GloveHand>(2);if(leftHand.HasValue)gloves.Add(new(true,leftHand.Value*locomotion.Origin,leftCurls));if(rightHand.HasValue)gloves.Add(new(false,rightHand.Value*locomotion.Origin,rightCurls));GloveHands=gloves.ToArray();
        }
        if(Environment.TickCount64-lastInputReport>=1000)
        {
            lastInputReport=Environment.TickCount64;
            QueueDiagnostics();
        }
        return true;
    }
    internal (Matrix4x4 ViewProjection,Vector3 Eye) Camera(EVREye eye,Matrix4x4 head)
    {
        return CameraFromMatrices(eyeTransforms[(int)eye],head,locomotion.Origin,projections[(int)eye]);
    }
    internal (Matrix4x4 ViewProjection,Vector3 Eye) MirrorCamera(Matrix4x4 head,int width,int height)
        =>CameraFromMatrices(Matrix4x4.Identity,head,locomotion.Origin,MirrorProjection(projections[0],width,height));
    internal Matrix4x4 OverlayViewProjection(Matrix4x4 head,int width,int height)
        =>CameraFromMatrices(Matrix4x4.Identity,head,Matrix4x4.Identity,MirrorProjection(projections[0],width,height)).ViewProjection;
    internal static Matrix4x4 MirrorProjection(Matrix4x4 eyeProjection,int width,int height)
    {
        if(width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width),"Mirror dimensions must be positive.");
        float x=Math.Abs(eyeProjection.M11),y=Math.Abs(eyeProjection.M22);
        if(!float.IsFinite(x)||!float.IsFinite(y)||x<=0||y<=0)throw new ArgumentException("Invalid eye projection.",nameof(eyeProjection));
        float aspect=(float)width/height;
        // Keep the calibrated field of view inside a centered flat-screen camera.
        // Crop excess vertical/horizontal coverage instead of distorting the scene.
        if(aspect>=y/x)y=x*aspect;else x=y/aspect;
        var projection=eyeProjection;projection.M11=x;projection.M22=y;
        projection.M31=projection.M32=0;
        return projection;
    }
    internal static (Matrix4x4 ViewProjection,Vector3 Eye) CameraFromMatrices(Matrix4x4 eyeToHead,Matrix4x4 head,Matrix4x4 origin,Matrix4x4 projection)
    {
        var eyeWorld=eyeToHead*head*origin;
        Matrix4x4.Invert(eyeWorld,out var view);
        return (view*projection,eyeWorld.Translation);
    }
    private void RefreshCameras()
    {
        foreach(var eye in new[]{EVREye.Eye_Left,EVREye.Eye_Right})
        {
            // Keep the calibrated asymmetry and eye rotation supplied by the
            // runtime. Rebuilding from raw bounds previously inverted the
            // vertical centre offset for asymmetric frusta.
            projections[(int)eye]=Projection(System.GetProjectionMatrix(eye,.04f,150));
            eyeTransforms[(int)eye]=Pose(System.GetEyeToHeadTransform(eye));
        }
    }
    internal static Matrix4x4 Projection(HmdMatrix44_t m)=>new(
        m.m0,m.m4,m.m8,m.m12,m.m1,m.m5,m.m9,m.m13,
        m.m2,m.m6,m.m10,m.m14,m.m3,m.m7,m.m11,m.m15);
    internal void EndFrame()=>compositor.PostPresentHandoff();
    // Reads SteamVR's per-frame timing a few times a second (32 frames covers
    // 200 ms even at 144 Hz) and records each frame once.
    internal void SampleFrameTiming()
    {
        if(Environment.TickCount64-lastTimingSample<200)return;
        lastTimingSample=Environment.TickCount64;
        try
        {
            for(int i=0;i<frameTimings.Length;i++)frameTimings[i]=new Compositor_FrameTiming{m_nSize=(uint)Marshal.SizeOf<Compositor_FrameTiming>()};
            uint count=compositor.GetFrameTimings(frameTimings);uint newest=lastTimedFrame;
            for(int i=0;i<count&&i<frameTimings.Length;i++)
            {
                var t=frameTimings[i];
                if(t.m_nFrameIndex<=lastTimedFrame)continue;
                BridgeTiming.RecordCompositor(t);newest=Math.Max(newest,t.m_nFrameIndex);
            }
            lastTimedFrame=newest;
        }
        catch(Exception ex){Diagnostics.Log(ex);lastTimingSample=long.MaxValue;}
    }
    private void QueueDiagnostics()
    {
        var timing=new Compositor_FrameTiming{m_nSize=(uint)Marshal.SizeOf<Compositor_FrameTiming>()};
        bool available=compositor.GetFrameTiming(ref timing,0);
        TimingStatus=available?$"GPU {timing.m_flTotalRenderGpuMs:F1} ms · headset {refreshRate:F0} Hz · dropped {timing.m_nNumDroppedFrames} last frame":"Timing unavailable";
        if(reportWrite is {IsCompleted:false})return;
        float[] Values(Matrix4x4 m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        var report=new{
            ProjectionSource="SteamVR GetProjectionMatrix (transposed for row-vector shader)",Width,Height,RefreshHz=refreshRate,
            LeftProjection=Values(projections[0]),RightProjection=Values(projections[1]),
            LeftEyeToHead=Values(eyeTransforms[0]),RightEyeToHead=Values(eyeTransforms[1]),
            TimingAvailable=available,AppGpuMs=timing.m_flTotalRenderGpuMs,FrameIntervalMs=timing.m_flClientFrameIntervalMs,
            DroppedFramesLastFrame=timing.m_nNumDroppedFrames,ReprojectionFlags=timing.m_nReprojectionFlags,
            Frame=timing.m_nFrameIndex
        };
        string input=DateTimeOffset.Now+"\n"+InputDetails+"\n"+TimingStatus;
        // Avoid disk writes between obtaining the predicted pose and submitting
        // its eye images; a slow drive should not stall the tracking frame.
        reportWrite=Task.Run(()=>
        {
            try
            {
                Directory.CreateDirectory(Diagnostics.Folder);
                File.WriteAllText(Path.Combine(Diagnostics.Folder,"controller-input.txt"),input);
                File.WriteAllText(Path.Combine(Diagnostics.Folder,"vr-rendering.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(IOException){ }
            catch(UnauthorizedAccessException){ }
        });
    }
    internal void Submit(EVREye eye,IntPtr texture)
    {
        var image=new Texture_t{handle=texture,eType=ETextureType.DirectX,eColorSpace=EColorSpace.Gamma};
        var bounds=new VRTextureBounds_t{uMin=0,vMin=0,uMax=1,vMax=1};
        var error=compositor.Submit(eye,ref image,ref bounds,EVRSubmitFlags.Submit_Default);
        if(error!=EVRCompositorError.None)throw new InvalidOperationException("SteamVR could not display the "+eye+" image: "+error);
    }
    internal static Matrix4x4 Pose(HmdMatrix34_t m)=>new(
        m.m0,m.m4,m.m8,0,m.m1,m.m5,m.m9,0,m.m2,m.m6,m.m10,0,m.m3,m.m7,m.m11,1);
    public void Dispose(){if(disposed)return;disposed=true;DetachEditor();followLabels?.Dispose();Transport?.Dispose();bodyMotion?.Dispose();bodyMotion=null;Menu?.Dispose();Menu=null;OpenVR.Shutdown();}
}
