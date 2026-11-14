using System.Diagnostics;
using System.Xml.Linq;
using Johnny5.VrBridge;
namespace ServoAnimator;
public partial class MainWindow
{
    private string _lastVrMotionId="",_vrMotionModel="";
    private long _lastVrMotionTime;
    private double _vrLastMotionMs;
    private readonly Dictionary<string,double> _vrMotionVelocity=[];
    private void ApplyVrMotionBatch(VrMotionBatch batch)
    {
        long motionStart=Stopwatch.GetTimestamp();
        if(batch.Id==_lastVrMotionId)return;
        if(!Guid.TryParse(batch.Id,out _)||!float.IsFinite(batch.Seconds)||!float.IsFinite(batch.MaximumDegreesPerSecond)||batch.MaximumDegreesPerSecond<=0||batch.MaximumDegreesPerSecond>360||batch.Targets.Count>128||batch.Targets.Values.Any(v=>!float.IsFinite(v))||batch.Speeds.Count>64||batch.Speeds.Any(s=>!FollowSpeedCatalog.Controls.Any(c=>c.Key==s.Key)||!float.IsFinite(s.Value)||s.Value<.1f||s.Value>5))
            throw new InvalidOperationException("Invalid tracked pose batch.");
        if((!VrPoseAllowed&&!_vrRecording)||batch.Token!=VrPoseToken())return;
        CheckVrRecording();if(batch.Token!=VrPoseToken())return;
        var source=VrEditorMode.Source??EmbeddedHeadView;
        if(_vrMotionModel!=source.LoadedModelFile)
        {
            _vrMotionVelocity.Clear();_vrMotionModel=source.LoadedModelFile;
            foreach(var joint in XDocument.Load(_vrMotionModel).Descendants("joint"))
                if(double.TryParse(joint.Element("limit")?.Attribute("velocity")?.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double velocity)&&velocity>0)
                    _vrMotionVelocity[joint.Attribute("name")?.Value??""]=velocity;
        }
        var handles=CaptureVrPoseState(source).Handles.ToDictionary(h=>h.Key);
        var valid=new List<(ServoNames Servo,double Value)>();
        long now=Stopwatch.GetTimestamp();double elapsed=_lastVrMotionTime==0?.033:(now-_lastVrMotionTime)/(double)Stopwatch.Frequency;
        double dt=Math.Clamp(elapsed,0,.05);
        bool hasNod=batch.Targets.ContainsKey("NeckNodUp"),hasTilt=batch.Targets.ContainsKey("NeckTiltRight");
        if(_vrRecording&&!VrRecordingOwns(ServoNames.NeckNodUp))hasNod=hasTilt=false;
        if(hasNod!=hasTilt)throw new InvalidOperationException("Tracked neck nod/tilt must be sent together.");
        var neck=hasNod?source.LimitVrNeckMix(handles["Servo:NeckNodUp"].Value,handles["Servo:NeckTiltRight"].Value,batch.Targets["NeckNodUp"],batch.Targets["NeckTiltRight"],dt,FollowSpeedCatalog.Value(batch.Speeds,"NeckNodUp"),FollowSpeedCatalog.Value(batch.Speeds,"NeckTiltRight")):(0d,0d);
        bool waitForNeck=batch.Targets.TryGetValue("B_NeckLift",out float neckTarget)&&FollowNeutralMapper.WaitForNeck(neckTarget,source.VrBodyAngle(ServoNames.B_NeckLift),source.VrReachableBodyAngle(ServoNames.B_NeckLift,BodyControlCatalog.Find(ServoNames.B_NeckLift).MinRadians));
        foreach(var target in batch.Targets)
        {
            if(_vrRecording&&!VrRecordingSelection.Owns(_vrRecordingTracks,target.Key))continue;
            if(!float.IsFinite(target.Value)||!Enum.TryParse<ServoNames>(target.Key,out var servo)||!handles.TryGetValue("Servo:"+target.Key,out var handle)||(handle.Ganged&&servo is not (ServoNames.NeckNodUp or ServoNames.NeckTiltRight)))
                throw new InvalidOperationException("Unsupported tracked pose target.");
            double before=handle.Value,value;
            if(BodyControlCatalog.Find(servo) is {} definition)
            {
                double rate=Math.Min(_vrMotionVelocity.GetValueOrDefault(definition.Joint,double.PositiveInfinity),batch.MaximumDegreesPerSecond*Math.PI/180)*FollowSpeedCatalog.Value(batch.Speeds,target.Key);
                double requested=target.Value;
                if(waitForNeck&&servo is ServoNames.B_ChestTilt or ServoNames.B_WaistLean)
                    requested=Math.Min(requested,source.VrBodyAngle(servo));
                value=source.LimitVrBodyAngle(servo,requested,dt,rate);
            }
            else if(UrdfStageSettings.IsMotion(servo))
            {
                // 25% above the player's drive speed (90 deg/s, 80 cm/s) so the robot catches up
                // after a late update instead of trailing the stick and rolling on after release.
                double rate=(servo==ServoNames.URDFStageHeading?90:80)*1.25*FollowSpeedCatalog.Value(batch.Speeds,target.Key);
                value=before+Math.Clamp(target.Value-before,-rate*dt,rate*dt);
            }
            else if(servo is ServoNames.NeckNodUp or ServoNames.NeckTiltRight)value=servo==ServoNames.NeckNodUp?neck.Item1:neck.Item2;
            else if(servo==ServoNames.NeckTurn)
            {
                double delta=target.Value-before;
                double limited=source.AdvanceControllerTarget(servo,null,before,Math.Sign(delta),dt*FollowSpeedCatalog.Value(batch.Speeds,target.Key),ServoSpeed.NoChange);
                value=before+Math.Clamp(delta,-Math.Abs(limited-before),Math.Abs(limited-before));
            }
            else throw new InvalidOperationException("Unsupported tracked control.");
            valid.Add((servo,Math.Clamp(value,handle.Minimum,handle.Maximum)));
        }
        foreach(var target in valid)
        {
            if(UrdfStageSettings.IsMotion(target.Servo))_controllerPositions["Servo:"+target.Servo]=target.Value;
            _manualPoseOverrides.Add(target.Servo);_vrPoseTouched.Add(target.Servo);
            var row=_rows.FirstOrDefault(r=>r.Servo==target.Servo);if(row!=null)row.Value=target.Value;
        }
        var stage=source.CaptureStage();
        double? stageX=null,stageY=null,stageHeading=null;
        if(stage!=null)
        {
            double Value(ServoNames servo,double fallback){int index=valid.FindIndex(t=>t.Servo==servo);return index<0?fallback:valid[index].Value;}
            double x=Value(ServoNames.URDFStageX,stage.RobotX*100)/100,y=Value(ServoNames.URDFStageY,stage.RobotY*100)/100,heading=Value(ServoNames.URDFStageHeading,stage.HeadingDegrees);
            stageX=x;stageY=y;stageHeading=heading;
        }
        ForEachHeadView(v=>v.ApplyVrMotionPose(valid,stageX,stageY,stageHeading));
        CaptureVrRecordedFrame(valid,source);
        _lastVrMotionId=batch.Id;_lastVrMotionTime=now;_vrPoseLastRequest=batch.Id;
        _vrLastMotionMs=Stopwatch.GetElapsedTime(motionStart).TotalMilliseconds;
        _vrPoseMessage="Following tracked body · preview only · modeled joint limits and following speed limits enforced";
    }
}
public sealed partial class RobotHeadView
{
    internal int VrBatchRefreshes {get;private set;}
    internal void ApplyVrMotionPose(IReadOnlyList<(ServoNames Servo,double Value)> targets,double? x,double? y,double? heading)
    {
        bool suppressed=_suppressCollisionRefresh;long before=_poseRevision;
        try
        {
            _suppressCollisionRefresh=true;
            foreach(var target in targets)if(!UrdfStageSettings.IsMotion(target.Servo))ApplyVrDirectPose(target.Servo,null,target.Value);
            if(x.HasValue&&y.HasValue&&heading.HasValue)ApplyVrTrackedStage(x.Value,y.Value,heading.Value);
        }
        finally
        {
            _suppressCollisionRefresh=suppressed;
            if(!suppressed&&(_poseRevision!=before||x.HasValue)){UpdatePoseOverlayLayout();RefreshCollisionState();VrBatchRefreshes++;}
        }
    }

