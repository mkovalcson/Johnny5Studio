namespace ServoAnimator;

public sealed record FollowSpeedControl(string Key,string Label,float Initial);
public static class FollowSpeedCatalog
{
    // A key without a side is shared by both physical sides, including in mirrored mode.
    public static readonly FollowSpeedControl[] Controls = [
        new("NeckTurn","Neck turn",1),new("NeckNodUp","Neck nod",1),new("NeckTiltRight","Neck tilt",1),
        new("B_NeckLift","Neck lift",1),new("B_ChestTilt","Chest tilt",1),new("B_WaistSwivel","Waist swivel",1),new("B_WaistLean","Waist lean",1),
        new("B_ShoulderRotate","Shoulder rotate · L/R",3),new("B_ArmTwist","Arm twist · L/R",1),new("B_Elbow","Elbow · L/R",3),
        new("B_WristPitch","Wrist pitch · L/R",2),new("B_WristRoll","Wrist roll · L/R",2),
        new("B_ThumbCurl","Thumb curl · L/R",2),new("B_ThumbTipCurl","Thumb tip · L/R",2),
        new("B_FingerSpreadUpper","Upper finger spread · L/R",2),new("B_FingerCurlUpper","Upper finger curl · L/R",2),
        new("B_FingerTipCurlUpper","Upper finger tip · L/R",2),new("B_FingerEndCurlUpper","Upper finger end · L/R",2),
        new("B_FingerSpreadLower","Lower finger spread · L/R",2),new("B_FingerCurlLower","Lower finger curl · L/R",2),
        new("B_FingerTipCurlLower","Lower finger tip · L/R",2),new("B_FingerEndCurlLower","Lower finger end · L/R",2),
        new("B_LaserLower","Laser up/down",1),new("URDFStageTravel","Robot forward/reverse",1),new("URDFStageHeading","Robot turn",1)
    ];
    public static string Key(string control)=>control.StartsWith("B_",StringComparison.Ordinal)?
        control.EndsWith("Left",StringComparison.Ordinal)?control[..^4]:control.EndsWith("Right",StringComparison.Ordinal)?control[..^5]:control:
        control is "URDFStageX" or "URDFStageY"?"URDFStageTravel":control;
    public static float Value(IReadOnlyDictionary<string,float> values,string control)
    {
        string key=Key(control);float value=values!=null&&values.TryGetValue(key,out var saved)?saved:Controls.FirstOrDefault(c=>c.Key==key)?.Initial??1;
        return float.IsFinite(value)?Math.Clamp(value,.1f,5):1;
    }
    public static float FromSlider(float value)=>MathF.Round(.1f+Math.Clamp(value,0,1)*4.9f,1);
    public static float ToSlider(float value)=>(Math.Clamp(value,.1f,5)-.1f)/4.9f;
}
