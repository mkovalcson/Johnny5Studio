using System.IO;
using System.Windows;
using System.Windows.Threading;
using Johnny5.VrBridge;

namespace ServoAnimator;

public partial class MainWindow
{
    internal void BeginVrPoseChecks(string report,bool rehearsalOnly=false)
    {
        var start=Environment.TickCount64;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        timer.Tick+=(_,_)=>
        {
            if(Environment.TickCount64-start<60000&&EmbeddedHeadView.VrPoseHandles((_,_)=>0).Count()<40)return;
            timer.Stop();var original=_doc.Commands.ToList();
            try
            {
                var results=new List<string>();
                void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);results.Add("PASS: "+message);}
                if(rehearsalOnly){RunVrRecordingChecks(Check,EmbeddedHeadView);File.WriteAllLines(report,results);return;}
                VrFollowSceneChecks.Run(Check);
                PairedBodyEditorChecks.Run(Check,EmbeddedHeadView.LoadedModelFile);
                _doc.Commands.Clear();ClearManualPoseOverrides();SetCursor(.123);var source=EmbeddedHeadView;
                var state=CaptureVrPoseState(source);
                using(var writer=new BinaryWriter(File.Create(Path.Combine(Path.GetDirectoryName(report),"pose-control-catalog.bin"))))PoseWire.Write(writer,state);
                Check(state.Editable&&state.Handles.Length>40,"Full robot supplies head, body and hand handles.");
                Check(state.Handles.Any(h=>h.Key=="Servo:BothEyePop"&&h.Ganged)&&state.Handles.Any(h=>h.Key=="Servo:B_OpenLeftHand"&&h.Ganged),"Eye-pop and hand gangs have labeled handles.");
                var before=source.CaptureVrScene("before");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(report),"pose-control-bounds.json"),System.Text.Json.JsonSerializer.Serialize(new{before.RobotFloor,before.RobotHeight}));
                VrPoseRequest Set(string key,float value)
                {
                    var request=new VrPoseRequest(Guid.NewGuid().ToString(),CaptureVrPoseState(source).Token,VrPoseAction.Set,key,value);ApplyVrPoseRequest(request);return request;
                }
                Set("Servo:NeckTurn",25);
                Check(Math.Abs(source.CapturePose().NeckTurn-25)<.1,"Preview updates the actual neck pose.");
                Check(_doc.Commands.Count==0,"Grabbing never inserts timeline commands.");
                var after=source.CaptureVrScene("after");var worlds=before.Nodes.ToDictionary(n=>n.Id,n=>n.World);
                Check(after.Nodes.Any(n=>worlds.TryGetValue(n.Id,out var m)&&m!=n.World),"Preview changes streamed robot geometry transforms.");
                RunVrTipToeChecks(Check,source,Path.GetDirectoryName(report),Set);
                Set("Servo:FlapsOpen",30);Set("Child:FlapsOpen:BrowLeftTopOpen",50);
                PushHeadPose();UpdateServoState(_cursorTime);
                var pose=source.CapturePose();
                Check(Math.Abs(pose.LeftTopFlapOpen-50)<.2&&Math.Abs(pose.RightTopFlapOpen-30)<.2&&Math.Abs(pose.LeftBottomFlapOpen-30)<.2,$"Individual flap edit refines a gang without moving its siblings ({pose.LeftTopFlapOpen:0.###}, {pose.RightTopFlapOpen:0.###}, {pose.LeftBottomFlapOpen:0.###}).");
                Set("Servo:B_OpenLeftHand",65);
                Check(source.BodyPosition(ServoNames.B_OpenLeftHand)>60,"Hand gang articulates the finger joints.");
                Set("Servo:B_ThumbCurlLeft",10);PushHeadPose();UpdateServoState(_cursorTime);
                Check(Math.Abs(source.BodyPosition(ServoNames.B_ThumbCurlLeft)-10)<.2,"Individual finger refinement survives routine preview updates.");
                foreach(var pair in PairedBodyControls.All)
                {
                    Check(CaptureVrPoseState(source).Handles.Any(h=>h.Key==pair.Target&&h.Ganged),pair.Name+" has a paired handle.");
                    Set(pair.Target,35);
                    var expected=source.VrBodyPairValues(pair,35);
                    Check(expected.All(m=>Math.Abs(source.BodyPosition(m.Servo)-m.Value)<.2),pair.Name+" previews both individual joints.");
                }
                Set("Servo:URDFStageX",25);
                Check(Math.Abs(source.CaptureStage().RobotX-.25)<.001,"Robot position handle moves its scene placement.");
                Set("Servo:BothEyePop",300);Set("Servo:LeftEyePop",100);
                state=CaptureVrPoseState(source);
                var insert=new VrPoseRequest(Guid.NewGuid().ToString(),state.Token,VrPoseAction.Insert);
                ApplyVrPoseRequest(insert);int count=_doc.Commands.Count;ApplyVrPoseRequest(insert);
                Check(count>5&&_doc.Commands.Count==count,"Insert is idempotent and includes the changed control set.");
                Check(_doc.Commands.All(c=>c.OffsetSeconds==.123),"Changed pose uses the nonzero active cursor time.");
                Check(_doc.Commands.Any(c=>c.Servo==ServoNames.URDFStageX&&c.NumericValue==25),"Robot scene placement is included in the changed pose.");
                Check(_doc.Commands.Any(c=>c.Servo==ServoNames.LeftEyePop&&c.NumericValue==100)&&_doc.Commands.Any(c=>c.Servo==ServoNames.RightEyePop&&c.NumericValue==300),"Eye-pop gang followed by an individual edit saves both eyes.");
                Check(_doc.Commands.Any(c=>c.Servo==ServoNames.FlapsOpen&&c.Control==RobotControls.BrowLeftTopOpen&&c.NumericValue==50),"Inserted pose preserves asymmetric child values.");
                Check(PairedBodyControls.All.All(p=>_doc.Commands.Any(c=>c.Servo==p.Left)&&_doc.Commands.Any(c=>c.Servo==p.Right)),"Paired controls insert ordinary left and right commands together.");
                Check(!_doc.Commands.Any(c=>c.Servo==ServoNames.IrisClose),"Untouched controls are not inserted.");
                UndoSteps(1);Check(_doc.Commands.Count==0,"One Undo removes the entire pose insertion.");
                var stale=CaptureVrPoseState(source).Token;SetCursor(.1);
                bool rejected=false;try{ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),stale,VrPoseAction.Set,"Servo:NeckTurn",80));}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"Stale cursor request is rejected.");
                Set("Servo:NeckTurn",40);state=CaptureVrPoseState(source);
                ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),state.Token,VrPoseAction.Discard));
                Check(CaptureVrPoseState(source).Changed==0&&_doc.Commands.Count==0,"Discard restores timeline preview without inserting commands.");
                _doc.Commands.Add(new(){Servo=ServoNames.NeckTurn,NumericValue=5,OffsetSeconds=ServoCommand.TimeKey(_cursorTime)});RefreshAfterEdit();
                Set("Servo:NeckTurn",35);state=CaptureVrPoseState(source);int undoCount=_undoStack.Count;
                var cancel=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                cancel.Tick+=(_,_)=>{var conflict=Application.Current.Windows.OfType<CommandConflictWindow>().FirstOrDefault();if(conflict!=null){cancel.Stop();conflict.DialogResult=false;}};
                cancel.Start();try{ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),state.Token,VrPoseAction.Insert));}finally{cancel.Stop();}
                Check(_doc.Commands.Count==1&&_doc.Commands[0].NumericValue==5&&_undoStack.Count==undoCount&&_vrPoseMessage.Contains("canceled"),"Canceling a same-time command conflict restores the previous timeline and Undo history.");
                _doc.Commands.Clear();_doc.Commands.Add(new(){Servo=ServoNames.B_Elbow,NumericValue=40,OffsetSeconds=0});
                RefreshAfterEdit();SetCursor(1);CaptureMovieCarryPose();
                var elbowPair=PairedBodyControls.Find(ServoNames.B_Elbow);var carriedElbows=source.VrBodyPairValues(elbowPair,40);
                _doc.Commands.Clear();RefreshAfterEdit();SetCursor(0);
                Check(carriedElbows.All(m=>Math.Abs(source.BodyPosition(m.Servo)-m.Value)<.2),"Paired movie pose carries its resolved members into an empty next sequence.");
                _movieCarryPose=null;
                int originalAppearance=source.VrAppearance,originalAccessory=source.VrAccessory;
                foreach(int accessory in new[]{0,1,2})
                {
                    ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrPoseAction.Accessory,Value:accessory));
                    Check(CaptureVrPoseState(source).Accessory==accessory,"VR accessory radio applies and reports selection "+accessory);
                }
                ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrPoseAction.Appearance,Value:4));
                Check(CaptureVrPoseState(source).Appearance==4,"Gold radio applies and reports the gold finish.");
                ApplyVrPoseRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrPoseAction.Appearance,Value:0));
                Check(CaptureVrPoseState(source).Appearance==0,"Normal radio restores original finish and zero wear.");
                source.SetVrAppearance(originalAppearance);source.SetVrAccessory(originalAccessory);
                source.ApplyVrDirectPose(ServoNames.B_ShoulderRotateLeft,null,0);
                double beforeTracked=source.VrBodyAngle(ServoNames.B_ShoulderRotateLeft);_lastVrMotionTime=0;
                var trackedBatch=new VrMotionBatch(Guid.NewGuid().ToString(),VrPoseToken(),.033f,30,new(){{"B_ShoulderRotateLeft",(float)beforeTracked+.6f}});
                ApplyVrMotionBatch(trackedBatch);
                double afterTracked=source.VrBodyAngle(ServoNames.B_ShoulderRotateLeft);
                Check(afterTracked>beforeTracked&&afterTracked-beforeTracked<=3*30*Math.PI/180*.034,"Tracked arm batch obeys the body joint angular speed ceiling.");
                ApplyVrMotionBatch(trackedBatch);Check(source.VrBodyAngle(ServoNames.B_ShoulderRotateLeft)==afterTracked,"Duplicate tracked batch IDs do not advance the pose again.");
                bool badBatch=false;
                try{ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.04f,30,new(){{"B_ShoulderRotateLeft",1},{"B_ElbowLeft",float.NaN}}));}catch(InvalidOperationException){badBatch=true;}
                Check(badBatch&&source.VrBodyAngle(ServoNames.B_ShoulderRotateLeft)==afterTracked,"Invalid tracked batch is rejected before applying any joint targets.");
                _lastVrMotionTime=System.Diagnostics.Stopwatch.GetTimestamp()-(long)(System.Diagnostics.Stopwatch.Frequency*10d);
                ApplyVrMotionBatch(trackedBatch with{Id=Guid.NewGuid().ToString(),Seconds=10});
                Check(source.VrBodyAngle(ServoNames.B_ShoulderRotateLeft)-afterTracked<=3*30*Math.PI/180*.051,"A long tracking gap never produces a catch-up jump.");
                var placement=source.CaptureStage();source.AdvanceVrDriveVisualsForCheck(1);var driveBefore=source.VrDriveVisualState;
                _lastVrMotionTime=System.Diagnostics.Stopwatch.GetTimestamp()-(long)(System.Diagnostics.Stopwatch.Frequency*.04);
                ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.04f,60,new(){{"URDFStageX",(float)placement.RobotX*100+10},{"URDFStageY",(float)placement.RobotY*100},{"URDFStageHeading",(float)placement.HeadingDegrees}}));
                source.AdvanceVrDriveVisualsForCheck(2);var driveAfter=source.VrDriveVisualState;
                Check(Math.Abs(driveAfter.Forward-driveBefore.Forward)>.001,"Tracked placement travel advances the tread animation.");
                {var vrScene=source.CaptureVrScene("check");Check(vrScene.Treads is {Layers.Length:>0} treads&&treads.Layers.Any(l=>l.Left)&&treads.Layers.Any(l=>!l.Left)&&treads.Layers.All(l=>l.Frames.Length>=2&&l.Frames.Contains(vrScene.Nodes.First(n=>n.Id==l.Node).Mesh))&&treads.Layers.SelectMany(l=>l.Frames).All(id=>vrScene.Meshes.Any(m=>m.Id==id&&!m.Dynamic))&&treads.LeftTravel!=0,"The VR scene lists every tread belt frame, the frame shown and the belt travel.");}
                Check(Math.Abs(driveAfter.LeftWheel-driveBefore.LeftWheel)+Math.Abs(driveAfter.RightWheel-driveBefore.RightWheel)>.001,"Tracked placement travel rolls the rear caster wheels.");
                _lastVrMotionTime=System.Diagnostics.Stopwatch.GetTimestamp()-(long)(System.Diagnostics.Stopwatch.Frequency*.04);
                ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.04f,60,new(){{"URDFStageHeading",(float)placement.HeadingDegrees+30}}));
                source.AdvanceVrDriveVisualsForCheck(3);
                Check(Math.Abs(source.VrDriveVisualState.Turn-driveAfter.Turn)>.1,"Tracked turning advances differential tread/caster motion.");
                {double turned=source.CaptureStage()!.HeadingDegrees-placement.HeadingDegrees;Check(turned>90*.04+.3&&turned<=112.5*.05+.01,$"Follow turning has catch-up headroom above the player's 90 deg/s ({turned:F2} deg in 40 ms).");}
                double elbowBefore=source.VrBodyAngle(ServoNames.B_ElbowLeft),elbowDirection=elbowBefore>0?-1:1;
                _lastVrMotionTime=System.Diagnostics.Stopwatch.GetTimestamp()-(long)(System.Diagnostics.Stopwatch.Frequency*.04);
                ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.04f,30,new(){{"B_ElbowLeft",(float)(elbowBefore+elbowDirection*.3)}}));
                double elbowMoved=Math.Abs(source.VrBodyAngle(ServoNames.B_ElbowLeft)-elbowBefore);
                Check(elbowMoved>30*Math.PI/180*.05&&elbowMoved<=3*30*Math.PI/180*.051,"Tracked elbows move at three times the previous angular speed while retaining a bounded per-frame step.");
                var neckLimited=source.LimitVrNeckMix(0,0,70,50,.04);
                Check(double.IsFinite(neckLimited.Nod+neckLimited.Tilt)&&Math.Abs(neckLimited.Nod)<=100&&Math.Abs(neckLimited.Tilt)<=100,"Tracked nod/tilt is limited through its shared physical piston mix.");
                _lastVrMotionTime=System.Diagnostics.Stopwatch.GetTimestamp()-(long)(System.Diagnostics.Stopwatch.Frequency*.04);
                ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.04f,60,new(){{"NeckTurn",15},{"NeckNodUp",10},{"NeckTiltRight",5}}));
                Check(_vrPoseTouched.Contains(ServoNames.NeckTurn)&&_vrPoseTouched.Contains(ServoNames.NeckNodUp)&&_vrPoseTouched.Contains(ServoNames.NeckTiltRight),"Tracked head axes use the calibrated preview path without hardware output.");
                int touchedBefore=_vrPoseTouched.Count;
                ApplyVrMotionBatch(trackedBatch with{Id=Guid.NewGuid().ToString(),Token="stale"});
                Check(_vrPoseTouched.Count==touchedBefore,"Stale tracked batches cannot edit a changed timeline context.");
                RunVrRecordingChecks(Check,source);
                RunLibraryInsertionChecks(Check,report);
                source.ApplyVrDirectPose(ServoNames.B_ElbowLeft,null,0);source.ApplyVrDirectPose(ServoNames.B_WristPitchLeft,null,0);
                int refreshes=source.VrBatchRefreshes;
                source.ApplyVrMotionPose(new[]{(ServoNames.B_ElbowLeft,37d),(ServoNames.B_WristPitchLeft,29d)},null,null,null);
                Check(source.VrBatchRefreshes==refreshes+1&&Math.Abs(source.BodyPosition(ServoNames.B_ElbowLeft)-37)<.01&&Math.Abs(source.BodyPosition(ServoNames.B_WristPitchLeft)-29)<.01,"VR motion batches apply all joints with one final collision/overlay refresh.");
                CaptureVrPoseState(source);int metadataBuilds=source.VrHandleMetadataBuilds;
                for(int repeat=0;repeat<5;repeat++)CaptureVrPoseState(source);
                Check(source.VrHandleMetadataBuilds==metadataBuilds,"Stable control labels and ranges reuse their metadata cache.");
                var heldCommands=_doc.Commands;
                try
                {
                    _doc.Commands=new(){new(){Servo=ServoNames.NeckTurn,NumericValue=10,OffsetSeconds=0}};RefreshAfterEdit();_vrFollowing=true;
                    var stableToken=VrPoseToken();int fingerprintBuilds=VrFingerprintBuilds;
                    for(int repeat=0;repeat<20;repeat++)Check(VrPoseToken()==stableToken,"Unchanged Follow context retains its token.");
                    Check(VrFingerprintBuilds==fingerprintBuilds,"Unchanged Follow updates do not rebuild the sequence fingerprint.");
                    _doc.Commands[0].NumericValue=11;RefreshAfterEdit();var editedToken=VrPoseToken();
                    Check(editedToken!=stableToken&&VrFingerprintBuilds==fingerprintBuilds+1,"A same-count command edit invalidates Follow context immediately through the editor edit generation.");
                    SetCursor(_cursorTime+.031);Check(VrPoseToken()!=editedToken,"Cursor movement invalidates cached Follow tokens.");
                    var beforeMetadata=VrPoseToken();_doc.Description+=" cache invalidation";Check(VrPoseToken()!=beforeMetadata,"Sequence metadata edits invalidate cached Follow context.");
                    var beforeDrag=VrPoseToken();_dragUndoPushed=true;_doc.Commands[0].NumericValue=12;
                    Check(!VrPoseAllowed&&VrPoseToken()!=beforeDrag,"An in-progress spline edit invalidates Follow context before the drag-completed refresh.");
                    _dragUndoPushed=false;

                }
                finally{_dragUndoPushed=false;_vrFollowing=false;_doc.Commands=heldCommands;RefreshAfterEdit();}

                IsEnabled=false;Check(!CaptureVrPoseState(source).Editable,"Modal/disabled editor blocks posing.");IsEnabled=true;
                File.WriteAllLines(report,results);
            }
            catch(Exception ex){File.WriteAllText(report,ex.ToString());Environment.ExitCode=1;}
            finally{IsEnabled=true;_doc.Commands=original;_vrPoseTouched.Clear();_savedSequenceFingerprint=CurrentSequenceFingerprint();Application.Current.Shutdown();}
        };
        timer.Start();
    }
}
