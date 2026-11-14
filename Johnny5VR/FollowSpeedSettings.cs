using System.Text.Json;
using ServoAnimator;
namespace Johnny5VR;
internal sealed class FollowSpeedSettings
{
    internal Dictionary<string,float> Values {get;private set;}=[];
    private string path="";
    private long dirtyAt;
    private Task? saving;
    internal void Load(string config)
    {
        Flush();path=Path.Combine(config,"VR","FollowSpeeds.json");Values=[];
        try {if(File.Exists(path))Values=JsonSerializer.Deserialize<Dictionary<string,float>>(File.ReadAllText(path))??[];}
        catch(Exception ex){Diagnostics.Log(ex);}
        Values=FollowSpeedCatalog.Controls.ToDictionary(c=>c.Key,c=>FollowSpeedCatalog.Value(Values,c.Key));
    }
    internal const string AllFingers="AllFingers";
    internal static bool IsFinger(string key)=>key.StartsWith("B_Finger",StringComparison.Ordinal)||key.StartsWith("B_Thumb",StringComparison.Ordinal);
    private IEnumerable<FollowSpeedControl> FingerControls=>FollowSpeedCatalog.Controls.Where(c=>IsFinger(c.Key));
    internal bool MixedFingers=>FingerControls.Select(c=>FollowSpeedCatalog.Value(Values,c.Key)).Distinct().Skip(1).Any();
    internal float Get(string key)=>key==AllFingers?FingerControls.Average(c=>FollowSpeedCatalog.Value(Values,c.Key)):FollowSpeedCatalog.Value(Values,key);
    internal void Set(string key,float slider)
    {
        float value=FollowSpeedCatalog.FromSlider(slider);
        if(key==AllFingers)
        {
            bool changed=false;foreach(var control in FingerControls)
                if(FollowSpeedCatalog.Value(Values,control.Key)!=value){Values[control.Key]=value;changed=true;}
            if(changed)dirtyAt=Environment.TickCount64;return;
        }
        if(Get(key)==value)return;
        Values[key]=value;dirtyAt=Environment.TickCount64;
    }
    internal void Tick()
    {
        if(saving is {IsFaulted:true}){Diagnostics.Log(saving.Exception!);saving=null;dirtyAt=Environment.TickCount64;}
        if(dirtyAt==0||Environment.TickCount64-dirtyAt<600||saving is {IsCompleted:false})return;
        string file=path,json=JsonSerializer.Serialize(Values,new JsonSerializerOptions{WriteIndented=true});dirtyAt=0;
        saving=Task.Run(()=>Write(file,json));
    }
    private static void Write(string file,string json)
    {Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",json);File.Move(file+".tmp",file,true);}
    internal void Flush()
    {
        try {saving?.GetAwaiter().GetResult();if(dirtyAt!=0&&path.Length>0)Write(path,JsonSerializer.Serialize(Values,new JsonSerializerOptions{WriteIndented=true}));dirtyAt=0;}
        catch(Exception ex){Diagnostics.Log(ex);}
    }
}
