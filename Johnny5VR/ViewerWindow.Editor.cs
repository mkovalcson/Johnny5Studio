using Johnny5.VrBridge;

namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private EditorConnection? editor;
    private VrFrame? lastEditorFrame;
    private bool editorMode,waitingForEditorClose;
    private readonly bool editorSmoke;
    private int editorFrames;
    private bool editorSmokeDone;
    private int editorSmokePhase;
    private bool editorCommandChecked;
    private int poseSmokePhase;
    private string poseSmokeId="";
    private float poseSmokeValue;
    private long editorSmokeStart=Environment.TickCount64;
    private bool editorReady;
    private Task? editorOpening,editorResources;
    private Task OpenEditor()=>editorOpening is {IsCompleted:false}?editorOpening:editorOpening=OpenEditorCore();
    private async Task OpenEditorCore()
    {
        EndArcade();
        if(loading||loadingShow||preparingScene)return;
        try
        {
            waitingForEditorClose=false;
            if(editor is {Process.HasExited:false}&&!string.Equals(Path.GetFullPath(config.Text),editor.Config,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Close the VR editor and resolve its save dialogs before changing Config folders.");
            if(!string.Equals(Path.GetFullPath(config.Text),settings.ConfigFolder,StringComparison.OrdinalIgnoreCase))await LoadScene();
            if(editor==null||editor.Process.HasExited)
            {
                editor?.Dispose();lastEditorFrame=null;editorResources=null;editorReady=false;
                string executable=new[]{Path.Combine(ViewerSettings.Home,"Editor","AnimationEditorPlayer.exe"),Path.Combine(ViewerSettings.Home,"packaging","editor-"+ReleaseVersion,"AnimationEditorPlayer.exe")}.FirstOrDefault(File.Exists)
                    ??throw new FileNotFoundException("This release needs its bundled Editor folder. Extract the complete ZIP.");
                string editorVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(
                    Path.ChangeExtension(executable, ".dll")).ProductVersion ?? "unknown";
                if (editorVersion != ReleaseVersion)
                    throw new InvalidOperationException($"VR Player {ReleaseVersion} requires the matching VR Editor. Found {editorVersion}.");
                editor=new(executable,settings.ConfigFolder);
                editor.Process.EnableRaisingEvents=true;
                editor.Process.Exited+=(_,_)=>
                {
                    if(IsDisposed||!IsHandleCreated)return;
                    BeginInvoke(new Action(()=>{config.ReadOnly=false;if(browseConfig!=null)browseConfig.Enabled=true;UseStandalone();if(waitingForEditorClose){waitingForEditorClose=false;Close();}}));
                };
            }
            player.Stop();effectAudio.Dispose();previewEffects=null;editor.Active=true;editorMode=true;editorReady=false;
            config.ReadOnly=true;if(browseConfig!=null)browseConfig.Enabled=false;
            status.Text="Preparing VR Editor — loading scene and controls…";
            var opening=editor;long deadline=Environment.TickCount64+180000;
            while(true)
            {
                if(closing||IsDisposed||!editorMode||!ReferenceEquals(editor,opening))return;
                if(opening.Process.HasExited)throw new InvalidOperationException("The VR Editor closed before it was ready.");
                if(Environment.TickCount64>deadline)throw new TimeoutException("The VR Editor is still preparing. Check its desktop window for a loading error or dialog, then try opening it again.");
                // PumpEditor uploads complete scene assets while normal VR rendering continues.
                if(lastEditorFrame is {Nodes.Length:>0,Pose: {Handles.Length:>0,AppearanceBusy:false} pose}&&renderer?.Live==true)
                {
                    editorResources??=Task.Run(()=>PoseHandleOverlay.Preload(pose));
                    if(editorResources.IsFaulted)await editorResources;
                    if(editorResources.IsCompletedSuccessfully&&opening.Desktop!=null)break;
                    status.Text=editorResources.IsCompletedSuccessfully?"Preparing VR Editor — waiting for its first screen…":"Preparing VR Editor — preloading icons, labels and dial…";
                }
                else status.Text=lastEditorFrame?.Pose?.AppearanceBusy==true?"Preparing VR Editor — finishing robot appearance…":"Preparing VR Editor — loading scene and controls…";
                playbackStatus.Text=status.Text;
                await Task.Delay(50);
            }
            editorReady=true;
            vr?.AttachEditor(editor.Process.Id,editor.Command,editor.Navigate,()=>editor.Desktop,editor.Pose,editor.Motion,editor.Config,editor.Recording);BuildVrHome();
            status.Text="VR editing · Trigger-grab the top bar to move, or corners to resize. Trigger clicks; B right-clicks. Right stick pans/zooms timelines or scrolls panels. A opens the menu.";
        }
        catch(Exception ex){editorReady=false;if(editorMode)UseStandalone();Fail(ex);}
    }
    private void CheckEditorSmoke()
    {
        if(Environment.TickCount64-editorSmokeStart>150000)throw new InvalidOperationException("Timed out waiting for the VR editor scene.");
        if(editorFrames>=5&&!editorCommandChecked){editorCommandChecked=true;editor!.Command(VrEditorCommand.Stop);}
        if(editorSmokePhase==0&&editorFrames>=8&&lastEditorFrame?.Pose is {} pose)
        {
            if(lastEditorFrame.Status.Contains("VR input error"))throw new InvalidOperationException(lastEditorFrame.Status);
            if(poseSmokePhase==0&&pose.Editable)
            {
                var h=pose.Handles.First(h=>h.Key=="Servo:NeckTurn");poseSmokeValue=h.Value>0?-20:20;poseSmokeId=Guid.NewGuid().ToString();
                editor!.Pose(new(poseSmokeId,pose.Token,VrPoseAction.Set,h.Key,poseSmokeValue));poseSmokePhase=1;
            }
            else if(poseSmokePhase==1&&pose.LastRequest==poseSmokeId)
            {
                if(pose.Changed==0||Math.Abs(pose.Handles.First(h=>h.Key=="Servo:NeckTurn").Value-poseSmokeValue)>.2f)throw new InvalidOperationException("Pose preview did not reach the VR editor.");
                poseSmokeId=Guid.NewGuid().ToString();editor!.Pose(new(poseSmokeId,pose.Token,VrPoseAction.Discard));poseSmokePhase=2;
            }
            else if(poseSmokePhase==2&&pose.LastRequest==poseSmokeId)
            {
                if(pose.Changed!=0)throw new InvalidOperationException("Pose discard did not reach the VR editor.");
                File.WriteAllText(Path.Combine(Diagnostics.Folder,"pose-bridge-check.txt"),"PASS: live control metadata, preview and discard traversed the real viewer/editor pipe.");poseSmokePhase=3;
            }
            else if(poseSmokePhase==3)
            {
                poseSmokeId=Guid.NewGuid().ToString();editor!.Motion(new(poseSmokeId,pose.Token,.033f,30,new(){{"B_ElbowLeft",.7f}}));poseSmokePhase=4;
            }
            else if(poseSmokePhase==4&&pose.LastRequest==poseSmokeId)
            {
                if(pose.Changed==0)throw new InvalidOperationException("Tracked pose batch did not reach the VR editor.");
                poseSmokeId=Guid.NewGuid().ToString();editor!.Pose(new(poseSmokeId,pose.Token,VrPoseAction.Discard));poseSmokePhase=5;
            }
            else if(poseSmokePhase==5&&pose.LastRequest==poseSmokeId)
            {
                if(pose.Changed!=0)throw new InvalidOperationException("Tracked pose discard failed.");
                File.WriteAllText(Path.Combine(Diagnostics.Folder,"body-motion-bridge-check.txt"),"PASS: batched tracked pose and discard traversed the real viewer/editor pipe.");poseSmokePhase=6;
            }
            else if(poseSmokePhase==6&&pose.Editable)
            {
                poseSmokeId=Guid.NewGuid().ToString();editor!.Recording(new(poseSmokeId,pose.Token,VrRecordingAction.Record,VrRecordingTracks.LeftArm));poseSmokePhase=7;
            }
            else if(poseSmokePhase==7&&pose.Recording)
            {
                editor!.Motion(new(Guid.NewGuid().ToString(),pose.Token,.033f,30,new(){{"B_ElbowLeft",.8f}}));poseSmokePhase=8;
            }
            else if(poseSmokePhase==8&&pose.Recording)
            {
                if(pose.RecordedSeconds<.15)editor!.Motion(new(Guid.NewGuid().ToString(),pose.Token,.033f,30,new(){{"B_ElbowLeft",.8f}}));
                else{poseSmokeId=Guid.NewGuid().ToString();editor!.Recording(new(poseSmokeId,pose.Token,VrRecordingAction.Stop,VrRecordingTracks.LeftArm));poseSmokePhase=9;}
            }
            else if(poseSmokePhase==9&&pose.LastRequest==poseSmokeId&&!pose.Recording)
            {
                if(!pose.HasTake)throw new InvalidOperationException("Recording did not retain a draft take.");
                poseSmokeId=Guid.NewGuid().ToString();editor!.Recording(new(poseSmokeId,pose.Token,VrRecordingAction.Discard,VrRecordingTracks.LeftArm));poseSmokePhase=10;
            }
            else if(poseSmokePhase==10&&pose.LastRequest==poseSmokeId)
            {
                if(pose.HasTake)throw new InvalidOperationException("Draft recording discard failed.");
                File.WriteAllText(Path.Combine(Diagnostics.Folder,"body-recording-bridge-check.txt"),"PASS: recording start, motion samples, stop/review and discard traversed the real viewer/editor pipe without editing the sequence.");poseSmokePhase=11;
            }
        }
        if(editorSmokePhase==0&&editorFrames>=15&&poseSmokePhase==11)
        {
            if(lastEditorFrame?.Status.Contains("VR: Stop")!=true)throw new InvalidOperationException("VR toolbar command did not reach the editor.");
            editorSmokePhase=1;UseStandalone();_=ResumeEditorSmoke();return;
        }
        if(editorSmokePhase==1)return;
        if(lastEditorFrame==null||!renderer!.Live)return;
        if(editorFrames<30||editorSmokeDone||editor?.Desktop==null||editor.DesktopFrames<10)return;editorSmokeDone=true;
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"pose-surface-picking-check.txt"),renderer.CheckRobotPicking());
        renderer.Screenshot(Path.Combine(Diagnostics.Folder,"live-editor-render.png"));Diagnostics.VerifyScreenshot(Path.Combine(Diagnostics.Folder,"live-editor-render.png"));
        var capture=editor!.Desktop!;
        var chrome=EditorPanelChrome.Draw(capture,new(PanelTarget.TopRight,false,"Trigger-grab the top bar to move; corner handles resize; release to place"));
        using(var bitmap=new Bitmap(chrome.Width,chrome.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            var bits=bitmap.LockBits(new(0,0,chrome.Width,chrome.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes=(byte[])chrome.Rgba.Clone();for(int i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);
            try{for(int y=0;y<chrome.Height;y++)System.Runtime.InteropServices.Marshal.Copy(bytes,y*chrome.Width*4,bits.Scan0+y*bits.Stride,chrome.Width*4);}finally{bitmap.UnlockBits(bits);}
            bitmap.Save(Path.Combine(Diagnostics.Folder,"editor-workspace.png"));
        }
        Diagnostics.VerifyScreenshot(Path.Combine(Diagnostics.Folder,"editor-workspace.png"));
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"editor-smoke-result.txt"),$"PASS: toolbar Stop, standalone switch/resume, {editorFrames} scene updates and {editor.DesktopFrames} WPF-rendered desktop frames. Actual D3D rendering and framed editor image stream verified without PrintWindow.");
        waitingForEditorClose=true;editor.RequestClose();
    }
    private async Task ResumeEditorSmoke()
    {
        await Task.Delay(350);await OpenEditor();editorSmokePhase=2;
    }
    private void UseStandalone()
    {
        EndArcade();
        if(editor!=null){editor.Pause();editor.Active=false;editor.Take();}
        editorMode=false;editorReady=false;vr?.DetachEditor();
        if(scene!=null)renderer?.Upload(scene);
        BuildVrHome();status.Text=editor is {Process.HasExited:false}?"Standalone playback · The editor is paused and remains open for your unsaved work.":"Standalone playback · VR editor closed.";
    }
    private void PumpEditor()
    {
        if(editor==null)return;
        if(editor.Process.HasExited){UseStandalone();return;}
        // Only the VR workspace panel (and the editor smoke check) shows editor window images.
        editor.FollowActive=vr?.BodyMotion.Following==true;editor.UseFollowBackdrop=settings.UseEditorFollowBackdrop;
        editor.DesktopWanted=!editorReady||editorSmoke||vr?.EditorPanel?.Visible==true;
        // Pieces glide between scene updates only while following (see Renderer.SmoothLive).
        renderer!.SmoothLive=editor.FollowActive;
        if(editor.Take() is {} frame){lastEditorFrame=frame;var applyStart=BridgeTiming.Start();renderer.ApplyLive(frame,settings);BridgeTiming.Stop(BridgeTiming.Section.ApplyLive,applyStart);editor.ReportApply(System.Diagnostics.Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds);editorFrames++;}
        renderer.AdvanceLive();
        playbackStatus.Text=editor.Status;
        if(lastEditorFrame?.Pose is {} selectionsPose)UpdateFollowSelections(selectionsPose);
        if(vr?.Menu is {LiveBodyRecordingStatus:true} recordingMenu&&lastEditorFrame?.Pose is {} recordingPose)
            recordingMenu.Detail(recordingPose.RecordingMessage+"\nNeck Turn / Nod / Tilt are one group, initially OFF. B or opening a menu stops for review. Replay previews the draft with existing audio. Keep writes one Undo step; Save the sequence afterward. Armed controls replace only their take interval. Existing audio and unarmed movements are preserved.");
        if(vr?.Menu is {LiveBodyMotionStatus:true} bodyMenu)
        {
            var motion=vr.BodyMotion;
            bodyMenu.Rows[2]=bodyMenu.Rows[2] with{Label=motion.Following?"Stop following (hold current pose)":"Follow my movements / re-center"};
            bodyMenu.Detail(motion.Status+"\nA: menu pauses following · B: stop. Stand upright before Follow/re-center. Headset + hands estimate elbows and body. Config/VR stores calibration. Physical robot output is not enabled.");
        }
        if(vr?.Menu is {LivePoseStatus:true} menu&&lastEditorFrame?.Pose is {} pose)
        {
            menu.Rows[0]=menu.Rows[0] with{Label=vr.RobotPose.Enabled?"Hide robot pose controls":"Pose robot with controllers"};
            menu.Rows[1]=menu.Rows[1] with{Label=$"Insert changed pose at cursor ({pose.Cursor:0.000} s)"};
            menu.Detail($"{pose.Changed} changed controls · {(pose.Editable?"Ready to pose":"Pause playback / close dialogs to pose")}\n{pose.Message}\nCyan: individual · Purple: ganged. Select a label, then hold Trigger on its range dial. Release to finish. Right stick: fine adjustment. Insert commits one Undo step. Timeline/cursor changes clear previews.");
        }
    }
    private async Task StartRobotPose()
    {
        if(vr==null)return;
        vr.RobotPose.Enabled=true;vr.EditorPanel?.Hide();vr.Menu?.Navigation.Close();status.Text="Loading robot pose controls…";
        // Let the loading overlay appear before starting or attaching the editor.
        if(!editorReady||!editorMode||editor==null||editor.Process.HasExited){await Task.Delay(30);await OpenEditor();}
        if(vr!=null&&editorMode&&editorReady){vr.RobotPose.Enabled=true;vr.EditorPanel?.Hide();vr.Menu?.Navigation.Close();}
        else if(vr!=null){vr.RobotPose.Enabled=false;status.Text="Could not start robot posing. Check editor status.";}
    }
}
