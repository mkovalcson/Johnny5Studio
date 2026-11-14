using System.Numerics;
using System.Text.Json;

namespace Johnny5VR;

internal static partial class NavigationTests
{
    internal static void Run(Action<bool,string> check)
    {
        var followDefaults=JsonSerializer.Deserialize<ViewerSettings>("{}")!;
        check(followDefaults.UseEditorFollowBackdrop&&!followDefaults.ShowFollowStatus,"Existing settings default to editor backdrop and hidden overhead Follow status.");
        var followRoundTrip=JsonSerializer.Deserialize<ViewerSettings>(JsonSerializer.Serialize(new ViewerSettings{UseEditorFollowBackdrop=false,ShowFollowStatus=true}))!;
        check(!followRoundTrip.UseEditorFollowBackdrop&&followRoundTrip.ShowFollowStatus,"Follow scene/status preferences survive settings serialization.");
        var head=Matrix4x4.CreateTranslation(1,1.7f,2);
        var walk=new Locomotion();
        for(int i=0;i<90;i++)walk.Step(head,new(0,1),0,1f/90,1.2f,30,true);
        check(Vector3.Distance(walk.Origin.Translation,new(0,0,-1.2f))<.0001f,"Walking speed is in metres/second at 90 Hz.");
        var slower=new Locomotion();for(int i=0;i<30;i++)slower.Step(head,new(0,1),0,1f/30,1.2f,30,true);
        check(Vector3.Distance(walk.Origin.Translation,slower.Origin.Translation)<.0001f,"Movement is independent of frame rate.");
        var still=new Locomotion();still.Step(head,new(.1f,.1f),0,.1f,1.2f,30,true);
        check(still.Origin==Matrix4x4.Identity,"Thumbstick dead zone prevents drifting.");
        var diagonal=new Locomotion();diagonal.Step(head,new(1,1),0,.1f,1.2f,30,true);
        check(Math.Abs(diagonal.Origin.Translation.Length()-.12f)<.0001f,"Diagonal walking cannot exceed configured speed.");
        var pitched=new Locomotion();pitched.Step(Matrix4x4.CreateRotationX(.8f)*head,new(0,1),0,.1f,1.2f,30,true);
        check(Math.Abs(pitched.Origin.Translation.Y)<.0001f,"Looking up/down does not fly or sink.");
        var snap=new Locomotion();snap.Step(head,Vector2.Zero,1,.01f,1.2f,30,true);
        check(Vector3.Distance((head*snap.Origin).Translation,head.Translation)<.0001f,"Snap turns pivot around the current headset position.");
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,snap.Origin);
        check(forward.X>.49f&&forward.Z<-.86f,"Right thumbstick snaps the viewing direction right.");
        var once=snap.Origin;for(int i=0;i<90;i++)snap.Step(head,Vector2.Zero,1,.01f,1.2f,30,true);
        check(snap.Origin==once,"Holding the stick does not repeat snap turns.");
        snap.Step(head,Vector2.Zero,0,.01f,1.2f,30,true);snap.Step(head,Vector2.Zero,1,.01f,1.2f,30,true);
        check(snap.Origin!=once,"Returning the stick to centre rearms snap turning.");
        var before=snap.Origin;snap.Step(head,new(1,1),-1,10,1.2f,30,false);
        check(snap.Origin==before,"Disabled input, dashboard focus loss and tracking loss cannot move the viewer.");
        var paused=new Locomotion();paused.Step(head,new(0,1),0,20,1.2f,30,true);
        check(paused.Origin.Translation.Length()<=.12001f,"A stalled frame cannot cause a large movement jump.");
        snap.Reset();check(snap.Origin==Matrix4x4.Identity,"Reset VR position restores the starting virtual origin.");
        snap.Step(head,Vector2.Zero,1,.01f,1.2f,30,true);check(snap.Origin==Matrix4x4.Identity,"Reset requires neutral before the next snap.");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Input/actions.json")));
        var actions=manifest.RootElement.GetProperty("actions").EnumerateArray().Select(a=>a.GetProperty("name").GetString()).ToHashSet();
        using var binding=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Input/bindings_index.json")));
        check(binding.RootElement.GetProperty("controller_type").GetString()=="knuckles","Default bindings target Valve Index controllers.");
        var sources=binding.RootElement.GetProperty("bindings").GetProperty("/actions/navigation").GetProperty("sources").EnumerateArray().ToArray();
        var sticks=sources.Where(s=>s.GetProperty("mode").GetString()=="joystick").ToArray();
        check(sticks.Length==2&&sticks.All(s=>actions.Contains(s.GetProperty("inputs").GetProperty("position").GetProperty("output").GetString())),"Both thumbstick bindings refer to declared actions.");
        check(sources.All(s=>s.GetProperty("inputs").EnumerateObject().All(p=>actions.Contains(p.Value.GetProperty("output").GetString()))),"Every button and stick binding refers to a declared action.");
        check(sources[0].GetProperty("path").GetString()=="/user/hand/left/input/thumbstick"&&sources[1].GetProperty("path").GetString()=="/user/hand/right/input/thumbstick","Walking and turning use their intended left and right thumbsticks.");
        check(!new ViewerSettings().UseSteamVrBindings,"Direct controller input is the default for a new install.");
        var route=new ControllerInputRoute();int boundReads=0;
        var rawInput=new ControllerReading(new(.2f,.4f),new(.1f,0),"legacy sticks","legacy sticks",true,"");
        var actionInput=new ControllerReading(Vector2.Zero,Vector2.Zero,"bound","bound",true,"",Menu:true,Select:true,Back:true){BoundMenu=true,BoundSelect=true,BoundBack=true,BoundGrab=true};
        ControllerReading Bound(){boundReads++;return actionInput;}
        check(!route.Read(false,()=>rawInput,Bound).Select&&boundReads==0,"Direct mode remains unchanged before body tracking enables actions.");
        route.EnableTracking(true);var trackedInput=route.Read(false,()=>rawInput,Bound);
        check(trackedInput.Menu&&trackedInput.Select&&trackedInput.Back&&boundReads==1,"After skeletal activation, A, B and trigger come from the shared action update even when legacy buttons are empty.");
        check(trackedInput.Move==rawInput.Move&&trackedInput.Right==rawInput.Right,"Shared tracking actions preserve the working direct thumbsticks.");
        route.EnableTracking(false);check(route.Read(false,()=>rawInput,Bound).Back,"Action buttons remain readable after calibration exits.");
        actionInput=actionInput with{Menu=false,Select=false,Back=false};var released=route.Read(false,()=>rawInput,Bound);
        check(!released.Menu&&!released.Select&&!released.Back,"Shared input correctly releases A, B and trigger.");
        var stalePressed=route.Read(false,()=>rawInput with{Menu=true,Select=true,Back=true,Grab=true},Bound);
        check(!stalePressed.Menu&&!stalePressed.Select&&!stalePressed.Back&&!stalePressed.Grab,"Released bound actions override stale pressed legacy button/trigger values.");
        actionInput=actionInput with{Select=true};check(route.Read(false,()=>rawInput,Bound).Select,"A fresh bound trigger press is still captured when the legacy trigger is released.");
        actionInput=actionInput with{Select=false};check(!route.Read(false,()=>rawInput with{Select=true},Bound).Select,"Releasing the bound trigger clears capture even if the legacy analog trigger remains high.");
        actionInput=actionInput with{Move=new(.3f,.5f),Right=new(.8f,0)};
        var remapped=route.Read(false,()=>rawInput with{Move=Vector2.Zero,Right=Vector2.Zero},Bound);
        check(remapped.Move==actionInput.Move&&remapped.Right==actionInput.Right,"Action sticks remain available if SteamVR suppresses the legacy stick values after skeletal activation.");
        actionInput=ControllerReading.Failed("binding unavailable");check(route.Read(false,()=>rawInput with{Back=true},Bound).Back,"Legacy B remains available if action binding fails.");
        var indexA=ControllerInput.DecodeButtons(1UL<<2,true);
        check(indexA.Menu&&!indexA.Grab,"Index legacy A opens the menu without grabbing the editor workspace.");
        check(ControllerInput.DecodeButtons(1UL<<7,true).Menu,"An Index binding exposing generic A remains supported.");
        var squeeze=ControllerInput.DecodeButtons(1UL<<34,true);
        check(squeeze.Grab&&!squeeze.Menu,"Index squeeze grabs the workspace without opening the menu.");
        check(ControllerInput.DecodeButtons(1UL<<35,true).Grab,"Index joystick click can grab the workspace.");
        check(!ControllerInput.DecodeButtons(1UL<<2,false).Menu&&ControllerInput.DecodeButtons(1UL<<2,false).Grab,"Other controllers retain their ordinary grip mapping.");
        var rawMenu=new MenuNavigation();rawMenu.Step(new(Vector2.Zero,Vector2.Zero,"","",true,"",indexA.Menu),true,3);
        check(rawMenu.Visible,"The actual Index legacy A bit reaches and opens menu navigation.");
        check(ControllerInput.FindNavigationAxis([1,3,2,0,0])==2,"Direct input prefers a declared joystick over a trackpad or trigger.");
        check(ControllerInput.FindNavigationAxis([0,3,3,0,0])==-1,"A trigger cannot be mistaken for a walking stick.");
        check(ControllerInput.FindNavigationAxis([1,3,3,0,0])==0,"The user's reported Index legacy mapping uses two-dimensional axis zero.");
        var legacy=new Valve.VR.VRControllerState_t{rAxis0=new(){x=0,y=1},rAxis1=new(){x=1},rAxis2=new(){x=1}};
        check(ControllerInput.DecodeTrigger(legacy,[1,3,3,0,0],true,false),"An Index analog trigger starts capture even when its legacy click bit is absent.");
        legacy.rAxis1=new(){x=.45f};check(ControllerInput.DecodeTrigger(legacy,[1,3,3,0,0],true,true),"Trigger hysteresis keeps a partially released pull stable.");
        legacy.rAxis1=new(){x=.1f};check(!ControllerInput.DecodeTrigger(legacy,[1,3,3,0,0],true,true),"Releasing the Index trigger clears capture despite a squeezed grip axis.");
        check(ControllerInput.DecodeTrigger(new(){ulButtonPressed=1UL<<33},[1,3,3,0,0],true,false),"Legacy trigger click remains supported alongside analog capture.");
        var legacyWalk=new Locomotion();
        legacyWalk.Step(head,ControllerInput.Axis(legacy,ControllerInput.FindNavigationAxis([1,3,3,0,0])),0,.1f,1.2f,30,true);
        check(Vector3.Distance(legacyWalk.Origin.Translation,new Vector3(0,0,-.12f))<.0001f,"The exact reported controller mapping produces forward movement without using trigger values.");
        legacy.rAxis0=new(){x=1,y=0};var legacyTurn=new Locomotion();
        legacyTurn.Step(head,Vector2.Zero,ControllerInput.Axis(legacy,0).X,.01f,1.2f,30,true);
        check(Vector3.TransformNormal(-Vector3.UnitZ,legacyTurn.Origin).X>.49f,"The reported legacy axis also produces right-stick snap turning.");
        var raw=new Valve.VR.VRControllerState_t{rAxis0=new(){x=.9f,y=.9f},rAxis1=new(){x=1},rAxis2=new(){x=-.4f,y=.7f}};
        check(ControllerInput.Axis(raw,2)==new Vector2(-.4f,.7f),"Direct input decodes the selected Index thumbstick axis.");
        raw.rAxis2.x=float.NaN;check(ControllerInput.Axis(raw,2)==Vector2.Zero,"Invalid controller values cannot move the viewer.");
        using var direct=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Input/viewer-direct.vrmanifest")));
        check(!direct.RootElement.GetProperty("applications")[0].TryGetProperty("action_manifest_path",out _),"Direct mode is registered without an action manifest, as required by OpenVR.");
    }
}
