using System.Numerics;
namespace Johnny5VR;
internal sealed record ArcadePad(string Name,Vector3 Centre,float Radius=.86f);
internal sealed class ArcadeTeleport
{
    internal readonly IReadOnlyList<ArcadePad> Pads;
    private readonly IReadOnlyList<ArcadeObstacle> obstacles;
    private bool held,used,cancelled;
    private float duration;
    internal bool CanTeleport=true;
    internal bool Aiming {get;private set;}
    internal ArcadePad? Selected {get;private set;}
    internal Vector3? Landing {get;private set;}
    internal Vector3[] Arc {get;private set;}=[];
    internal ArcadePad? Commit {get;private set;}
    internal ArcadeTeleport(IReadOnlyList<ArcadePad> pads,IReadOnlyList<ArcadeObstacle> obstacles){Pads=pads;this.obstacles=obstacles;}
    internal ControllerReading Route(ControllerReading input,bool allowed,bool menuVisible,float seconds,Matrix4x4? rightHand)
    {
        Commit=null;
        if(menuVisible){Cancel();held=input.Menu;return input;}
        if(!allowed){Cancel();cancelled|=input.Menu;held=input.Menu;return input with{Menu=false};}
        bool press=input.Menu;
        if(press)
        {
            if(!held){duration=0;used=false;cancelled=false;}
            duration+=Math.Clamp(seconds,0,.05f);held=true;
            Aiming=duration>=.25f&&!cancelled&&CanTeleport;
            if(Aiming&&rightHand.HasValue){used=true;Trace(rightHand.Value,input.Right);}
            else {Selected=null;Landing=null;Arc=[];}
            return input with{Menu=false,Right=Vector2.Zero,Move=Vector2.Zero,Select=false};
        }
        bool tap=held&&!used&&!cancelled&&duration<.25f;
        if(held&&used&&!cancelled)Commit=Selected;
        bool consumed=used;held=false;used=false;duration=0;Aiming=false;Selected=null;Landing=null;Arc=[];cancelled=false;
        return input with{Menu=tap,Select=input.Select&&!consumed};
    }
    private void Cancel(){Aiming=false;Selected=null;Landing=null;Arc=[];Commit=null;cancelled=true;}
    internal void Trace(Matrix4x4 hand,Vector2 stick)
    {
        var position=Vector3.Transform(new(0,0,-.04f),hand);
        var direction=Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ,hand)+Vector3.TransformNormal(Vector3.UnitX,hand)*stick.X*.25f);
        var velocity=direction*(10+6*Math.Clamp(stick.Y,-1,1));var points=new List<Vector3>{position};
        Selected=null;Landing=null;
        for(int i=1;i<=150;i++)
        {
            float t=i*.025f;var next=position+velocity*t-Vector3.UnitY*(4.905f*t*t);var previous=points[^1];var delta=next-previous;float length=delta.Length();var aim=delta/Math.Max(.00001f,length);float nearest=length;bool hit=false;
            foreach(var obstacle in obstacles)if(ArcadeGame.RayBox(previous,aim,obstacle.Minimum,obstacle.Maximum,nearest,out float distance))
            {nearest=distance;hit=true;}
            if(next.Y<=0&&previous.Y>0){float ground=length*previous.Y/(previous.Y-next.Y);if(ground<nearest){nearest=ground;hit=true;}}
            if(hit)
            {
                var end=previous+aim*nearest;points.Add(end);Landing=end;
                Selected=Pads.FirstOrDefault(p=>Math.Abs(p.Centre.Y-end.Y)<.08f&&Vector2.Distance(new(p.Centre.X,p.Centre.Z),new(end.X,end.Z))<=p.Radius);
                break;
            }
            points.Add(next);if(next.Y<-.2f)break;
        }
        Arc=points.ToArray();
    }
    internal FlyingBolt[] Beams()
    {
        if(!Aiming)return [];
        var color=Selected!=null?new Vector4(.12f,1,.35f,.9f):new Vector4(1,.25f,.12f,.8f);
        var beams=Arc.Zip(Arc.Skip(1),(a,b)=>new FlyingBolt(a,b,color,0,a)).ToList();
        if(Landing is {} landing)for(int i=0;i<36;i++)
        {Vector3 P(int n)=>landing+new Vector3(MathF.Cos(n*MathF.Tau/36)*.3f,.025f,MathF.Sin(n*MathF.Tau/36)*.3f);beams.Add(new(P(i),P(i+1),color,0,landing));}
        return beams.ToArray();
    }
}
