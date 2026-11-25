using System.Numerics;
using System.Text.Json;

namespace ServoAnimator;

public enum CameraShot { Standard, Tight, Wide, HandLeft, HandRight }

public sealed record CameraShotCue(CameraShot Shot=CameraShot.Tight,double Seconds=2,double StageFront=0)
{
    public static CameraShotCue Read(string text)
    {
        var cue=string.IsNullOrWhiteSpace(text)?new CameraShotCue():JsonSerializer.Deserialize<CameraShotCue>(text);
        if(cue==null||!Enum.IsDefined(cue.Shot)||!double.IsFinite(cue.Seconds)||cue.Seconds<0||cue.Seconds>60
            ||!double.IsFinite(cue.StageFront)||Math.Abs(cue.StageFront)>360)
            throw new FormatException("Invalid camera shot or transition. Use 0–60 seconds and a stage heading within ±360°.");
        return cue;
    }
    public string ToJson()=>JsonSerializer.Serialize(this);
}

// Z-up coordinates, horizontal field of view and a level camera.
public readonly record struct ShotView(Vector3 Target,float Yaw,float Distance,float Pitch=0)
{
    public const float HorizontalFov=38*MathF.PI/180;
    public Vector3 Eye=>Target+Distance*new Vector3(MathF.Cos(Yaw)*MathF.Cos(Pitch),MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch));
    public static ShotView Blend(ShotView from,ShotView to,float amount)
    {
        float turn=MathF.IEEERemainder(to.Yaw-from.Yaw,2*MathF.PI);
        return new(Vector3.Lerp(from.Target,to.Target,amount),from.Yaw+turn*amount,
            from.Distance+(to.Distance-from.Distance)*amount,from.Pitch+(to.Pitch-from.Pitch)*amount);
    }
}

public static class CameraShots
{
    public const string LeftShoulder="body_seg_shoulder_pivot_left_link";
    public const string RightShoulder="body_seg_shoulder_pivot_right_link";
    public const string BackWall="backdrop_wall_back_link";

    public static ShotView Evaluate((double Time,CameraShotCue Cue)[] cues,double time,ShotView baseline,
        Func<CameraShotCue,ShotView> resolve)
    {
        // Evaluate on timeline time, so seeking and playback give the same result.
        var blends=new Stack<(CameraShotCue Cue,float Amount)>();
        for(int i=cues.Length-1;i>=0;i--)
        {
            var entry=cues[i];
            if(entry.Time>time)continue;
            float t=entry.Cue.Seconds==0?1:(float)Math.Clamp((time-entry.Time)/entry.Cue.Seconds,0,1);
            blends.Push((entry.Cue,t*t*(3-2*t)));
            if(t>=1)break;
            time=entry.Time;
        }
        while(blends.TryPop(out var item))
            baseline=ShotView.Blend(baseline,resolve(item.Cue),item.Amount);
        return baseline;
    }

    public static ShotView Fit(IEnumerable<Vector3> points,float yaw,float aspect,float margin=.08f)
    {
        var front=new Vector3(MathF.Cos(yaw),MathF.Sin(yaw),0);
        var right=new Vector3(-front.Y,front.X,0);
        var min=new Vector3(float.PositiveInfinity);
        var max=new Vector3(float.NegativeInfinity);
        foreach(var point in points)
        {
            var projected=new Vector3(Vector3.Dot(point,front),Vector3.Dot(point,right),point.Z);
            min=Vector3.Min(min,projected);
            max=Vector3.Max(max,projected);
        }
        var center=(min+max)*.5f;
        var half=(max-min)*.5f;
        float tan=MathF.Tan(ShotView.HorizontalFov/2)*(1-2*margin);
        float distance=half.X+Math.Max(half.Y,half.Z*aspect)/tan+.04f;
        return new(front*center.X+right*center.Y+Vector3.UnitZ*center.Z,yaw,distance);
    }

    public static IEnumerable<Vector3> Corners(Vector3 min,Vector3 max)
    {
        for(int i=0;i<8;i++)yield return new((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z);
    }

    public static IEnumerable<Vector3> TightEnvelope(Vector3 shoulders,Vector3 neck,float yaw,float scale)
    {
        // A fixed envelope covers the stock neck travel and the open flaps.
        // Actual articulated bounds are included separately for custom poses/models.
        var front=new Vector3(MathF.Cos(yaw),MathF.Sin(yaw),0);
        var side=new Vector3(-front.Y,front.X,0);
        foreach(float forward in new[]{-.22f,.32f})
        foreach(float lateral in new[]{-.34f,.34f})
        {
            var point=neck+scale*(forward*front+lateral*side);
            yield return new(point.X,point.Y,shoulders.Z+.02f*scale);
            yield return point+Vector3.UnitZ*(.38f*scale);
        }
    }

    public static float Heading(Vector3 direction)=>MathF.Atan2(direction.Y,direction.X);
    public static Vector3 FromVr(Vector3 point)=>new(point.Z,point.X,point.Y);
    public static Vector3 ToVr(Vector3 point)=>new(point.Y,point.Z,point.X);
}
