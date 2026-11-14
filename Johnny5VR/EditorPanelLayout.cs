using System.Numerics;

namespace Johnny5VR;

internal enum PanelTarget { None,Content,Move,TopLeft,TopRight,BottomLeft,BottomRight,PlayPause,Stop,Undo,Redo,ShowRobot,Recenter,Lock }

// Interaction geometry is independent of SteamVR so grabs can be exercised
// without a headset. The frame preserves the captured editor's aspect ratio.
internal sealed class EditorPanelLayout
{
    internal Matrix4x4 World=Matrix4x4.Identity;
    internal float Width=1.8f;
    internal float Aspect=.7f;
    internal bool Locked;
    internal PanelTarget Drag {get;private set;}
    private Matrix4x4 offset,startWorld;
    private Vector3 anchor,firstHit,firstCorner;
    private Vector2 corner;
    internal static bool Finite(Matrix4x4 m)=>float.IsFinite(m.M11+m.M12+m.M13+m.M14+m.M21+m.M22+m.M23+m.M24+m.M31+m.M32+m.M33+m.M34+m.M41+m.M42+m.M43+m.M44);
    internal static Matrix4x4 Upright(Matrix4x4 pose)
    {
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,pose);forward.Y=0;
        if(forward.LengthSquared()<1e-8f)forward=-Vector3.UnitZ;
        return Matrix4x4.CreateWorld(pose.Translation,Vector3.Normalize(forward),Vector3.UnitY);
    }
    internal static bool RayPlane(Matrix4x4 pose,Vector3 from,Vector3 direction,out Vector3 point)
    {
        point=default;var normal=new Vector3(pose.M31,pose.M32,pose.M33);float denominator=Vector3.Dot(normal,direction);
        if(Math.Abs(denominator)<1e-5f)return false;
        float distance=Vector3.Dot(pose.Translation-from,normal)/denominator;
        if(!float.IsFinite(distance)||distance<0||distance>20)return false;
        point=from+direction*distance;return true;
    }
    internal bool Begin(PanelTarget target,Matrix4x4 controller,Vector3 hit)
    {
        if(Locked||!Finite(controller)||!Finite(World))return false;
        if(target==PanelTarget.Move)
        {
            if(!Matrix4x4.Invert(Upright(controller),out var inverse))return false;
            offset=World*inverse;
        }
        else if(target is PanelTarget.TopLeft or PanelTarget.TopRight or PanelTarget.BottomLeft or PanelTarget.BottomRight)
        {
            corner=new(target is PanelTarget.TopLeft or PanelTarget.BottomLeft?-1:1,target is PanelTarget.TopLeft or PanelTarget.TopRight?1:-1);
            startWorld=World;firstHit=hit;
            firstCorner=Vector3.Transform(new(corner.X*Width/2,corner.Y*Width*Aspect/2,0),World);
            anchor=Vector3.Transform(new(-corner.X*Width/2,-corner.Y*Width*Aspect/2,0),World);
        }
        else return false;
        Drag=target;return true;
    }
    internal void Update(Matrix4x4 controller,Vector3 from,Vector3 direction)
    {
        if(!Finite(controller))return;
        if(Drag==PanelTarget.Move)
        {
            var next=offset*Upright(controller);if(Finite(next))World=next;
        }
        else if(Drag!=PanelTarget.None&&RayPlane(startWorld,from,direction,out var hit)&&Matrix4x4.Invert(startWorld,out var inverse))
        {
            var delta=Vector3.TransformNormal(firstCorner+hit-firstHit-anchor,inverse);
            float width=(corner.X*delta.X+corner.Y*Aspect*delta.Y)/(1+Aspect*Aspect);
            if(!float.IsFinite(width))return;
            Width=Math.Clamp(width,.7f,4);
            World=startWorld;World.Translation=anchor+Vector3.TransformNormal(new(corner.X*Width/2,corner.Y*Width*Aspect/2,0),startWorld);
        }
    }
    internal void End()=>Drag=PanelTarget.None;
}
