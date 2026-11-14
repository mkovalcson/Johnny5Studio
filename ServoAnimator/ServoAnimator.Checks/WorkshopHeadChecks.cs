using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void WorkshopHeadChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        Exception failure=null;
        app.Dispatcher.BeginInvoke(async ()=>
        {
            Window window=null;
            try
            {
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                string path=Path.GetFullPath("Config/Models/Robots/Johnny5Head/johnny5_head.urdf");
                var scene=UrdfScene.Load(path);
                var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",flags).GetValue(scene);
                var original=visuals.ToDictionary(p=>p.Key,p=>p.Value.Material);
                var view=new RobotHeadView(deferModelLoad:true);
                view.InstallModel(path,scene,new UrdfFraming { Distance=.95,Yaw=.5,Pitch=.25 });
                view.SetTimelineBackground(new UrdfPresentationSettings{BackgroundMode=UrdfBackgroundMode.Color,BackgroundColor="#34383F"});
                view.PrepareExportView();view.SetExportBackground(new SolidColorBrush(Color.FromRgb(52,56,63)));
                window=new Window{Content=view,Width=1100,Height=900,Left=20,Top=20,ShowInTaskbar=false,Title="Workshop Used head comparison"};
                window.Show();await Task.Delay(1000);
                Check(view.SelectRenderer(UrdfRenderer.Direct3D11),"Workshop head uses actual DX11 hardware");
                await Task.Delay(1000);
                string output=PreviewDirectory("weathering-previews");Directory.CreateDirectory(output);
                async Task Capture(string name,double yaw=.5)
                {
                    var bounds=scene.RootModel.Bounds;
                    var target=new Point3D(bounds.X+bounds.SizeX*.5,bounds.Y+bounds.SizeY*.5,bounds.Z+bounds.SizeZ*.53);
                    var camera=(PerspectiveCamera)typeof(RobotHeadView).GetField("_camera",flags).GetValue(view);
                    double distance=.83;
                    var offset=new Vector3D(distance*Math.Cos(yaw)*.96,distance*Math.Sin(yaw)*.96,distance*.28);
                    camera.Position=target+offset;camera.LookDirection=-offset;camera.UpDirection=new Vector3D(0,0,1);
                    if(view.ActiveRenderer==UrdfRenderer.Direct3D11)
                    {
                        long frames=view.Dx11RenderedFrames;
                        for(int i=0;i<50&&view.Dx11RenderedFrames<frames+4;i++)await Task.Delay(150);
                        Check(view.Dx11RenderedFrames>=frames+4,"Hardware frames complete before "+name);
                    }
                    else await Task.Delay(400);
                    var bytes=WindowCapture.CaptureClient(window,out int width,out int height);
                    var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgr32,null,bytes,width*4);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
                }
                await Capture("Workshop-head-clean");
                var config=UrdfConfiguration.CreateDefault();config.Rendering.SelectedRenderer=UrdfRenderer.Direct3D11;config.Rendering.WeatheringLevel=2;
                view.SetUrdfConfiguration(config);await view.AppearancePreparation;
                var revised=visuals.ToDictionary(p=>p.Key,p=>p.Value.Material);
                var surfaces=(IEnumerable)typeof(UrdfScene).GetField("_weatheringSurfaces",flags).GetValue(scene);
                foreach(var surface in surfaces)
                {
                    object Get(string key)=>surface.GetType().GetProperty(key).GetValue(surface);
                    var model=(GeometryModel3D)Get("Model");
                    var legacy=UrdfWeathering.Material((Color)Get("Color"),(Color)Get("Specular"),(double)Get("Shine"),(bool)Get("Rubber"),2,(MeshGeometry3D)model.Geometry,(bool)Get("Stainless"));
                    if(model.Material!=null)model.Material=legacy;if(model.BackMaterial!=null)model.BackMaterial=legacy;
                }
                await Capture("Workshop-head-before");
                scene.ApplyWeathering(0);scene.ApplyWeathering(2);
                await Capture("Workshop-head-after");
                await Capture("Workshop-head-after-side",-.65);
                await Capture("Workshop-head-after-vents",2.15);
                Check(view.SelectRenderer(UrdfRenderer.Wpf),"Revised Workshop head supports WPF");
                await Capture("Workshop-head-after-Wpf");
                foreach(string name in new[]{"static_01","static_02","upper_left_flap_panel","upper_right_flap_panel","rear_cover_cad"})
                {
                    Check(visuals[name].Material is MaterialGroup g&&g.Children.OfType<DiffuseMaterial>().Any(d=>d.Brush is ImageBrush),"Target head part receives its Workshop recipe: "+name);
                    Check(ReferenceEquals(visuals[name].Material,revised[name]),"Workshop cache restores revised material: "+name);
                }
                foreach(var optical in visuals.Where(p=>p.Key.Contains("iris")||p.Key.Contains("pupil")))
                    Check(ReferenceEquals(optical.Value.Material,original[optical.Key]),"Workshop preserves optics: "+optical.Key);
                var panel=(MaterialGroup)visuals["static_02"].Material;
                var image=(BitmapSource)((ImageBrush)panel.Children.OfType<DiffuseMaterial>().First().Brush).ImageSource;
                var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
                double silver=(double)Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[i*4+2]>90&&Math.Abs(pixels[i*4]-pixels[i*4+2])<45)/(pixels.Length/4);
                Console.WriteLine($"Workshop blue panel silver texel coverage: {silver:P2}");
                Check(silver>.0002&&silver<.035,"Workshop blue paint has visible chips while retaining broad unchipped areas");
                Check(UrdfWeathering.WorkshopPart("Meshes/RearCover/rear_cover.stl","")==UrdfWorkshopPart.Shell,"Silver rear cover is separate from blue vent fins");
                var convexMaps=(System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D,float[]>)typeof(UrdfWeathering).GetField("ConvexEdgeMaps",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                foreach(string side in new[]{"left","right"})for(int fin=1;fin<=5;fin++)
                {
                    string name=$"vent_{side}_fin{fin}";
                    Check(UrdfWeathering.WorkshopPart($"Meshes/SimplifiedHead2/{name}.stl","")==UrdfWorkshopPart.Vent,"Actual animated blue fin receives vent treatment: "+name);
                    var ventMesh=(MeshGeometry3D)visuals[name+"_01"].Geometry;
                    Check(convexMaps.TryGetValue(ventMesh,out var finMask)&&finMask.Count(v=>v>.2)>20,"Blue fin has visible edge wear mask: "+name);
                }
                var sample=UrdfWeathering.Map(PrimitiveMeshes.Box(.1,.1,.02));
                byte[] Tex(Color color,UrdfWorkshopPart part)
                {
                    var texture=UrdfWeathering.WorkshopTexture(color,sample,part);
                    var data=new byte[texture.PixelWidth*texture.PixelHeight*4];texture.CopyPixels(data,texture.PixelWidth*4,0);return data;
                }
                var black=Tex(Color.FromRgb(3,3,3),UrdfWorkshopPart.PaintedHousing);
                var blue=Tex(Color.FromRgb(8,11,78),UrdfWorkshopPart.PaintedHousing);
                var blueFin=Tex(Color.FromRgb(8,11,78),UrdfWorkshopPart.Vent);
                var flap=Tex(Color.FromRgb(3,3,3),UrdfWorkshopPart.Flap);
                double MedianRed(byte[] data)=>Enumerable.Range(0,data.Length/4).Select(i=>(double)data[i*4+2]).Order().ElementAt(data.Length/8);
                int Chips(byte[] data)=>Enumerable.Range(0,data.Length/4).Count(i=>data[i*4+2]>90);
                Check(MedianRed(black)<50&&MedianRed(blue)>43&&MedianRed(blue)<51&&Math.Abs(MedianRed(blue)-MedianRed(blueFin))<2,"Workshop housing and fins share the midpoint blue while retaining distinct edge wear");
                Check(Chips(flap)>Chips(black),"Flap rims have more paint wear than ordinary black panels");
                scene.ApplyWeathering(0);
                Check(visuals.All(p=>ReferenceEquals(p.Value.Material,original[p.Key])),"Clean restores exact original materials");
            }
            catch(Exception error){failure=error;}
            finally{window?.Close();app.Shutdown();}
        });
        app.Run();if(failure!=null)throw failure;
    }
}