    internal (double Nod,double Tilt) LimitVrNeckMix(double nod,double tilt,double targetNod,double targetTilt,double seconds,double nodSpeed=1,double tiltSpeed=1)
    {
        if(!NeckMix.IsComplementary(_servoConfiguration))throw new InvalidOperationException("Correct the neck piston gang directions before tracked head following.");
        var before=NeckMix.Pistons(_servoConfiguration,nod,tilt);
        (double Nod,double Tilt) Limited(double requestedNod,double requestedTilt,double duration)
        {
            var target=NeckMix.Pistons(_servoConfiguration,requestedNod,requestedTilt);
            double Limit(RobotControls piston,double value,double requested)
            {
                double allowed=AdvanceControllerTarget(ServoNames.NeckNodUp,piston,value,Math.Sign(requested-value),duration,ServoSpeed.NoChange);
                return value+Math.Clamp(requested-value,-Math.Abs(allowed-value),Math.Abs(allowed-value));
            }
            return NeckMix.Decompose(_servoConfiguration,Limit(RobotControls.NeckTiltLeft,before.Left,target.Left),Limit(RobotControls.NeckTiltRight,before.Right,target.Right));
        }
        // Preserve the original coupled piston limiter at 1x. At different axis
        // speeds first find each axis's calibrated travel budget, then enforce
        // the shared piston budget on the combined request. Do not scale the
        // destination itself: that would turn a speed limit into easing near it.
        if(nodSpeed==1&&tiltSpeed==1)return Limited(targetNod,targetTilt,seconds);
        var n=Limited(targetNod,tilt,seconds*nodSpeed);
        var t=Limited(nod,targetTilt,seconds*tiltSpeed);
        return Limited(n.Nod,t.Tilt,seconds*Math.Max(nodSpeed,tiltSpeed));
    }

