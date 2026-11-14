using System.Text.Json;
using ServoAnimator;
namespace Johnny5VR;
internal sealed class FollowInputSettings
{
    internal FollowInputFilter Filter {get;}=new();
    private string path="";
    private long dirtyAt;
    private Task? saving;
    internal void Load(string config)
    {
        Flush();path=Path.Combine(config,"VR","FollowInputTuning.json");Filter.Settings.Clear();Filter.Reset();
        try
        {
            if(File.Exists(path)&&JsonSerializer.Deserialize<Dictionary<string,FollowInputAdjustment>>(File.ReadAllText(path)) is {} values)
                foreach(var c in FollowNeutralCatalog.Controls)
                    if(values.TryGetValue(c.Key,out var value))Filter.Settings[c.Key]=value.Normalize();
        }
        catch(Exception ex){Diagnostics.Log(ex);}
    }
    internal void Set(string key,FollowInputAdjustment adjustment)
    {
        adjustment=adjustment.Normalize();
        if(Filter.Get(key)==adjustment)return;
        if(adjustment==FollowInputAdjustment.Default)Filter.Settings.Remove(key);
        else Filter.Settings[key]=adjustment;
        dirtyAt=Environment.TickCount64;
    }
    internal void Tick()
    {
        if(saving is {IsFaulted:true}){Diagnostics.Log(saving.Exception!);saving=null;dirtyAt=Environment.TickCount64;}
        if(dirtyAt==0||Environment.TickCount64-dirtyAt<600||saving is {IsCompleted:false})return;
        string file=path,json=JsonSerializer.Serialize(Filter.Settings,new JsonSerializerOptions{WriteIndented=true});dirtyAt=0;
        saving=Task.Run(()=>Write(file,json));
    }
    private static void Write(string file,string json)
    {Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",json);File.Move(file+".tmp",file,true);}
    internal void Flush()
    {
        try {saving?.GetAwaiter().GetResult();if(dirtyAt!=0&&path.Length>0)Write(path,JsonSerializer.Serialize(Filter.Settings,new JsonSerializerOptions{WriteIndented=true}));dirtyAt=0;}
        catch(Exception ex){Diagnostics.Log(ex);}
    }
}
