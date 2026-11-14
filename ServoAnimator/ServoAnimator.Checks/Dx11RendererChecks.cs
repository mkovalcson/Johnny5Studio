using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ServoAnimator;
using H = HelixToolkit.Wpf.SharpDX;

internal static partial class Program
{
    private static void Dx11RendererChecks(bool benchmark, bool goldOnly=false)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        Dx11MirrorChecks();
        RendererSettingsChecks();
        Exception failure = null;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { await Dx11HardwareChecks(benchmark,goldOnly); }
            catch (Exception error) { failure = error; }
            finally { app.Shutdown(); }
        });
        app.Run();
        if (failure != null) throw failure;
    }
    private static void Dx11MirrorChecks()
    {
        var mesh = PrimitiveMeshes.Box(1, 1, 1);
        mesh.Freeze();
        var brush = new SolidColorBrush(Colors.Red); var material = new DiffuseMaterial(brush);
        var motion = new TranslateTransform3D();
        var part = new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = motion };
        var source = new Model3DGroup(); source.Children.Add(new AmbientLight(Colors.White)); source.Children.Add(part);
        source.Children.Add(new GeometryModel3D(mesh, material));
        using var mirror = new Dx11SceneMirror(); mirror.Synchronize(source);
        Check(mirror.MeshCount == 1 && mirror.MaterialCount == 1, "DX11 shares repeated source meshes and materials");
        long geometryUpdates = mirror.GeometryUpdates, materialUpdates = mirror.MaterialUpdates;
        motion.OffsetX = .5; mirror.Synchronize(source);
        var group = (H.GroupModel3D)mirror.Root.Children[0];
        Check(ReferenceEquals(group.Children[1].Transform, motion) && group.Children[1].Transform.Value.OffsetX == .5, "DX11 retains the existing animated transforms");
        Check(mirror.GeometryUpdates == geometryUpdates, "Joint movement does not rebuild geometry buffers");
        brush.Color = Colors.Blue; mirror.Synchronize(source);
        Check(mirror.MaterialUpdates == materialUpdates + 1 && mirror.GeometryUpdates == geometryUpdates, "Shared light/material changes update once without re-uploading geometry");
        for (int i = 0; i < 50; i++) { part.Geometry = PrimitiveMeshes.Box(1 + i * .001, 1, 1).Clone(); mirror.Synchronize(source); }
        Check(mirror.MeshCount <= 2, "Transient animated meshes do not accumulate in the DX11 cache");
        source.Children.RemoveAt(2); mirror.Synchronize(source);
        Check(group.Children.Count == 2, "Hidden/detached model parts leave the DX11 scene");
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 255, 0, 255 }, 4);
        var imageBrush = new ImageBrush(image); part.Material = new EmissiveMaterial(imageBrush); mirror.Synchronize(source);
        Check(mirror.MaterialCount > 0, "LCD/image materials can be converted to DX11 textures");
        materialUpdates = mirror.MaterialUpdates; imageBrush.ImageSource = image.Clone(); mirror.Synchronize(source);
        Check(mirror.MaterialUpdates > materialUpdates, "Animated LCD images invalidate their DX11 texture");
        double aspect = 16.0 / 9, fov = 38;
        var camera = new HelixToolkit.SharpDX.Cameras.PerspectiveCameraCore { FieldOfView = (float)Dx11Viewport.VerticalFieldOfView(fov, aspect) };
        var projection = camera.CreateProjectionMatrix((float)aspect);
        Check(Math.Abs(projection.M11 - 1 / Math.Tan(fov * Math.PI / 360)) < 1e-5, "DX11 projection matches WPF's horizontal field of view");
    }
    private static async Task Dx11HardwareChecks(bool benchmark,bool goldOnly=false)
    {
        string output = PreviewDirectory("renderer-previews"); Directory.CreateDirectory(output);
        var view = new RobotHeadView(deferModelLoad: true);
        string body = Path.GetFullPath("Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming { Distance = 5.3, VerticalPercent = -50 });
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#91B4D2" });
        view.PrepareExportView(); view.SetExportBackground(new SolidColorBrush(Color.FromRgb(145, 180, 210)));
        var window = new Window { Content = view, Width = 960, Height = 620, Left = 40, Top = 40, Title = "Optional DX11 renderer verification", ShowInTaskbar = false };
        byte[] Capture(string name)
        {
            var pixels = WindowCapture.CaptureClient(window, out int width, out int height);
            Check(pixels != null && pixels.Length == width * height * 4, "Capture renderer output: " + name);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
            return pixels;
        }
        async Task Detail(string name, Point3D target, double distance)
        {
            var frame=view.CaptureCurrentFraming();
            var camera=(PerspectiveCamera)typeof(RobotHeadView).GetField("_camera",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);
            var offset=new Vector3D(distance*.88,-distance*.44,distance*.18);
            camera.Position=target+offset; camera.LookDirection=-offset; camera.UpDirection=new Vector3D(0,0,1);
            await Task.Delay(350); Capture(name); view.ApplyFraming(frame); await Task.Delay(100);
        }
        async Task BodyDetail(string capture,string visualName,double distance)
        {
            var detailVisuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view.CurrentModelScene);
            var visual=detailVisuals[visualName];var bounds=visual.Geometry.Bounds;
            var center=visual.Transform.Transform(new Point3D(bounds.X+bounds.SizeX/2,bounds.Y+bounds.SizeY/2,bounds.Z+bounds.SizeZ/2));
            var xml=System.Xml.Linq.XDocument.Load(body);var link=xml.Descendants("visual").Single(v=>v.Attribute("name")?.Value==visualName).Parent.Attribute("name").Value;
            Check(view.CurrentModelScene.TryTransformLinkPoint(link,center,out var target),"Locate body detail for actual renderer comparison: "+visualName);
            await Detail(capture,target,distance);
        }
        try
        {
            window.Show(); await Task.Delay(1500);
            Check(view.ActiveRenderer == UrdfRenderer.Wpf && view.RendererAdapter == null, "WPF remains the default without constructing a DX11 device");
            var wpfPixels = Capture("Wpf-full-body");
            Check(view.SelectRenderer(UrdfRenderer.Direct3D11), "Hardware DX11 selection succeeds: " + view.RendererError);
            await Task.Delay(2000);
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11 && view.Dx11RenderedFrames > 0, "DX11 actually renders hardware frames: " + view.RendererError);
            Console.WriteLine("DX11 adapter: " + view.RendererAdapter);
            var draw = (Action<TimeSpan>)typeof(RobotHeadView).GetMethod("RenderDirect3D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .CreateDelegate(typeof(Action<TimeSpan>), view);
            UrdfRenderLoop.Current.RemoveRender(draw);
            long pausedFrames = view.Dx11RenderedFrames; await Task.Delay(300);
            Check(view.Dx11RenderedFrames == pausedFrames, "DX11 has no independent automatic draw loop outside the ordered render phase");
            UrdfRenderLoop.Current.AddRender(draw); await Task.Delay(300);
            Check(view.Dx11RenderedFrames > pausedFrames, "The final phase resumes actual DX11 drawing");
            var dxPixels = Capture("Dx11-full-body");
            if(!goldOnly)
            {
                await BodyDetail("Dx11-body-shoulder-0","cad_shoulder_left_bump",.5);
                await BodyDetail("Dx11-body-hand-0","body_finger_base_pad_left_o_dome_visual",.35);
                await BodyDetail("Dx11-body-chest-0","body_chest_shell_visual",.7);
            }
            Check(Enumerable.Range(0, 3).All(c => Math.Abs(wpfPixels[400 + c] - dxPixels[400 + c]) <= 2), "DX11 preserves the WPF background through its transparent surface");
            long beforeResize = view.Dx11RenderedFrames;
            window.Width += 80; await Task.Delay(400); window.Width -= 80; await Task.Delay(400);
            Check(view.Dx11RenderedFrames > beforeResize && view.ActiveRenderer == UrdfRenderer.Direct3D11, "DX11 keeps rendering after resizing");
            var beforePointerZoom=view.CaptureCurrentFraming();
            var zoomPointer=new Point(view.ActualWidth*.58,view.ActualHeight*.35);
            Check(view.TryPointerZoomAnchor(zoomPointer,out var zoomAnchor)&&view.ZoomAtPointer(zoomPointer,120),"Actual DX11 view accepts pointer-centered zoom");
            object[] zoomProjection={zoomAnchor,new Point()};
            Check((bool)typeof(RobotHeadView).GetMethod("ProjectZoomAnchor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,zoomProjection)&&
                ((Point)zoomProjection[1]-zoomPointer).Length<.05,"Actual DX11 camera keeps the selected surface under the cursor");
            await Task.Delay(250);
            Check(view.ActiveRenderer==UrdfRenderer.Direct3D11&&view.Dx11RenderedFrames>beforeResize,"DX11 continues rendering after pointer zoom");
            view.ApplyFraming(beforePointerZoom);await Task.Delay(100);
            var appearance = UrdfConfiguration.CreateDefault(); appearance.Rendering.SelectedRenderer = UrdfRenderer.Direct3D11;
            var weatherClose = view.CaptureCurrentFraming(); weatherClose.Distance = 1.15; weatherClose.VerticalPercent = 0; weatherClose.Yaw = .5; weatherClose.Pitch = .12;
            view.ApplyFraming(weatherClose); await Task.Delay(400); Capture("Dx11-weathering-head-0");
            view.ApplyFraming(beforePointerZoom); await Task.Delay(100);
            long weatherGeometry = 0;
            double previousWeatherLight = double.MaxValue;
            for (int level = 1; !goldOnly && level <= 3; level++)
            {
                long finishFrames=view.Dx11RenderedFrames;
                appearance.Rendering.WeatheringLevel = level; view.SetUrdfConfiguration(appearance);
                if(level==1)
                {
                    view.WaitForAppearancePreparation();
                    Check(view.AppearancePreparation.IsCompleted,"Explicit capture waits for background appearance preparation while pumping the UI");
                }
                await view.AppearancePreparation;
                finishFrames=view.Dx11RenderedFrames;
                // First selection uploads many new texture charts. Wait for
                // completed hardware frames, not just a fixed wall-clock delay.
                for(int attempt=0;attempt<75 && view.Dx11RenderedFrames<finishFrames+3;attempt++) await Task.Delay(200);
                Check(view.Dx11RenderedFrames>=finishFrames+3,"New finish completes hardware frames before comparison");
                await Task.Delay(250);
                var finish = Capture("Dx11-weathering-" + level);
                var bodyPixels = Enumerable.Range(0, Math.Min(finish.Length, dxPixels.Length) / 4)
                    .Where(p => Math.Abs(dxPixels[p * 4] - 210) + Math.Abs(dxPixels[p * 4 + 1] - 180) + Math.Abs(dxPixels[p * 4 + 2] - 145) > 45).ToArray();
                double Light(byte[] bytes) => bodyPixels.Average(p => .0722 * bytes[p * 4] + .7152 * bytes[p * 4 + 1] + .2126 * bytes[p * 4 + 2]);
                double weatherLight = Light(finish), cleanLight = Light(dxPixels);
                Console.WriteLine($"DX11 weathering {level}: body brightness {weatherLight:F1}, clean {cleanLight:F1}");
                Check(weatherLight <= cleanLight + 2, "Weathering does not wash out the robot on actual DX11 hardware");
                Check(weatherLight <= previousWeatherLight + 1, "Increasing distress does not increase overall body brightness");
                previousWeatherLight = weatherLight;
                Check(finish.Zip(dxPixels).Count(p => Math.Abs(p.First - p.Second) > 8) > 200, "Weathering produces visible detail on actual DX11 hardware: " + level);
                var weatherRenderer = (Dx11Viewport)typeof(RobotHeadView).GetField("_dx11", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
                if (level > 1) Check(weatherRenderer.Mirror.GeometryUpdates == weatherGeometry, "Higher distress levels reuse existing GPU geometry");
                weatherGeometry = weatherRenderer.Mirror.GeometryUpdates;
                Check(view.ActiveRenderer == UrdfRenderer.Direct3D11, "Weathering retains hardware rendering");
                view.ApplyFraming(weatherClose); await Task.Delay(300); Capture("Dx11-weathering-head-" + level);
                view.ApplyFraming(beforePointerZoom); await Task.Delay(100);
                if(level<=2)
                {
                    await BodyDetail("Dx11-body-shoulder-"+level,"cad_shoulder_left_bump",.5);
                    await BodyDetail("Dx11-body-hand-"+level,"body_finger_base_pad_left_o_dome_visual",.35);
                    await BodyDetail("Dx11-body-chest-"+level,"body_chest_shell_visual",.7);
                }
                if(level==3)
                {
                    var other=weatherClose.Clone(); other.Yaw=-.65; other.Pitch=.35;
                    view.ApplyFraming(other); await Task.Delay(350); Capture("Dx11-weathering-antenna-3"); view.ApplyFraming(beforePointerZoom);
                    if(view.CurrentModelScene.TryTransformLinkPoint("body_seg_finger0_left_link",new Point3D(),out var hand)) await Detail("Dx11-weathering-hand-3",hand,.5);
                    if(view.CurrentModelScene.TryTransformLinkPoint("body_shoulder_housing_left_link",new Point3D(),out var shoulder)) await Detail("Dx11-weathering-shoulder-3",shoulder,.65);
                }
            }
            appearance.Rendering.WeatheringLevel = 0; view.SetUrdfConfiguration(appearance); await Task.Delay(300);
            foreach(var accessory in Enum.GetValues<UrdfAccessory>())
            {
                appearance.Rendering.DefaultAccessory=accessory; view.SetUrdfConfiguration(appearance);
                view.ApplyAccessoryTimeline(Array.Empty<ServoCommand>(),0);
                Check(view.LaserHidden==(accessory==UrdfAccessory.None)&&view.ToolboxShown==(accessory==UrdfAccessory.Toolbox),"Accessory default selects "+accessory);
            }
            appearance.Rendering.DefaultAccessory=UrdfAccessory.None; view.SetUrdfConfiguration(appearance);
            view.ApplyAccessoryTimeline(new[]{new ServoCommand{Servo=ServoNames.ToolboxSwap,OffsetSeconds=0,TextValue=new ToolboxSwapCommand{Swap=true}.ToJson()}},0);
            Check(view.ToolboxShown&&!view.LaserHidden,"Explicit Toolbox Swap overrides None");
            view.ApplyAccessoryTimeline(Array.Empty<ServoCommand>(),0);
            Check(view.LaserHidden,"Returning before accessory commands restores None");
            appearance.Rendering.RobotFinish=UrdfRobotFinish.GoldJohnny; view.SetUrdfConfiguration(appearance); await Task.Delay(500);
            var goldVisuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view.CurrentModelScene);
            foreach (string robot in new[] { "Johnny5Head", "Johnny5FullBody" })
            {
                var tubeScene = UrdfScene.Load(Path.GetFullPath("Config/Models/Robots/" + robot + "/johnny5_head.urdf"));
                var tubeVisuals = (Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(tubeScene);
                var original = tubeVisuals.ToDictionary(p=>p.Key,p=>p.Value.Material);
                var neckGold=(MaterialGroup)tubeVisuals["left_fabco_body_smooth_gold"].Material;
                var finishGold=(MaterialGroup)UrdfWeathering.Gold;
                Check(((SolidColorBrush)finishGold.Children.OfType<DiffuseMaterial>().Single().Brush).Color==Color.FromRgb(171,121,54),
                    robot+" Gold Johnny uses the slightly lighter #AB7936 bronze-gold shade");
                var neckSpec=neckGold.Children.OfType<SpecularMaterial>().Single();var goldSpec=finishGold.Children.OfType<SpecularMaterial>().Single();
                Check(((SolidColorBrush)neckSpec.Brush).Color==((SolidColorBrush)goldSpec.Brush).Color&&neckSpec.SpecularPower==goldSpec.SpecularPower,
                    robot+" Gold Johnny uses the neck cylinder's neutral highlight");
                tubeScene.ApplyWeathering(0,true);
                if(robot=="Johnny5FullBody")
                {
                    var casterRubber=tubeVisuals.Where(p=>p.Key.StartsWith("body_caster_wheel_",StringComparison.Ordinal)).ToArray();
                    Check(casterRubber.Length==66,"Both caster tires and all 64 tread lugs are present");
                    foreach(var tire in casterRubber)
                    {
                        Check(ReferenceEquals(tire.Value.Material,original[tire.Key]),"Gold Johnny preserves original caster rubber: "+tire.Key);
                        var tireColor=((SolidColorBrush)((MaterialGroup)tire.Value.Material).Children.OfType<DiffuseMaterial>().Single().Brush).Color;
                        Check(Math.Max(tireColor.R,Math.Max(tireColor.G,tireColor.B))<20,"Caster tire/tread rubber stays black");
                    }
                    foreach(string rod in new[]{"body_neck_front_rod_visual","body_neck_rear_core_l_visual","body_neck_rear_core_r_visual",
                        "cad_neck_front_boot","cad_neck_rear_boot_l","cad_neck_rear_boot_r"})
                        Check(ReferenceEquals(tubeVisuals[rod].Material,original[rod]),"Gold Johnny preserves the original pivoting neck rod/boot finish: "+rod);
                    foreach(string mount in new[]{"body_neck_front_sleeve_visual","cad_neck_upper_cheek_l","cad_neck_upper_cheek_r"})
                        Check(ReferenceEquals(tubeVisuals[mount].Material,UrdfWeathering.Gold),"Gold Johnny retains gold on the neighboring neck mount: "+mount);
                }
                Check(ReferenceEquals(tubeVisuals["neck_rigid_support"].Material,UrdfWeathering.Gold),robot+" Gold Johnny covers the rigid neck support below the black bellows");
                foreach (string tube in new[] { "left_eye_tube", "right_eye_tube" })
                    Check(ReferenceEquals(tubeVisuals[tube].Material,UrdfWeathering.Gold),robot+" Gold Johnny covers "+tube);
                var goldSupports=new HashSet<string>{"left_eye_tube","right_eye_tube"};
                foreach(string side in new[]{"left","right"})foreach(string support in new[]{"eye_fixed_cad1ee_rest","eye_fixed_f6f6f3","gimbal_bar_top","gimbal_bar_bottom","eye_gimbal_spacers_cad1ee","eye_gimbal_a0a0a0","eye_gimbal_pivot_screws_cad1ee","eye_gimbal_side_pivot_screws_cad1ee"})
                {
                    string supportName=side+"_"+support;goldSupports.Add(supportName);
                    Check(ReferenceEquals(tubeVisuals[supportName].Material,UrdfWeathering.Gold),robot+" Gold Johnny covers eye support: "+supportName);
                }
                var bellows=tubeVisuals.Where(p=>p.Key.Contains("bellows")||p.Key=="neck_static_black").ToArray();
                Check(bellows.Any(p=>p.Key=="neck_static_black")&&(robot!="Johnny5FullBody"||bellows.Any(p=>p.Key.Contains("waist_bellows"))),robot+" includes the expected neck/waist bellows");
                foreach(var bellow in bellows)
                {
                    Check(ReferenceEquals(bellow.Value.Material,original[bellow.Key]),robot+" Gold Johnny preserves bellows: "+bellow.Key);
                    var black=((SolidColorBrush)((MaterialGroup)bellow.Value.Material).Children.OfType<DiffuseMaterial>().Single().Brush).Color;
                    Check(Math.Max(black.R,Math.Max(black.G,black.B))<20,"Bellows retain their original black color");
                }
                var mouthLights=tubeVisuals.Where(p=>p.Key.StartsWith("lip_voice_led_")||p.Key.StartsWith("mouth_side_")||p.Key=="simple_mouth_static_0f4e11").ToArray();
                Check(mouthLights.Count(p=>p.Key.StartsWith("lip_voice_led_"))==14,robot+" has fourteen original mouth LED lenses");
                foreach(var light in mouthLights)
                    Check(ReferenceEquals(light.Value.Material,original[light.Key]),robot+" Gold Johnny preserves mouth lights and their board: "+light.Key);
                tubeScene.SetMaterialColor("lip_led_06_dynamic",Colors.OrangeRed);
                tubeScene.SetMaterialEmissive("lip_led_06_dynamic",Colors.Orange);
                var activeLed=(MaterialGroup)tubeVisuals["lip_voice_led_06"].Material;
                Check(((SolidColorBrush)activeLed.Children.OfType<DiffuseMaterial>().Single().Brush).Color==Colors.OrangeRed&&activeLed.Children.OfType<EmissiveMaterial>().Any(),robot+" mouth LEDs remain animated in Gold Johnny");
                foreach (var optical in tubeVisuals.Where(p=>(p.Key.Contains("eye")||p.Key.Contains("iris")||p.Key.Contains("pupil"))&&!goldSupports.Contains(p.Key)))
                    Check(ReferenceEquals(optical.Value.Material,original[optical.Key]),robot+" preserves other eye components: "+optical.Key);
                tubeScene.ApplyWeathering(0);
                Check(tubeVisuals.All(p=>ReferenceEquals(p.Value.Material,original[p.Key])),robot+" restores original colors after Gold Johnny");
            }
            Check(ReferenceEquals(goldVisuals["left_eye_tube"].Material,UrdfWeathering.Gold)&&ReferenceEquals(goldVisuals["right_eye_tube"].Material,UrdfWeathering.Gold),"Live Gold Johnny preview includes both eye tubes");
            var blackPads=goldVisuals.Where(p=>p.Key.Contains("tread_pads")).Select(p=>p.Value).ToArray();
            Check(blackPads.Length==2&&blackPads.All(p=>p.Material is MaterialGroup group&&group.Children.OfType<DiffuseMaterial>().Any(d=>d.Brush is SolidColorBrush brush&&brush.Color.R<20&&brush.Color.G<20&&brush.Color.B<20)),"Gold Johnny preserves both black track tread pads");
            Check(goldVisuals.Where(p=>p.Key.Contains("tread_plates")).All(p=>ReferenceEquals(p.Value.Material,UrdfWeathering.Gold)),"Gold Johnny still covers the metal tread plates");
            view.CurrentModelScene.SetTreadTravel(.04,.07);
            Check(blackPads.All(p=>!ReferenceEquals(p.Material,UrdfWeathering.Gold)),"Moving treads retain their black pads in Gold Johnny");
            view.CurrentModelScene.SetTreadTravel(0,0);
            Capture("Dx11-Gold-Johnny"); view.ApplyFraming(weatherClose); await Task.Delay(300); Capture("Dx11-Gold-Johnny-head");
            var goldBounds=view.CurrentModelScene.RootModel.Bounds;
            await Detail("Dx11-Gold-Johnny-full",new Point3D(goldBounds.X+goldBounds.SizeX/2,goldBounds.Y+goldBounds.SizeY/2,goldBounds.Z+goldBounds.SizeZ/2),goldBounds.SizeZ*3.2);
            view.ApplyFraming(beforePointerZoom);
            appearance.Rendering.RobotFinish=UrdfRobotFinish.Original; view.SetUrdfConfiguration(appearance); await Task.Delay(200);
            var shoulderVisual=goldVisuals["cad_shoulder_left_round_sleeve"];
            var sleeveBounds=shoulderVisual.Geometry.Bounds;
            var sleeveCenter=shoulderVisual.Transform.Transform(new Point3D(sleeveBounds.X+sleeveBounds.SizeX/2,sleeveBounds.Y+sleeveBounds.SizeY/2,sleeveBounds.Z+sleeveBounds.SizeZ/2));
            if(view.CurrentModelScene.TryTransformLinkPoint("body_shoulder_housing_left_link",sleeveCenter,out var smoothShoulder))
                await Detail("Dx11-smoothed-shoulder",smoothShoulder,.65);
            if(goldOnly)return;
            foreach (int quality in new[] { 0, 1, 3, 2 })
            {
                appearance.Rendering.Direct3D11.AntiAliasing = quality;
                view.SetUrdfConfiguration(appearance); await Task.Delay(600);
                var dx = (Dx11Viewport)typeof(RobotHeadView).GetField("_dx11", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
                int samples = dx.View.RenderHost.RenderBuffer.ColorBufferSampleDesc.Count;
                Check(samples == (quality == 0 ? 1 : 1 << quality), "Live anti-aliasing changes the hardware render target sample count");
                var aaPixels = Capture("Dx11-AA-" + samples + "x");
                Check(Enumerable.Range(0, 3).All(c => Math.Abs(aaPixels[400 + c] - dxPixels[400 + c]) <= 2), "AA preserves transparent composition over the background");
                Check(view.ActiveRenderer == UrdfRenderer.Direct3D11, "Changing AA retains the hardware renderer");
            }
            appearance.Rendering.Direct3D11.AntiAliasingMode = UrdfAntiAliasingMode.Fxaa;
            byte[] fxaaOff = null;
            foreach (int quality in new[] { 0, 1, 2, 3, 4 })
            {
                appearance.Rendering.Direct3D11.FxaaQuality = quality;
                view.SetUrdfConfiguration(appearance); await Task.Delay(600);
                var dx = (Dx11Viewport)typeof(RobotHeadView).GetField("_dx11", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
                Check(dx.View.RenderHost.RenderBuffer.ColorBufferSampleDesc.Count == 1, "FXAA disables multisampling on the hardware render target");
                Check(dx.View.FXAALevel == HelixToolkit.SharpDX.FXAALevel.None, "Opaque library FXAA stays disabled");
                Check(quality == 0 ? dx.View.Effect == null : dx.View.Effect is UrdfToneEffect effect && effect.FxaaQuality == quality,
                    "Live FXAA quality reaches the alpha-preserving GPU shader");
                var aaPixels = Capture("Dx11-FXAA-" + quality);
                Check(Enumerable.Range(0, 3).All(c => Math.Abs(aaPixels[400 + c] - dxPixels[400 + c]) <= 2), "FXAA preserves transparent composition over the background");
                if (quality == 0) fxaaOff = aaPixels;
                else Check(Changed(fxaaOff, aaPixels) > 20, "FXAA visibly smooths rendered pixels");
            }
            appearance.Rendering.Direct3D11.AntiAliasingMode = UrdfAntiAliasingMode.Msaa;
            appearance.Rendering.Direct3D11.Brightness = .65; appearance.Rendering.Direct3D11.Gamma = 1.2;
            appearance.Rendering.Direct3D11.Contrast = .9; appearance.Rendering.Direct3D11.Highlights = .8;
            appearance.Rendering.Direct3D11.AmbientLight = 1.25; appearance.Rendering.Direct3D11.KeyLight = .8;
            appearance.Rendering.Wpf.Brightness = .55;
            view.SetUrdfConfiguration(appearance); await Task.Delay(700);
            var adjusted = Capture("Dx11-profile-adjusted");
            static int Changed(byte[] a, byte[] b) => Enumerable.Range(0, Math.Min(a.Length, b.Length) / 4)
                .Count(p => Math.Abs(a[p * 4] - b[p * 4]) + Math.Abs(a[p * 4 + 1] - b[p * 4 + 1]) + Math.Abs(a[p * 4 + 2] - b[p * 4 + 2]) > 12);
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11 && Changed(dxPixels, adjusted) > 500, "Saved DX11 profile applies lighting and GPU tone controls to visible pixels");
            Check(Enumerable.Range(0, 3).All(c => Math.Abs(adjusted[400 + c] - dxPixels[400 + c]) <= 2), "Tone adjustment preserves transparency and the background color");
            var snapshot = view.RenderSnapshot(240, null, TvFit.Contain);
            var snapshotPixels = new byte[snapshot.PixelWidth * snapshot.PixelHeight * 4];
            snapshot.CopyPixels(snapshotPixels, snapshot.PixelWidth * 4, 0);
            Check(Enumerable.Range(0, snapshotPixels.Length / 4).Count(p =>
                Math.Abs(snapshotPixels[p * 4] - 210) + Math.Abs(snapshotPixels[p * 4 + 1] - 180) + Math.Abs(snapshotPixels[p * 4 + 2] - 145) > 20) > 100,
                "API bitmap snapshots include the robot while DX11 is selected");
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11, "Bitmap capture retains the configured DX11 renderer");
            appearance.Rendering.SelectedRenderer = UrdfRenderer.Wpf; view.SetUrdfConfiguration(appearance); await Task.Delay(600);
            var adjustedWpf = Capture("Wpf-profile-adjusted");
            Check(view.ActiveRenderer == UrdfRenderer.Wpf && Changed(wpfPixels, adjustedWpf) > 500, "WPF uses its separately configured tone profile");
            appearance.Rendering.Direct3D11 = new(); appearance.Rendering.Wpf = new(); appearance.Rendering.SelectedRenderer = UrdfRenderer.Direct3D11;
            view.SetUrdfConfiguration(appearance); await Task.Delay(400);
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11, "Configuration selects DX11 without a viewport selector");
            var frame = view.CaptureCurrentFraming(); frame.Yaw = .3; view.ApplyFraming(frame);
            view.SetServo(ServoNames.NeckTurn, 30); view.SetMouth(.8);
            await Task.Delay(300); Capture("Dx11-pose-and-mouth");
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11, "Camera, servo and mouth changes retain DX11 rendering");
            var privateFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object ViewField(string name) => typeof(RobotHeadView).GetField(name, privateFlags).GetValue(view);
            void ViewCall(string name, params object[] values) => typeof(RobotHeadView).GetMethod(name, privateFlags).Invoke(view, values);
            appearance.Rendering.Direct3D11.AntiAliasingMode = UrdfAntiAliasingMode.Fxaa;
            appearance.Rendering.Direct3D11.FxaaQuality = 3;
            view.SetUrdfConfiguration(appearance);
            var movingImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 255, 255, 255, 0, 0, 255, 0, 255, 0, 255, 255, 255, 255, 255 }, 8);
            typeof(RobotHeadView).GetField("_configuredBackground", privateFlags).SetValue(view, new ImageBrush(movingImage));
            ViewCall("ConfigureMovingBackground", new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundFrameRate = 30 });
            view.SetExportBackground((Brush)ViewField("_configuredBackground"));
            ViewCall("UpdateMovingBackgroundSubscription");
            var initialMatrix = ((MatrixTransform)ViewField("_backgroundTransform")).Matrix;
            await Task.Delay(500); Capture("Dx11-moving-background");
            Check((bool)ViewField("_backgroundSubscribed") && initialMatrix != ((MatrixTransform)ViewField("_backgroundTransform")).Matrix,
                "Moving background advances in the shared cycle while DX11 draws the robot");
            window.Hide(); double pausedBackground = (double)ViewField("_backgroundElapsed"); await Task.Delay(200);
            Check(!(bool)ViewField("_backgroundSubscribed") && (double)ViewField("_backgroundElapsed") == pausedBackground,
                "Hiding the real preview suspends background updates");
            window.Show(); await Task.Delay(300);
            Check((double)ViewField("_backgroundElapsed") > pausedBackground && view.ActiveRenderer == UrdfRenderer.Direct3D11,
                "Showing the preview resumes coordinated background and DX11 updates");
            UrdfRenderLoop.Current.RemoveRender(draw); pausedFrames = view.Dx11RenderedFrames; await Task.Delay(150);
            Check(view.Dx11RenderedFrames == pausedFrames, "Resize and visibility changes cannot restart an independent DX11 draw loop");
            UrdfRenderLoop.Current.AddRender(draw);
            ViewCall("StopMovingBackground"); view.SetExportBackground(new SolidColorBrush(Color.FromRgb(145, 180, 210)));
            window.Hide(); appearance.Rendering.WeatheringLevel=3; view.SetUrdfConfiguration(appearance);
            Check(view.CurrentModelScene.WeatheringLevel==0&&view.AppearancePreparation.IsCompleted,"Hidden URDF views defer unnecessary weathering preparation");
            window.Show();await view.AppearancePreparation;
            Check(view.CurrentModelScene.WeatheringLevel==3,"Showing a deferred view restores its selected finish");
            appearance.Rendering.WeatheringLevel=0;view.SetUrdfConfiguration(appearance);
            view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D,
                Stage = new UrdfStageSettings { ModelFile = Path.GetFullPath("Config/Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf") } });
            await Task.Delay(300); Capture("Dx11-scenery");
            Check(view.HasTvScreen(TvFrontCommand.DefaultScreen), "DX11 keeps existing TV screen projection/overlay geometry");
            view.CurrentModelScene.SetFallApartFrame(70); await Task.Delay(300); Capture("Dx11-disassembly");
            Check(view.CurrentModelScene.FallApartActive && view.ActiveRenderer == UrdfRenderer.Direct3D11, "DX11 displays disassembly transforms");
            view.CurrentModelScene.SetFallApartFrame(null); view.CurrentModelScene.SetTreadTravel(.02, .02);
            view.CurrentModelScene.SetToolboxShown(false); view.CurrentModelScene.SetLaserHidden(false); view.CurrentModelScene.SetJoint("LaserLower", BodyControlCatalog.LaserLowerMaximum);
            view.SetLaserShots(FireLaserTimeline.ShotsAt(new[] { new ServoCommand { Servo = ServoNames.FireLaser, TextValue = new FireLaserCommand { Sound = FireLaserCommand.NoSound }.ToJson() } }, .05));
            await Task.Delay(300); Capture("Dx11-treads-and-laser");
            Check(view.LaserShotModels > 0 && view.ActiveRenderer == UrdfRenderer.Direct3D11, "DX11 renders tread geometry changes and generated laser parts");
            string video = Path.Combine(output, "renderer-video-fixture.mp4");
            WriteTestVideo(video, 320, 240, 30, 60);
            var tv = new TvState(1, new TvFrontCommand(), true, new TvShowCommand { Path = video, Volume = 0 }, .5, 1);
            view.SetTimelineTv(tv, true, 1, output, false);
            await Task.Delay(1800);
            var tvPixels = Capture("Dx11-tv-video");
            static int Colored(byte[] pixels, bool red)
            {
                int count = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (red ? pixels[i + 2] > 180 && pixels[i] < 70 : pixels[i] > 180 && pixels[i + 2] < 70) count++;
                return count;
            }
            Check(Colored(tvPixels, true) > 1000 && Colored(tvPixels, false) > 1000, "TV video stays visible above DX11 with the robot hidden for the TV shot");
            view.SetTimelineTv(TvState.Inactive, false, 0, output, false);
            var clip = new ServoCommand { Servo = ServoNames.VideoClip, TextValue = new VideoClipCommand { Path = video, SourceSeconds = 2, Muted = true }.ToJson() };
            view.SetTimelineVideo(VideoClipTimeline.At(new[] { clip }, .5), true, 1, output);
            await Task.Delay(1800);
            var videoPixels = Capture("Dx11-video-backdrop");
            Check(Colored(videoPixels, true) > 1000 && Colored(videoPixels, false) > 1000, "Backdrop video stays visible behind the DX11 robot");
            view.SetTimelineVideo(null, false, 0, output); view.ReleasePackageMedia(); File.Delete(video);
            typeof(RobotHeadView).GetMethod("FallbackRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(view, new object[] { new InvalidOperationException("Simulated device failure") });
            Check(view.ActiveRenderer == UrdfRenderer.Wpf && view.RendererError.Contains("Simulated"), "Renderer failure restores WPF and reports the reason");
            Check(view.SelectRenderer(UrdfRenderer.Direct3D11), "DX11 can be selected again after fallback");
            Check(view.SelectRenderer(UrdfRenderer.Wpf), "The view switches back to WPF"); await Task.Delay(400); Capture("Wpf-restored");
            Check(view.Dx11RenderedFrames == 0 && view.ActiveRenderer == UrdfRenderer.Wpf, "Switching back releases the DX11 renderer");
            if (benchmark)
            {
                view.SetLaserShots(Array.Empty<LaserShot>());
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await RendererComparison.Run(view, Console.WriteLine, timeout.Token);
                File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(result, ProjectPackage.JsonOptions));
                Console.WriteLine(result.Summary);
                Check(result.Trials.Length == 4 && result.Trials.All(t => t.Frames > 4), "Performance comparison records four real, warmed-up trials");
            }
            Check(view.SelectRenderer(UrdfRenderer.Direct3D11), "DX11 can be reopened for unload verification");
            window.Content = null; await Task.Delay(100);
            Check(view.ActiveRenderer == UrdfRenderer.Wpf && view.RendererAdapter == null, "Unloading releases DX11 resources");
            window.Content = view; await Task.Delay(1800);
            Check(view.ActiveRenderer == UrdfRenderer.Direct3D11 && view.Dx11RenderedFrames > 0,
                $"Reloading the view restores the configured renderer (active {view.ActiveRenderer}, selected {appearance.Rendering.SelectedRenderer}, frames {view.Dx11RenderedFrames}, loaded {view.IsLoaded}, error {view.RendererError})");
        }
        finally { view.SelectRenderer(UrdfRenderer.Wpf); view.ReleasePackageMedia(); window.Close(); }
    }
}
