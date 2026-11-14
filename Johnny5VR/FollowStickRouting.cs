using System.Numerics;
namespace Johnny5VR;
internal static class FollowStickRouting
{
    internal static (float Forward,float Turn) Robot(ControllerReading input)=>(input.Move.Y,input.Move.X);
    internal static (Vector2 Move,float Turn) Viewer(ControllerReading input,bool following)
        =>following?(new(0,input.Right.Y),input.Right.X):(input.Move,input.Right.X);
}
