using System.Numerics;
namespace Johnny5VR;

internal static class SpecialEffectChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        string file=Path.Combine(settings.ConfigFolder,"Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        var rig=new AnimationRig(file,settings.ConfigFolder);
        check(rig.FallApart is {Frames:>100},"Full robot contains its baked disassembly clip.");
        var clip=rig.FallApart!;EffectCue[] fall=[new("FallApart",1,"{}"),new("FallApart",1+clip.Duration+.5,"{\"reverse\":true}")];
        check(clip.FrameAt(fall,0)==null&&clip.FrameAt(fall,1)==0,"Disassembly begins exactly at its authored time.");
        check(clip.FrameAt(fall,1+clip.Duration+.1)==clip.Frames-1,"Disassembly holds the final pile of parts.");
        check(clip.FrameAt(fall,2+2*clip.Duration)==null,"Reassembly releases normal articulated control at completion.");
        rig.SetEffects(fall,0);rig.Apply(new());var whole=rig.Palette.ToArray();
        rig.SetEffects(fall,1+clip.Duration);rig.Apply(new());var apart=rig.Palette.ToArray();
        check(apart.Zip(whole).Count(p=>p.First!=p.Second)>20,"Baked disassembly transforms move the real robot's parts.");
        rig.SetEffects(fall,2+2*clip.Duration);rig.Apply(new());
        check(rig.Palette.Zip(whole).All(p=>p.First==p.Second),"Reassembly restores exactly the live robot pose.");
        rig.SetEffects(fall,2);rig.Apply(new());var mid=rig.Palette.ToArray();rig.SetEffects(fall,3);rig.Apply(new());rig.SetEffects(fall,2);rig.Apply(new());
        check(rig.Palette.SequenceEqual(mid),"Backward seeking deterministically restores a disassembly frame.");
        EffectCue[] fire=[new("ToolboxSwap",0,"{\"swap\":false}"),new("HideLaser",0,"{\"hide\":false}"),new("FireLaser",1,"{\"shots\":3,\"interval\":0.25}")];
        rig.SetEffects(fire,1.3);rig.Apply(new());
        check(rig.LaserVisible&&!rig.ToolboxVisible&&rig.Bolts.Count==2,"Firing shows the laser and two overlapping bolts at the expected burst time.");
        check(rig.Bolts.All(b=>Vector3.Distance(b.Tail,b.Tip)<=.401f),"Bolts retain their authored physical length.");
        var tail=rig.Bolts[0].Tail;rig.SetEffects(fire,1.31);rig.Apply(new(){["servo:B_LaserLower"]=(100,0,"B_LaserLower")});
        check(Vector3.Distance(tail,rig.Bolts[0].Tail)<.2f,"A bolt in flight does not swing when the laser moves.");
        rig.SetEffects(fire.Concat(new[]{new EffectCue("HideLaser",1.2,"{\"hide\":true}")}).OrderBy(c=>c.Time).ToArray(),1.3);rig.Apply(new());
        check(!rig.LaserVisible&&rig.Bolts.Count==0,"Hidden laser cannot fire.");
        rig.SetEffects(fire.Concat(new[]{new EffectCue("ToolboxSwap",1.2,"{\"swap\":true}")}).OrderBy(c=>c.Time).ToArray(),1.3);rig.Apply(new());
        check(rig.ToolboxVisible&&rig.Bolts.Count==0,"Toolbox substitution suppresses laser bolts.");
        double now=0;using var player=new PlaybackPlayer(()=>now);
        var first=new PlaybackSequence("one","one",2,[],[],false,"",[]){Effects=[new("FallApart",0,"{}"),new("HideLaser",0,"{\"hide\":false}")]};
        player.Load(new("fixture",[first,new("two","two",2,[],[],false,"",[])],[]));player.Toggle();now=2.1;player.Tick();
        check(player.Effects.Any(e=>e.Servo=="FallApart"&&e.Time==-2)&&player.Effects.Any(e=>e.Servo=="HideLaser"),"Movies carry disassembly/accessory state across sequence boundaries.");
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"special-effects-check.txt"),"PASS: baked disassembly/reassembly, reverse seeking, laser bursts, attachment visibility and movie carry state.");
    }
}
