using System.Diagnostics;
using System.Windows.Threading;
using Johnny5.VrBridge;
namespace ServoAnimator;
public partial class MainWindow
{
    private bool _vrRecording;
    private List<ServoCommand> _vrReplayCommands;
    private long _vrReplayGeneration;
    private string _vrReplayToken="";
    private double _vrReplayReturn,_vrReplayOriginalRate;
    private string _vrRecordingStartRequestToken="";
    private List<ServoCommand> VrPlaybackCommands=>_vrReplayCommands??_doc.Commands;

    private void StartVrReplay()
    {
        if(!VrHasTake||!VrPoseAllowed||_moviePlaybackActive)
            throw new InvalidOperationException("Pause the current sequence and finish recording before replaying a draft.");
        if(!ReferenceEquals(_doc,_vrRecordingDocument)||CurrentSequenceFingerprint()!=_vrRecordingFingerprint)
            throw new InvalidOperationException("The sequence changed after recording. Discard this draft and record again.");
        _vrReplayToken=Guid.NewGuid().ToString("N");_vrReplayReturn=_cursorTime;_vrReplayGeneration=_apiEditGeneration;
        _vrReplayCommands=MergeVrRecordedTake(_doc.Commands);
        _vrReplayOriginalRate=PlaybackSpeedSlider.Value;PlaybackSpeedSlider.Value=1;
        _vrRecordingMessage="Replaying draft with sequence audio. The timeline is unchanged.";
        try
        {
            StartPlaybackAt(_vrRecordingStart);
            if(!IsRunning)throw new InvalidOperationException("Draft playback could not start.");
        }
        catch {FinishVrReplay();throw;}
    }
    private void FinishVrReplay()
    {
        if(_vrReplayCommands==null)return;
        _vrReplayCommands=null;
        if(IsRunning)PausePlayback();
        PlaybackSpeedSlider.Value=_vrReplayOriginalRate;
        RebuildSplineData();
        if(ReferenceEquals(_doc,_vrRecordingDocument))SetCursor(_vrReplayReturn);
        _vrRecordingMessage="Draft ready · Replay, Keep or Discard.";
    }
    private string _vrRecordingToken="",_vrRecordingMessage="",_vrRecordingFingerprint="";
    private object _vrRecordingDocument;
    private VrRecordingTracks _vrRecordingTracks;
    private readonly HashSet<string> _vrRecordingReceipts=[];
    private readonly List<ServoCommand> _vrRecordedCommands=[];
    private readonly HashSet<ServoNames> _vrRecordedServos=[];
    private double _vrRecordingStart,_vrRecordingEnd,_vrRecordingLimit,_vrRecordingOriginalRate;
    private long _vrRecordingLastFrame;
    private DispatcherTimer _vrRecordingWatch;
    private bool VrHasTake=>!_vrRecording&&_vrRecordedCommands.Count>0&&_vrRecordingEnd>_vrRecordingStart;
    private bool VrRecordingOwns(ServoNames servo)=>_vrRecording&&VrRecordingSelection.Owns(_vrRecordingTracks,servo.ToString());
    private void ApplyVrRecordingRequest(VrRecordingRequest request)
    {
        if(!Guid.TryParse(request.Id,out _)||!Enum.IsDefined(request.Action)||(request.Tracks&~VrRecordingTracks.All)!=0)throw new InvalidOperationException("Invalid recording request.");
        if(_vrRecordingReceipts.Contains(request.Id))return;
        if(request.Token!=VrPoseToken()&&!(request.Action==VrRecordingAction.Stop&&_vrRecording&&request.Token==_vrRecordingStartRequestToken))throw new InvalidOperationException("Timeline changed. Review the current sequence/cursor before recording.");
        if(request.Action is VrRecordingAction.Record or VrRecordingAction.Overdub)
        {
            if(_vrRecording||VrHasTake)throw new InvalidOperationException("Keep or discard the previous take first.");
            if(!VrPoseAllowed||request.Tracks==0||!ApplyOpenCommandEditor())throw new InvalidOperationException("Pause playback, close dialogs and arm at least one control group.");
            if(_moviePlaybackActive)throw new InvalidOperationException("Open the sequence to record, rather than recording a running movie.");
            var source=VrEditorMode.Source??EmbeddedHeadView;
            if(!CaptureVrPoseState(source).Handles.Any(h=>h.Key=="Servo:B_ShoulderRotateLeft"))throw new InvalidOperationException("Load a full-body robot model first.");
            double start=ServoCommand.TimeKey(_cursorTime),limit=request.Action==VrRecordingAction.Overdub?ContentEnd:start+600;
            if(limit<=start+.05)throw new InvalidOperationException("Move the timeline cursor before the end of the sequence to overdub.");
            _vrRecordedCommands.Clear();_vrRecordedServos.Clear();_vrPoseTouched.Clear();ClearManualPoseOverrides();
            _vrRecordingTracks=request.Tracks;_vrRecordingStart=_vrRecordingEnd=start;_vrRecordingLimit=limit;
            _vrRecordingOriginalRate=PlaybackSpeedSlider.Value;PlaybackSpeedSlider.Value=1;
            _vrRecordingDocument=_doc;_vrRecordingFingerprint=CurrentSequenceFingerprint();
            _vrRecordingStartRequestToken=request.Token;_vrRecordingToken=Guid.NewGuid().ToString("N");_vrRecording=true;_lastVrMotionTime=0;
            try
            {
                StartPlaybackAt(start);
                if(!IsRunning)throw new InvalidOperationException("Sequence playback could not start.");
                _vrRecordingLastFrame=Stopwatch.GetTimestamp();
                _vrRecordingMessage="● Recording at 1× · "+request.Tracks+" · B: stop and review";
                _vrRecordingWatch??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                _vrRecordingWatch.Tick-=VrRecordingWatchTick;_vrRecordingWatch.Tick+=VrRecordingWatchTick;_vrRecordingWatch.Start();
            }
            catch{FinishVrRecording("Recording could not start.");throw;}
        }
        else if(request.Action==VrRecordingAction.Stop)
        {
            if(_vrReplayCommands!=null)FinishVrReplay();
            else FinishVrRecording("Take stopped. Replay, keep or discard it.");
        }
        else if(request.Action==VrRecordingAction.Replay)StartVrReplay();
        else
        {
            if(_vrRecording||_vrReplayCommands!=null)throw new InvalidOperationException("Stop recording or replay before keeping or discarding the take.");
            if(request.Action==VrRecordingAction.Keep)
            {
                if(!VrHasTake)throw new InvalidOperationException("No recorded take to keep.");
                if(!VrPoseAllowed)throw new InvalidOperationException("Pause playback and close dialogs before keeping a take.");
                if(!ReferenceEquals(_doc,_vrRecordingDocument)||CurrentSequenceFingerprint()!=_vrRecordingFingerprint)throw new InvalidOperationException("The sequence changed after recording. Discard this take and record against the current sequence.");
                var merged=MergeVrRecordedTake(_doc.Commands);
                PushUndo("Record VR body performance");_doc.Commands=merged;
                if(!RefreshAfterEdit())throw new InvalidOperationException("Take could not be kept; the previous timeline was restored.");
                _vrRecordingMessage=$"Kept {_vrRecordedCommands.Count:N0} motion samples at {_vrRecordingStart:F3}–{_vrRecordingEnd:F3} s. One Undo restores the timeline. Save the sequence to keep it on disk.";
            }
            else{if(IsRunning)PausePlayback();_vrRecordingMessage="Recorded take discarded; timeline unchanged.";}
            _vrRecordedCommands.Clear();_vrRecordedServos.Clear();SetCursor(_vrRecordingStart);
        }
        _vrPoseLastRequest=request.Id;_vrRecordingReceipts.Add(request.Id);ShowStatus(_vrRecordingMessage);
    }
    private void VrRecordingWatchTick(object sender,EventArgs e)=>CheckVrRecording();
    private void CheckVrRecording()
    {
        if(_vrReplayCommands!=null&&(!IsRunning||!ReferenceEquals(_doc,_vrRecordingDocument)||_apiEditGeneration!=_vrReplayGeneration))FinishVrReplay();
        if(!_vrRecording)return;
        string reason=null;
        if(!ReferenceEquals(_doc,_vrRecordingDocument)||CurrentSequenceFingerprint()!=_vrRecordingFingerprint)reason="Sequence changed.";
        else if(!IsRunning)reason="Playback stopped.";
        else if(!IsEnabled||OwnedWindows.Cast<System.Windows.Window>().Any(w=>w.IsVisible&&w!=_head))reason="Editor dialog opened.";
        else if(_cursorTime>=_vrRecordingLimit-.005)reason="End of recording range reached.";
        else if((Stopwatch.GetTimestamp()-_vrRecordingLastFrame)/(double)Stopwatch.Frequency>1)reason="Tracking/controller connection paused.";
        if(reason!=null)FinishVrRecording(reason+" Recorded take is ready to keep or discard.");
    }
    private void CaptureVrRecordedFrame(IEnumerable<(ServoNames Servo,double Value)> values,RobotHeadView source)
    {
        if(!_vrRecording)return;
        var frame=values.Where(v=>VrRecordingOwns(v.Servo)).ToArray();
        if(frame.Length==0)return;
        double time=ServoCommand.TimeKey(Math.Clamp(_playbackClock.EstimatedTime,_vrRecordingStart,_vrRecordingLimit));
        if(time<_vrRecordingEnd)return;
        if(_vrRecordedCommands.Count==0)Add(_vrRecordingStart);
        Add(time);_vrRecordingEnd=time;_vrRecordingLastFrame=Stopwatch.GetTimestamp();
        _vrRecordingMessage=$"● Recording {time-_vrRecordingStart:F1} s · {_vrRecordedServos.Count} controls · B: stop and review";
        void Add(double at)
        {
            var commands=frame.Where(v=>v.Servo is not (ServoNames.NeckNodUp or ServoNames.NeckTiltRight)).Select(v=>new ServoCommand{Servo=v.Servo,NumericValue=(int)Math.Round(v.Value),OffsetSeconds=at,Speed=ServoSpeed.NoChange}).ToList();
            if(frame.Any(v=>v.Servo==ServoNames.NeckNodUp))commands.AddRange(BuildCommandsFromPose(source.CapturePose(),at).Where(c=>c.Servo is ServoNames.NeckNodUp or ServoNames.NeckTiltRight));
            foreach(var command in commands){command.Reason="VR body recording";_vrRecordedServos.Add(command.Servo);}
            // Each packet is one frame; replace a duplicate quantized timestamp atomically.
            if(_vrRecordedCommands.Count>0&&_vrRecordedCommands[^1].OffsetSeconds==at)_vrRecordedCommands.RemoveAll(c=>c.OffsetSeconds==at);
            _vrRecordedCommands.AddRange(commands);
        }
    }
    private void FinishVrRecording(string message)
    {
        if(!_vrRecording)return;
        _vrRecording=false;_vrRecordingWatch?.Stop();
        if(IsRunning)PausePlayback();PlaybackSpeedSlider.Value=_vrRecordingOriginalRate;
        _vrPoseTouched.Clear();ClearManualPoseOverrides();_vrPoseContext="";
        _vrRecordingMessage=message+(VrHasTake?$" Take: {_vrRecordingStart:F3}–{_vrRecordingEnd:F3} s.":" No usable motion take.");
        if(!VrHasTake){_vrRecordedCommands.Clear();_vrRecordedServos.Clear();}
        ShowStatus(_vrRecordingMessage);
    }
    private List<ServoCommand> MergeVrRecordedTake(IEnumerable<ServoCommand> existing)
    {
        // Expand body gangs before replacing armed joints, preserving their unarmed partners.
        var source=VrEditorMode.Source??EmbeddedHeadView;var retained=new List<ServoCommand>();
        foreach(var command in existing)
        {
            if(command.OffsetSeconds<_vrRecordingStart||command.OffsetSeconds>_vrRecordingEnd||command.IsTextServo){retained.Add(command.Clone());continue;}
            IEnumerable<ServoCommand> expanded=[command];
            if(BodyControlCatalog.IsBody(command.Servo)&&(BodyControlCatalog.IsGang(command.Servo)||BodyControlCatalog.IsLegacyShoulder(command.Servo)))
            {
                IEnumerable<(ServoNames Servo,double Value)> members=PairedBodyControls.Find(command.Servo) is {} pair?source.VrBodyPairValues(pair,command.NumericValue)
                    :BodyControlCatalog.IsHandGang(command.Servo)?source.VrHandValues(command.Servo,command.NumericValue):[(BodyControlCatalog.Canonical(command.Servo),source.VrCanonicalValue(command.Servo,command.NumericValue))];
                var resolved=members.ToArray();
                if(!resolved.Any(v=>_vrRecordedServos.Contains(v.Servo))){retained.Add(command.Clone());continue;}
                expanded=resolved.Select(v=>{var c=command.Clone();c.Servo=v.Servo;c.NumericValue=(int)Math.Round(v.Value);return c;});
            }
            retained.AddRange(expanded.Where(c=>!_vrRecordedServos.Contains(c.Servo)).Select(c=>c.Clone()));
        }
        string group=Guid.NewGuid().ToString("N");
        return retained.Concat(_vrRecordedCommands.Select(c=>{var copy=c.Clone();copy.GroupId=group;return copy;})).OrderBy(c=>c.OffsetSeconds).ToList();
    }
}
public sealed partial class RobotHeadView
{
    internal double VrCanonicalValue(ServoNames servo,double value)
    {
        if(!BodyControlCatalog.IsLegacyShoulder(servo))return value;
        var original=BodyControlCatalog.Find(servo);var canonical=BodyControlCatalog.Find(BodyControlCatalog.Canonical(servo));
        var a=_urdfConfiguration.BodySetting(original)??BodyMotionSetting.Default(original);var b=_urdfConfiguration.BodySetting(canonical)??BodyMotionSetting.Default(canonical);
        return b.CommandForAngle(a.Map(value,original),canonical);
    }
    internal IEnumerable<(ServoNames Servo,double Value)> VrHandValues(ServoNames servo,double value)=>HandOpeningValues(servo,value);
}
