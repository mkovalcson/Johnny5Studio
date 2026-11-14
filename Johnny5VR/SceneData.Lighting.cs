using System.Numerics;

namespace Johnny5VR;

internal sealed partial class UrdfReader
{
    private static void AddRingGeometry(List<Part> parts,AnimationRig rig,Func<string,Matrix4x4> world)
    {
        void Add(IEnumerable<Triangle> triangles,string link,Matrix4x4 local,int tag,bool transparent)
        {
            if(rig.Bone(link)==0)return;var transform=local*world(link);
            foreach(var t in triangles)parts.Add(new(new(Vector3.Transform(t.A,transform),Vector3.Transform(t.B,transform),Vector3.Transform(t.C,transform),SafeNormal(Vector3.TransformNormal(t.NA,transform)),SafeNormal(Vector3.TransformNormal(t.NB,transform)),SafeNormal(Vector3.TransformNormal(t.NC,transform))),new(.043f,.043f,.047f,transparent?.1f:1),"vr_light_"+tag,rig.Bone(link),null,0,tag));
        }
        for(int side=0;side<2;side++)
        {
            string link=side==0?"left_eye_pop_link":"right_eye_pop_link";
            if(rig.Bone(link)==0)continue;
            for(int pixel=0;pixel<16;pixel++)
            {
                int front=side*32+pixel,vent=front+16;
                double angle=pixel*22.5;
                Add(Sector(.0167f,.02125f,.00072f,angle-10.55,angle+10.55),link,Matrix4x4.CreateTranslation(.02495f,0,0),128+front,false);
                float radians=(float)(-angle*Math.PI/180),y=.018975f*MathF.Sin(radians),z=.00508f+.018975f*MathF.Cos(radians);
                Add(Shapes.Box(new(.00072f,.00455f,.00455f)),link,Matrix4x4.CreateRotationX(radians)*Matrix4x4.CreateTranslation(.00789f,y,z),128+vent,false);
                Add(Shapes.Sphere(.0105f),link,Matrix4x4.CreateTranslation(.00619f,y,z),384+vent,true);
            }
            Add(Tube(.04245f,.091f),"head_link",Matrix4x4.CreateTranslation(.137625f,side==0?.099822f:-.099822f,0),194+side,true);
        }
    }
    private static Triangle Face(Vector3 a,Vector3 b,Vector3 c)
    {var normal=SafeNormal(Vector3.Cross(b-a,c-a));return new(a,b,c,normal,normal,normal);}
    private static IEnumerable<Triangle> Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {yield return Face(a,b,c);yield return Face(a,c,d);}
    private static IEnumerable<Triangle> Sector(float inner,float outer,float thickness,double start,double end)
    {
        Vector3 P(float x,float r,double angle)=>new(x,r*(float)Math.Sin(angle*Math.PI/180),r*(float)Math.Cos(angle*Math.PI/180));
        for(int i=0;i<5;i++)
        {
            double a=start+(end-start)*i/5,b=start+(end-start)*(i+1)/5;float f=thickness*.5f;
            foreach(var t in Quad(P(f,inner,a),P(f,outer,a),P(f,outer,b),P(f,inner,b)))yield return t;
            foreach(var t in Quad(P(-f,inner,a),P(-f,inner,b),P(-f,outer,b),P(-f,outer,a)))yield return t;
            foreach(var t in Quad(P(-f,outer,a),P(-f,outer,b),P(f,outer,b),P(f,outer,a)))yield return t;
            foreach(var t in Quad(P(-f,inner,a),P(f,inner,a),P(f,inner,b),P(-f,inner,b)))yield return t;
        }
        foreach(double angle in new[]{start,end})foreach(var t in Quad(P(-thickness*.5f,inner,angle),P(-thickness*.5f,outer,angle),P(thickness*.5f,outer,angle),P(thickness*.5f,inner,angle)))yield return t;
    }
    private static IEnumerable<Triangle> Tube(float radius,float length)
    {
        Vector3 P(float x,int i)=>new(x,radius*MathF.Sin(i*MathF.Tau/96),radius*MathF.Cos(i*MathF.Tau/96));
        for(int i=0;i<96;i++)foreach(var t in Quad(P(-length*.5f,i),P(-length*.5f,i+1),P(length*.5f,i+1),P(length*.5f,i)))yield return t;
    }
}
