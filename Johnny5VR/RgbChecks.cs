using Johnny5VR.Lighting;

namespace Johnny5VR;

internal static class RgbChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        var simulator=new ArduinoRgbTimelineSimulator();
        var commands=new[]{new RgbCommand(0,"SetRGBColor,255,0,0,255,Eyes,Left"),new RgbCommand(.1,"SetRGBColor,0,0,255,128,Vents,Right"),new RgbCommand(1,"ClearAll")};
        var frame=simulator.Evaluate(commands,.2);
        check(frame.LeftEye.All(c=>c.R==255&&c.G==0&&c.B==0),"Eye RGB uses readable Red/Green/Blue color order.");
        check(frame.RightVent.All(c=>c.B is 127 or 128&&c.R==0&&c.G==0)&&frame.LeftVent.All(c=>c==RgbColors.Black)&&frame.RightEye.All(c=>c==RgbColors.Black),"Ring targets, side selection and authored RGB brightness remain independent.");
        frame=simulator.Evaluate(commands,1.1);check(frame.LeftEye.Concat(frame.RightVent).All(c=>c==RgbColors.Black),"ClearAll extinguishes eye and vent glow.");
        frame=simulator.Evaluate(commands,.2);check(frame.LeftEye[0].R==255&&frame.RightVent[0].B>=127,"Backward seek restores the correct RGB frame.");
        simulator=new();var wipe=new[]{new RgbCommand(0,"ColorWipeEyes,0,255,0,255,Eyes,LR,100")};
        var early=simulator.Evaluate(wipe,0).LeftEye;var later=simulator.Evaluate(wipe,.35).LeftEye;
        check(later.Count(c=>c.G>0)>early.Count(c=>c.G>0)&&later.All(c=>c.R==0&&c.B==0),"Animated per-pixel eye patterns advance with timeline time.");
        double now=0;using(var player=new PlaybackPlayer(()=>now))
        {
            var first=new PlaybackSequence("first","first",1,[],[],false,"",[]){RgbCommands=[new(0,"SetRGBColor,0,255,0,255,Both,LR")]};
            var second=new PlaybackSequence("second","second",1,[],[],false,"",[]);
            player.Load(new("RGB movie",[first,second],[]));player.Toggle();now=.2;
            check(player.RgbFrame.LeftEye[0].G==255,"Playback applies authored RGB commands to the current frame.");
            player.Next();check(player.RgbFrame.LeftEye[0].G==255&&player.RgbFrame.RightVent[0].G==255,"RGB colors carry into the next movie sequence until replaced.");
            var paused=player.RgbFrame;player.Toggle();now+=2;check(player.RgbFrame.LeftEye.SequenceEqual(paused.LeftEye),"Paused RGB lighting holds its timeline state.");
        }
        string folder=Path.Combine(Diagnostics.Folder,"RGB Fixtures");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"rgb.json"),"""{"commands":[{"servo":"RGBCommand","offsetSeconds":0,"value":"SetRGBColor,0,0,255,255,Eyes,LR"}],"durationSeconds":1}""");
        var show=PlaybackLibrary.Load(folder,new("rgb.json","RGB",false,""));
        check(show.Sequences[0].RgbCommands.Length==1&&show.Warnings.Length==0,"Standalone library loading recognizes RGB effects as supported.");
        var scene=new UrdfReader().Load(new ViewerSettings{ConfigFolder=settings.ConfigFolder,Backdrop=""});
        check(scene.Opaque.Where(v=>v.Mouth>=128&&v.Mouth<192).Select(v=>v.Mouth).Distinct().Count()==64,"All four 16-pixel light rings are present on the actual robot.");
        check(scene.Glass.Any(v=>v.Mouth==192)&&scene.Glass.Any(v=>v.Mouth==193)&&scene.Glass.Any(v=>v.Mouth==194)&&scene.Glass.Any(v=>v.Mouth==195),"Eye diffusers and both 360-degree vent-tube glow surfaces are present.");
    }
}
