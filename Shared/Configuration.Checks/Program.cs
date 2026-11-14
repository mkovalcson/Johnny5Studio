using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

string assemblyPath=Path.GetFullPath(args[0]), root=Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving+=(_,name)=>{
    string file=Path.Combine(Path.GetDirectoryName(assemblyPath)!,name.Name+".dll");
    return File.Exists(file)?AssemblyLoadContext.Default.LoadFromAssemblyPath(file):null;
};
var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type T(string name)=>app.GetType("ServoAnimator."+name,true)!;
object? Call(string type,string method,params object[] values)=>T(type).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name==method&&m.GetParameters().Length==values.Length).Invoke(null,values);
JsonNode? Json(object? value)=>value==null?null:JsonSerializer.SerializeToNode(value,value.GetType());
var snapshot=new JsonObject();
foreach(string type in new[]{"ServoConfigEntry","UrdfMotionSetting","UrdfFraming","UrdfStageSettings","UrdfPresentationSettings","UrdfRenderingSettings","UrdfRenderProfile","BackgroundLibraryItem"})
    snapshot[type]=Json(Activator.CreateInstance(T(type)));
snapshot["ServoDefault"]=Json(Call("ServoConfiguration","CreateDefault"));
snapshot["UrdfDefault"]=Json(Call("UrdfConfiguration","CreateDefault"));
snapshot["ModelSettings"]=Json(Call("UrdfModelSettings","Load",root));
foreach(string file in new[]{"ServoConfig.json","URDFconfig.json"})
    if(File.Exists(Path.Combine(root,file)))snapshot[file]=Json(Call(file=="ServoConfig.json"?"ServoConfiguration":"UrdfConfiguration","Load",Path.Combine(root,file)));
var scenes=new JsonObject();
foreach(string folder in new[]{"LibraryScenes","LibraryBackgrounds"})
    if(Directory.Exists(Path.Combine(root,folder)))foreach(var file in Directory.GetFiles(Path.Combine(root,folder),"*.json").Order())
        scenes[Path.GetRelativePath(root,file)]=Json(JsonSerializer.Deserialize(File.ReadAllText(file),T("BackgroundLibraryItem")));
snapshot["Scenes"]=scenes;
string fixtures=Path.Combine(root,"..","ServoAnimator","DevelopmentArtifacts","Checks","shared-configuration-compat");Directory.CreateDirectory(fixtures);
try {
    foreach(int version in new[]{1,2,6,7,9,10}) {
        string file=Path.Combine(fixtures,"legacy-"+version+".json");
        File.WriteAllText(file,"{\"version\":"+version+",\"motions\":[{\"servo\":\"IrisClose\",\"minExtent\":15,\"maxExtent\":85},{\"servo\":\"NeckTiltRight\",\"control\":\"NeckTiltLeft\",\"minExtent\":-10,\"maxExtent\":10,\"reverseOverride\":false}]}");
        var overload=T("UrdfConfiguration").GetMethod("Load",new[]{typeof(string),typeof(string),typeof(string)});
        var model=Call("UrdfModelSettings","Load",root)!;
        snapshot["Legacy"+version]=Json(overload==null?Call("UrdfConfiguration","Load",file):overload.Invoke(null,new[]{file,model.GetType().GetProperty("SelectedFile")!.GetValue(model),root}));
    }
    string partial=Path.Combine(fixtures,"partial-servos.json");
    File.WriteAllText(partial,"{\"servos\":[{\"control\":\"NeckTurn\",\"defaultPwm\":999,\"minPwm\":1,\"maxPwm\":9999}],\"gangDirections\":[],}");
    snapshot["PartialServos"]=Json(Call("ServoConfiguration","Load",partial));
} finally {foreach(var f in Directory.GetFiles(fixtures))File.Delete(f);Directory.Delete(fixtures);}
JsonNode? Normalize(JsonNode? node) {
    if(node is JsonObject obj){var result=new JsonObject();foreach(var p in obj)result[p.Key.Replace(root,"<Config>",StringComparison.OrdinalIgnoreCase)]=Normalize(p.Value);return result;}
    if(node is JsonArray arr)return new JsonArray(arr.Select(Normalize).ToArray());
    if(node is JsonValue value&&value.TryGetValue<string>(out var text))return JsonValue.Create(text.Replace(root,"<Config>",StringComparison.OrdinalIgnoreCase));
    return node?.DeepClone();
}
var normalized=Normalize(snapshot)!;string baseline=args[2];
if(args.Contains("--capture")){File.WriteAllText(baseline,normalized.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Captured pre-extraction settings baseline.");}
else {
    var expected=JsonNode.Parse(File.ReadAllText(baseline));
    if(!JsonNode.DeepEquals(expected,normalized)){
        File.WriteAllText(baseline+".actual.json",normalized.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        var different=snapshot.Select(p=>p.Key).Where(k=>!JsonNode.DeepEquals(expected?[k],normalized[k]));
        throw new Exception("Configuration compatibility changed: "+string.Join(", ",different));
    }
    if(T("UrdfConfiguration").Assembly.GetName().Name!="J5Studio.Configuration")throw new Exception("Settings were not extracted.");
    PersistenceChecks.Run();
    LibraryAudioImportChecks.Run();
    {   // Every editor audio picker must route through the shared importer (IL scan; no window is opened).
        bool Calls(string method,Func<MethodBase,bool> target){
            var m=T("MainWindow").GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)??throw new Exception(method+" not found.");
            byte[] il=m.GetMethodBody()!.GetILAsByteArray()!;
            for(int i=0;i+4<il.Length;i++)if(il[i]==0x28||il[i]==0x6F)try{var r=m.Module.ResolveMethod(BitConverter.ToInt32(il,i+1));if(r!=null&&target(r))return true;}catch{}
            return false;
        }
        bool Named(MethodBase r,string name)=>r.DeclaringType?.Name=="MainWindow"&&r.Name==name;
        if(!Calls("ImportPickedAudio",r=>r.DeclaringType?.Name=="LibraryAudioImport"))throw new Exception("ImportPickedAudio does not use LibraryAudioImport.");
        foreach(string picker in new[]{"InsertAudioFileAtCursor","OpenAudio_Click"})
            if(!Calls(picker,r=>Named(r,"ImportPickedAudio"))||Calls(picker,r=>Named(r,"RequireConfigPath")))
                throw new Exception(picker+" still rejects outside audio instead of copying it through ImportPickedAudio.");
        if(!Calls("EmptyInsertAudio_Click",r=>Named(r,"OpenAudio_Click"))&&!Calls("EmptyInsertAudio_Click",r=>Named(r,"ImportPickedAudio")))
            throw new Exception("The Start a sequence panel's Insert Audio does not reach ImportPickedAudio.");
        Console.WriteLine("PASS: Open Audio, the Start panel's Insert Audio and Insert audio at the cursor copy outside audio through LibraryAudioImport.");
    }
    Console.WriteLine("PASS: settings defaults, current servo/URDF files, saved scene schemas, six legacy migrations and partial servo backfill match the original.");
}
