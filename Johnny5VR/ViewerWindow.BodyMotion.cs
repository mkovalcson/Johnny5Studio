using Johnny5.VrBridge;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private async Task OpenBodyMotion()
    {
        if(vr==null)return;
        if(!editorMode||!editorReady)await OpenEditor();
        if(editorMode)BuildBodyMotionMenu();
    }
    private Action? recordingMenuParent;
    private void OpenRecordingMenu(Action parent){recordingMenuParent=parent;BuildBodyRecordingMenu();}
    private void BuildBodyRecordingMenu()
    {
        if(vr?.Menu is not {} menu)return;
        var motion=vr.BodyMotion;menu.LivePoseStatus=menu.LivePlaybackStatus=menu.LiveBodyMotionStatus=false;menu.GoBack=recordingMenuParent??BuildBodyMotionMenu;
        void Run(Action action,bool close=false){try{action();if(close)menu.Navigation.Close();else BuildBodyRecordingMenu();}catch(Exception ex){menu.Detail(ex.Message);}}
        menu.Set("Body recording / overdub",[
            new("Start recording from timeline cursor",()=>Run(()=>{vr.RobotPose.Enabled=false;vr.EditorPanel?.Hide();motion.StartRecording(false,3);SetTransportVisible(true);},true)),
            new("Start overdub with sequence audio and motion",()=>Run(()=>{vr.RobotPose.Enabled=false;vr.EditorPanel?.Hide();motion.StartRecording(true,3);SetTransportVisible(true);},true)),
            new("Stop recording — review take",()=>Run(()=>motion.RecordingAction(VrRecordingAction.Stop))),
            new("Replay draft with sequence audio",()=>Run(()=>motion.RecordingAction(VrRecordingAction.Replay),true)),
            new("Keep take on timeline (one Undo)",()=>Run(()=>motion.RecordingAction(VrRecordingAction.Keep))),
            new("Discard take",()=>Run(()=>motion.RecordingAction(VrRecordingAction.Discard))),
            new("Choose recording groups…",BuildRecordingGroupsMenu),
            new("Back to body calibration / following",BuildBodyMotionMenu)],
            (lastEditorFrame?.Pose?.RecordingMessage??motion.Status)+"\nNeck capture defaults OFF; existing face tracking and audio play unchanged. Armed controls replace their recorded interval only when you Keep. B or opening the menu stops for review. Recording runs at 1×, up to 10 minutes; overdub ends at the sequence end. Save the sequence after Keep.");
        menu.LiveBodyRecordingStatus=true;
    }
    private void BuildRecordingGroupsMenu()
    {
        if(vr?.Menu is not {} menu)return;var motion=vr.BodyMotion;menu.GoBack=BuildBodyRecordingMenu;
        string Label(string name,VrRecordingTracks group)=>((motion.RecordingTracks&group)!=0?"☑ ":"☐ ")+name;
        void Toggle(VrRecordingTracks group){motion.RecordingTracks^=group;BuildRecordingGroupsMenu();}
        menu.Set("Record · Capture groups",[
            new(Label("Left arm + wrist",VrRecordingTracks.LeftArm),()=>Toggle(VrRecordingTracks.LeftArm)),
            new(Label("Right arm + wrist",VrRecordingTracks.RightArm),()=>Toggle(VrRecordingTracks.RightArm)),
            new(Label("Left hand / fingers",VrRecordingTracks.LeftHand),()=>Toggle(VrRecordingTracks.LeftHand)),
            new(Label("Right hand / fingers",VrRecordingTracks.RightHand),()=>Toggle(VrRecordingTracks.RightHand)),
            new(Label("Neck lift + chest tilt + waist lean / swivel",VrRecordingTracks.Torso),()=>Toggle(VrRecordingTracks.Torso)),
            new(Label("Neck Turn + Nod + Tilt (all together)",VrRecordingTracks.Neck),()=>Toggle(VrRecordingTracks.Neck)),
            new(Label("Robot travel + heading",VrRecordingTracks.Travel),()=>Toggle(VrRecordingTracks.Travel)),
        ],"Choose which controls the take will capture. Neck Turn/Nod/Tilt remain one group, OFF by default. Unarmed movement and audio are preserved.");
    }
    private void BuildBodyMotionMenu()
    {
        if(vr?.Menu is not {} menu)return;
        var motion=vr.BodyMotion;menu.LivePoseStatus=menu.LivePlaybackStatus=false;menu.GoBack=BuildVrHome;
        void Run(Action action){try{action();}catch(Exception ex){status.Text=ex.Message;menu.Detail(ex.Message);}}
        menu.Set("Body calibration and motion following",[
            new("Start guided calibration · wrists / arm twist",()=>Run(()=>{vr.RobotPose.Enabled=false;vr.EditorPanel?.Hide();motion.Calibrate();menu.Navigation.Close();})),
            new("Repeat previous calibration step",()=>Run(()=>{motion.Previous();menu.Navigation.Close();})),
            new(motion.Following?"Stop following (hold current pose)":"Follow my movements / re-center",()=>Run(()=>{if(motion.Following)motion.Stop();else{vr.RobotPose.Enabled=false;vr.EditorPanel?.Hide();motion.StartFollowing();}menu.Navigation.Close();})),
            new("Record / overdub body movements…",()=>OpenRecordingMenu(BuildBodyMotionMenu)),
            new("Follow settings · Backdrop, status, feedback",BuildFollowSettingsMenu),
            new("Stop / cancel calibration",()=>{motion.Stop();menu.Navigation.Close();}),
            new("Insert changed pose at timeline cursor",()=>Run(()=>{motion.Stop("Following stopped before insertion",byUser:true);vr.RobotPose.Insert();menu.Navigation.Close();})),
            new("Return to calibration / close menu",()=>menu.Navigation.Close()),
            new("Back to editor controls",BuildVrEditorMenu)],
            motion.Status+"\nStand upright before starting/re-centering. Headset and two controllers estimate elbows and body; they do not measure them directly. B stops. No physical robot output. Profile saved under Config/VR. Base speeds use URDF/profile limits; shoulder rotate and elbows use 3× that rate. Left A: mirror toggle. Left B: Selections. Left stick drives/turns the robot; right stick moves/turns you. Left stick click raises/lowers a visible laser; left trigger fires it when lowered. Physical walking/turning never drives robot travel.");
        menu.LiveBodyMotionStatus=true;
    }
}
