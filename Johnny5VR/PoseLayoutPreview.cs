using System.Numerics;
using Johnny5.VrBridge;

namespace Johnny5VR;

// Diagnostic single-eye preview. Uses the real model, layout and label/dial rasterizers.
internal static class PoseLayoutPreview
{
    internal static void Write(ViewerSettings source)
    {
        const int size=2800;
        var settings=new ViewerSettings{ConfigFolder=source.ConfigFolder,Backdrop="",Robot=source.Robot,Distance=2.5f};
        var scene=new UrdfReader().Load(settings);
        using var form=new Form{ClientSize=new(size,size),ShowInTaskbar=false};
        using var renderer=new Renderer(form.Handle,size,size);
        renderer.Upload(scene);
        var eye=new Vector3(0,1.40f,.65f);
        var view=Matrix4x4.CreateLookAt(eye,new(0,1.40f,-2.5f),Vector3.UnitY);
        renderer.Render(null,view,eye);
        string path=Path.Combine(Diagnostics.Folder,"vr-persistent-dial-preview.png");
        renderer.Screenshot(path);
        using var reader=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));
        var state=PoseWire.Read(reader)!;
        using var bounds=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Diagnostics.Folder,"pose-control-bounds.json")));
        float floor=bounds.RootElement.GetProperty("RobotFloor").GetSingle();
        float scale=settings.RobotHeightMetres/bounds.RootElement.GetProperty("RobotHeight").GetSingle();
        state=state with{Appearance=0,Accessory=2,AppearanceBusy=false,Handles=state.Handles.Select(h=>h with{Anchor=new(h.Anchor.Y*scale,(h.Anchor.Z-floor)*scale,h.Anchor.X*scale-settings.Distance)}).ToArray()};
        var pose=new RobotPoseInteraction{Enabled=true,State=state,RobotHeight=settings.RobotHeightMetres};
        pose.Step(Matrix4x4.CreateTranslation(eye),Matrix4x4.CreateTranslation(eye),false,true);
        var hover=pose.Visible.FirstOrDefault(t=>t.Control.Key=="Servo:B_ThumbCurlLeft");
        if(hover!=null)
        {
            var normal=new Vector3(pose.Orientation.M31,pose.Orientation.M32,pose.Orientation.M33);
            var hand=Matrix4x4.CreateWorld(hover.Label+normal*.4f,-normal,Vector3.UnitY);
            pose.Step(Matrix4x4.CreateTranslation(eye),hand,false,true);
        }
        var projection=view*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.03f,150);
        PointF Project(Vector3 p){var q=Vector4.Transform(new Vector4(p,1),projection);return new((q.X/q.W+1)*size/2,(1-q.Y/q.W)*size/2);}
        var right=new Vector3(pose.Orientation.M11,pose.Orientation.M12,pose.Orientation.M13);
        void Image(Graphics g,Bitmap bitmap,Vector3 center,float width,float height)
        {
            var corners=new[]{Project(center-right*width/2+Vector3.UnitY*height/2),Project(center+right*width/2+Vector3.UnitY*height/2),Project(center-right*width/2-Vector3.UnitY*height/2)};
            g.DrawImage(bitmap,corners);
        }
        using var result=new Bitmap(path);
        using(var g=Graphics.FromImage(result))
        {
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach(var target in pose.Visible)
            {
                using var label=new Bitmap(600,Math.Max(1,(int)Math.Ceiling(target.Height/target.Width*600)));
                using(var text=Graphics.FromImage(label)){text.Clear(Color.Transparent);PoseHandleOverlay.DrawLabel(text,target,pose.Dial?.Control.Key==target.Control.Key);}
                Image(g,label,target.Label,target.Width,target.Height);
            }
            using var dial=PoseHandleOverlay.DrawDial(pose.Dial!,false);
            Image(g,dial,pose.Dial!.Center,.60f,.60f);
            using var options=PoseHandleOverlay.DrawOptions(pose);
            Image(g,options,pose.OptionsCenter,RobotPoseInteraction.OptionsWidth,RobotPoseInteraction.OptionsHeight);
            if(pose.ExpandedLabel is {} expanded)
            {
                using var label=new Bitmap(600,Math.Max(1,(int)Math.Ceiling(expanded.Height/expanded.Width*600)));
                using(var text=Graphics.FromImage(label)){text.Clear(Color.FromArgb(245,17,25,37));PoseHandleOverlay.DrawLabel(text,expanded,true);}
                Image(g,label,expanded.Label,expanded.Width,expanded.Height);
            }
        }
        string output=Path.Combine(Diagnostics.Folder,"vr-pose-pointing-layout.png");result.Save(output,System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine(output);
    }
}
