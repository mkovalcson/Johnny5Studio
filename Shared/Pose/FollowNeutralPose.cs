namespace ServoAnimator;

public sealed record FollowNeutralControl(string Key,string Label,string Section,float Minimum,float Maximum,float Initial)
{
    public float FromSlider(float slider)=>slider<=0?Minimum:slider>=1?Maximum:Minimum+slider*(Maximum-Minimum);
    public float ToSlider(float value)=>(Math.Clamp(value,Minimum,Maximum)-Minimum)/(Maximum-Minimum);
    public string Display(float value)=>Key.StartsWith("B_",StringComparison.Ordinal)?$"{value*180/MathF.PI:0.#}°":$"{value:0.#}%";
}

public static class FollowNeutralCatalog
{
    public static readonly FollowNeutralControl[] Controls=Build().ToArray();
    private static IEnumerable<FollowNeutralControl> Build()
    {
        foreach(var (key,label) in new[]{("B_NeckLift","Neck lift"),("B_ChestTilt","Chest bend"),("B_WaistLean","Waist bend"),("B_WaistSwivel","Waist swivel")})yield return Body(key,label,"Torso");
        foreach(var (key,label) in new[]{("NeckTurn","Head turn"),("NeckNodUp","Head nod"),("NeckTiltRight","Head tilt")})yield return new(key,label,"Head",-100,100,0);
        foreach(string side in new[]{"Left","Right"})
        {
            foreach(var (key,label,initial) in new[]{("ShoulderRotate","Shoulder rotate",25*MathF.PI/180),("ArmTwist","Arm twist",0f),("Elbow","Elbow",BodyTrackingSolver.PointElbowRadians),("WristPitch","Wrist pitch",0f),("WristRoll","Wrist roll",0f)})
                yield return Body("B_"+key+side,label,side+" arm",initial);
            foreach(var (key,label,initial) in new[]{("ThumbCurl","Thumb curl",-.3f),("ThumbTipCurl","Thumb tip",-.3f),("FingerSpreadUpper","Upper spread",.6f),("FingerCurlUpper","Upper curl",-.3f),("FingerTipCurlUpper","Upper tip",-.3f),("FingerEndCurlUpper","Upper end",-.3f),("FingerSpreadLower","Lower spread",.6f),("FingerCurlLower","Lower curl",-.3f),("FingerTipCurlLower","Lower tip",-.3f),("FingerEndCurlLower","Lower end",-.3f)})
                yield return Body("B_"+key+side,label,side+" hand",initial);
        }
    }
    private static FollowNeutralControl Body(string key,string label,string section,float initial=0)
    {var d=BodyControlCatalog.Find(Enum.Parse<ServoNames>(key));return new(key,label,section,(float)d.MinRadians,(float)d.MaxRadians,initial);}
    public static Dictionary<string,float> Sanitize(IReadOnlyDictionary<string,float> values)=>Controls.Where(c=>values!=null&&values.TryGetValue(c.Key,out var v)&&float.IsFinite(v)).ToDictionary(c=>c.Key,c=>Math.Clamp(values[c.Key],c.Minimum,c.Maximum));
}

/// <summary>Offsets relative to the measured starting stance. Settings belong to robot
/// sides; swapping presentation also swaps the measured reference, never the defaults.</summary>
public sealed class FollowNeutralMapper
{
    private BodyTrackingResult reference;
    private Dictionary<string,float> mappedReference=new();
    private bool? mappedMirror;
    public void Capture(BodyTrackingResult pose){reference=pose with{Targets=new(pose.Targets)};mappedMirror=null;}
    private void MapReference(bool mirror)
    {if(mappedMirror==mirror)return;mappedReference=reference==null?new():BodyTrackingSolver.MapPresentation(reference,mirror).Targets;mappedMirror=mirror;}
    public float Neutral(FollowNeutralControl control,IReadOnlyDictionary<string,float> values,bool mirror)
    {
        MapReference(mirror);
        return values!=null&&values.TryGetValue(control.Key,out var value)&&float.IsFinite(value)?Math.Clamp(value,control.Minimum,control.Maximum):
            Math.Clamp(mappedReference.GetValueOrDefault(control.Key,control.Initial),control.Minimum,control.Maximum);
    }
    public BodyTrackingResult Apply(BodyTrackingResult raw,bool mirror,IReadOnlyDictionary<string,float> values,FollowInputFilter input=null,float seconds=0)
    {
        MapReference(mirror);var pose=BodyTrackingSolver.MapPresentation(raw,mirror);var targets=pose.Targets;
        foreach(var control in FollowNeutralCatalog.Controls)
        {
            if(!targets.TryGetValue(control.Key,out float target)){input?.Forget(control.Key);continue;}
            if(control.Key=="B_NeckLift")
            {
                float lower=Math.Clamp(target/control.Minimum,0,1);
                float neutral=Neutral(control,values,mirror);
                targets[control.Key]=neutral+(control.Minimum-neutral)*lower;
                continue;
            }
            bool customNeutral=values?.ContainsKey(control.Key)==true;
            bool customInput=input!=null&&input.Get(control.Key)!=FollowInputAdjustment.Default;
            if(!customNeutral&&!customInput){input?.Forget(control.Key);continue;}
            // Retain the explicit point gesture's straight finger / aligned wrist pose.
            bool left=control.Key.EndsWith("Left",StringComparison.Ordinal),right=control.Key.EndsWith("Right",StringComparison.Ordinal);
            bool pointing=mirror?(left?raw.Right.Pointing:right&&raw.Left.Pointing):(left?raw.Left.Pointing:right&&raw.Right.Pointing);
            if(pointing&&(control.Key.StartsWith("B_Wrist",StringComparison.Ordinal)||control.Key.StartsWith("B_Finger",StringComparison.Ordinal)||control.Key.StartsWith("B_Thumb",StringComparison.Ordinal))){input?.Forget(control.Key);continue;}
            float baseline=mappedReference.GetValueOrDefault(control.Key,control.Initial);
            float delta=target-baseline;
            if(customInput)delta=input.Shape(control.Key,delta,seconds);
            else input?.Forget(control.Key);
            targets[control.Key]=Math.Clamp(Neutral(control,values,mirror)+delta,control.Minimum,control.Maximum);
        }
        return pose with{Targets=targets};
    }
    // Used on the editor side with the actual speed-limited joint angle, so a
    // fast crouch cannot make the chest bend while a slow neck is still lowering.
    public static bool WaitForNeck(float neckTarget,double actualNeck,double? reachableMinimum=null)
    {double minimum=BodyControlCatalog.Find(ServoNames.B_NeckLift).MinRadians;return neckTarget<=minimum+.001&&actualNeck>(reachableMinimum??minimum)+.01;}
}
