using System.Numerics;
using NAudio.Wave;
namespace Johnny5VR;
internal static class ArcadeCombatChecks
{
    internal static void Run()
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
        var input=ControllerReading.Failed("");(Vector3,Vector3) Gun(ArcadeGame g)=>(g.Position+Vector3.UnitY,-Vector3.UnitZ);
        void Tick(ArcadeGame g,int n){for(int i=0;i<n;i++)g.Step(input,true,.05f,.5,.04,Gun);}
        var game=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ){AnimationSeconds=.5};var events=new List<ArcadeCue>();game.Event=events.Add;
        for(int life=3;life>0;life--)
        {
            game.TakeHit();Check(game.Damage==1&&game.Lives==life,"First hit: life "+life);game.TakeHit();Check(game.Damage==1,"Invulnerability prevents stacked same-frame hits.");Tick(game,18);
            game.TakeHit();Check(game.Damage==2&&game.Lives==life,"Second hit: life "+life);Tick(game,18);game.TakeHit();
            Check(game.Lives==life-1&&game.Phase==ArcadePhase.Disassembling,"Third hit costs exactly one life.");
            Tick(game,36);
            if(life>1)
            {Check(game.Phase==ArcadePhase.Reassembling&&game.Protected&&game.Position.Z>0,"Reassembly starts in the safe bay.");Check(events.Count(e=>e==ArcadeCue.Reassemble)==3-life,"Alive audio waits for completed reassembly.");Tick(game,12);Check(game.Phase==ArcadePhase.Playing&&game.Protected&&game.Damage==0,"New life has full health and protection.");game.TakeHit();Check(game.Damage==0,"Spawn shield blocks damage.");Tick(game,102);}
            else Check(game.Phase==ArcadePhase.GameOver,"Third lost life ends the game after disassembly.");
        }
        Check(events.Count(e=>e==ArcadeCue.Hit)==9&&events.Count(e=>e==ArcadeCue.Disassemble)==3&&events.Count(e=>e==ArcadeCue.Reassemble)==2,"Each damage/life phase emits its matching audio event.");
        var enemy=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ){CombatEnabled=true};enemy.Targets.Clear();var drone=new ArcadeTarget(new(0,0,-5),0){NextFire=.01};enemy.Targets.Add(drone);
        Tick(enemy,1);Check(drone.WarningUntil>enemy.Time&&enemy.EnemyShots.Count==0,"Drone warns before firing.");Tick(enemy,15);Check(enemy.EnemyShots.Count>0&&enemy.EnemyShots[0].Direction.Z>0,"Drone fires toward Johnny after its warning.");
        Tick(enemy,25);Check(enemy.Damage==1,"Enemy projectile can hit Johnny.");
        var cover=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ,[new(new(-1,0,-3),new(1,2,-2))]){CombatEnabled=true};cover.Targets.Clear();cover.Targets.Add(new(new(0,0,-5),0){NextFire=.01});Tick(cover,100);
        Check(cover.Damage==0&&cover.EnemyShots.Count==0,"Cover prevents enemy fire through obstacles.");
        var presentation=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);presentation.BeginPresentation(true);Tick(presentation,10);Check(presentation.Phase==ArcadePhase.Introduction&&presentation.Heading==0,"Ground-level introduction waits for the sequence.");
        presentation.FinishIntroduction(2,0,0);Tick(presentation,102);Check(presentation.Phase==ArcadePhase.Playing&&Math.Abs(presentation.X)<.001&&Math.Abs(presentation.Heading-180)<.001,"After instructions Johnny reaches his start and faces the range.");
        var skipped=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);skipped.BeginPresentation(false);Check(skipped.Phase==ArcadePhase.Playing&&skipped.Protected&&skipped.X==0,"No configured introduction starts ready with protection.");
        var pad=new ArcadePad("test",new(0,2,-2));var obstacle=new ArcadeObstacle(new(-1,0,-3),new(1,2,-1));var teleport=new ArcadeTeleport([pad],[obstacle]);
        var hand=Matrix4x4.CreateRotationX(-MathF.PI/6);hand.Translation=new(0,3,0);teleport.Trace(hand,Vector2.Zero);
        Check(teleport.Selected==pad&&teleport.Landing.HasValue&&Math.Abs(teleport.Landing.Value.Y-2)<.01,"Ballistic arc lands on a designated deck.");
        var up=Matrix4x4.CreateRotationX(.5f);up.Translation=new(4,2,0);var down=Matrix4x4.CreateRotationX(-.5f);down.Translation=up.Translation;
        teleport.Trace(down,Vector2.Zero);float low=teleport.Arc.Max(p=>p.Y);teleport.Trace(up,Vector2.Zero);
        Check(teleport.Arc.Max(p=>p.Y)>low+.5f&&teleport.Selected==null,"Pointing higher raises the arc; unmarked ground is invalid.");
        var routed=teleport.Route(input with{Menu=true},true,false,.05f,hand);Check(!routed.Menu,"A press waits to distinguish tap from hold.");
        routed=teleport.Route(input,true,false,.05f,hand);Check(routed.Menu,"A tap opens the menu.");
        for(int i=0;i<7;i++)routed=teleport.Route(input with{Menu=true,Right=new(0,0)},true,false,.05f,hand);
        Check(teleport.Aiming&&teleport.Selected==pad&&routed.Right==Vector2.Zero&&!routed.Select,"Holding A selects a pad and suppresses laser aiming/fire.");
        routed=teleport.Route(input,true,false,.05f,hand);Check(teleport.Commit==pad&&!routed.Menu,"Release over a green pad commits teleport without opening a menu.");
        routed=teleport.Route(input with{Select=true},true,false,.05f,hand);Check(routed.Select,"Normal trigger operation returns after teleport release.");
        for(int i=0;i<7;i++)teleport.Route(input with{Menu=true},true,false,.05f,up);teleport.Route(input,true,false,.05f,up);Check(teleport.Commit==null,"Invalid landing cannot teleport.");
        var movement=new Locomotion();var head=Matrix4x4.CreateTranslation(.7f,1.65f,-.3f);var feet=new Vector3(5,2.2f,-3);movement.Teleport(head,feet,Vector3.Zero);var world=head*movement.Origin;
        Check(Vector2.Distance(new(world.M41,world.M43),new(feet.X,feet.Z))<.001&&Math.Abs(world.M42-3.85)<.001,"Teleport centres the viewer on the deck and preserves standing eye height.");
        var direction=Vector3.TransformNormal(-Vector3.UnitZ,world);direction.Y=0;var expected=-feet;expected.Y=0;
        Check(Vector3.Dot(Vector3.Normalize(direction),Vector3.Normalize(expected))>.999,"Teleport faces Johnny without forcing head pitch.");movement.Restore(Matrix4x4.Identity);Check(movement.Origin==Matrix4x4.Identity,"Leaving game can restore the original room transform.");
        var landscape=new ArcadeLandscape(Vector3.Zero,-Vector3.UnitZ);Check(landscape.Pads.Count==4&&landscape.Pads.All(p=>landscape.Obstacles.Any(o=>Math.Abs(o.Maximum.Y-p.Centre.Y)<.01&&p.Centre.X>=o.Minimum.X&&p.Centre.X<=o.Maximum.X&&p.Centre.Z>=o.Minimum.Z&&p.Centre.Z<=o.Maximum.Z)),"All four pads sit on solid flat obstacles.");
        var file=Path.Combine(Diagnostics.Folder,"arcade-combat-fixture");Directory.CreateDirectory(file);string wav=Path.Combine(file,"event.wav");using(var writer=new WaveFileWriter(wav,new WaveFormat(24000,16,1)))writer.Write(ArcadeAudio.Tone(false),0,ArcadeAudio.Tone(false).Length);
        var settings=new ArcadeSettings{Introduction="Library/Animation/instructions.json"};foreach(var cue in Enum.GetValues<ArcadeCue>())settings.SetAudio(cue,"event.wav");settings.Save(file);var loaded=ArcadeSettings.Load(file);
        Check(loaded.Introduction==settings.Introduction&&Enum.GetValues<ArcadeCue>().All(c=>loaded.Audio(c)=="event.wav"),"Game media settings round-trip independently of calibration.");
        var clips=ArcadeEventAudio.Load(loaded,file).GetAwaiter().GetResult();Check(clips.Count==4&&clips.Values.All(c=>c.Bytes.Length>0),"Custom event clips preload without opening an output device.");
        var defaults=ArcadeEventAudio.Load(new(),file).GetAwaiter().GetResult();Check(defaults.Count==4&&defaults.Values.All(c=>c.Bytes.Length>0),"All default hit/voice resources preload.");
        var current=ViewerSettings.Load();var rig=new AnimationRig(current.Resolve(current.Robot),current.ConfigFolder);
        if(rig.FallApart is {} clip)
        {
            var animated=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ){AnimationSeconds=clip.Duration};animated.TakeHit();Tick(animated,18);animated.TakeHit();Tick(animated,18);animated.TakeHit();
            Tick(animated,26);rig.ApplyGame(animated);Check(rig.FallFrame.HasValue,"Fatal hit drives the real model's baked disassembly clip.");
            for(int i=0;i<Math.Ceiling(clip.Duration/.05)+10&&animated.Phase==ArcadePhase.Disassembling;i++)Tick(animated,1);
            rig.ApplyGame(animated);Check(animated.Phase==ArcadePhase.Reassembling&&rig.FallFrame.HasValue,"Safe respawn drives the real baked clip in reverse.");
        }
        else Check(false,"Current full robot must include its baked FallApart clip.");
        using(var hud=ArcadeHud.Image(game))hud.Save(Path.Combine(Diagnostics.Folder,"arcade-combat-status.png"));
        Console.WriteLine($"PASS: {checks} focused combat/startup/teleport checks. Full suite and headset testing not run.");
    }
}
