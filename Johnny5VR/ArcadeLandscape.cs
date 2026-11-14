using System.Numerics;
namespace Johnny5VR;
internal sealed record ArcadeObstacle(Vector3 Minimum,Vector3 Maximum);
internal sealed class ArcadeLandscape
{
    internal readonly List<ArcadeObstacle> Obstacles=[];
    internal readonly List<ArcadePad> Pads=[];
    internal Vertex[] Vertices {get;}
    internal Vector3 Origin {get;}
    private readonly Vector3 forward,right;
    internal Vector3 World(float x,float z,float y=0)=>Origin+right*x+forward*z+Vector3.UnitY*y;
    internal ArcadeLandscape(Vector3 origin,Vector3 direction)
    {
        Origin=origin;forward=Vector3.Normalize(new(direction.X,0,direction.Z));right=Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));
        var vertices=new List<Vertex>();var sand=new Vector4(.36f,.27f,.18f,1);var metal=new Vector4(.18f,.25f,.28f,1);var rust=new Vector4(.38f,.19f,.10f,1);
        var frame=new Matrix4x4(right.X,0,right.Z,0,0,1,0,0,forward.X,0,forward.Z,0,0,0,0,1);
        void Box(float x,float z,float y,Vector3 size,Vector4 color,float yaw=0)
            =>ArcadeGeometry.Add(vertices,Shapes.Box(size),color,Matrix4x4.CreateRotationY(yaw)*frame*Matrix4x4.CreateTranslation(World(x,z,y)));
        void Solid(float x,float z,float width,float depth,float height)
        {
            var center=World(x,z,height*.5f);var ext=new Vector3(Math.Abs(right.X)*width/2+Math.Abs(forward.X)*depth/2,height/2,Math.Abs(right.Z)*width/2+Math.Abs(forward.Z)*depth/2);
            Obstacles.Add(new(center-ext,center+ext));
        }
        Box(0,6,-.06f,new(38,.12f,38),sand);
        // Dusty aggregate: deterministic low-profile patches rather than a texture dependency.
        var random=new Random(773);
        for(int i=0;i<115;i++)
        {
            float x=(float)random.NextDouble()*34-17,z=(float)random.NextDouble()*32-10;
            float shade=.8f+(float)random.NextDouble()*.35f;
            Box(x,z,.002f,new(.35f+(float)random.NextDouble()*1.8f,.005f,.25f+(float)random.NextDouble()),sand*new Vector4(shade,shade,shade,1),i*.7f);
        }
        // A broad S-shaped route through the yard; floor markings are flush and drivable.
        for(int i=0;i<19;i++)
        {
            float z=-2+i,center=MathF.Sin(z*.42f)*2.5f;
            Box(center,z,.009f,new(2.4f,.012f,.92f),new(.29f,.23f,.17f,1));
            foreach(float side in new[]{-1.3f,1.3f})Box(center+side,z,.02f,new(.07f,.028f,.33f),new(.75f,.52f,.18f,1));
        }
        void Crate(float x,float z,float width,float height)
        {
            Box(x,z,height/2,new(width,height,width),metal);Solid(x,z,width,width,height);
            foreach(float band in new[]{-.32f,.32f})Box(x+band*width,z,height/2,new(.065f,height+.05f,width+.045f),rust);
            Box(x,z,height+.03f,new(width+.05f,.08f,width+.05f),new(.3f,.36f,.37f,1));
            Box(x,z-width*.51f,height*.55f,new(width*.7f,.16f,.035f),new(.85f,.60f,.15f,1));
        }
        Crate(-2.8f,3.5f,1.6f,1.35f);Crate(2.8f,5.3f,1.6f,1.35f);Crate(-5.6f,8.5f,1.35f,1);Crate(-5.6f,8.5f,1.2f,2.1f);
        Crate(4.5f,11.5f,1.9f,1.6f);
        // Horizontal salvage pipe: a low barrier with supports and contrasting collars.
        ArcadeGeometry.Add(vertices,ArcadeGeometry.Frustum(.42f,.42f,2.5f),rust,Matrix4x4.CreateRotationZ(MathF.PI/2)*frame*Matrix4x4.CreateTranslation(World(.2f,8,.5f)));
        Solid(.2f,8,2.6f,.9f,.95f);
        foreach(float x in new[]{-.7f,1.1f})Box(x,8,.18f,new(.18f,.36f,1.1f),metal);
        // Reactor with upper beacon and a protective concrete plinth.
        Box(5.6f,3,.1f,new(1.65f,.2f,1.65f),new(.36f,.37f,.34f,1));Solid(5.6f,3,1.65f,1.65f,2.1f);
        ArcadeGeometry.Add(vertices,ArcadeGeometry.Frustum(.52f,.52f,1.8f),metal,Matrix4x4.CreateTranslation(World(5.6f,3,1.1f)));
        for(int i=0;i<3;i++)Box(5.6f,3,.6f+i*.4f,new(1.15f,.07f,1.15f),new(.12f,.58f,.65f,1));
        // Broken gantry: side posts collide; the overhead beam leaves a navigable opening.
        foreach(float x in new[]{-2.4f,2.4f}){Box(x,15,1.9f,new(.5f,3.8f,.65f),rust);Solid(x,15,.5f,.65f,3.8f);}
        Box(-.4f,15,3.85f,new(4.4f,.35f,.65f),metal,.04f);
        var beam=World(-.4f,15,3.85f);var beamExtent=new Vector3(Math.Abs(right.X)*2.2f+Math.Abs(forward.X)*.325f,.22f,Math.Abs(right.Z)*2.2f+Math.Abs(forward.Z)*.325f);Obstacles.Add(new(beam-beamExtent,beam+beamExtent));
        // Faceted rock spires and dune ridge frame the arena without blocking its centre.
        for(int i=0;i<18;i++)
        {
            float x=i<9?-13.5f:13.5f,z=-5+(i%9)*3.3f,h=2+(i%4)*.7f;
            var color=new Vector4(.25f+(i%3)*.035f,.20f,.16f,1);
            ArcadeGeometry.Add(vertices,Rock(i,1.5f,h),color,Matrix4x4.CreateScale(1,1,1.25f)*Matrix4x4.CreateTranslation(World(x,z)));
            Solid(x,z,3,3.75f,h);
        }
        for(int i=0;i<9;i++)
        {
            float x=-12+i*3,h=2.5f+(i%3)*.7f;
            ArcadeGeometry.Add(vertices,Rock(i+30,1.7f,h),new(.28f,.22f,.17f,1),Matrix4x4.CreateTranslation(World(x,22)));Solid(x,22,3.4f,3.4f,h);
        }
        // Low luminous guide beacons near the start.
        foreach(float x in new[]{-1.3f,1.3f})
        {Box(x,-1,.22f,new(.22f,.44f,.22f),metal);Box(x,-1,.47f,new(.28f,.09f,.28f),new(.15f,.8f,.9f,1));}
        foreach(var site in new[]{(X:4f,Z:-3f,H:2.2f,Name:"Start lookout"),(X:-7f,Z:4f,H:2.5f,Name:"West lookout"),(X:7f,Z:13f,H:2.8f,Name:"East lookout"),(X:-7f,Z:16f,H:3f,Name:"Far lookout")})
        {
            Box(site.X,site.Z,site.H/2,new(2.5f,site.H,2.5f),metal);Solid(site.X,site.Z,2.5f,2.5f,site.H);
            Box(site.X,site.Z,site.H-.045f,new(2.6f,.09f,2.6f),new(.35f,.43f,.45f,1));
            Pads.Add(new(site.Name,World(site.X,site.Z,site.H)));
        }
        // Protected respawn bay behind the starting line, with no collision walls.
        Box(0,-1.5f,.012f,new(2.2f,.02f,2.2f),new(.13f,.26f,.32f,1));
        foreach(float x in new[]{-1.15f,1.15f})Box(x,-1.5f,.025f,new(.06f,.03f,2.3f),new(.12f,.65f,.9f,1));
        Vertices=vertices.ToArray();
    }
    private static Triangle[] Rock(int seed,float radius,float height)
    {
        var random=new Random(seed+491);const int n=9;var lower=new Vector3[n];var middle=new Vector3[n];var upper=new Vector3[n];
        float dx=(float)(random.NextDouble()-.5)*radius,dz=(float)(random.NextDouble()-.5)*radius;
        for(int i=0;i<n;i++)
        {
            float angle=i*MathF.Tau/n,spread=.75f+(float)random.NextDouble()*.3f;
            lower[i]=new(MathF.Sin(angle)*radius*spread,0,MathF.Cos(angle)*radius*spread);
            middle[i]=new(lower[i].X*(.55f+(float)random.NextDouble()*.25f)+dx*.3f,height*(.35f+(float)random.NextDouble()*.28f),lower[i].Z*.72f+dz*.3f);
            upper[i]=new(dx+MathF.Sin(angle)*radius*.18f,height*(.85f+(float)random.NextDouble()*.15f),dz+MathF.Cos(angle)*radius*.18f);
        }
        var mesh=new List<Triangle>();
        for(int i=0;i<n;i++)
        {
            int j=(i+1)%n;mesh.Add(Shapes.Tri(lower[i],lower[j],middle[j]));mesh.Add(Shapes.Tri(lower[i],middle[j],middle[i]));
            mesh.Add(Shapes.Tri(middle[i],middle[j],upper[j]));mesh.Add(Shapes.Tri(middle[i],upper[j],upper[i]));mesh.Add(Shapes.Tri(upper[i],upper[j],new(dx,height,dz)));
        }
        return mesh.ToArray();
    }
}
