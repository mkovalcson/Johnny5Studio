using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void PointerZoomChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var view=new RobotHeadView(deferModelLoad:true);
        var viewport=(Viewport3D)typeof(RobotHeadView).GetField("_viewport",flags)!.GetValue(view)!;
        var camera=(PerspectiveCamera)typeof(RobotHeadView).GetField("_camera",flags)!.GetValue(view)!;
        var source=new Model3DGroup();var visual=new ModelVisual3D{Content=source};viewport.Children.Add(visual);
        var material=new DiffuseMaterial(Brushes.Blue);
        Point Project(Point3D anchor)
        {
            object[] args={anchor,new Point()};
            Check((bool)typeof(RobotHeadView).GetMethod("ProjectZoomAnchor",flags)!.Invoke(view,args)!,"Anchor projects in front of camera");
            return (Point)args[1];
        }
        void Layout(Size size){view.Measure(size);view.Arrange(new Rect(size));view.UpdateLayout();}
        int notifications=0;view.CameraInteracted+=_=>notifications++;
        foreach(var size in new[]{new Size(800,600),new Size(1600,900),new Size(450,800)})
        foreach(double aspect in new[]{0,16d/9})
        foreach(bool hidden in new[]{false,true})
        {
            typeof(RobotHeadView).GetField("_staticBackdropAspect",flags)!.SetValue(view,aspect);
            viewport.Visibility=hidden?Visibility.Hidden:Visibility.Visible;Layout(size);
            foreach(var angle in new[]{(0d,0d),(.7,.4),(-1.2,-.5)})
            {
                view.ApplyFraming(new UrdfFraming{Distance=1.6,Yaw=angle.Item1,Pitch=angle.Item2,HorizontalPercent=12,VerticalPercent=-20});
                var focus=camera.Position+camera.LookDirection;
                var rotation=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,2,1),27));
                var transform=new Transform3DGroup();transform.Children.Add(rotation);transform.Children.Add(new TranslateTransform3D((Vector3D)focus));
                source.Children.Clear();source.Children.Add(new GeometryModel3D(PrimitiveMeshes.Box(.30,.24,.28),material){BackMaterial=material,Transform=transform});
                var pointer=new Point(size.Width*.5,size.Height*.5);
                Check(view.TryPointerZoomAnchor(pointer,out var surface),"Model under pointer yields an anchor");
                var local=transform.Inverse!.Transform(surface);
                double face=Math.Min(Math.Abs(Math.Abs(local.X)-.15),Math.Min(Math.Abs(Math.Abs(local.Y)-.12),Math.Abs(Math.Abs(local.Z)-.14)));
                Check(face<1e-6,"Ray hits the transformed model surface, including the retained hidden WPF graph used by DX11");
                foreach(var p in new[]{pointer,Project(focus+new Vector3D(0,.035,.025)),new Point(size.Width*.90,size.Height*.15)})
                {
                    Check(view.TryPointerZoomAnchor(p,out var anchor),"Surface or empty-space focus plane supplies an anchor");
                    int before=notifications;
                    Check(view.ZoomAtPointer(p,120),"Wheel zoom changes distance");
                    Check((Project(anchor)-p).Length<.05,"Point under cursor remains fixed while zooming in");
                    Check(view.ZoomAtPointer(p,-120),"Wheel zoom out changes distance");
                    Check((Project(anchor)-p).Length<.05,"Point under cursor remains fixed while zooming out");
                    Check(notifications==before+2,"Each wheel action raises one camera interaction event");
                    var frame=view.CaptureCurrentFraming();
                    view.ApplyFraming(frame);
                    Check((Project(anchor)-p).Length<.05,"Compensating horizontal and vertical framing survives save/restore");
                }
            }
        }
        view.ApplyFraming(new UrdfFraming{Distance=.55});
        Check(!view.ZoomAtPointer(new Point(225,400),120),"Minimum zoom distance does not pan or zoom further");
        view.ApplyFraming(new UrdfFraming{Distance=11.5});
        Check(!view.ZoomAtPointer(new Point(225,400),-120),"Maximum zoom distance does not pan or zoom further");
        Check(!view.ZoomAtPointer(new Point(225,400),0),"Zero wheel delta is a no-op");
        viewport.Visibility=Visibility.Visible;typeof(RobotHeadView).GetField("_staticBackdropAspect",flags)!.SetValue(view,0d);Layout(new Size(1200,800));
        foreach(string model in new[]{"Johnny5Head","Johnny5FullBody"})
        {
            string path=System.IO.Path.GetFullPath("Config/Models/Robots/"+model+"/johnny5_head.urdf");
            var scene=UrdfScene.Load(path);view.InstallModel(path,scene,new UrdfFraming{Distance=model=="Johnny5Head"?1.15:5.3,VerticalPercent=model=="Johnny5Head"?0:-80});
            scene.TryTransformLinkPoint("left_pupil_link",new Point3D(.0085,0,0),out var eye);
            var pointer=Project(eye);
            Check(pointer.X>=0&&pointer.X<=1200&&pointer.Y>=0&&pointer.Y<=800,"Real robot eye is in view before zooming: "+model);
            for(int i=0;i<30;i++)
            {
                if(!view.TryPointerZoomAnchor(pointer,out var anchor))throw new Exception("No eye zoom anchor");
                if(!view.ZoomAtPointer(pointer,120))break;
                Check((Project(anchor)-pointer).Length<.1,"Repeated eye-centered zoom preserves the pointed surface: "+model);
            }
            foreach(var mode in new[]{UrdfStageMode.RobotCentric,UrdfStageMode.RoomCentric})
            {
                view.ApplyFraming(new UrdfFraming{Distance=5.3,VerticalPercent=-50});
                view.SetTimelineBackground(new UrdfPresentationSettings{BackgroundMode=UrdfBackgroundMode.Urdf3D,
                    Stage=new UrdfStageSettings{ModelFile=System.IO.Path.GetFullPath("Config/Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf"),MovementMode=mode,ViewZoomPercent=130,ViewYawDegrees=15}});
                var presentation=(Model3DGroup)typeof(RobotHeadView).GetField("_presentationModel",flags)!.GetValue(view)!;
                pointer=Project(presentation.Transform.Transform(eye));
                Check(view.TryPointerZoomAnchor(pointer,out var anchor),"3D scenery camera has an anchor: "+mode);
                view.ZoomAtPointer(pointer,120);
                Check((Project(anchor)-pointer).Length<.1,"Cursor anchor remains stable in 3D scenery: "+mode);
                view.SetTimelineBackground(new UrdfPresentationSettings{BackgroundMode=UrdfBackgroundMode.Studio});
            }
        }
    }
}
