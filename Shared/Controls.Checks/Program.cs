using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections;

// Run each editor in a separate process: both intentionally retain the same assembly name.
string assemblyPath = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) => {
    string candidate = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};
var app = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type T(string name) => app.GetType("ServoAnimator." + name, throwOnError:true)!;
object? Call(string type, string method, params object[] values) => T(type).GetMethod(method)!.Invoke(null, values);
object? Member(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o) ?? o.GetType().GetField(name)?.GetValue(o);
object[] Items(object o) => ((IEnumerable)o).Cast<object>().ToArray();
object[] Enums(string name) => Enum.GetNames(T(name)).Select(n => (object)new { Name=n, Id=Convert.ToInt32(Enum.Parse(T(name),n)) }).ToArray();
object[] Ranges(string method) => Enum.GetValues(T("ServoNames")).Cast<object>().Select(v => {
    var range=Call("ServoCommand",method,v)!;
    return (object)new { Name=v.ToString(), Min=Member(range,"Item1"), Max=Member(range,"Item2") };
}).ToArray();
var gangs=(IDictionary)T("ServoConfiguration").GetField("GangMap")!.GetValue(null)!;
object[] Commands() => Enum.GetValues(T("ServoNames")).Cast<object>().SelectMany(servo => new[]{false,true}.Select(disable => {
    var command=Activator.CreateInstance(T("ServoCommand"))!;
    void Set(string name, object value) => T("ServoCommand").GetProperty(name)!.SetValue(command,value);
    Set("Servo",servo); Set("OffsetSeconds",.125); Set("NumericValue",37); Set("TextValue","example");
    Set("Speed",Enum.Parse(T("ServoSpeed"),disable?"NoChange":"Crawl")); Set("Disable",disable);
    return (object)JsonSerializer.Serialize(command,T("ServoCommand"));
})).ToArray();
var snapshot=new {
    Commands=Commands(),
    ServoNames=Enums("ServoNames"), ServoSpeed=Enums("ServoSpeed"), RobotControls=Enums("RobotControls"),
    Ranges=Ranges("RangeFor"), NominalRanges=Ranges("NominalRangeFor"),
    Gangs=gangs.Keys.Cast<object>().ToDictionary(k=>k.ToString()!, k=>Items(gangs[k]!).Select(v=>v.ToString()).ToArray()),
    Defaults=JsonSerializer.SerializeToNode(Call("ServoConfiguration","CreateDefault"),T("ServoConfiguration")),
    Body=new[]{"Controls","LegacyControls"}.ToDictionary(n=>n,n=>Items(T("BodyControlCatalog").GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).Select(v=>new {
        Servo=Member(v,"Servo")!.ToString(), Joint=Member(v,"Joint"), Min=Member(v,"MinRadians"), Max=Member(v,"MaxRadians"),
        Canonical=Call("BodyControlCatalog","Canonical",Member(v,"Servo")!)!.ToString(),
        HandMembers=Items(Call("BodyControlCatalog","HandMembers",Member(v,"Servo")!)!).Select(m=>Member(m,"Joint")).ToArray()
    }).ToArray())
};
var result=JsonSerializer.SerializeToNode(snapshot)!;
if(args.Length==3 && args[1]=="--capture") {
    File.WriteAllText(args[2],result.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("Captured control compatibility baseline: "+args[2]);
} else {
    var baseline=JsonNode.Parse(File.ReadAllText(args[1]));
    // The five new commands are explicitly additive; compare all original IDs,
    // ranges, serialized commands and catalog entries against the frozen baseline.
    // B_TipToe (2026-11-12) is appended after them and is equally additive.
    var additions=new HashSet<string>{"B_ShoulderRotate","B_ShoulderHug","B_Elbow","B_WristPitch","B_WristRoll","B_TipToe"};
    if(((JsonArray)result["ServoNames"]!).Count==((JsonArray)baseline!["ServoNames"]!).Count+additions.Count)
    {
        foreach(string property in new[]{"ServoNames","Ranges","NominalRanges"})
            foreach(var node in ((JsonArray)result[property]!).Where(n=>additions.Contains(n!["Name"]!.GetValue<string>())).ToArray())((JsonArray)result[property]!).Remove(node);
        foreach(var node in ((JsonArray)result["Commands"]!).Where(n=>additions.Contains(JsonNode.Parse(n!.GetValue<string>())!["servo"]!.GetValue<string>())).ToArray())((JsonArray)result["Commands"]!).Remove(node);
        foreach(var node in ((JsonArray)result["Body"]!["Controls"]!).Where(n=>additions.Contains(n!["Servo"]!.GetValue<string>())).ToArray())((JsonArray)result["Body"]!["Controls"]!).Remove(node);
    }
    if(!JsonNode.DeepEquals(result,baseline)) {
        string mismatch=args[1]+".actual.json";
        File.WriteAllText(mismatch,result.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        throw new Exception("Control compatibility changed. Actual: "+mismatch);
    }
    bool shared=T("ServoNames").Assembly.GetName().Name=="J5Studio.Controls";
    if(args.Contains("--require-shared") && !shared) throw new Exception("Control types still live in editor assembly.");
    // Defaults must not share mutable arrays between configurations or channels.
    var first=Call("ServoConfiguration","CreateDefault")!; var second=Call("ServoConfiguration","CreateDefault")!;
    var channels=Items(Member(first,"Servos")!); var others=Items(Member(second,"Servos")!);
    var speeds=(int[])Member(channels[0],"Speeds")!;
    int old=((int[])Member(others[0],"Speeds")!)[0]; speeds[0]=-999;
    if(((int[])Member(others[0],"Speeds")!)[0]!=old || ReferenceEquals(speeds,Member(channels[1],"Speeds"))) throw new Exception("Defaults share speed arrays.");
    var json=JsonSerializer.Serialize(second,T("ServoConfiguration"));
    var roundTrip=JsonSerializer.Deserialize(json,T("ServoConfiguration"));
    if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(second,T("ServoConfiguration")),JsonSerializer.SerializeToNode(roundTrip,T("ServoConfiguration")))) throw new Exception("Configuration JSON round trip changed.");
    Console.WriteLine("PASS: "+Enum.GetNames(T("ServoNames")).Length+" command identities/ranges and 192 serialized commands, "+gangs.Count+" mappings, stock calibration, body limits/aliases, JSON round trip and default isolation. Shared="+shared);
}
