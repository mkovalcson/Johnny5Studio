using System.Numerics;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public partial class MainWindow
{
    private ServoCommand[] _shotSource;
    private (double Time,CameraShotCue Cue)[] _parsedShots=[];
    private void ApplyCameraShotTimeline()
    {
        RebuildCameraShots();
        var active=_editingUrdfFraming||_meetingActive||!PlaybackOutputAllowed(false)?[]:_parsedShots;
        if(active.Length==0&&!_displayingCameraShots)return;
        _displayingCameraShots=active.Length>0;
        ForEachHeadView(view=>view.SetCameraShots(active,_cursorTime));
    }

    private void RebuildCameraShots()
    {
        _gangCommandIndex.TryGetValue(ServoNames.CameraShot,out var commands);
        if(!ReferenceEquals(_shotSource,commands))
        {
            _shotSource=commands;
            var cues=new List<(double,CameraShotCue)>();
            foreach(var command in commands??[])
            {
                try { cues.Add((command.OffsetSeconds,CameraShotCue.Read(command.TextValue))); }
                catch(Exception error) when(error is FormatException or JsonException) { }
            }
            _parsedShots=cues.ToArray();
        }
    }
    private bool _displayingCameraShots;

    private double CameraShotEnd => _parsedShots.Length==0?0:_parsedShots[^1].Time+_parsedShots[^1].Cue.Seconds;
}

public sealed partial class RobotHeadView
{
    private (double Time,CameraShotCue Cue)[] _shotCues=[];
    private double _shotTime;
    private ShotView? _cleanShot;
    internal ShotView? CleanCameraShot=>_cleanShot;

    internal void SetCameraShots((double Time,CameraShotCue Cue)[] cues,double time)
    {
        if(_shotCues.Length==0&&cues.Length==0)return;
        _shotCues=cues;
        _shotTime=time;
        UpdateCamera();
    }

    private void ApplyShotCamera(ref Point3D position,ref Point3D target)
    {
        _cleanShot=null;
        if(_tvState.Active||_shotCues.Length==0||_scene==null||_presentationModel==null)return;
        int latest=Array.FindLastIndex(_shotCues,c=>c.Time<=_shotTime);
        if(latest<0)return;
        var last=_shotCues[latest];
        if(last.Cue.Shot==CameraShot.Standard&&_shotTime>=last.Time+last.Cue.Seconds)return;
        static Vector3 Vector(Point3D p)=>new((float)p.X,(float)p.Y,(float)p.Z);
        static Point3D Point(Vector3 p)=>new(p.X,p.Y,p.Z);
        var delta=Vector(position)-Vector(target);
        var standard=new ShotView(Vector(target),CameraShots.Heading(delta),delta.Length(),
            MathF.Atan2(delta.Z,new Vector2(delta.X,delta.Y).Length()));
        var transform=_presentationModel.Transform;
        Vector3 World(Point3D p)=>Vector(transform.Transform(p));
        Vector3 Link(string name,Vector3 fallback)=>_scene.TryTransformLinkPoint(name,new(),out var point)?World(point):fallback;
        var bounds=_presentationModel.Bounds;
        if(bounds.IsEmpty)return;
        var min=new Vector3((float)bounds.X,(float)bounds.Y,(float)bounds.Z);
        var max=min+new Vector3((float)bounds.SizeX,(float)bounds.SizeY,(float)bounds.SizeZ);
        var center=(min+max)*.5f;
        var left=Link(CameraShots.LeftShoulder,center);
        var right=Link(CameraShots.RightShoulder,center);
        var shoulders=(left+right)*.5f;
        var front=Vector3.Cross(left-right,Vector3.UnitZ);
        if(front.LengthSquared()<1e-6f)
        {
            var forward=transform.Transform(new Vector3D(1,0,0));
            front=new((float)forward.X,(float)forward.Y,0);
        }
        float yaw=CameraShots.Heading(front);
        float stageYaw=0;
        if(_stageScene!=null&&_stageModel!=null)
        {
            var stage=_stageModel.Transform;
            var axis=stage.Transform(new Vector3D(1,0,0));
            if(_stageScene.TryTransformLinkPoint(CameraShots.BackWall,new(),out var wall)&&
                _stageScene.TryTransformLinkPoint(CameraShots.BackWall,new Point3D(1,0,0),out var normal))
            {
                axis=stage.Transform(normal-wall);
                if(Vector3D.DotProduct(axis,stage.Transform(new Point3D())-stage.Transform(wall))<0)axis=-axis;
            }
            stageYaw=MathF.Atan2((float)axis.Y,(float)axis.X);
        }
        var inputs=new Dictionary<CameraShotCue,(Vector3[] Points,float Heading)>();
        ShotView Resolve(CameraShotCue cue,float aspect)
        {
            if(cue.Shot==CameraShot.Standard)return standard;
            if(!inputs.TryGetValue(cue,out var input))
            {
                var points=new List<Vector3>();
                float heading=yaw;
                if(cue.Shot==CameraShot.Wide)
                {
                    heading=stageYaw+(float)cue.StageFront*MathF.PI/180;
                    points.AddRange(CameraShots.Corners(min,max));
                }
                else if(cue.Shot==CameraShot.Tight)
                {
                    points.AddRange(_scene.GetWorldVisualBoundsPoints(LibraryPoseThumbnailLinks).Select(World)
                        .Where(p=>left==right||p.Z>=shoulders.Z+.02f));
                    if(left!=right)
                        points.AddRange(CameraShots.TightEnvelope(shoulders,Link("body_seg_neck_rods_link",shoulders),yaw,1));
                }
                else
                {
                    bool leftHand=cue.Shot==CameraShot.HandLeft;
                    string root=leftHand?CameraShots.LeftShoulder:CameraShots.RightShoulder;
                    heading+= (leftHand?1:-1)*MathF.PI/3;
                    points.AddRange(_scene.GetWorldVisualBoundsPoints(_scene.ShotSubtree(root)).Select(World));
                    if(points.Count>0)
                    {
                        var forward=new Vector3(MathF.Cos(yaw),MathF.Sin(yaw),0);
                        var reach=points.MaxBy(p=>Vector3.Dot(p,forward));
                        points.Add(reach+forward*.24f);
                    }
                }
                input=(points.Count==0?CameraShots.Corners(min,max).ToArray():points.ToArray(),heading);
                inputs.Add(cue,input);
            }
            return CameraShots.Fit(input.Points,input.Heading,aspect);
        }
        var width=Math.Max(1,_viewport.ActualWidth);
        var height=Math.Max(1,_viewport.ActualHeight);
        var view=CameraShots.Evaluate(_shotCues,_shotTime,standard,cue=>Resolve(cue,(float)(width/height)));
        _cleanShot=CameraShots.Evaluate(_shotCues,_shotTime,standard,cue=>Resolve(cue,16f/9));
        position=Point(view.Eye);
        target=Point(view.Target);
    }
}

internal sealed partial class UrdfScene
{
    private readonly Dictionary<string,string[]> _shotSubtrees=new();
    internal IEnumerable<string> ShotSubtree(string root)
    {
        if(_shotSubtrees.TryGetValue(root,out var saved))return saved;
        bool DescendsFrom(string link)
        {
            while(link!=root&&_parentJointByChild.TryGetValue(link,out var joint))link=joint.ParentLink;
            return link==root;
        }
        return _shotSubtrees[root]=_linkVisuals.Keys.Where(DescendsFrom).ToArray();
    }
}
