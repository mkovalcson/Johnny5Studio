using System.Numerics;

namespace Johnny5VR;

// Maps the tracked room into virtual space without changing tracked floor height.
internal sealed class Locomotion
{
    internal Matrix4x4 Origin {get;private set;}=Matrix4x4.Identity;
    private bool turnArmed=true;
    internal static float DeadZone(float value)=>!float.IsFinite(value)||Math.Abs(value)<=.18f?0:MathF.CopySign((Math.Min(Math.Abs(value),1)-.18f)/.82f,value);
    internal void Restore(Matrix4x4 origin)=>Origin=origin;
    internal void Teleport(Matrix4x4 head,Vector3 feet,Vector3 lookAt)
    {
        var current=Vector3.TransformNormal(-Vector3.UnitZ,head*Origin);current.Y=0;var desired=lookAt-feet;desired.Y=0;
        float yaw=current.LengthSquared()>.0001f&&desired.LengthSquared()>.0001f?MathF.Atan2(desired.X,desired.Z)-MathF.Atan2(current.X,current.Z):0;
        var rotation=Origin;rotation.Translation=Vector3.Zero;rotation*=Matrix4x4.CreateRotationY(yaw);
        var horizontal=Vector3.Transform(head.Translation,rotation);rotation.Translation=feet-new Vector3(horizontal.X,0,horizontal.Z);Origin=rotation;
    }
    internal void Reset(){Origin=Matrix4x4.Identity;turnArmed=false;}
    internal void Step(Matrix4x4 trackedHead,Vector2 stick,float turn,float seconds,float speed,float angle,bool enabled,bool smooth=false)
    {
        if(!enabled){turnArmed=false;return;}
        if(Math.Abs(turn)<.25f)turnArmed=true;
        if(smooth||turnArmed&&Math.Abs(turn)>.65f)
        {
            var pivot=(trackedHead*Origin).Translation;
            pivot.Y=0;
            // Positive right-stick input turns right around the viewer's current
            // position, rather than orbiting the physical play-area origin.
            Origin=Origin*Matrix4x4.CreateTranslation(-pivot)
                *Matrix4x4.CreateRotationY(-(smooth?DeadZone(turn)*90*Math.Clamp(seconds,0,.05f):MathF.Sign(turn)*angle)*MathF.PI/180)
                *Matrix4x4.CreateTranslation(pivot);
            turnArmed=false;
        }
        float magnitude=stick.Length();
        if(magnitude<=.18f||!float.IsFinite(magnitude))return;
        stick=stick/magnitude*((Math.Min(magnitude,1)-.18f)/.82f);
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,trackedHead*Origin);forward.Y=0;
        if(forward.LengthSquared()<.001f)forward=Vector3.TransformNormal(-Vector3.UnitZ,Origin);
        forward=Vector3.Normalize(forward);var right=Vector3.Cross(forward,Vector3.UnitY);
        var movement=(right*stick.X+forward*stick.Y)*Math.Clamp(seconds,0,.1f)*speed;
        Origin*=Matrix4x4.CreateTranslation(movement);
    }
}
