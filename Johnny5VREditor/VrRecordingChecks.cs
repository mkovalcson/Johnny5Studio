using Johnny5.VrBridge;
namespace ServoAnimator;
public partial class MainWindow
{
    private void RunVrRecordingChecks(Action<bool,string> check,RobotHeadView source)
    {
        _vrPoseTouched.Clear();ClearManualPoseOverrides();_doc.Commands.Clear();
        _doc.Commands.AddRange(new[]{
            new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=.1,NumericValue=22},
            new ServoCommand{Servo=ServoNames.NeckNodUp,OffsetSeconds=.1,NumericValue=12},
            new ServoCommand{Servo=ServoNames.NeckTiltRight,OffsetSeconds=.1,NumericValue=8},
            new ServoCommand{Servo=ServoNames.IrisClose,OffsetSeconds=.1,NumericValue=20},
            new ServoCommand{Servo=ServoNames.B_Elbow,OffsetSeconds=.15,NumericValue=30},
            new ServoCommand{Servo=ServoNames.B_ElbowLeft,OffsetSeconds=0,NumericValue=10},
            new ServoCommand{Servo=ServoNames.B_ElbowLeft,OffsetSeconds=2,NumericValue=60},
            new ServoCommand{Servo=ServoNames.Play,OffsetSeconds=.1,TextValue="missing-check-audio.wav"}});
        RefreshAfterEdit();SetCursor(.1);var originals=_doc.Commands.Select(c=>c.Clone()).ToArray();
        check(!VrRecordingSelection.Owns(VrRecordingTracks.Body,"NeckTurn")&&!VrRecordingSelection.Owns(VrRecordingTracks.Body,"NeckNodUp")&&!VrRecordingSelection.Owns(VrRecordingTracks.Body,"NeckTiltRight"),"Default body overdub leaves all three neck axes off together.");
        check(VrRecordingSelection.Owns(VrRecordingTracks.Torso,"B_WaistSwivel")&&!VrRecordingSelection.Owns(VrRecordingTracks.Neck,"B_WaistSwivel"),"Coordinated waist swivel records with torso, independently of neck capture.");
        check(VrRecordingSelection.Owns(VrRecordingTracks.Body,"B_NeckLift")&&VrRecordingSelection.Owns(VrRecordingTracks.Torso,"B_NeckLift")&&!VrRecordingSelection.Owns(VrRecordingTracks.Neck,"B_NeckLift"),"The crouch neck lift records with the torso (chest and waist) group.");
        var start=new VrRecordingRequest(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Overdub,VrRecordingTracks.LeftArm);
        ApplyVrRecordingRequest(start);ApplyVrRecordingRequest(start);
        check(_vrRecording&&IsRunning&&_vrRecordingStart==.1&&_vrRecordingLimit==ContentEnd,"Overdub starts existing sequence playback at the captured nonzero cursor; duplicate Start is idempotent.");
        string token=VrPoseToken();_playbackClock.Start(.2);_cursorTime=.2;
        ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),token,.033f,60,new(){{"B_ElbowLeft",1},{"B_ElbowRight",1},{"NeckTurn",80},{"NeckNodUp",80},{"NeckTiltRight",80}}));
        _playbackClock.Start(.3);_cursorTime=.3;
        ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),token,.033f,60,new(){{"B_ElbowLeft",1},{"B_ElbowRight",1},{"NeckTurn",80},{"NeckNodUp",80},{"NeckTiltRight",80}}));
        check(VrPoseToken()==token&&CaptureVrPoseState(source).Recording,"Recording retains a stable context token as the playback cursor advances.");
        check(_vrRecordedCommands.Count>1&&_vrRecordedCommands.All(c=>c.Servo==ServoNames.B_ElbowLeft),"Overdub captures only armed, speed-limited native controls; unarmed neck/right arm are not sampled.");
        check(_doc.Commands.Count==originals.Length,"A draft recording does not modify timeline commands.");
        _vrPoseTouched.Clear();UpdateServoState(.3);
        check(Math.Abs(_rows.First(r=>r.Servo==ServoNames.NeckTurn).Value-22)<.01,"Unarmed face-tracked neck motion continues during body overdub.");
        FinishVrRecording("Test stop");check(!IsRunning&&VrHasTake,"Stop pauses playback and leaves a draft take for review.");
        PlaybackSpeedSlider.Value=.75;int undo=_undoStack.Count;var documentCommands=_doc.Commands;var savedCursor=_cursorTime;
        var replay=new VrRecordingRequest(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Replay,VrRecordingTracks.LeftArm);
        ApplyVrRecordingRequest(replay);
        check(IsRunning&&PlaybackSpeedSlider.Value==1&&CaptureVrPoseState(source).Replaying&&VrHasTake,"Replay plays the uncommitted draft while retaining it for review.");
        check(ReferenceEquals(_doc.Commands,documentCommands)&&_undoStack.Count==undo&&_orderedCommands.Any(c=>c.Reason=="VR body recording")&&_orderedCommands.Any(c=>c.Servo==ServoNames.Play),"Draft replay uses temporary playback data and preserves audio without touching the document or Undo.");
        string replayToken=VrPoseToken();_cursorTime+=.04;
        check(VrPoseToken()==replayToken&&!PlaybackOutputAllowed(true),"Replay has a stable stop token and cannot send physical output.");
        ApplyVrRecordingRequest(new(Guid.NewGuid().ToString(),replayToken,VrRecordingAction.Stop,VrRecordingTracks.LeftArm));
        check(!IsRunning&&_vrReplayCommands==null&&VrHasTake&&_cursorTime==savedCursor&&PlaybackSpeedSlider.Value==.75&&ReferenceEquals(_doc.Commands,documentCommands)&&!_orderedCommands.Any(c=>c.Reason=="VR body recording"),"Stopping replay restores the cursor and authored playback without committing the draft.");
        ApplyVrRecordingRequest(replay);
        check(!IsRunning&&VrHasTake,"A duplicate Replay receipt does not restart the draft.");
        var keep=new VrRecordingRequest(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Keep,VrRecordingTracks.LeftArm);
        ApplyVrRecordingRequest(keep);int kept=_doc.Commands.Count;ApplyVrRecordingRequest(keep);
        check(_undoStack.Count==undo+1&&kept==_doc.Commands.Count&&!VrHasTake,"Keeping a take is one undoable, idempotent timeline edit.");
        check(originals.Where(c=>c.Servo is ServoNames.NeckTurn or ServoNames.NeckNodUp or ServoNames.NeckTiltRight or ServoNames.IrisClose or ServoNames.Play).All(c=>_doc.Commands.Any(n=>n.Servo==c.Servo&&n.OffsetSeconds==c.OffsetSeconds&&n.NumericValue==c.NumericValue&&n.TextValue==c.TextValue)),"Keeping body overdub preserves audio, eye and all neck commands.");
        check(_doc.Commands.Any(c=>c.Servo==ServoNames.B_ElbowRight&&c.OffsetSeconds==.15)&&!_doc.Commands.Any(c=>c.Servo==ServoNames.B_Elbow&&c.OffsetSeconds==.15),"Replacing one arm in a ganged command preserves its unarmed partner.");
        check(_doc.Commands.Any(c=>c.Servo==ServoNames.B_ElbowLeft&&c.OffsetSeconds==0&&c.NumericValue==10)&&_doc.Commands.Any(c=>c.Servo==ServoNames.B_ElbowLeft&&c.OffsetSeconds==2&&c.NumericValue==60),"Existing commands before and after the take interval remain intact.");
        UndoSteps(1);check(_doc.Commands.Count==originals.Length&&_doc.Commands.Any(c=>c.Servo==ServoNames.B_Elbow),"One Undo restores the complete pre-overdub timeline.");
        SetCursor(.1);ApplyVrRecordingRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Record,VrRecordingTracks.LeftArm));
        _playbackClock.Start(.2);_cursorTime=.2;ApplyVrMotionBatch(new(Guid.NewGuid().ToString(),VrPoseToken(),.033f,60,new(){{"B_ElbowLeft",1}}));
        _vrRecordingLastFrame=System.Diagnostics.Stopwatch.GetTimestamp()-System.Diagnostics.Stopwatch.Frequency*2;
        CheckVrRecording();check(!_vrRecording&&VrHasTake,"Lost body packets stop recording while retaining the partial take.");
        int beforeDiscard=_doc.Commands.Count;
        ApplyVrRecordingRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Discard,VrRecordingTracks.Body));
        check(!VrHasTake&&_doc.Commands.Count==beforeDiscard,"Discard removes the draft without modifying the timeline.");
        SetCursor(.1);ApplyVrRecordingRequest(new(Guid.NewGuid().ToString(),VrPoseToken(),VrRecordingAction.Record,VrRecordingTracks.LeftArm));
        SetCursor(.4);check(!_vrRecording,"Changing the timeline cursor stops an active take.");
        _vrRecordedCommands.Clear();_vrRecordedServos.Clear();
    }
}
