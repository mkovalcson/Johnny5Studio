using System.Text.Json;
namespace Johnny5VR;
internal sealed class ArcadeSettings
{
    public string Introduction {get;set;}="";
    public string HitAudio {get;set;}="";
    public string DisassembleAudio {get;set;}="";
    public string ReassembleAudio {get;set;}="";
    public string DalekAudio {get;set;}="";
    internal static string FileFor(string config)=>Path.Combine(config,"VR","LaserRange.json");
    internal static ArcadeSettings Load(string config)
    {try{return File.Exists(FileFor(config))?JsonSerializer.Deserialize<ArcadeSettings>(File.ReadAllText(FileFor(config)))??new():new();}catch(Exception ex){Diagnostics.Log(ex);return new();}}
    internal void Save(string config)
    {
        if(Path.IsPathRooted(Introduction))Introduction=Path.GetRelativePath(config,Introduction);
        foreach(var cue in Enum.GetValues<ArcadeCue>()){string audio=Audio(cue);if(Path.IsPathRooted(audio))SetAudio(cue,Path.GetRelativePath(config,audio));}
        string file=FileFor(config);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(file+".tmp",file,true);}
    internal string Audio(ArcadeCue cue)=>cue switch{ArcadeCue.Hit=>HitAudio,ArcadeCue.Disassemble=>DisassembleAudio,ArcadeCue.Reassemble=>ReassembleAudio,_=>DalekAudio};
    internal void SetAudio(ArcadeCue cue,string path){switch(cue){case ArcadeCue.Hit:HitAudio=path;break;case ArcadeCue.Disassemble:DisassembleAudio=path;break;case ArcadeCue.Reassemble:ReassembleAudio=path;break;default:DalekAudio=path;break;}}
}
