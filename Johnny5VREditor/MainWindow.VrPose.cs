using System.Globalization;
using System.Windows;
using System.Windows.Media.Media3D;
using Johnny5.VrBridge;
using N=System.Numerics;

namespace ServoAnimator;

public partial class MainWindow
{
    private readonly HashSet<ServoNames> _vrPoseTouched=new();
    private readonly HashSet<string> _vrPoseReceipts=new();
    private string _vrPoseContext="",_vrPoseMessage="",_vrPoseLastRequest="";
    private string _vrTokenKey="",_vrTokenFingerprint="",_vrCachedToken="";
    private string VrPoseToken()
    {
        if(_vrReplayCommands!=null)return _vrReplayToken;
        if(_vrRecording)return _vrRecordingToken;
        if(!ReferenceEquals(_apiDocument,_doc)){_apiDocument=_doc;_apiDocumentId=Guid.NewGuid().ToString("N");}
        string key=_apiDocumentId+"|"+_jsonPath+"|"+_moviePath+"|"+_cursorTime.ToString("R",CultureInfo.InvariantCulture)+"|"+IsRunning+"|"+(VrEditorMode.Source??EmbeddedHeadView)?.LoadedModelFile;
        string fingerprint=VrSequenceFingerprint();
        if(_vrFollowing&&key==_vrTokenKey&&ReferenceEquals(fingerprint,_vrTokenFingerprint))return _vrCachedToken;
        string token=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key+"|"+fingerprint)));
        if(_vrFollowing){_vrTokenKey=key;_vrTokenFingerprint=fingerprint;_vrCachedToken=token;}return token;
    }
    private AnimationDocument _vrFingerprintDocument;
    private object _vrFingerprintCommands;
    private long _vrFingerprintGeneration=-1;
    private int _vrFingerprintCount=-1;
    private string _vrFingerprintMetadata="",_vrFingerprint="";
    internal int VrFingerprintBuilds {get;private set;}
    private string VrSequenceFingerprint()
    {
        if(!_vrFollowing)return CurrentSequenceFingerprint();
        string metadata=string.Join("|",_doc.Description,_doc.UrdfBackgroundFile,_audioPath,_audioOffset.ToString("R",CultureInfo.InvariantCulture),_splineHz,_doc.AnimateMode,_doc.ScaleValues,_dragUndoPushed,_markerDragUndoPushed,_clipDragUndoPushed,
            string.Join(",",_rows.Where(r=>r.SplineEnabled).Select(r=>r.Servo).OrderBy(s=>s)));
        if(!ReferenceEquals(_doc,_vrFingerprintDocument)||!ReferenceEquals(_doc.Commands,_vrFingerprintCommands)||_apiEditGeneration!=_vrFingerprintGeneration||_doc.Commands.Count!=_vrFingerprintCount||metadata!=_vrFingerprintMetadata)
        {
            _vrFingerprint=CurrentSequenceFingerprint();VrFingerprintBuilds++;
            _vrFingerprintDocument=_doc;_vrFingerprintCommands=_doc.Commands;_vrFingerprintCount=_doc.Commands.Count;
            _vrFingerprintGeneration=_apiEditGeneration;_vrFingerprintMetadata=metadata;
        }
        return _vrFingerprint;
    }
    private bool VrPoseAllowed=>VrEditorMode.Active&&IsEnabled&&!IsRunning&&!_dragUndoPushed&&!_markerDragUndoPushed&&!_clipDragUndoPushed&&_controllerLibraryRun==null&&_recordingWindow==null&&!_speedCalibrationBusy&&
        !OwnedWindows.Cast<Window>().Any(w=>w.IsVisible&&w!=_head);
    private bool VrPosePreviewActive=>!_vrRecording&&VrEditorMode.Active&&_vrPoseTouched.Count>0&&_vrPoseContext==VrPoseToken();
    private VrPoseState CaptureVrPoseState(RobotHeadView source)
    {
        CheckVrRecording();
        string context=VrPoseToken();
        if(!_vrRecording&&_vrPoseContext!=context)
        {
            if(_vrPoseTouched.Count>0)
            {
                _vrPoseMessage="Preview cleared: timeline, document or playback changed.";
                ClearManualPoseOverrides();if(!IsRunning)SetCursor(_cursorTime);
            }
            _vrPoseTouched.Clear();_vrPoseContext=context;
            foreach(var key in _controllerPositions.Keys.Where(k=>ControllerTargets.TryServo(k,out var s,out _)&&UrdfStageSettings.IsMotion(s)).ToArray())_controllerPositions.Remove(key);
        }
        var commands=BuildCommandsFromPose(source.CapturePose(),0).GroupBy(c=>c.Servo).ToDictionary(g=>g.Key,g=>g.ToArray());
        float Value(ServoNames servo,RobotControls? child)
        {
            if(BodyControlCatalog.IsBody(servo))return (float)source.BodyPosition(servo);
            var matches=commands.GetValueOrDefault(servo)??Array.Empty<ServoCommand>();
            return (float)(matches.FirstOrDefault(c=>c.Control==child)?.NumericValue??
                matches.FirstOrDefault(c=>c.Control==null)?.NumericValue??(matches.Length>0?matches.Average(c=>c.NumericValue):ControllerCurrentValue(child.HasValue?$"Child:{servo}:{child}":$"Servo:{servo}")));
        }
        var handles=source.VrPoseHandles(Value).ToArray();
        return new(context,_cursorTime,VrPoseAllowed||_vrRecording,_vrPoseTouched.Count,_vrPoseMessage,_vrPoseLastRequest,handles){TravelExtents=_vrTravelExtents,TravelExtentReceipt=_vrTravelReceipt,TravelExtentMessage=_vrTravelMessage,Appearance=source.VrAppearance,Accessory=source.VrAccessory,AppearanceBusy=!source.AppearancePreparation.IsCompleted,Recording=_vrRecording,Replaying=_vrReplayCommands!=null,HasTake=VrHasTake,RecordingMessage=_vrRecordingMessage,RecordedSeconds=Math.Max(0,_vrRecordingEnd-_vrRecordingStart)};
    }
    private void ApplyVrPoseRequest(VrPoseRequest request)
    {
        if(!Guid.TryParse(request.Id,out _)||!Enum.IsDefined(request.Action)||!float.IsFinite(request.Value))throw new InvalidOperationException("Invalid VR pose request.");
        if(request.Action is VrPoseAction.ReadTravelExtents or VrPoseAction.SaveTravelExtent)
        {
            ApplyVrTravelRequest(request);
            return;
        }
        if(_vrPoseReceipts.Contains(request.Id))return;
        // A fresh token anchors every action to the visible sequence and cursor.
        if(!VrPoseAllowed||request.Token!=VrPoseToken())throw new InvalidOperationException("Pose context changed. Pause playback, close dialogs, and point at the robot again.");
        var source=VrEditorMode.Source??EmbeddedHeadView;
        if(request.Action is VrPoseAction.Appearance or VrPoseAction.Accessory)
        {
            int maximum=request.Action==VrPoseAction.Appearance?4:2;
            if(request.Value<0||request.Value>maximum||request.Value!=(int)request.Value)throw new InvalidOperationException("Invalid appearance selection.");
            ForEachHeadView(v=>{if(request.Action==VrPoseAction.Appearance)v.SetVrAppearance((int)request.Value);else v.SetVrAccessory((int)request.Value);});
            _vrPoseMessage="VR appearance applied for this session.";
        }
        else if(request.Action==VrPoseAction.Set)
        {
            var handle=CaptureVrPoseState(source).Handles.FirstOrDefault(h=>h.Key==request.Key)??throw new InvalidOperationException("This control is not available on the current model.");
            float value=Math.Clamp(request.Value,handle.Minimum,handle.Maximum);
            void Apply(ServoNames servo,RobotControls? child,double value)
            {
                if(UrdfStageSettings.IsMotion(servo))_controllerPositions[$"Servo:{servo}"]=value;
                _manualPoseOverrides.Add(servo);
                var row=_rows.FirstOrDefault(r=>r.Servo==servo);
                if(child.HasValue){var member=row?.Children.FirstOrDefault(c=>c.Control==child);if(member!=null)member.Value=value;}
                else if(row!=null)row.Value=value;
                ForEachHeadView(v=>v.ApplyVrDirectPose(servo,child,value));
                if(BodyControlCatalog.IsHandGang(servo))foreach(var member in BodyControlCatalog.HandMembers(servo))_vrPoseTouched.Add(member.Servo);
                else _vrPoseTouched.Add(servo);
                if(servo==ServoNames.BothEyePop){_vrPoseTouched.Add(ServoNames.LeftEyePop);_vrPoseTouched.Add(ServoNames.RightEyePop);}
                if(NeckMix.IsNeckParent(servo)){_vrPoseTouched.Add(ServoNames.NeckNodUp);_vrPoseTouched.Add(ServoNames.NeckTiltRight);}
            }
            if(PairedBodyControls.Find(handle.Key) is {} pair)
                foreach(var member in source.VrBodyPairValues(pair,value))Apply(member.Servo,null,member.Value);
            else if(ControllerTargets.TryServo(handle.Key,out var servo,out var child))Apply(servo,child,value);
            else throw new InvalidOperationException("Invalid pose target.");
            _vrPoseMessage=$"{handle.Name}: {value:0} · preview only";
        }
        else if(request.Action==VrPoseAction.Discard)
        {
            foreach(var servo in _vrPoseTouched.Where(UrdfStageSettings.IsMotion))_controllerPositions.Remove($"Servo:{servo}");
            _vrPoseTouched.Clear();ClearManualPoseOverrides();SetCursor(_cursorTime);_vrPoseMessage="Pose preview discarded.";
        }
        else
        {
            if(_vrPoseTouched.Count==0)throw new InvalidOperationException("No changed controls to insert.");
            if(!ApplyOpenCommandEditor())throw new InvalidOperationException("Resolve the open command editor first.");
            if(request.Token!=VrPoseToken())throw new InvalidOperationException("Timeline changed. Review the pose before inserting again.");
            double time=ServoCommand.TimeKey(_cursorTime);
            var commands=BuildCommandsFromPose(source.CapturePose(),time).Where(c=>_vrPoseTouched.Contains(c.Servo)&&!BodyControlCatalog.IsHandGang(c.Servo)).ToList();
            foreach(var servo in _vrPoseTouched.Where(UrdfStageSettings.IsMotion))commands.Add(new(){Servo=servo,NumericValue=(int)Math.Round(ControllerCurrentValue($"Servo:{servo}")),OffsetSeconds=time,Speed=ServoSpeed.NoChange});
            if(commands.Count==0)throw new InvalidOperationException("No pose commands could be prepared.");
            PushUndo($"Insert changed VR pose at {time:F3} s");
            _doc.Commands.AddRange(commands);_vrPoseTouched.Clear();ClearManualPoseOverrides();
            // Existing conflict resolution remains responsible for commands
            // already at this time; the entire pose remains one undoable edit.
            bool accepted=RefreshAfterEdit();SetCursor(time);
            _vrPoseContext=VrPoseToken();_vrPoseMessage=accepted?$"Inserted changed VR pose at {time:F3} s · Undo restores the previous commands.":"Pose insertion canceled; previous timeline commands restored.";
            ShowStatus(_vrPoseMessage);
        }
        _vrPoseLastRequest=request.Id;_vrPoseReceipts.Add(request.Id);
    }
}

