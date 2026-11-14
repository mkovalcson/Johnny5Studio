namespace ServoAnimator;

public readonly record struct FollowInputAdjustment(float Scale, float SmoothingMs)
{
    public static FollowInputAdjustment Default => new(1,0);
    public FollowInputAdjustment Normalize()=>new(float.IsFinite(Scale)?Math.Clamp(Scale,0,2):1,
        float.IsFinite(SmoothingMs)?Math.Clamp(SmoothingMs,0,300):0);
}

public sealed class FollowInputFilter
{
    private readonly Dictionary<string,float> history=new();
    public Dictionary<string,FollowInputAdjustment> Settings {get;}=new();
    public FollowInputAdjustment Get(string key)=>Settings.GetValueOrDefault(key,FollowInputAdjustment.Default);
    public void Reset()=>history.Clear();
    public void Forget(string key)=>history.Remove(key);
    public float Shape(string key,float delta,float seconds,string settingKey=null)
    {
        var setting=Get(settingKey??key);
        float target=delta*setting.Scale;
        if(setting.SmoothingMs<=0){Forget(key);return target;}
        if(!history.TryGetValue(key,out float previous))return history[key]=target;
        float weight=1-MathF.Exp(-Math.Clamp(seconds,0,.1f)*1000/setting.SmoothingMs);
        float next=previous+(target-previous)*weight;
        // Settle exactly, including at the end of the neck's lowering phase.
        return history[key]=Math.Abs(next-target)<.0001f?target:next;
    }
}
