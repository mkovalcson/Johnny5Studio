using System.Numerics;
namespace Johnny5VR;
internal enum ArcadePhase { Introduction,Deploying,Playing,Disassembling,Reassembling,GameOver }
internal enum ArcadeCue { Hit,Disassemble,Reassemble,Dalek }
internal sealed partial class ArcadeGame
{
    internal ArcadePhase Phase {get;private set;}=ArcadePhase.Playing;
    internal bool CombatEnabled;
    internal int Lives {get;private set;}=3;
    internal int Damage {get;private set;}
    internal double AnimationSeconds=4;
    internal double PhaseAge=>Time-phaseStart;
    internal bool Protected=>Phase!=ArcadePhase.Playing||Time<protectedUntil;
    internal readonly List<ArcadeShot> EnemyShots=[];
    internal Action<ArcadeCue>? Event;
    internal Action? Deployed;
    private double phaseStart,protectedUntil,nextVoice;
    private float deployX,deployY,deployHeading;
    internal double FallTime=>Phase==ArcadePhase.Disassembling?Math.Max(0,PhaseAge-1.2):Phase==ArcadePhase.GameOver?AnimationSeconds:PhaseAge;
    internal void BeginPresentation(bool intro)
    {
        if(!intro){X=Y=0;Heading=180;Elevation=0;Phase=ArcadePhase.Playing;protectedUntil=Time+5;ResetEnemyTimers();return;}
        X=2/scale;Y=0;Heading=0;Elevation=0;Travel=default;
        Phase=intro?ArcadePhase.Introduction:ArcadePhase.Deploying;phaseStart=Time;
        deployX=X;deployY=Y;deployHeading=Heading;
    }
    internal void FinishIntroduction(float x=0,float y=0,float heading=0)
    {
        X=x;Y=y;Heading=heading;deployX=x;deployY=y;deployHeading=heading;
        Phase=ArcadePhase.Deploying;phaseStart=Time;triggerReady=false;
    }
    private bool AdvancePhase(float dt,double casterRadius,double casterTrail)
    {
        if(Phase==ArcadePhase.Playing)return !Won;
        if(Phase==ArcadePhase.Deploying)
        {
            float fraction=(float)Math.Clamp(PhaseAge/5,0,1),oldX=X,oldY=Y,oldHeading=Heading;
            float travel=Math.Clamp(fraction*2-1,0,1),turn=Math.Clamp(fraction*2,0,1);
            X=deployX*(1-travel);Y=deployY*(1-travel);Heading=deployHeading+(180-deployHeading)*turn;
            double distance=Math.Sqrt((X-oldX)*(X-oldX)+(Y-oldY)*(Y-oldY));
            double deltaHeading=Heading-oldHeading,previous=Travel.Caster;
            double target=ServoAnimator.CasterSwivel.TravelDirection(distance,deltaHeading,casterRadius)??Travel.CasterTarget;
            double caster=casterTrail>.001?ServoAnimator.CasterSwivel.Roll(previous,distance,deltaHeading,casterRadius,casterTrail):ServoAnimator.CasterSwivel.Advance(previous,target,dt);
            var wheels=ServoAnimator.CasterWheelTravel.Step(previous,caster,distance,deltaHeading,casterRadius,.034875);
            Travel=Travel with{Forward=Travel.Forward+distance,Heading=Heading,Caster=caster,CasterTarget=target,CasterLeft=Travel.CasterLeft+wheels.Left,CasterRight=Travel.CasterRight+wheels.Right};
            if(fraction>=1){Phase=ArcadePhase.Playing;phaseStart=Time;protectedUntil=Time+5;ResetEnemyTimers();Deployed?.Invoke();}
        }
        else if(Phase==ArcadePhase.Disassembling&&PhaseAge>=AnimationSeconds+1.2)
        {
            if(Lives==0){Phase=ArcadePhase.GameOver;phaseStart=Time;}
            else {X=1.5f/scale;Y=0;Heading=180;Travel=default;Damage=0;Phase=ArcadePhase.Reassembling;phaseStart=Time;}
        }
        else if(Phase==ArcadePhase.Reassembling&&PhaseAge>=AnimationSeconds)
        {Phase=ArcadePhase.Playing;phaseStart=Time;protectedUntil=Time+5;ResetEnemyTimers();triggerReady=false;Event?.Invoke(ArcadeCue.Reassemble);}
        return false;
    }
    private void ResetEnemyTimers()
    {
        EnemyShots.Clear();int i=0;foreach(var enemy in Targets){enemy.WarningUntil=0;enemy.NextFire=Time+1.8+(i++)*.42;}
    }
    internal void TakeHit()
    {
        if(Protected||Won)return;
        Damage++;protectedUntil=Time+.8;Event?.Invoke(ArcadeCue.Hit);
        if(Damage<3)return;
        Lives--;Phase=ArcadePhase.Disassembling;phaseStart=Time;Shots.Clear();EnemyShots.Clear();triggerReady=false;
        foreach(var enemy in Targets)enemy.WarningUntil=0;
        Event?.Invoke(ArcadeCue.Disassemble);
    }
    private void UpdateEnemies(float dt)
    {
        if(!CombatEnabled||Protected||Won){EnemyShots.Clear();int stagger=0;foreach(var target in Targets){target.WarningUntil=0;target.NextFire=Time+1.8+(stagger++)*.42;}return;}
        int index=0;
        foreach(var enemy in Targets.Where(t=>t.Alive))
        {
            var muzzle=Vector3.Transform(new(.17f,1.16f,-.65f),Matrix4x4.CreateRotationY(enemy.Yaw))+enemy.Position;var target=Position+Vector3.UnitY*1.05f;
            var direction=target-muzzle;float range=direction.Length();direction/=Math.Max(.001f,range);
            bool covered=Obstacles.Any(o=>RayBox(muzzle,direction,o.Minimum,o.Maximum,range,out _));
            if(covered){enemy.WarningUntil=0;enemy.NextFire=Time+1;continue;}
            if(enemy.NextFire==0)enemy.NextFire=Time+2.5+(index++)*.42;
            if(enemy.WarningUntil>0&&Time>=enemy.WarningUntil)
            {EnemyShots.Add(new(muzzle,enemy.ShotAim));enemy.WarningUntil=0;enemy.NextFire=Time+3.8+(index++%4)*.35;}
            else if(enemy.WarningUntil==0&&Time>=enemy.NextFire)
            {
                enemy.WarningUntil=Time+.7;enemy.ShotAim=direction;
                if(Time>=nextVoice){nextVoice=Time+6;Event?.Invoke(ArcadeCue.Dalek);}
            }
        }
        foreach(var shot in EnemyShots.ToArray())
        {
            float distance=Math.Min(5*dt,Range-shot.Distance),nearest=distance;bool blocked=false;
            foreach(var o in Obstacles)if(RayBox(shot.Position,shot.Direction,o.Minimum,o.Maximum,nearest,out float d)){nearest=d;blocked=true;}
            bool hit=RayBox(shot.Position,shot.Direction,Position+new Vector3(-.38f,.12f,-.38f),Position+new Vector3(.38f,1.7f,.38f),nearest,out float robotDistance);
            shot.Position+=shot.Direction*(hit?robotDistance:nearest);shot.Distance+=hit?robotDistance:nearest;
            if(hit){EnemyShots.Remove(shot);TakeHit();if(Phase!=ArcadePhase.Playing)break;}
            else if(blocked||shot.Distance>=Range-.001f)EnemyShots.Remove(shot);
        }
    }
}
