using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections;

string assemblyPath=Path.GetFullPath(args[0]); bool player=args.Contains("--player");
AssemblyLoadContext.Default.Resolving+=(_,name)=>{string p=Path.Combine(Path.GetDirectoryName(assemblyPath)!,name.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type T(string name)=>app.GetType((player?"Johnny5VR.":"ServoAnimator.")+name,true)!;
object? Call(string type,string name,params object[] values)=>T(type).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name==name&&m.GetParameters().Length==values.Length).Invoke(null,values);
var snapshot=new JsonObject();
if(player)
{
    var cases=new[]{new[]{(0d,0d,false),(1d,20d,false),(3d,-10d,false)},new[]{(0d,5d,true),(1d,25d,false),(3d,-5d,false)},new[]{(0d,0d,false),(0d,7d,false),(1d,20d,false)},new[]{(1d,20d,false)},Array.Empty<(double,double,bool)>()};
    var results=new JsonArray();
    foreach(var points in cases)foreach(bool spline in new[]{false,true})
    {
        var array=Array.CreateInstance(T("KeyPoint"),points.Length);
        for(int i=0;i<points.Length;i++)array.SetValue(Activator.CreateInstance(T("KeyPoint"),points[i].Item1,points[i].Item2,points[i].Item3),i);
        var track=Activator.CreateInstance(T("MotionTrack"),"NeckTurn","",array,spline)!;
        var samples=new JsonArray();
        foreach(double time in new[]{-1,0,.001,.5,1,1.5,2.5,3,4})
        {
            var value=T("MotionTrack").GetMethod("At",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(track,new object[]{time});
            samples.Add(value==null?null:JsonSerializer.SerializeToNode(value,value.GetType(),new JsonSerializerOptions{IncludeFields=true}));
        }
        results.Add(samples);
    }
    snapshot["MotionTracks"]=results;
}
else
{
    string temporary=Directory.CreateTempSubdirectory("J5-AnimationBaseline-").FullName;
    string root=Path.Combine(temporary,"Config");Directory.CreateDirectory(root);
    try
    {
        string sequence=Path.Combine(root,"sequence.json"),movie=Path.Combine(root,"movie.json"),library=Path.Combine(root,"library.json");
        File.WriteAllText(sequence,"""
        { // Native editing format, including trim metadata and legacy rotation units
          "description":"Fixture", "audioFilePath":"Projects/voice.wav", "splineServos":["NeckTurn"], "scaleValues":true,
          "commands":[
            {"servo":"NeckTurn","offsetSeconds":2,"value":30,"speed":"N/C","breakSpline":true,"groupId":"neck"},
            {"servo":"NeckTurn","control":"NeckTurn","offsetSeconds":0,"value":0.875,"reason":"fraction"},
            {"servo":"URDFRotate","offsetSeconds":0,"value":25},
            {"servo":"Play","offsetSeconds":1,"value":"Projects/clip.wav","audioTakeId":"take","audioTrim":{"skipCommitted":true,"skipSeconds":.2,"hiddenCommands":[{"servo":"NoseBody","offsetSeconds":.1,"value":10}]}},
            {"servo":"RGBCommand","offsetSeconds":1,"value":"Disable"},
            {"servo":"LeftEyePop","offsetSeconds":0,"value":1800}
          ],
        }
        """.Replace(":.2",":0.2").Replace(":.1",":0.1"));
        var document=Call("AnimationDocument","Load",sequence)!;
        snapshot["Document"]=JsonSerializer.SerializeToNode(document,document.GetType());
        var clone=T("AnimationDocument").GetMethod("Clone")!.Invoke(document,null)!;
        T("AnimationDocument").GetMethod("Save")!.Invoke(clone,new object[]{sequence});snapshot["SavedSequence"]=JsonNode.Parse(File.ReadAllText(sequence));
        File.WriteAllText(movie,"""{"description":"Movie","createdDate":"2000-01-01","sequences":["Projects/a.json","Projects/b.json"],"sequenceLoops":[true],"sequenceTriggers":["","button"]}""");
        var film=Call("MovieDocument","Load",movie)!;T("MovieDocument").GetMethod("Save")!.Invoke(film,new object[]{movie});snapshot["SavedMovie"]=JsonNode.Parse(File.ReadAllText(movie));
        File.WriteAllText(library,"""[{"servo":"FlapsOpen","offsetSeconds":0,"value":20}]""");
        var item=Call("AnimationDocument","LoadLibraryItem",library)!;snapshot["LegacyLibrary"]=JsonSerializer.SerializeToNode(item,item.GetType());
        File.WriteAllText(library,"""{"name":"Old name","category":" Samples ","commands":[],"future":{"keep":true}}""");
        Call("AnimationDocument","UpdateLibraryDescription",library,"New description");Call("AnimationDocument","UpdateLibraryCategory",library," Examples ");snapshot["UpdatedLibrary"]=JsonNode.Parse(File.ReadAllText(library));
        var curves=new JsonArray();
        foreach(var breaks in new[]{new[]{false,false,false},new[]{true,false,false},new[]{false,true,false}})
        {
            double[] t={0,1,3},v={0,20,-10};var m=(double[])Call("SplineUtil","Tangents",t,v,breaks)!;
            curves.Add(JsonSerializer.SerializeToNode(new { Tangents=m, Values=new[]{-1d,0,.25,.5,1,1.25,2.5,3,4}.Select(x=>(double)Call("SplineUtil","Eval",t,v,m,x,breaks)!).ToArray() }));
        }
        snapshot["SplineMath"]=curves;
        snapshot["Names"]=JsonSerializer.SerializeToNode(Enum.GetValues(T("ServoNames")).Cast<object>().Select(s=>Call("ServoCommand","DisplayName",s)).ToArray());
    }
    finally { if(Path.GetFileName(temporary).StartsWith("J5-AnimationBaseline-")&&temporary.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(temporary,true); }
}
string baseline=args[1];
if(args.Contains("--capture")){File.WriteAllText(baseline,snapshot.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Captured "+(player?"Player evaluation":"editor animation")+" baseline.");}
else {
    var expected=JsonNode.Parse(File.ReadAllText(baseline));
    if(!player && snapshot["Names"] is JsonArray names && expected!["Names"] is JsonArray oldNames && names.Count==oldNames.Count+5)
    {
        var additions=new[]{"B_ShoulderRotate","B_ShoulderHug","B_Elbow","B_WristPitch","B_WristRoll"};
        var appended=Enum.GetValues(T("ServoNames")).Cast<object>().Skip(oldNames.Count).Select(s=>s.ToString()).ToArray();
        if(!appended.SequenceEqual(additions))throw new Exception("Unexpected appended command identities.");
        while(names.Count>oldNames.Count)names.RemoveAt(names.Count-1);
    }
    if(!JsonNode.DeepEquals(expected,snapshot)) {File.WriteAllText(baseline+".actual.json",snapshot.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));throw new Exception("Animation compatibility changed.");}
    if(!player && T("AnimationDocument").Assembly.GetName().Name!="J5Studio.Animation")throw new Exception("Animation documents are not shared.");
    if(args.Contains("--shared")) SharedBehaviorChecks.Run();
    Console.WriteLine("PASS: "+(player?"Player keypoint evaluation, endpoint/stamp behavior, duplicate times and spline breaks.":"editor command/document serialization, cloning, movies, legacy libraries, names and spline math."));
}
