using System.Numerics;
using ServoAnimator;
namespace Johnny5VR;

internal sealed class ArcadeTarget(Vector3 position,float yaw)
{
    internal Vector3 Position=position;
    internal float Yaw=yaw;
    internal bool Alive=true;
    internal double NextFire,WarningUntil;
    internal Vector3 ShotAim;
}
internal sealed class ArcadeShot(Vector3 origin,Vector3 aim)
{
    internal Vector3 Origin=origin,Position=origin,Direction=Vector3.Normalize(aim);
    internal float Distance;
}
internal sealed record ArcadeBlast(Vector3 Position,double Time,int Seed,float Energy=1);
internal sealed partial class ArcadeGame
{
    internal const float DriveSpeed=.8f,TurnSpeed=75,AimSpeed=40,ShotSpeed=35,Range=32;
    internal readonly List<ArcadeTarget> Targets=[];
    internal readonly List<ArcadeShot> Shots=[];
    internal readonly List<ArcadeBlast> Blasts=[];
    internal Vector3 Origin {get;}
    internal Vector3 Forward {get;}
    internal double Time {get;private set;}
    internal float X {get;private set;}
    internal float Y {get;private set;}
    internal float Heading {get;private set;}=180;
    internal float Elevation {get;private set;}
    internal int Score {get;private set;}
    internal int Fired {get;private set;}
    internal int Hits {get;private set;}
    internal int Wave {get;private set;}=1;
    internal int Remaining=>Targets.Count(t=>t.Alive);
    internal bool Won {get;private set;}
    internal bool Paused {get;private set;}
    internal TravelState Travel {get;private set;}
    internal Action<bool>? Sound;
    internal IReadOnlyList<ArcadeObstacle> Obstacles {get;}
    private readonly float scale;
    private double nextShot,clearAt=-1;
    private bool triggerReady;
    internal ArcadeGame(Vector3 origin,Vector3 forward,IReadOnlyList<ArcadeObstacle>? obstacles=null,float nativeScale=1)
    {Origin=origin;Forward=Vector3.Normalize(new(forward.X,0,forward.Z));Obstacles=obstacles??[];scale=Math.Max(.01f,nativeScale);Spawn();}
    internal Vector3 Position=>Origin+(-Forward*X+Vector3.Normalize(Vector3.Cross(Forward,Vector3.UnitY))*Y)*scale;
    internal bool Blocked(Vector3 point)
        =>Obstacles.Any(o=>o.Minimum.Y<Origin.Y+1.9f&&point.X>o.Minimum.X-.55f&&point.X<o.Maximum.X+.55f&&point.Z>o.Minimum.Z-.55f&&point.Z<o.Maximum.Z+.55f)||Targets.Any(t=>t.Alive&&Vector2.Distance(new(point.X,point.Z),new(t.Position.X,t.Position.Z))<1);
    private void Spawn()
    {
        Targets.Clear();Shots.Clear();clearAt=-1;
        var right=Vector3.Normalize(Vector3.Cross(Forward,Vector3.UnitY));int n=4+Wave*2;
        for(int i=0;i<n;i++)
        {
            float angle=(i/(float)(n-1)-.5f)*1.15f,distance=7.5f+(i%3)*1.8f+Wave*.5f;
            var p=Origin+(Forward*MathF.Cos(angle)+right*MathF.Sin(angle))*distance;
            for(int attempt=0;attempt<8&&Obstacles.Any(o=>p.X>o.Minimum.X-.7f&&p.X<o.Maximum.X+.7f&&p.Z>o.Minimum.Z-.7f&&p.Z<o.Maximum.Z+.7f);attempt++)p+=Forward*.9f;
            Targets.Add(new(p,MathF.Atan2(Origin.X-p.X,Origin.Z-p.Z)+MathF.PI));
        }
    }
    internal void Step(ControllerReading input,bool allowed,float seconds,double casterRadius,double casterTrail,Func<ArcadeGame,(Vector3 Muzzle,Vector3 Aim)> applyPose)
    {
        float dt=float.IsFinite(seconds)?Math.Clamp(seconds,0,.05f):0;
        Paused=!allowed;
        if(!allowed){triggerReady=false;applyPose(this);return;}
        Time+=dt;
        if(!AdvancePhase(dt,casterRadius,casterTrail)){applyPose(this);Blasts.RemoveAll(b=>Time-b.Time>1.6);return;}
        if(!input.Select)triggerReady=true;
        float forward=Locomotion.DeadZone(input.Move.Y)*DriveSpeed*dt/scale;
        float turn=-Locomotion.DeadZone(input.Move.X)*TurnSpeed*dt;
        Heading+=turn;float mid=(Heading-turn*.5f)*MathF.PI/180;
        float x=X+forward*MathF.Cos(mid),y=Y+forward*MathF.Sin(mid);
        // Keep Johnny in a recoverable practice area rather than letting him disappear.
        var desired=Origin+(-Forward*x+Vector3.Normalize(Vector3.Cross(Forward,Vector3.UnitY))*y)*scale;
        if((x*x+y*y)*scale*scale<=100&&!Blocked(desired)){X=x;Y=y;}else forward=0;
        Elevation=Math.Clamp(Elevation+Locomotion.DeadZone(input.Right.Y)*AimSpeed*dt,-25,55);
        double before=Travel.Caster,target=ServoAnimator.CasterSwivel.TravelDirection(forward,turn,casterRadius)??Travel.CasterTarget;
        double swivel=casterTrail>.001?ServoAnimator.CasterSwivel.Roll(before,forward,turn,casterRadius,casterTrail):ServoAnimator.CasterSwivel.Advance(before,target,dt);
        var wheels=ServoAnimator.CasterWheelTravel.Step(before,swivel,forward,turn,casterRadius,.034875);
        Travel=new(0,0,Travel.Forward+forward,swivel,target,Heading,Travel.CasterLeft+wheels.Left,Travel.CasterRight+wheels.Right);
        var gun=applyPose(this);
        if(!Won&&Phase==ArcadePhase.Playing&&input.Select&&triggerReady&&Time>=nextShot)
        {
            Shots.Add(new(gun.Muzzle,gun.Aim));Fired++;nextShot=Time+.24;Sound?.Invoke(false);
        }
        foreach(var shot in Shots.ToArray())
        {
            float distance=Math.Min(ShotSpeed*dt,Range-shot.Distance),nearest=distance;ArcadeTarget? hit=null;bool blocked=false;
            foreach(var obstacle in Obstacles)if(RayBox(shot.Position,shot.Direction,obstacle.Minimum,obstacle.Maximum,nearest,out float wallDistance)){nearest=wallDistance;blocked=true;}
            foreach(var enemy in Targets.Where(t=>t.Alive))
                if(RayBox(shot.Position,shot.Direction,enemy.Position+new Vector3(-.47f,.03f,-.47f),enemy.Position+new Vector3(.47f,1.63f,.47f),nearest,out float d))
                {nearest=d;hit=enemy;blocked=false;}
            shot.Position+=shot.Direction*nearest;shot.Distance+=nearest;
            if(hit!=null)
            {
                hit.Alive=false;Hits++;Score+=100;Blasts.Add(new(hit.Position+Vector3.UnitY*.8f,Time,Hits));Shots.Remove(shot);Sound?.Invoke(true);
            }
            else if(blocked){Blasts.Add(new(shot.Position,Time,Fired+200,.22f));Shots.Remove(shot);}
            else if(shot.Distance>=Range-.001f)Shots.Remove(shot);
        }
        UpdateEnemies(dt);
        Blasts.RemoveAll(b=>Time-b.Time>1.6);
        if(Remaining==0&&!Won)
        {
            if(clearAt<0)clearAt=Time;
            if(Time-clearAt>1.8){if(Wave==3)Won=true;else{Wave++;Spawn();}}
        }
    }
    internal static bool RayBox(Vector3 origin,Vector3 direction,Vector3 minimum,Vector3 maximum,float range,out float distance)
    {
        float lo=0,hi=range;
        for(int i=0;i<3;i++)
        {
            float o=i==0?origin.X:i==1?origin.Y:origin.Z,d=i==0?direction.X:i==1?direction.Y:direction.Z;
            float min=i==0?minimum.X:i==1?minimum.Y:minimum.Z,max=i==0?maximum.X:i==1?maximum.Y:maximum.Z;
            if(Math.Abs(d)<1e-7f){if(o<min||o>max){distance=0;return false;}}
            else {float a=(min-o)/d,b=(max-o)/d;lo=Math.Max(lo,Math.Min(a,b));hi=Math.Min(hi,Math.Max(a,b));if(hi<lo){distance=0;return false;}}
        }
        distance=lo;return hi>=lo&&hi>=0;
    }
    internal FlyingBolt[] Bolts()=>EnemyShots.Select(s=>new FlyingBolt(s.Position-s.Direction*Math.Min(.5f,s.Distance),s.Position,new(.35f,.5f,1,1),0,s.Origin)).Concat(Shots.Select(s=>new FlyingBolt(s.Position-s.Direction*Math.Min(.65f,s.Distance),s.Position,new(1,.15f,.04f,1),(float)Math.Max(0,1-s.Distance/2),s.Origin))).ToArray();
    internal string Status=>(Paused?"PAUSED":Phase==ArcadePhase.GameOver?"GAME OVER":Won?"RANGE CLEARED!":Phase!=ArcadePhase.Playing?Phase.ToString().ToUpperInvariant():$"WAVE {Wave} / 3")+$"   LIVES {Lives}   HITS LEFT {Math.Max(0,3-Damage)}   SCORE {Score}"+$"\nTARGETS {Remaining}   AIM {Elevation:+0;-0;0}°"+(Protected&&Phase==ArcadePhase.Playing?"   SHIELD":"");
}
