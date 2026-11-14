using System.Numerics;
namespace Johnny5VR;
internal sealed class ArcadeGeometry
{
    private int version=-1;private Vertex[] targets=[];
    private static readonly Vector4 Bronze=new(.47f,.30f,.12f,1),Dark=new(.065f,.07f,.08f,1),Silver=new(.53f,.56f,.59f,1);
    private static readonly Vertex[] Drone=BuildDrone();
    internal Vertex[] Targets(ArcadeGame game)
    {
        int key=game.Wave*1000+game.Hits;if(version==key)return targets;version=key;
        var output=new List<Vertex>();foreach(var target in game.Targets.Where(t=>t.Alive))
        {
            var world=Matrix4x4.CreateRotationY(target.Yaw)*Matrix4x4.CreateTranslation(target.Position);
            foreach(var v in Drone)output.Add(new(Vector3.Transform(v.Position,world),Vector3.TransformNormal(v.Normal,world),v.Color));
        }
        return targets=output.ToArray();
    }
    internal static Vertex[] Particles(ArcadeGame game,(Vector3 Muzzle,Vector3 Aim) ray)
    {
        var output=new List<Vertex>();
        foreach(var blast in game.Blasts)
        {
            float age=(float)(game.Time-blast.Time);var random=new Random(blast.Seed);
            if(age<.55f)
            {
                float radius=MathF.Sin(age/.55f*MathF.PI)*.75f*blast.Energy+.06f;
                Add(output,Sphere(12,6),new(1,.22f,.015f,1),Matrix4x4.CreateScale(radius)*Matrix4x4.CreateTranslation(blast.Position));
                Add(output,Sphere(12,6),new(1,.8f,.12f,1),Matrix4x4.CreateScale(radius*.65f)*Matrix4x4.CreateTranslation(blast.Position+Vector3.UnitY*.08f));
            }
            for(int i=0;i<22;i++)
            {
                var velocity=new Vector3((float)(random.NextDouble()-.5)*4,(float)random.NextDouble()*3+1,(float)(random.NextDouble()-.5)*4);
                var point=blast.Position+velocity*age*blast.Energy+Vector3.UnitY*(-3.8f*age*age);point.Y=Math.Max(game.Origin.Y+.04f,point.Y);
                float size=(.035f+(i%4)*.012f)*blast.Energy*Math.Clamp((1.6f-age)*2,0,1);
                var matrix=Matrix4x4.CreateScale(size)*Matrix4x4.CreateFromYawPitchRoll(age*(i+1),age*4,age*7)*Matrix4x4.CreateTranslation(point);
                Add(output,Shapes.Box(Vector3.One),i%3==0?new(1,.55f,.05f,1):Bronze,matrix);
            }
        }
        float distance=12;bool hit=false;
        foreach(var obstacle in game.Obstacles)if(ArcadeGame.RayBox(ray.Muzzle,ray.Aim,obstacle.Minimum,obstacle.Maximum,distance,out float wall))distance=wall;
        foreach(var target in game.Targets.Where(t=>t.Alive))
            if(ArcadeGame.RayBox(ray.Muzzle,ray.Aim,target.Position+new Vector3(-.47f,.03f,-.47f),target.Position+new Vector3(.47f,1.63f,.47f),distance,out float d))
            {distance=d;hit=true;}
        var reticle=Matrix4x4.CreateWorld(ray.Muzzle+ray.Aim*(distance-.03f),ray.Aim,Vector3.UnitY);
        var color=hit?new Vector4(.2f,1,.25f,1):new Vector4(.2f,.9f,1,1);
        Add(output,Shapes.Box(new(.13f,.012f,.012f)),color,reticle);Add(output,Shapes.Box(new(.012f,.13f,.012f)),color,reticle);
        return output.ToArray();
    }
    private static Vertex[] BuildDrone()
    {
        var list=new List<Vertex>();
        void Ring(float bottom,float top,float y,float height,Vector4 color)=>Add(list,Frustum(bottom,top,height),color,Matrix4x4.CreateTranslation(0,y,0));
        void Ball(Vector3 size,Vector3 position,Vector4 color)=>Add(list,Sphere(10,6),color,Matrix4x4.CreateScale(size)*Matrix4x4.CreateTranslation(position));
        void Rod(float radius,float length,Vector3 position,Vector4 color)=>Add(list,Frustum(radius,radius,length),color,Matrix4x4.CreateRotationX(MathF.PI/2)*Matrix4x4.CreateTranslation(position));
        Ring(.48f,.47f,.07f,.14f,Dark);Ring(.45f,.28f,.59f,.94f,Bronze);
        Ring(.32f,.32f,1.08f,.1f,Dark);Ring(.29f,.29f,1.19f,.16f,Bronze);
        for(int i=0;i<3;i++)Ring(.255f,.255f,1.31f+i*.035f,.017f,Silver);
        Ball(new(.27f,.24f,.27f),new(0,1.43f,0),Bronze);
        Rod(.034f,.37f,new(0,1.47f,-.38f),Dark);Ball(new(.067f,.067f,.06f),new(0,1.47f,-.59f),new(.12f,.65f,1,1));
        Rod(.028f,.42f,new(.17f,1.16f,-.4f),Silver);Rod(.065f,.06f,new(.17f,1.16f,-.62f),Dark);
        Rod(.025f,.35f,new(-.16f,1.16f,-.38f),Silver);Rod(.085f,.045f,new(-.16f,1.16f,-.57f),Dark);
        for(int row=0;row<4;row++)for(int i=0;i<10;i++)
        {
            float angle=i*MathF.Tau/10,y=.27f+row*.2f,radius=.45f-(y-.12f)/.94f*.17f;
            Ball(new(.052f),new(MathF.Sin(angle)*radius,y,MathF.Cos(angle)*radius),row%2==0?Silver:Bronze);
        }
        return list.ToArray();
    }
    internal static void Add(List<Vertex> list,IEnumerable<Triangle> triangles,Vector4 color,Matrix4x4 matrix)
    {
        Matrix4x4.Invert(matrix,out var inverse);var normals=Matrix4x4.Transpose(inverse);
        foreach(var t in triangles)
        {
            list.Add(new(Vector3.Transform(t.A,matrix),Vector3.Normalize(Vector3.TransformNormal(t.NA,normals)),color));
            list.Add(new(Vector3.Transform(t.B,matrix),Vector3.Normalize(Vector3.TransformNormal(t.NB,normals)),color));
            list.Add(new(Vector3.Transform(t.C,matrix),Vector3.Normalize(Vector3.TransformNormal(t.NC,normals)),color));
        }
    }
    internal static Triangle[] Frustum(float bottom,float top,float height)
    {
        var list=new List<Triangle>();
        for(int i=0;i<16;i++)
        {
            float a=i*MathF.Tau/16,b=(i+1)*MathF.Tau/16;
            Vector3 P(float angle,float radius,float y)=>new(MathF.Sin(angle)*radius,y,MathF.Cos(angle)*radius);
            var p=P(a,bottom,-height/2);var q=P(b,bottom,-height/2);var u=P(a,top,height/2);var v=P(b,top,height/2);
            var n=Vector3.Normalize(new Vector3(MathF.Sin(a),(bottom-top)/height,MathF.Cos(a)));var m=Vector3.Normalize(new Vector3(MathF.Sin(b),(bottom-top)/height,MathF.Cos(b)));
            list.Add(new(p,q,v,n,m,m));list.Add(new(p,v,u,n,m,n));
            list.Add(Shapes.Tri(new(0,-height/2,0),q,p));list.Add(Shapes.Tri(new(0,height/2,0),u,v));
        }
        return list.ToArray();
    }
    internal static Triangle[] Sphere(int segments,int rings)
    {
        var list=new List<Triangle>();Vector3 P(int x,int y){float a=x*MathF.Tau/segments,b=y*MathF.PI/rings;return new(MathF.Sin(b)*MathF.Cos(a),MathF.Cos(b),MathF.Sin(b)*MathF.Sin(a));}
        for(int y=0;y<rings;y++)for(int x=0;x<segments;x++)
        {var a=P(x,y);var b=P(x+1,y);var c=P(x+1,y+1);var d=P(x,y+1);if(y>0)list.Add(new(a,b,c,a,b,c));if(y<rings-1)list.Add(new(a,c,d,a,c,d));}
        return list.ToArray();
    }
}
