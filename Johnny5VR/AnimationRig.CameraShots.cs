using System.Numerics;
using Johnny5.VrBridge;
using ServoAnimator;

namespace Johnny5VR;

internal sealed partial class AnimationRig
{
    private readonly Dictionary<int,(Vector3 Min,Vector3 Max)> shotBounds=new();
    private readonly HashSet<int> shotHead=new(),shotLeftArm=new(),shotRightArm=new();
    private float shotScale=1,shotStageYaw;

    internal void SetShotBounds(IEnumerable<(int Bone,Triangle Triangle)> parts,float scale)
    {
        shotScale=scale;
        foreach(var (bone,t) in parts)
        {
            if(bone==0)continue;
            var min=Vector3.Min(t.A,Vector3.Min(t.B,t.C));
            var max=Vector3.Max(t.A,Vector3.Max(t.B,t.C));
            if(shotBounds.TryGetValue(bone,out var saved))
            { min=Vector3.Min(min,saved.Min);max=Vector3.Max(max,saved.Max); }
            shotBounds[bone]=(min,max);
        }
        bool Under(string link,string root)
        {
            while(link!=root&&joints.TryGetValue(link,out var parent))link=parent.Parent;
            return link==root;
        }
        foreach(var name in links)
        {
            if(Under(name,"base_link")&&!name.StartsWith("body_",StringComparison.Ordinal))shotHead.Add(indices[name]);
            if(Under(name,CameraShots.LeftShoulder))shotLeftArm.Add(indices[name]);
            if(Under(name,CameraShots.RightShoulder))shotRightArm.Add(indices[name]);
        }
        SetShotStageFront(Vector3.TransformNormal(Vector3.UnitX,placement));
    }

    internal void SetShotStageFront(Vector3 front)=>shotStageYaw=CameraShots.Heading(CameraShots.FromVr(front));

    internal VrCaptureCamera? CaptureShot((double Time,CameraShotCue Cue)[] cues,double time,Vector3 eye,Vector3 target)
    {
        int latest=Array.FindLastIndex(cues,cue=>cue.Time<=time);
        if(latest<0||cues[latest].Cue.Shot==CameraShot.Standard&&time>=cues[latest].Time+cues[latest].Cue.Seconds)
            return null;
        var delta=CameraShots.FromVr(eye-target);
        var baseline=new ShotView(CameraShots.FromVr(target),CameraShots.Heading(delta),delta.Length(),
            MathF.Atan2(delta.Z,new Vector2(delta.X,delta.Y).Length()));
        Vector3 Link(string name,Vector3 fallback)=>lastWorlds.TryGetValue(name,out var matrix)
            ?CameraShots.FromVr(Vector3.Transform(Vector3.Zero,matrix*lastScene*placement)):fallback;
        var center=CameraShots.FromVr(Vector3.Transform(Vector3.Zero,lastScene*placement));
        var left=Link(CameraShots.LeftShoulder,center);
        var right=Link(CameraShots.RightShoulder,center);
        var shoulders=(left+right)*.5f;
        var front=Vector3.Cross(left-right,Vector3.UnitZ);
        if(front.LengthSquared()<1e-6f)front=CameraShots.FromVr(Vector3.TransformNormal(Vector3.UnitX,lastScene*placement));
        var yaw=CameraShots.Heading(front);
        var forward=new Vector3(MathF.Cos(yaw),MathF.Sin(yaw),0);
        List<Vector3> Points(HashSet<int>? bones)
        {
            var result=new List<Vector3>();
            foreach(var (bone,bounds) in shotBounds)
            {
                if((bones!=null&&!bones.Contains(bone))||Palette[bone]==default)continue;
                foreach(var point in CameraShots.Corners(bounds.Min,bounds.Max))
                    result.Add(CameraShots.FromVr(Vector3.Transform(Vector3.Transform(point,placement),Palette[bone])));
            }
            return result;
        }
        ShotView Resolve(CameraShotCue cue)
        {
            if(cue.Shot==CameraShot.Standard)return baseline;
            float heading=yaw;
            List<Vector3> points;
            if(cue.Shot==CameraShot.Wide)
            {
                points=Points(null);
                heading=shotStageYaw+(float)cue.StageFront*MathF.PI/180;
            }
            else if(cue.Shot==CameraShot.Tight)
            {
                points=Points(shotHead);
                if(left!=right)
                {
                    points.RemoveAll(p=>p.Z<shoulders.Z+.02f*shotScale);
                    points.AddRange(CameraShots.TightEnvelope(shoulders,Link("body_seg_neck_rods_link",shoulders),yaw,shotScale));
                }
            }
            else
            {
                bool useLeft=cue.Shot==CameraShot.HandLeft;
                points=Points(useLeft?shotLeftArm:shotRightArm);
                heading+=(useLeft?1:-1)*MathF.PI/3;
                if(points.Count>0)points.Add(points.MaxBy(p=>Vector3.Dot(p,forward))+forward*(.24f*shotScale));
            }
            if(points.Count==0)points=Points(null);
            return points.Count==0?baseline:CameraShots.Fit(points,heading,16f/9);
        }
        var view=CameraShots.Evaluate(cues,time,baseline,Resolve);
        return new(CameraShots.ToVr(view.Eye),CameraShots.ToVr(view.Target));
    }
}
