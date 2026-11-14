using Johnny5.VrBridge;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private void BuildLibraryMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildVrHome;
        menu.Set("Library",[new("Movies",()=>BrowseVr(true)),new("Sequences",()=>BrowseVr(false))],"Choose a movie or sequence from the selected Config folder. Playback controls are on the Playback page.");
    }
    private void BuildPlaybackMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildVrHome;
        if(editorMode)
            menu.Set("Playback · VR Editor",[
                new("Play / Pause",()=>editor?.Command(VrEditorCommand.PlayPause)),new("Stop and rewind",()=>editor?.Command(VrEditorCommand.Stop)),
                new("Record / overdub…",()=>OpenRecordingMenu(BuildPlaybackMenu)),new("Robot effects",BuildEffectsMenu),
                new("Compact transport panel",()=>{SetTransportVisible(true);menu.Navigation.Close();}),new("Switch to standalone playback",UseStandalone)],"Controls the open VR Editor sequence. The compact panel shows its timeline cursor. Record uses the armed body capture groups.");
        else
        {
            menu.Set("Playback · Standalone",[
                new(player.Running?"Pause":"Play / Resume",()=>PlaybackAction(player.Toggle)),new("Stop and rewind",()=>PlaybackAction(()=>{player.Stop();scene?.Rig?.Reset();})),
                new("Next sequence / advance trigger",()=>PlaybackAction(player.Next)),new("Choose from library",BuildLibraryMenu),new("Robot effects",BuildEffectsMenu),
                new("Playback details",BuildPlaybackDetails),new("Compact transport panel",()=>SetTransportVisible(true))],player.Status+(playbackWarnings!=""?"\n"+playbackWarnings:""));menu.LivePlaybackStatus=true;
        }
    }
    private void BuildPlaybackDetails()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildPlaybackMenu;
        menu.Set("Playback details",[],File.Exists(Path.Combine(Diagnostics.Folder,"playback-report.txt"))?File.ReadAllText(Path.Combine(Diagnostics.Folder,"playback-report.txt")).Truncate(420):"No movie or sequence loaded.");
    }
    private async Task OpenPoseMenu()
    {
        if(!editorMode||!editorReady)await OpenEditor();if(editorMode)BuildPoseMenu();
    }
    private void BuildPoseMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildVrHome;
        menu.Set("Pose · Robot controls",[
            new(vr.RobotPose.Enabled?"Hide robot pose controls":"Pose robot with controllers",()=>{vr.RobotPose.Enabled=!vr.RobotPose.Enabled;if(vr.RobotPose.Enabled)vr.EditorPanel?.Hide();menu.Navigation.Close();}),
            new($"Insert changed pose at cursor ({vr.RobotPose.State?.Cursor:0.000} s)",()=>{vr.RobotPose.Insert();menu.Navigation.Close();}),
            new("Discard pose preview",()=>{vr.RobotPose.Discard();menu.Navigation.Close();}),new("Robot effects",()=>{BuildEffectsMenu();menu.GoBack=BuildPoseMenu;})],"Select a label, then hold Trigger on its circular dial. Release to finish. Insert writes the changed pose as one undoable edit.");menu.LivePoseStatus=true;
    }
    private void BuildVrEditorMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildVrHome;
        if(!editorMode){menu.Set("Editor · Workspace",[new("Open / resume VR Editor",()=>BeginInvoke(new Action(async()=>{await OpenEditor();if(editorMode)BuildVrEditorMenu();})))],"The dedicated VR Editor is also available on the desktop. It keeps its own unsaved work open when you switch to standalone playback.");return;}
        menu.Set("Editor · Workspace",[
            new("Show / hide editor workspace",()=>{vr.EditorPanel?.Toggle();menu.Navigation.Close();}),new("Bring editor workspace here",()=>{vr.EditorPanel?.Recenter();menu.Navigation.Close();}),
            new("Lock / unlock editor layout",()=>vr.EditorPanel?.ToggleLock()),new("Undo",()=>editor?.Command(VrEditorCommand.Undo)),new("Redo",()=>editor?.Command(VrEditorCommand.Redo)),
            new("Switch to standalone playback",UseStandalone),new("Close editor · resolve save dialogs",()=>editor?.RequestClose())],"Trigger-grab the workspace top bar to move it, or a corner to resize. Trigger clicks; B right-clicks. The desktop window remains available for mouse and keyboard.");
    }
    private void BuildVrSettingsMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildVrHome;
        menu.Set("Settings",[
            new("Follow settings · Backdrop and overhead status",()=>{BuildFollowSettingsMenu();menu.GoBack=BuildVrSettingsMenu;}),
            new("Controller feedback · Sounds / Voice / Off",()=>{BuildControllerFeedbackMenu();menu.GoBack=BuildVrSettingsMenu;}),
            new("Compact transport: "+(settings.ShowTransport?"Shown":"Hidden"),()=>{SetTransportVisible(!settings.ShowTransport);BuildVrSettingsMenu();}),
            new("Bring compact transport here",()=>{SetTransportVisible(true);vr.Transport.Recenter();menu.Navigation.Close();}),
            new("Reset my VR position",()=>vr.ResetPosition())],"Controller mappings stay unchanged. Follow mode left A toggles mirror; left B opens/closes Selections. Left stick drives/turns the robot; right stick moves/turns you. Right A opens the menu and right B stops/back. Scene and input-backend settings are available in the desktop sidebar.");
    }
    private void SetTransportVisible(bool visible)
    {
        settings.ShowTransport=visible;if(vr!=null)vr.Transport.Enabled=visible;
        try{var stored=ViewerSettings.Load();stored.ShowTransport=visible;stored.Save();}catch(Exception ex){Fail(ex);}
    }
    private void ConfigureTransport(VrSession session)
    {
        session.Transport.Source=()=>
        {
            if(!editorMode)return new(player.Current?.Name??"No sequence loaded",player.Position,false,false,false,player.Running?"Playing · Standalone":"Standalone playback");
            var pose=lastEditorFrame?.Pose;var motion=session.BodyMotion;
            return new("VR Editor sequence",pose?.Cursor??0,pose?.Recording==true,pose?.HasTake==true,motion.Following,
                motion.RecordingPending?motion.Status:pose?.RecordingMessage is {Length:>0} message?message:"Record or overdub from the current timeline cursor.")
                {Rehearsal=true,Pending=motion.RecordingPending,Countdown=motion.Countdown,Elapsed=pose?.RecordedSeconds??0,Replaying=pose?.Replaying==true};
        };
        session.Transport.Action=action=>
        {
            try
            {
                if(action==3){SetTransportVisible(false);return;}
                if(action is 2 or 8){BeginInvoke(new Action(()=>_=TransportRecord(action==8)));return;}
                if(editorMode)
                {
                    var motion=session.BodyMotion;var pose=lastEditorFrame?.Pose;
                    if(action==1&&motion.Active){motion.Stop("Stopped from rehearsal panel",byUser:true);return;}
                    VrRecordingAction? request=action switch
                    {5=>VrRecordingAction.Replay,6=>VrRecordingAction.Keep,7=>VrRecordingAction.Discard,
                        1 when pose is {Recording:true} or {Replaying:true}=>VrRecordingAction.Stop,_=>null};
                    if(request is {} operation&&pose!=null)
                    {editor?.Recording(new(Guid.NewGuid().ToString(),pose.Token,operation,motion.RecordingTracks));return;}
                    editor?.Command(action==0?VrEditorCommand.PlayPause:VrEditorCommand.Stop);
                }
                else PlaybackAction(()=>{if(action==0)player.Toggle();else{player.Stop();scene?.Rig?.Reset();}});
            }
            catch(Exception ex){Fail(ex);}
        };
    }
    private async Task TransportRecord(bool overdub)
    {
        if(vr==null)return;if(!editorMode||!editorReady)await OpenEditor();if(vr==null||!editorMode)return;
        var motion=vr.BodyMotion;
        if(!motion.HasCalibration||lastEditorFrame?.Pose?.HasTake==true)
        {if(!motion.HasCalibration)BuildBodyMotionMenu();else OpenRecordingMenu(BuildPlaybackMenu);vr.Menu?.Navigation.Open();return;}
        try{vr.RobotPose.Enabled=false;vr.EditorPanel?.Hide();motion.StartRecording(overdub,3);}
        catch(Exception ex){OpenRecordingMenu(BuildPlaybackMenu);vr.Menu?.Detail(ex.Message);vr.Menu?.Navigation.Open();}
    }
}