public sealed partial class RobotHeadView
{
    internal void ApplyVrDirectPose(ServoNames servo,RobotControls? child,double value)
    {
        bool editing=_poseEditEnabled,snap=SnapCalibratedMotion;
        try{_poseEditEnabled=false;SnapCalibratedMotion=true;if(child.HasValue)SetChildServo(servo,child.Value,value,true);else SetServo(servo,value);}
        finally{_poseEditEnabled=editing;SnapCalibratedMotion=snap;}
    }
    internal (ServoNames Servo,double Value)[] VrBodyPairValues(PairedBodyControl pair,double value)
        => PairedBodyControls.Resolve(pair,_urdfConfiguration,value);
    private readonly Dictionary<(ServoNames Servo,RobotControls? Child,bool Gang),VrPoseHandle> _vrHandleMetadata=new();
    private object _vrHandleScene,_vrHandleConfiguration;
    internal int VrHandleMetadataBuilds {get;private set;}
    internal IEnumerable<VrPoseHandle> VrPoseHandles(Func<ServoNames,RobotControls?,float> value)
    {
        if(_scene==null)yield break;
        if(!ReferenceEquals(_vrHandleScene,_scene)||!ReferenceEquals(_vrHandleConfiguration,_urdfConfiguration))
        {_vrHandleMetadata.Clear();_vrHandleScene=_scene;_vrHandleConfiguration=_urdfConfiguration;}
        string Name(string text)=>System.Text.RegularExpressions.Regex.Replace(ControlLabels.Text(text).Replace("B_","").Replace('_',' '),"(?<=[a-z])(?=[A-Z])"," ");
        VrPoseHandle Make(ServoNames servo,RobotControls? child,N.Vector3 p,bool gang)
        {
            var key=(servo,child,gang);
            if(!_vrHandleMetadata.TryGetValue(key,out var template))
            {
                var range=ServoCommand.RangeFor(servo);string target=child.HasValue?$"Child:{servo}:{child}":$"Servo:{servo}";
                string label=servo switch{ServoNames.URDFStageX=>"Robot X position",ServoNames.URDFStageY=>"Robot Y position",ServoNames.URDFStageHeading=>"Robot heading",_=>Name(ControlLabels.Text(child?.ToString()??servo.ToString()))};
                template=new(target,label+(gang?" · Ganged":""),default,range.Min,range.Max,0,gang);_vrHandleMetadata[key]=template;VrHandleMetadataBuilds++;
            }
            return template with{Anchor=p,Value=Math.Clamp(value(servo,child),template.Minimum,template.Maximum)};
        }
        bool Point(string joint,out N.Vector3 at)
        {
            at=default;if(!_scene.TryVrJointPoint(joint,out var p))return false;
            if(_presentationModel!=null)p=_presentationModel.Transform.Transform(p);
            at=new((float)p.X,(float)p.Y,(float)p.Z);return true;
        }
        var head=new[]{ServoNames.NeckTurn,ServoNames.NeckNodUp,ServoNames.NeckTiltRight,ServoNames.EyesHorizontalRight,ServoNames.EyesVerticalUp,ServoNames.IrisClose,ServoNames.FlapsOpen,ServoNames.FlapTiltUp,ServoNames.VentsOpen,ServoNames.NoseBody,ServoNames.NoseBasket,ServoNames.Whip_Antenna_RaiseLower,ServoNames.Whip_Antenna_Rotate,ServoNames.MFR_UpDown,ServoNames.MFR_Rotate,ServoNames.Microphone_RaiseLower,ServoNames.LeftEyePop,ServoNames.RightEyePop,ServoNames.BothEyePop};
        foreach(var servo in head)
        {
            var members=new List<(RobotControls Control,N.Vector3 At)>();
            var children=servo==ServoNames.BothEyePop?new[]{RobotControls.LeftEyePop,RobotControls.RightEyePop}:ServoConfiguration.ControlsFor(servo);
            foreach(var child in children)
            {
                string joint=NeckMix.IsNeckParent(servo)?"NeckTurn":child.ToString();
                if(Point(joint,out var at))members.Add((child,at));
            }
            if(members.Count==0&&Point(servo.ToString(),out var single)){yield return Make(servo,null,single,false);continue;}
            if(members.Count==0)continue;
            bool gang=members.Count>1;
            yield return Make(servo,null,members.Aggregate(N.Vector3.Zero,(p,m)=>p+m.At)/members.Count,gang);
            if(gang&&!NeckMix.IsNeckParent(servo)&&servo!=ServoNames.BothEyePop)
                foreach(var member in members)yield return Make(servo,member.Control,member.At,false);
        }
        foreach(var control in BodyControlCatalog.Controls.Where(HasBodyControl))
        {
            string joint=BodyControlCatalog.IsHandGang(control.Servo)?BodyControlCatalog.HandMembers(control.Servo).First(c=>_scene.HasJoint(c.Joint)).Joint:control.Joint;
            if(Point(joint,out var p))yield return Make(control.Servo,null,p,BodyControlCatalog.IsHandGang(control.Servo)||control.Servo==ServoNames.B_PanelsOpen);
        }
        foreach(var pair in PairedBodyControls.All)
            if(Point(BodyControlCatalog.Find(pair.Left).Joint,out var left)&&Point(BodyControlCatalog.Find(pair.Right).Joint,out var right))
                yield return new(pair.Target,pair.Name,(left+right)*.5f,-100,100,(float)PairedBodyControls.Position(pair,_urdfConfiguration,BodyPosition),true);
        if(_scene.TryTransformLinkPoint("base_link",new Point3D(0,0,.08),out var root))
        {
            if(_presentationModel!=null)root=_presentationModel.Transform.Transform(root);
            var at=new N.Vector3((float)root.X,(float)root.Y,(float)root.Z);
            foreach(var servo in new[]{ServoNames.URDFStageX,ServoNames.URDFStageY,ServoNames.URDFStageHeading})
                yield return Make(servo,null,at,false);
        }
    }
}

internal sealed partial class UrdfScene
{
    internal bool TryVrJointPoint(string joint,out Point3D point)
    {
        point=default;
        // TipToe has no URDF joint; its handle sits on the left track's front hub, which moves with it.
        if(joint==TipToeJoint)return HasTipToe&&TryTransformLinkPoint(TipToeGeometry.AnchorLink,new(),out point);
        return _joints.TryGetValue(joint,out var found)&&TryTransformLinkPoint(found.ChildLink,new(),out point);
    }
}
