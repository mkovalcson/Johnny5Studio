using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NAudio.Wave;

namespace Johnny5VR;

internal static class PlaybackChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        var folder=Path.Combine(Diagnostics.Folder,"Playback Fixtures");Directory.CreateDirectory(Path.Combine(folder,"Projects"));
        string audio=Path.Combine(folder,"Projects","test.wav");
        using(var wave=new WaveFileWriter(audio,new WaveFormat(16000,16,1)))wave.Write(new byte[32000],0,32000);
        string sequence=Path.Combine(folder,"Projects","first.json");
        File.WriteAllText(sequence,"""
        { // Native editor values and committed trim bounds
          "description":"Fixture", "audioFilePath":"Projects/test.wav", "splineServos":["NeckTurn"],
          "commands":[
            {"servo":"NeckTurn","offsetSeconds":0,"value":-100},
            {"servo":"NeckTurn","offsetSeconds":2,"value":100},
            {"servo":"FlapsOpen","control":"BrowLeftTopOpen","offsetSeconds":0.5,"value":25},
            {"servo":"Play","offsetSeconds":0.2,"value":"Projects/test.wav","audioTrim":{"skipCommitted":true,"skipSeconds":0.2,"endCommitted":true,"endSeconds":0.7}},
          ]
        }
        """);
        File.WriteAllText(Path.Combine(folder,"Projects","second.json"),"""{"commands":[{"servo":"NoseBody","offsetSeconds":1,"value":10}]}""");
        File.WriteAllText(Path.Combine(folder,"Projects","movie.json"),"""{"sequences":["Projects/first.json","Projects/second.json"],"sequenceLoops":[true,false],"sequenceTriggers":["","button"]}""");
        var before=SHA256.HashData(File.ReadAllBytes(sequence));
        var catalog=PlaybackLibrary.Scan(folder);
        check(catalog.Length==3&&catalog.Count(e=>e.Movie)==1,"Library identifies movies and sequences from their document contents.");
        var show=PlaybackLibrary.Load(folder,catalog.Single(e=>e.Movie));
        check(show.Sequences.Length==2&&show.Sequences[0].Loop&&show.Sequences[1].Trigger=="button","Movie preserves sequence order, looping and trigger metadata.");
        check(show.Sequences[0].Duration==2,"Sequence duration includes its last command when saved duration is zero.");
        check(show.Sequences[0].Audio.Length==2&&Math.Abs(show.Sequences[0].Audio[1].Length-.5)<1e-8&&show.Sequences[0].Audio[1].Skip==.2,"Audio primary and inserted clip retain committed source trim bounds.");
        check(show.Sequences[0].AudioAt(.3)==show.Sequences[0].Audio[1]&&show.Sequences[0].AudioAt(.9)==show.Sequences[0].Audio[0],"Inserted audio wins an overlap and the primary track resumes after it ends.");
        var neck=show.Sequences[0].Tracks.Single(t=>t.Servo=="NeckTurn");
        check(neck.At(-1)==null&&neck.At(1)?.Value==0&&neck.At(3)?.Value==100,"Hermite evaluation preserves native values and holds its final endpoint.");
        var irregular=new MotionTrack("NeckTurn","",[new(0,0,false),new(1,20,false),new(3,-10,false)],true);
        check(Math.Abs(irregular.At(.5)!.Value.Value-12.9166666667)<1e-6,"Nonuniform spline tangents match the editor's Hermite finite differences.");
        var broken=new MotionTrack("NeckTurn","",[new(0,10,true),new(1,90,false)],true);
        check(broken.At(.9)?.Value==10&&broken.At(1)?.Value==90,"BreakSpline holds until the next authored point.");
        var step=new MotionTrack("NeckTurn","",[new(0,10,false),new(1,90,false)],false);
        check(step.At(.5)?.Value==10,"Controls without splines hold their most recent command.");
        check(show.Sequences[0].Tracks.Any(t=>t.Control=="BrowLeftTopOpen"),"Individual child controls survive loading.");
        check(SHA256.HashData(File.ReadAllBytes(sequence)).SequenceEqual(before),"Playback leaves source sequence files unchanged.");
        File.WriteAllText(Path.Combine(folder,"scene.json"),"""{"RobotModelFile":"Models/Robots/Johnny5Head/johnny5_head.urdf","Settings":{"Background":{"Stage":{"ModelFile":"Models/Scenery/stage.urdf"},"BackgroundMode":"Studio"}}}""");
        var sceneSelection=PlaybackLibrary.Scene(folder,"scene.json","old robot","old stage");
        check(sceneSelection.Robot=="Models/Robots/Johnny5Head/johnny5_head.urdf"&&sceneSelection.Backdrop=="Models/Scenery/stage.urdf","Saved scenes choose their robot and 3D backdrop without changing VR camera settings.");
        bool traversal=false;try{LibraryJson.Resolve(folder,"../outside.json");}catch(InvalidDataException){traversal=true;}
        check(traversal,"Relative asset paths cannot escape the Config root.");
        var silent=show with{Sequences=show.Sequences.Select(s=>s with{Audio=[]}).ToArray()};
        double now=0;using var player=new PlaybackPlayer(()=>now);player.Load(silent);player.Toggle();now=.4;player.Tick();
        check(player.Running&&Math.Abs(player.Position-.4)<1e-6,"Playback uses elapsed time rather than frame count.");
        player.Toggle();now=10;check(Math.Abs(player.Position-.4)<1e-6,"Pause freezes the timeline clock.");
        player.Toggle();now=10.6;player.Tick();check(Math.Abs(player.Position-1)<1e-6,"Resume continues from the paused timeline position.");
        now=12;player.Tick();check(player.Index==0&&player.Position<.001,"A looping movie sequence restarts until Next is selected.");
        player.Next();check(player.Index==1&&player.Running,"Next advances through a loop and a manual trigger.");
        check(player.Pose().ContainsKey("servo:NeckTurn"),"Movie carries the previous sequence's final pose into the next sequence.");
        now+=2;player.Tick();check(!player.Running&&player.Index==1,"The final sequence stops without wrapping the movie.");
        player.Stop();check(player.Index==0&&player.Position==0&&!player.Running,"Stop rewinds the movie and resets its carried pose.");
        silent=silent with{Sequences=silent.Sequences.Select(s=>s with{Loop=false}).ToArray()};player.Load(silent);player.Toggle();now+=3;player.Tick();
        check(!player.Running&&player.Index==0,"An authored trigger waits for Next instead of being silently discarded.");
        var menu=new MenuNavigation();ControllerReading Input(bool a=false,bool trigger=false,bool b=false,float y=0)=>new(new(0,y),Vector2.Zero,"","",true,"",a,trigger,b);
        var open=menu.Step(Input(a:true),true,3);check(open.Opened&&menu.Visible,"Index A opens the playback menu.");
        menu.Step(Input(a:true),true,3);check(menu.Visible,"Holding A does not repeatedly toggle the menu.");
        menu.Step(Input(y:-1),true,3);menu.Step(Input(y:-1),true,3);check(menu.Selected==1,"A held stick advances only once until it returns to neutral.");
        menu.Step(Input(),true,3);menu.Step(Input(y:-1),true,3);check(menu.Selected==2,"Returning to neutral rearms menu navigation.");
        check(menu.Step(Input(trigger:true),true,3).Activate&&!menu.Step(Input(trigger:true),true,3).Activate,"Trigger selection uses a rising edge.");
        menu.Step(Input(),false,3);check(!menu.Step(Input(trigger:true),false,3).Activate,"A SteamVR focus loss prevents menu activation.");
        menu.Step(Input(),true,3);menu.Step(Input(a:true),true,3);check(!menu.Visible,"A closes the menu.");
        using(var bitmap=VrMenu.Draw("Johnny 5 · Movies","A: show / hide · Trigger: select",["Demo movie","Choose sequence","Play / Pause"],1))bitmap.Save(Path.Combine(Diagnostics.Folder,"vr-menu-preview.png"));
        string robot=settings.Resolve(settings.Robot);var rig=new AnimationRig(robot,settings.ConfigFolder);
        rig.SetPlacement(Matrix4x4.CreateRotationY(.2f)*Matrix4x4.CreateTranslation(0,0,-2.5f));
        check(rig.Palette.All(m=>m==Matrix4x4.Identity),"A loaded rig starts at the same rest pose as the static viewer.");
        rig.Apply(new(){["servo:NeckTurn"]=(60,0,"NeckTurn"),["servo:IrisClose"]=(100,0,"IrisClose")});
        var root=XDocument.Load(robot).Root!;string neckLink=(string)root.Elements("joint").Single(j=>(string?)j.Attribute("name")=="NeckTurn").Element("child")!.Attribute("link")!;
        var mixed=rig.NeckInputs(new(){["servo:NeckNodUp"]=(30,0,"NeckNodUp"),["servo:NeckTiltRight"]=(20,0,"NeckTiltRight")});
        check(Math.Abs(mixed.Nod-30)<1e-8&&Math.Abs(mixed.Tilt-20)<1e-8,"Complementary neck controls mix together before calibrated joint evaluation.");
        mixed=rig.NeckInputs(new(){["control:NeckTiltLeft"]=(40,0,"NeckNodUp"),["control:NeckTiltRight"]=(-40,0,"NeckNodUp")});
        check(Math.Abs(mixed.Nod)<1e-8&&Math.Abs(Math.Abs(mixed.Tilt)-40)<1e-8,"Direct neck piston commands resolve back into both nod and tilt.");
        check(rig.Palette[rig.Bone(neckLink)]!=Matrix4x4.Identity,"Calibrated NeckTurn produces an animated link transform.");
        check(rig.Apertures.X<0&&rig.Apertures.Y<0,"Closing the iris changes both openings using the current custom calibration.");
        rig.Apply(new(){["servo:IrisClose"]=(-100,0,"IrisClose")});
        check(rig.Apertures.X>0&&rig.Apertures.Y>0,"Opening the iris expands both apertures while retaining the lens geometry.");
        check(rig.Palette.All(m=>float.IsFinite(m.M11+m.M22+m.M33+m.M41+m.M42+m.M43)),"All animated link matrices remain finite.");
        rig.Reset();check(rig.Palette.All(m=>m==Matrix4x4.Identity||m==default(Matrix4x4))&&rig.Apertures==Vector4.Zero,"Reset restores every link and aperture while hiding inactive accessories.");
        var unsupported=rig.Unsupported(new("test",[new("","",1,[new("FallApart","",[new(0,1,false)],false)],[],false,"",[])],[]));
        check(unsupported.SequenceEqual(["FallApart"]),"Unsupported effects are identified before playing.");
        var actual=PlaybackLibrary.Scan(settings.ConfigFolder);check(actual.Any(e=>e.Movie)&&actual.Any(e=>!e.Movie),"The real editor Config contains discoverable movie and sequence entries.");
        var outcomes=new List<string>();int loaded=0;
        foreach(var e in actual)
        {
            try{var s=PlaybackLibrary.Load(settings.ConfigFolder,e);loaded++;outcomes.Add("OK "+e.File+" · "+s.Sequences.Length+" sequence(s)");}
            catch(Exception ex){outcomes.Add("UNAVAILABLE "+e.File+" · "+ex.Message);}
        }
        check(loaded>0,"Existing editor documents load in the standalone player.");
        File.WriteAllLines(Path.Combine(Diagnostics.Folder,"playback-library-check.txt"),outcomes);
        check(loaded==actual.Length,"Every discovered movie/sequence and its referenced audio loads; see playback-library-check.txt for any missing assets.");
    }
}