    internal void ApplyVrTrackedStage(double x,double y,double heading)
    {
        var before=CaptureStage();if(before==null)return;
        double angle=(before.HeadingDegrees+heading)*.5*Math.PI/180;
        double forward=(x-before.RobotX)*Math.Cos(angle)+(y-before.RobotY)*Math.Sin(angle);
        bool rootOnly=_stage==null;
        ApplyStageMotion(x,y,heading);
        AdvanceTreads(forward,rootOnly?heading-before.HeadingDegrees:0);
    }
    internal (double Forward,double Turn,double LeftWheel,double RightWheel) VrDriveVisualState=>( _treadForwardMetres,_treadTurnDegrees,_casterWheelLeft,_casterWheelRight);
    internal void AdvanceVrDriveVisualsForCheck(double seconds)=>RenderTreadFrame(TimeSpan.FromSeconds(seconds));
    internal double VrBodyAngle(ServoNames servo)
    {var definition=BodyControlCatalog.Find(servo);return (_urdfConfiguration.BodySetting(definition)??BodyMotionSetting.Default(definition)).Map(BodyPosition(servo),definition);}
    internal double VrReachableBodyAngle(ServoNames servo,double requested)
    {
        var definition=BodyControlCatalog.Find(servo);var setting=_urdfConfiguration.BodySetting(definition)??BodyMotionSetting.Default(definition);
        return setting.Map(setting.CommandForAngle(requested,definition),definition);
    }
    internal double LimitVrBodyAngle(ServoNames servo,double target,double seconds,double rate)
    {
        var definition=BodyControlCatalog.Find(servo);var setting=_urdfConfiguration.BodySetting(definition)??BodyMotionSetting.Default(definition);
        double before=setting.Map(BodyPosition(servo),definition);
        double angle=before+Math.Clamp(target-before,-rate*seconds,rate*seconds);
        return setting.CommandForAngle(angle,definition);
    }
}
