using System.Numerics;

namespace Johnny5VR;

internal static class ControllerRays
{
    internal static (Vector3 Start,Vector3 End)? FromPose(Matrix4x4 pose,Matrix4x4 origin,float length=2.5f)
    {
        if(!EditorPanelLayout.Finite(pose)||!EditorPanelLayout.Finite(origin))return null;
        var world=pose*origin;var direction=Vector3.TransformNormal(-Vector3.UnitZ,world);
        if(direction.LengthSquared()<1e-8f)return null;
        return(world.Translation,world.Translation+Vector3.Normalize(direction)*length);
    }
}
