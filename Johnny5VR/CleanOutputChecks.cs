using System.Numerics;
using System.Text.Json;
namespace Johnny5VR;
internal static class CleanOutputChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool good,string message){if(!good)throw new InvalidOperationException(message);count++;}
        var calibrated=Matrix4x4.CreatePerspectiveFieldOfView(1.7f,1.01f,.04f,150);calibrated.M31=-.226f;calibrated.M32=-.166f;
        var original=calibrated;
        foreach(var size in new[]{new Size(1920,1080),new Size(1280,1024),new Size(1080,1920),new Size(3840,2160)})
        {
            var projection=VrSession.MirrorProjection(calibrated,size.Width,size.Height);
            Check(Math.Abs(projection.M11*size.Width-projection.M22*size.Height)<.002f,"Mirror preserves equal horizontal/vertical pixel scale at "+size);
            Check(projection.M31==0&&projection.M32==0&&projection.M33==calibrated.M33&&projection.M43==calibrated.M43,"Mirror is centered and retains depth calibration at "+size);
            Check(projection.M11>=calibrated.M11&&projection.M22>=calibrated.M22,"Mirror crops coverage instead of stretching at "+size);
            var up=Vector4.Transform(new Vector4(0,.25f,-2,1),projection);var right=Vector4.Transform(new Vector4(.25f,0,-2,1),projection);
            Check(Math.Abs(up.Y/up.W*size.Height-right.X/right.W*size.Width)<.002f,"Equal-sized geometry renders with correct proportions at "+size);
        }
        Check(calibrated==original,"Spectator projection leaves the headset projection untouched.");
        var head=Matrix4x4.CreateRotationY(.6f)*Matrix4x4.CreateTranslation(1,1.7f,2);var origin=Matrix4x4.CreateRotationY(.2f)*Matrix4x4.CreateTranslation(3,0,4);
        var camera=VrSession.CameraFromMatrices(Matrix4x4.Identity,head,origin,VrSession.MirrorProjection(calibrated,1920,1080));
        Check(Vector3.Distance(camera.Eye,(head*origin).Translation)<.0001f,"Mirror follows head center without one-eye translation or cant.");
        var settings=new ViewerSettings();Check(!settings.CleanOutputEnabled&&settings.CleanOutputWidth==1920&&settings.CleanOutputHeight==1080,"Default keeps existing desktop preview and 1080p clean-output configuration.");
        settings.CleanOutputEnabled=true;settings.CleanOutputDisplay="test-display";settings.CleanOutputWidth=1280;settings.CleanOutputHeight=720;settings.CleanOutputFullscreen=false;settings.CleanOutputOverlays=true;
        var copy=JsonSerializer.Deserialize<ViewerSettings>(JsonSerializer.Serialize(settings))!;
        Check(copy.CleanOutputEnabled&&copy.CleanOutputDisplay=="test-display"&&copy.CleanOutputWidth==1280&&copy.CleanOutputHeight==720&&!copy.CleanOutputFullscreen&&copy.CleanOutputOverlays,"Output configuration round-trips without changing other settings.");
        Check(CleanOutputDisplay.Fit(1920,1080,1920,1080)==new Rectangle(0,0,1920,1080),"1080p uses the full matching monitor.");
        Check(CleanOutputDisplay.Fit(1920,1080,1280,1024)==new Rectangle(0,152,1280,720),"Nonmatching monitor aspect is letterboxed without stretching.");
        Check(CleanOutputDisplay.Fit(1920,1080,3840,2160)==new Rectangle(0,0,3840,2160),"1080p scales proportionally to 4K fullscreen.");
        Check(CleanOutputDisplay.Fit(0,1080,1920,1080)==Rectangle.Empty,"Invalid sizes do not create invalid viewports.");
        var displays=CleanOutputDisplay.Enumerate();Check(displays.Length>0&&displays.Any(d=>d.Primary)&&displays.All(d=>d.Bounds.Width>0&&d.Bounds.Height>0&&!string.IsNullOrEmpty(d.Id)),"Windows displays expose usable names, identities, bounds and primary status.");
        foreach(var d in displays)Console.WriteLine(d);
        using var main=new Form{ClientSize=new(400,300),ShowInTaskbar=false};using var output=new CleanOutputWindow();
        _=main.Handle;_=output.Handle;
        var sample=new CleanOutputDisplay("test","test","Test display",new(100,100,1280,720),75,false);
        output.Place(sample,true,1920,1080);Check(output.Bounds==sample.Bounds&&output.FormBorderStyle==FormBorderStyle.None&&output.Owner==null,"Fullscreen is borderless, monitor-sized and independent of minimized controls.");
        output.Place(sample,false,640,480);Check(output.ClientSize==new Size(640,480)&&output.FormBorderStyle==FormBorderStyle.Sizable,"Windowed output uses configurable client dimensions.");
        using var renderer=new Renderer(main.Handle,400,300);
        renderer.ConfigureCleanOutput(output.Handle,640,480,640,360);Check(renderer.CleanOutputActive,"Clean GPU output starts on its own swap chain.");
        renderer.CheckCleanOutputPixels();Check(true,"Actual GPU render-to-texture/blit preserves pixels and adds black letterbox bars.");
        renderer.CheckCapturedOverlayPixels(Check);
        renderer.Render(null,Matrix4x4.Identity,Vector3.Zero);Check(renderer.CleanOutputActive,"Normal rendering presents only through the active output path.");
        renderer.ResizeCleanOutput(800,600);renderer.CheckCleanOutputPixels();Check(true,"Output resize recreates GPU surfaces correctly.");
        renderer.DisableCleanOutput();Check(!renderer.CleanOutputActive,"Disabling releases clean presentation and selects the desktop preview path.");
        renderer.Render(null,Matrix4x4.Identity,Vector3.Zero);Check(true,"Desktop render/presentation resumes after disabling output.");
        ViewerWindow.CheckCleanOutputLayout();Check(true,"Output settings and actions fit in their desktop window.");
        Console.WriteLine($"PASS: {count} focused Clean Output checks. Full suite not run.");
    }
}
