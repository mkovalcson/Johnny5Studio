using System.Numerics;
namespace Johnny5VR;
internal sealed record GloveHand(bool Left,Matrix4x4 World,float[]? Curls);
internal static class ControllerGlove
{
    private static readonly Triangle[] box=Shapes.Box(Vector3.One);
    private static readonly Triangle[] joint=Octahedron();
    internal static float Curl(float[]? values,int finger)=>values?.Length==5&&float.IsFinite(values[finger])?Math.Clamp(values[finger],0,1):.15f;
    internal static Vertex[] Build(IEnumerable<GloveHand> hands)
    {
        var result=new List<Vertex>(6000);
        foreach(var hand in hands)
        {
            var steel=new Vector4(.48f,.53f,.58f,1);var bright=new Vector4(.7f,.74f,.77f,1);var rubber=new Vector4(.035f,.045f,.05f,1);var brass=new Vector4(.47f,.32f,.15f,1);
            void Mesh(Triangle[] mesh,Vector3 size,Matrix4x4 transform,Vector4 color)
            {
                var world=Matrix4x4.CreateScale(size)*transform*hand.World;Matrix4x4.Invert(world,out var inverse);var normal=Matrix4x4.Transpose(inverse);
                foreach(var triangle in mesh)
                {
                    result.Add(new(Vector3.Transform(triangle.A,world),Vector3.Normalize(Vector3.TransformNormal(triangle.NA,normal)),color));
                    result.Add(new(Vector3.Transform(triangle.B,world),Vector3.Normalize(Vector3.TransformNormal(triangle.NB,normal)),color));
                    result.Add(new(Vector3.Transform(triangle.C,world),Vector3.Normalize(Vector3.TransformNormal(triangle.NC,normal)),color));
                }
            }
            void Box(Vector3 size,Vector3 at,Vector4 color)=>Mesh(box,size,Matrix4x4.CreateTranslation(at),color);
            Box(new(.077f,.028f,.09f),new(0,0,.003f),rubber);
            Box(new(.087f,.009f,.095f),new(0,.019f,.003f),steel);
            Box(new(.09f,.034f,.032f),new(0,0,.069f),steel);Box(new(.084f,.006f,.027f),new(0,.021f,.069f),brass);
            // Raised metal bezel surrounds the single LCD on the back of the glove.
            Box(new(.086f,.01f,.057f),new(0,.028f,.012f),brass);Box(new(.079f,.006f,.051f),new(0,.034f,.012f),rubber);
            foreach(float x in new[]{-.039f,.039f})foreach(float z in new[]{-.012f,.036f})Mesh(joint,new(.004f),Matrix4x4.CreateTranslation(x,.034f,z),bright);
            for(int finger=0;finger<5;finger++)
            {
                bool thumb=finger==0;float side=hand.Left?1:-1;
                float total=thumb?.063f:finger switch{1=>.082f,2=>.091f,3=>.086f,_=>.068f};
                var chain=thumb?Matrix4x4.CreateRotationY(-side*.9f)*Matrix4x4.CreateTranslation(side*.047f,-.003f,.011f):Matrix4x4.CreateTranslation((2.5f-finger)*.022f*(hand.Left?1:-1),0,-.048f);
                float curl=Curl(hand.Curls,finger);
                for(int segment=0;segment<3;segment++)
                {
                    float length=total*(segment==0?.43f:segment==1?.33f:.24f),width=thumb?.022f:.018f;
                    chain=Matrix4x4.CreateRotationX(-(.04f+curl*(segment==0?1.1f:1.35f)))*chain;
                    Mesh(joint,new(width*.58f),chain,brass);
                    var center=Matrix4x4.CreateTranslation(0,0,-length/2)*chain;
                    Mesh(box,new(width,.017f,length-.003f),center,steel);
                    Mesh(box,new(width*.8f,.004f,length*.72f),Matrix4x4.CreateTranslation(0,.0105f,-length/2)*chain,bright);
                    chain=Matrix4x4.CreateTranslation(0,0,-length)*chain;
                }
            }
        }
        return result.ToArray();
    }
    private static Triangle[] Octahedron()
    {
        var points=new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitX,-Vector3.UnitY,-Vector3.UnitZ};int[] indices=[1,0,2,1,2,3,1,3,5,1,5,0,4,2,0,4,3,2,4,5,3,4,0,5];
        return Enumerable.Range(0,8).Select(i=>new Triangle(points[indices[i*3]],points[indices[i*3+1]],points[indices[i*3+2]],points[indices[i*3]],points[indices[i*3+1]],points[indices[i*3+2]])).ToArray();
    }
}
