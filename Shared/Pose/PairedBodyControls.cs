namespace ServoAnimator;

public sealed record PairedBodyControl(string Key,string Name,ServoNames Left,ServoNames Right)
{
    public ServoNames Servo => Enum.Parse<ServoNames>("B_"+Key.Split(':')[^1]);
    public string Target => "Servo:"+Servo;
}

/// <summary>Virtual posing controls expand to existing individual timeline commands.
/// Stock arm axes are mirrored axial vectors: matching joint angles produce symmetric motion.
/// Convert through each calibrated range so per-side Reverse does not break that symmetry.</summary>
public static class PairedBodyControls
{
    public static readonly PairedBodyControl[] All =
    [
        new("Gang:ShoulderRotate","Shoulder rotate",ServoNames.B_ShoulderRotateLeft,ServoNames.B_ShoulderRotateRight),
        new("Gang:ShoulderHug","Shoulder hug",ServoNames.B_ShoulderHugLeft,ServoNames.B_ShoulderHugRight),
        new("Gang:Elbow","Elbow",ServoNames.B_ElbowLeft,ServoNames.B_ElbowRight),
        new("Gang:WristPitch","Wrist pitch",ServoNames.B_WristPitchLeft,ServoNames.B_WristPitchRight),
        new("Gang:WristRoll","Wrist roll",ServoNames.B_WristRollLeft,ServoNames.B_WristRollRight)
    ];
    public static PairedBodyControl Find(string key)=>All.FirstOrDefault(p=>p.Key==key||p.Target==key);
    public static PairedBodyControl Find(ServoNames servo)=>All.FirstOrDefault(p=>p.Servo==servo);
    private static (BodyControlDefinition Definition,BodyMotionSetting Setting) Member(ServoNames servo,UrdfConfiguration config)
    {
        var definition=BodyControlCatalog.Find(servo);
        return (definition,config?.BodySetting(definition)??BodyMotionSetting.Default(definition));
    }
    private static (double Min,double Max) Range(PairedBodyControl pair,UrdfConfiguration config)
    {
        (double Min,double Max) Limits(ServoNames servo)
        {var m=Member(servo,config);double a=m.Setting.Map(-100,m.Definition),b=m.Setting.Map(100,m.Definition);return(Math.Min(a,b),Math.Max(a,b));}
        var l=Limits(pair.Left);var r=Limits(pair.Right);return(Math.Max(l.Min,r.Min),Math.Min(l.Max,r.Max));
    }
    public static (ServoNames Servo,double Value)[] Resolve(PairedBodyControl pair,UrdfConfiguration config,double value)
    {
        var range=Range(pair,config);if(range.Min>range.Max)throw new InvalidOperationException("The paired controls have no common calibrated travel.");
        double zero=Math.Clamp(0,range.Min,range.Max),v=Math.Clamp(value,-100,100)/100;
        double angle=zero+(v<0?-v*(range.Min-zero):v*(range.Max-zero));
        return new[]{pair.Left,pair.Right}.Select(servo=>{var m=Member(servo,config);return(servo,m.Setting.CommandForAngle(angle,m.Definition));}).ToArray();
    }
    public static double Position(PairedBodyControl pair,UrdfConfiguration config,Func<ServoNames,double> position)
    {
        var range=Range(pair,config);if(range.Min>range.Max)return 0;
        double angle=new[]{pair.Left,pair.Right}.Average(servo=>{var m=Member(servo,config);return m.Setting.Map(position(servo),m.Definition);});
        angle=Math.Clamp(angle,range.Min,range.Max);double zero=Math.Clamp(0,range.Min,range.Max);
        return angle<zero?-100*(zero-angle)/Math.Max(1e-12,zero-range.Min):100*(angle-zero)/Math.Max(1e-12,range.Max-zero);
    }
}
