using System.Numerics;
namespace Johnny5VR;
internal static class ArcadeChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool good,string name){if(!good)throw new InvalidOperationException(name);count++;}
        Directory.CreateDirectory(Diagnostics.Folder);var input=ControllerReading.Failed("");
        (Vector3,Vector3) Gun(ArcadeGame game)=>(new(0,1.2f,0),-Vector3.UnitZ);
        var game=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);
        Check(game.Remaining==6&&game.Wave==1,"Wave one starts with six targets.");
        var terrain=new ArcadeLandscape(Vector3.Zero,-Vector3.UnitZ);
        Check(terrain.Obstacles.Count>20&&terrain.Vertices.Length>1000,"Scrapyard includes terrain and obstacles.");
        var staged=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ,terrain.Obstacles);
        Check(staged.Targets.All(t=>!terrain.Obstacles.Any(o=>t.Position.X>o.Minimum.X-.4f&&t.Position.X<o.Maximum.X+.4f&&t.Position.Z>o.Minimum.Z-.4f&&t.Position.Z<o.Maximum.Z+.4f)),"Targets do not spawn inside scenery.");
        game.Step(input,true,.02f,.5,.04,Gun);game.Step(input with{Move=new(0,1)},true,.05f,.5,.04,Gun);
        Check(game.X<0&&game.Position.Z<0,"Forward stick drives Johnny forward into the range.");
        Check(game.Travel.Forward>0&&game.Travel.CasterLeft!=0,"Drive animates treads and caster rolling.");
        float heading=game.Heading;game.Step(input with{Move=new(1,0)},true,.05f,.5,.04,Gun);Check(game.Heading<heading,"Stick right turns Johnny right.");
        for(int i=0;i<50;i++)game.Step(input with{Right=new(0,1)},true,.05f,.5,.04,Gun);
        Check(game.Elevation==55,"Aim is clamped at upper limit.");
        for(int i=0;i<70;i++)game.Step(input with{Right=new(0,-1)},true,.05f,.5,.04,Gun);
        Check(game.Elevation==-25,"Aim is clamped at lower limit.");
        double time=game.Time;float x=game.X;game.Step(input with{Move=new(0,1),Select=true},false,1,.5,.04,Gun);
        Check(game.Time==time&&game.X==x&&game.Shots.Count==0,"Menu/focus pause freezes movement, firing and time.");
        var fire=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);fire.Targets.Clear();fire.Targets.Add(new(new(0,0,-5),0));fire.Targets.Add(new(new(0,0,-8),0));
        fire.Step(input with{Select=true},true,.02f,.5,.04,Gun);Check(fire.Fired==0,"Entering with held trigger cannot fire accidentally.");
        fire.Step(input,true,.02f,.5,.04,Gun);fire.Step(input with{Select=true},true,.05f,.5,.04,Gun);
        for(int i=0;i<3;i++)fire.Step(input,true,.05f,.5,.04,Gun);
        Check(fire.Hits==1&&fire.Score==100&&!fire.Targets[0].Alive&&fire.Targets[1].Alive,"Swept shots hit the closest drone once, even across a frame.");
        Check(fire.Blasts.Count==1&&fire.Shots.Count==0,"Hit creates explosion and consumes projectile.");
        var geometry=new ArcadeGeometry();var mesh=geometry.Targets(fire);Check(mesh.Length>0&&ReferenceEquals(mesh,geometry.Targets(fire)),"Intact targets cache static geometry.");
        var particles=ArcadeGeometry.Particles(fire,Gun(fire));Check(particles.Length>100&&particles.All(v=>float.IsFinite(v.Position.X+v.Position.Y+v.Position.Z)),"Explosion debris has finite rendered positions.");
        fire.Step(input with{Select=true},false,.05f,.5,.04,Gun);int fired=fire.Fired;
        fire.Step(input with{Select=true},true,.05f,.5,.04,Gun);Check(fire.Fired==fired,"Resume requires trigger release before another shot.");
        var wall=new ArcadeObstacle(new(-1,0,-3),new(1,2,-2));var blocked=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ,[wall]);blocked.Targets.Clear();blocked.Targets.Add(new(new(0,0,-6),0));
        blocked.Step(input,true,.02f,.5,.04,Gun);blocked.Step(input with{Select=true},true,.05f,.5,.04,Gun);blocked.Step(input,true,.05f,.5,.04,Gun);
        Check(blocked.Hits==0&&blocked.Score==0&&blocked.Shots.Count==0&&blocked.Blasts.Any(b=>b.Energy<1),"Cover blocks shots and produces impact sparks without scoring.");
        for(int i=0;i<100;i++)blocked.Step(input with{Move=new(0,1)},true,.05f,.5,.04,Gun);
        Check(blocked.Position.Z>wall.Maximum.Z+.5f,"Johnny stops before an obstacle.");
        Check(!ArcadeGame.RayBox(new(2,1,0),-Vector3.UnitZ,wall.Minimum,wall.Maximum,30,out _),"Parallel ray outside a wall misses.");
        Check(ArcadeGame.RayBox(new(0,1,0),-Vector3.UnitZ,wall.Minimum,wall.Maximum,30,out float distance)&&Math.Abs(distance-2)<.001,"Ray collision reports correct near distance.");
        var progress=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);
        for(int wave=1;wave<=3;wave++)
        {
            foreach(var target in progress.Targets)target.Alive=false;
            for(int i=0;i<40;i++)progress.Step(input,true,.05f,.5,.04,Gun);
            Check(wave==3?progress.Won:progress.Wave==wave+1,"Wave progression: "+wave);
        }
        var fixture=Path.Combine(Diagnostics.Folder,"arcade-rig-check");Directory.CreateDirectory(fixture);
        string file=Path.Combine(fixture,"laser.urdf");
        File.WriteAllText(file,"<robot name='test'><link name='root'/><link name='body_laser_head_link'/><joint name='LaserLower' type='revolute'><parent link='root'/><child link='body_laser_head_link'/><origin xyz='0 0 1' rpy='0 -1.221730476 0'/><axis xyz='0 1 0'/><limit lower='0' upper='1.221730476'/></joint></robot>");
        var rig=new AnimationRig(file,fixture);var axes=new Matrix4x4(0,0,1,0,1,0,0,0,0,1,0,0,0,0,0,1);rig.SetPlacement(axes);
        var aim=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ);rig.ApplyGame(aim);var level=rig.GameLaserRay();
        Check(rig.HasGameLaser&&rig.LaserVisible&&Math.Abs(level.Aim.Y)<.001,"Game shows the laser and level aim follows the barrel.");
        for(int i=0;i<10;i++)aim.Step(input with{Right=new(0,1)},true,.05f,.5,.04,g=>{rig.ApplyGame(g);return rig.GameLaserRay();});
        var up=rig.GameLaserRay();Check(up.Aim.Y>level.Aim.Y+.2,"Right stick up visibly tilts the barrel and shot upward together.");
        for(int i=0;i<25;i++)aim.Step(input with{Right=new(0,-1)},true,.05f,.5,.04,g=>{rig.ApplyGame(g);return rig.GameLaserRay();});
        Check(rig.GameLaserRay().Aim.Y<-.2,"Downward aiming tilts barrel and shot below level.");
        var doubled=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ,nativeScale:2);doubled.Step(input with{Move=new(0,1)},true,.05f,.5,.04,Gun);
        Check(Math.Abs(doubled.Position.Z+.04f)<.0001f,"Scene scaling preserves drive speed in world metres.");
        var overhead=new ArcadeGame(Vector3.Zero,-Vector3.UnitZ,[new(new(-1,3,-3),new(1,4,-2))]);Check(!overhead.Blocked(new(0,0,-2.5f)),"Gantry beam leaves clearance for Johnny to drive below it.");
        var tone=ArcadeAudio.Tone(false);var boom=ArcadeAudio.Tone(true);Check(tone.Length>0&&boom.Length>tone.Length&&!tone.SequenceEqual(boom),"Distinct laser/explosion sounds exist without external assets.");
        using(var hud=ArcadeHud.Image(fire))hud.Save(Path.Combine(Diagnostics.Folder,"arcade-score.png"));
        RenderPreview(terrain,staged);
        Check(File.Exists(Path.Combine(Diagnostics.Folder,"arcade-landscape.png")),"Actual GPU landscape preview rendered.");
        Console.WriteLine($"PASS: {count} focused game checks. Full suite and headset testing not run.");
    }
    internal static void PreviewRobot()
    {
        Directory.CreateDirectory(Diagnostics.Folder);var settings=ViewerSettings.Load();settings.Backdrop="";
        var scene=new UrdfReader().Load(settings);var rig=scene.Rig!;
        if(!rig.HasGameLaser)throw new InvalidOperationException("Select the full robot before making a game preview.");
        var start=rig.GameBase;start.Y=0;var probe=new ArcadeGame(start,Vector3.UnitZ);rig.ApplyGame(probe);
        var terrain=new ArcadeLandscape(start,rig.GameLaserRay().Aim);var game=new ArcadeGame(start,rig.GameLaserRay().Aim,terrain.Obstacles,rig.GameScale);rig.ApplyGame(game);
        using var window=new Form{ClientSize=new(1440,900),ShowInTaskbar=false};using var renderer=new Renderer(window.Handle,1440,900);
        renderer.Upload(scene with{Opaque=scene.Opaque.Where(v=>v.Bone>0).ToArray(),Glass=scene.Glass.Where(v=>v.Bone>0).ToArray()});
        var targets=new ArcadeGeometry();var teleport=new ArcadeTeleport(terrain.Pads,terrain.Obstacles);var hand=Matrix4x4.CreateRotationX(-MathF.PI/4);hand.Translation=terrain.Pads[0].Centre+new Vector3(0,1,1);for(int i=0;i<7;i++)teleport.Route(ControllerReading.Failed("") with{Menu=true},true,false,.05f,hand);
        renderer.UpdatePose(rig.Palette);renderer.UpdateSecondary(rig,0);renderer.UpdateEffects(teleport.Beams());renderer.UpdateArcade(terrain.Vertices.Concat(targets.Targets(game)).ToArray(),ArcadeGeometry.Particles(game,rig.GameLaserRay()).Concat(ArcadeCombatGeometry.Build(game,teleport)).ToArray());
        var eye=terrain.Pads[0].Centre+Vector3.UnitY*1.65f;
        renderer.Render(null,Matrix4x4.CreateLookAt(eye,start+game.Forward*6+Vector3.UnitY*.8f,Vector3.UnitY),eye);renderer.Screenshot(Path.Combine(Diagnostics.Folder,"arcade-johnny-range.png"));
        Console.WriteLine("Rendered Johnny 5 and the scrapyard range to Diagnostics/arcade-johnny-range.png.");
    }
    private static void RenderPreview(ArcadeLandscape terrain,ArcadeGame game)
    {
        using var window=new Form{ClientSize=new(1280,800),ShowInTaskbar=false};var handle=window.Handle;
        using var renderer=new Renderer(handle,1280,800);
        var floor=new SceneData([new(new(-50,-.1f,-50),Vector3.UnitY,new(.15f,.14f,.13f,1)),new(new(50,-.1f,-50),Vector3.UnitY,new(.15f,.14f,.13f,1)),new(new(0,-.1f,50),Vector3.UnitY,new(.15f,.14f,.13f,1))],[],0,new(-20),new(20),[],2);
        renderer.Upload(floor);var meshes=new ArcadeGeometry();renderer.UpdateArcade(terrain.Vertices.Concat(meshes.Targets(game)).ToArray(),[]);
        Vector3 eye=new(11,8,8);renderer.Render(null,Matrix4x4.CreateLookAt(eye,new(0,0,-8),Vector3.UnitY),eye);renderer.Screenshot(Path.Combine(Diagnostics.Folder,"arcade-landscape.png"));
        Diagnostics.VerifyScreenshot(Path.Combine(Diagnostics.Folder,"arcade-landscape.png"));
    }
}
