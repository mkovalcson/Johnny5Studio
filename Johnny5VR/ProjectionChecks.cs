using System.Numerics;
using Valve.VR;

namespace Johnny5VR;

internal static class ProjectionTests
{
    internal static void Run(Action<bool,string> check)
    {
        // Native layout and independently specified asymmetric frustum, using
        // Valve's documented matrix convention, not the old reconstruction.
        const float l=-1.2f,r=.8f,t=-.7f,b=1.1f,n=.04f,f=150;
        var native=new HmdMatrix44_t{
            m0=2/(r-l),m2=(r+l)/(r-l),m5=2/(b-t),m6=(b+t)/(b-t),
            m10=-f/(f-n),m11=-f*n/(f-n),m14=-1};
        var projection=VrSession.Projection(native);
        Vector3 Project(Vector3 p)
        {var v=Vector4.Transform(new Vector4(p,1),projection);return new(v.X/v.W,v.Y/v.W,v.Z/v.W);}
        check(Math.Abs(Project(new(l,0,-1)).X+1)<1e-5f,"Asymmetric left clipping ray maps exactly to the left edge.");
        check(Math.Abs(Project(new(r,0,-1)).X-1)<1e-5f,"Asymmetric right clipping ray maps exactly to the right edge.");
        check(Math.Abs(Project(new(0,t,-1)).Y+1)<1e-5f,"The lower vertical clipping ray preserves the runtime's vertical offset.");
        check(Math.Abs(Project(new(0,b,-1)).Y-1)<1e-5f,"The upper vertical clipping ray preserves the runtime's vertical offset.");
        check(Project(new(0,0,-1)).Y<-.22f,"The asymmetric vertical centre is not sign-flipped as in the previous raw-bound reconstruction.");
        check(Math.Abs(Project(new(0,0,-n)).Z)<1e-5f,"Native projection maps the Direct3D near plane to zero depth.");
        check(Math.Abs(Project(new(0,0,-f)).Z-1)<1e-5f,"Native projection maps the Direct3D far plane to one depth.");
        var full=new HmdMatrix44_t{m0=1,m1=2,m2=3,m3=4,m4=5,m5=6,m6=7,m7=8,m8=9,m9=10,m10=11,m11=12,m12=13,m13=14,m14=15,m15=16};
        check(Vector4.Transform(new Vector4(1,2,3,1),VrSession.Projection(full))==new Vector4(18,46,74,102),"Every native projection element is transposed exactly once for the row-vector shader.");
        var eyeLeft=Matrix4x4.CreateRotationY(.12f)*Matrix4x4.CreateTranslation(-.032f,0,0);
        var eyeRight=Matrix4x4.CreateRotationY(-.12f)*Matrix4x4.CreateTranslation(.032f,0,0);
        var origin=Matrix4x4.CreateRotationY(-.3f)*Matrix4x4.CreateTranslation(3,0,-4);
        foreach(float angle in new[]{0,.4f,1.2f,2.8f})
        {
            var head=Matrix4x4.CreateRotationX(.15f)*Matrix4x4.CreateRotationY(angle)*Matrix4x4.CreateTranslation(1,1.65f,2);
            var left=VrSession.CameraFromMatrices(eyeLeft,head,origin,projection);
            var right=VrSession.CameraFromMatrices(eyeRight,head,origin,projection);
            check(Math.Abs(Vector3.Distance(left.Eye,right.Eye)-.064f)<1e-5f,"Turning a tracked/canted headset preserves physical eye separation.");
            check(Vector3.Distance(left.Eye,Vector3.Transform(new(-.032f,0,0),head*origin))<1e-5f,"Left eye position follows the full tracked pose and virtual origin.");
            Matrix4x4.Invert(projection,out var inverseProjection);
            var view=left.ViewProjection*inverseProjection;
            check(Vector3.Transform(left.Eye,view).Length()<.0001f,"The camera view is the inverse of the composed eye pose at every head angle.");
            var worldPoint=new Vector4(.2f,1.1f,-3,1);
            var clip=Vector4.Transform(worldPoint,left.ViewProjection);
            Matrix4x4.Invert(left.ViewProjection,out var inverse);
            var restored=Vector4.Transform(clip,inverse);restored/=restored.W;
            check(Vector4.Distance(worldPoint,restored)<.001f,"World geometry remains stable through asymmetric projection and rotated eye transforms.");
        }
    }
}
