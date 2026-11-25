using System.Numerics;
using Johnny5.VrBridge;
using ServoAnimator;

namespace Johnny5VR;

internal static class CameraShotChecks
{
    internal static void Run(string config)
    {
        int count=0;
        void Check(bool pass,string text){if(!pass)throw new InvalidOperationException(text);count++;}
        bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
        Check(CameraShotCue.Read("").Seconds==2,"Blend defaults to two seconds.");
        var command=new ServoCommand{Servo=ServoNames.CameraShot,OffsetSeconds=1.2,
            TextValue=new CameraShotCue(CameraShot.HandRight,0,12).ToJson()};
        var copy=System.Text.Json.JsonSerializer.Deserialize<ServoCommand>(System.Text.Json.JsonSerializer.Serialize(command));
        Check(copy!=null&&copy.IsTextServo&&CameraShotCue.Read(copy.TextValue)==new CameraShotCue(CameraShot.HandRight,0,12),"Scene commands persist shot and transition.");
        ShotView a=new(Vector3.Zero,3,3),b=new(Vector3.One,-3,2);
        ShotView Resolve(CameraShotCue cue)=>cue.Shot==CameraShot.Wide?a:b;
        (double,CameraShotCue)[] cues=[(0,new(CameraShot.Wide,0)),(1,new(CameraShot.Tight,2))];
        Check(CameraShots.Evaluate(cues,1,a,Resolve).Target==a.Target,"Blend starts at the previous shot.");
        var halfway=CameraShots.Evaluate(cues,2,a,Resolve);
        Check(halfway.Target==new Vector3(.5f)&&Near(halfway.Yaw,MathF.PI),"Blend moves on the shortest arc.");
        Check(CameraShots.Evaluate(cues,3,a,Resolve).Target==b.Target,"Blend arrives at two seconds.");
        var box=CameraShots.Corners(new(-1,-2,-1),new(1,2,1)).ToArray();
        foreach(float aspect in new[]{.6f,1f,16f/9,2.4f})
        {
            const float yaw=.8f;
            var shot=CameraShots.Fit(box,yaw,aspect);
            var front=new Vector3(MathF.Cos(yaw),MathF.Sin(yaw),0);
            var side=Vector3.Cross(Vector3.UnitZ,front);
            foreach(var point in box)
            {
                var ray=point-shot.Eye;
                var depth=-Vector3.Dot(ray,front);
                var width=depth*MathF.Tan(ShotView.HorizontalFov/2);
                Check(depth>0&&Math.Abs(Vector3.Dot(ray,side))<width&&Math.Abs(ray.Z)<width/aspect,"Framing fits the bounds at any aspect.");
            }
        }
        BridgeChecks.Run(Check);
        var settings=new ViewerSettings{ConfigFolder=config,Robot="Models/Robots/Johnny5FullBody/johnny5_head.urdf",
            Backdrop="Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf"};
        var model=new UrdfReader().Load(settings);
        var rig=model.Rig!;
        var eye=new Vector3(0,1,4);
        var target=new Vector3(0,1,0);
        rig.Apply(new());
        VrCaptureCamera Shot(CameraShot kind)=>rig.CaptureShot([(0,new(kind,0))],0,eye,target)!;
        var tight=Shot(CameraShot.Tight);
        var wide=Shot(CameraShot.Wide);
        var left=Shot(CameraShot.HandLeft);
        var right=Shot(CameraShot.HandRight);
        Check(tight.Target.Y>wide.Target.Y&&Vector3.Distance(tight.Eye,tight.Target)<Vector3.Distance(wide.Eye,wide.Target),"Stock Tight targets head and shoulders; Wide frames the whole robot.");
        Check(left.Eye.X>left.Target.X&&right.Eye.X<right.Target.X,"Hand shots face inward from opposite sides.");
        rig.Apply(new(){["servo:URDFStageHeading"]=(90,0,"URDFStageHeading"),["servo:URDFStageX"]=(500,0,"URDFStageX")});
        var turned=Shot(CameraShot.Tight);var movedWide=Shot(CameraShot.Wide);
        Check(turned.Target.Z>tight.Target.Z+4,"The tight camera follows stage travel.");
        Check(Math.Abs((turned.Eye-turned.Target).X)>Math.Abs((turned.Eye-turned.Target).Z),"The tight camera follows shoulder heading.");
        Check(Vector3.Dot(Vector3.Normalize(wide.Eye-wide.Target),Vector3.Normalize(movedWide.Eye-movedWide.Target))>.999f,"The wide camera stays square to the stage wall through a robot turn.");
        Console.WriteLine($"PASS: {count} focused camera and bridge checks.");
    }
}
