using System.Reflection;
using System.Runtime.Loader;
using Johnny5.VrBridge;
using ServoAnimator;

namespace Johnny5VR;

internal static class ReleaseReadinessChecks
{
    internal static void Run()
    {
        int count=0;
        void Check(bool good,string label){if(!good)throw new InvalidOperationException(label);count++;}
        var root=Directory.GetParent(ViewerSettings.Home)!.FullName;
        string App(string folder)=>Path.Combine(root,folder,"bin","Release","net10.0-windows","AnimationEditorPlayer.dll");
        string desktop=App("ServoAnimator/ServoAnimator");
        string editor=App("Johnny5VREditor");
        var player=StudioBuildIdentity.Read(typeof(ViewerWindow).Assembly.Location);
        var vrEditor=StudioBuildIdentity.Read(editor);
        using var stamp=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"ServoAnimator/Build/PROJECT_VERSION.json")));
        string version=stamp.RootElement.GetProperty("version").GetString()!;
        Check(player.Version==version&&player.Protocol==SceneWire.Version.ToString(),"Release and protocol stamps are current.");
        Check(StudioBuildIdentity.Mismatch(player,vrEditor)==null,"Matching Player and Editor builds can connect.");
        Check(StudioBuildIdentity.Mismatch(player,StudioBuildIdentity.Read(desktop))==null,"Desktop uses the same source build identity.");
        Check(StudioBuildIdentity.Mismatch(player,vrEditor with{BuildId=new string('0',64)})!=null,"Same version from another source build is rejected.");
        Check(StudioBuildIdentity.Mismatch(player,vrEditor with{Protocol="11"})!=null,"Same version with another bridge protocol is rejected.");
        Check(StudioBuildIdentity.Mismatch(player,vrEditor with{BuildId=""})!=null,"An unstamped editor is rejected.");
        Check(StudioBuildIdentity.Mismatch(player,vrEditor with{Version="2.16.1"})!=null,"An older release is rejected.");
        Check(StudioBuildIdentity.EditorPath(ViewerSettings.Home,AppContext.BaseDirectory,"Release")
            ==Path.ChangeExtension(editor,".exe"),"Development uses the built Editor, not an old packaging copy.");

        string temporary=Path.Combine(Path.GetTempPath(),"J5ReleaseChecks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string portable=Path.Combine(temporary,"portable");
            Directory.CreateDirectory(portable);
            File.WriteAllText(Path.Combine(portable,"Johnny5VR.portable"),"");
            Check(StudioBuildIdentity.EditorPath(ViewerSettings.Home,portable,"Release")
                ==Path.Combine(portable,"Editor","AnimationEditorPlayer.exe"),"Portable launches retain their bundled Editor path.");

            foreach(string file in new[]{desktop,editor})
            {
                string config=Path.Combine(temporary,Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(config,"Projects"));
                string sequence=Path.Combine(config,"Projects","one.json");
                File.WriteAllText(sequence,"{}");
                var load=new AssemblyLoadContext("recovery-"+Guid.NewGuid(),isCollectible:true);
                try
                {
                    var assembly=load.LoadFromAssemblyPath(file);
                    var type=assembly.GetType("ServoAnimator.EditorRecoverySnapshot",true)!;
                    var snapshot=Activator.CreateInstance(type)!;
                    type.GetProperty("MovieItems")!.SetValue(snapshot,new List<MovieSequenceItem>{
                        new(){FilePath=sequence,Trigger="Ctrl+Shift+5",IdleMood=IdleMood.Curious,DurationSeconds=2,Description="First"},
                        new(){FilePath=sequence,Trigger="F8",IsLooping=true,DurationSeconds=3,Description="Second"}});
                    type.GetMethod("Save")!.Invoke(snapshot,[config]);
                    var restored=type.GetMethod("Load")!.Invoke(null,[config])!;
                    var items=(List<MovieSequenceItem>)type.GetProperty("MovieItems")!.GetValue(restored)!;
                    Check(items.Count==2&&items[0].Trigger=="Ctrl+Shift+5"&&items[1].Trigger=="F8","Recovery preserves distinct keyboard triggers.");
                    Check(items[0].IdleMood==IdleMood.Curious&&items[1].IsLooping,"Recovery retains idle and loop choices.");
                    Check(items[0].FilePath==sequence&&items[0].Description=="First"&&items[0].DurationSeconds==2,"Recovery retains sequence identity and metadata.");
                }
                finally{load.Unload();}
            }
        }
        finally{Directory.Delete(temporary,true);}
        var standalone=new TransportPanelState("Movie",1,false,false,false,"Waiting"){PlayLabel="Pause idle"};
        Check(VrTransportPanel.Buttons(standalone).Any(b=>b.Action==0&&b.Label=="Pause idle")&&
            VrTransportPanel.Buttons(standalone).Any(b=>b.Action==9&&b.Label=="Next"),
            "Compact transport separates Pause idle and Next.");
        using(var menu=new VrMenu(new FollowControlsChecks.NullOverlay()))
        {
            menu.Set("Playback",[new("Pause",()=>{})]);menu.LivePlaybackStatus=true;
            menu.PlaybackStatus("Waiting",true,"Pause idle");
            Check(menu.Rows[0].Label=="Pause idle","VR playback menu shows Pause idle.");
            menu.PlaybackStatus("Paused",false,"Resume idle");
            Check(menu.Rows[0].Label=="Resume idle","VR playback menu updates to Resume idle.");
        }
        ExpressiveIdleChecks.Run();
        Console.WriteLine($"PASS: {count} focused release identity, launch path and editor recovery checks.");
    }
}
