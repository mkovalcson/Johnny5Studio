using System.Text.Json;
using ServoAnimator;
namespace Johnny5VR;
internal sealed class FollowNeutralSettings
{
    internal Dictionary<string,float> Values {get;private set;}=[];
    private string path="";
    private long dirtyAt;
    private Task? saving;
    internal void Load(string config)
    {
        Flush();path=Path.Combine(config,"VR","FollowNeutralPose.json");Values=[];
        try {if(File.Exists(path))Values=JsonSerializer.Deserialize<Dictionary<string,float>>(File.ReadAllText(path))??[];}
        catch(Exception ex){Diagnostics.Log(ex);}
        Values=FollowNeutralCatalog.Sanitize(Values);
    }
    internal float Get(ServoAnimator.FollowNeutralControl control,ServoAnimator.FollowNeutralMapper mapper,bool mirror)=>mapper.Neutral(control,Values,mirror);
    internal void Set(ServoAnimator.FollowNeutralControl control,float slider)
    {Values[control.Key]=control.FromSlider(slider);dirtyAt=Environment.TickCount64;}
    internal void Reset(IEnumerable<ServoAnimator.FollowNeutralControl> controls)
    {foreach(var c in controls)Values.Remove(c.Key);dirtyAt=Environment.TickCount64;}
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
