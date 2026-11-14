using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ServoAnimator;

internal static partial class Program
{
    private static void BodyWeatheringChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);Exception failure=null;
        app.Dispatcher.BeginInvoke(async ()=>
        {
            try
            {
                var sample=UrdfWeathering.Map(PrimitiveMeshes.Box(.1,.1,.03));
                byte[] Pixels(BitmapSource image){var data=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(data,image.PixelWidth*4,0);return data;}
                var light=Pixels(UrdfWeathering.WorkshopTexture(Color.FromRgb(5,8,65),sample,UrdfWorkshopPart.BodyPaint,1));
                var workshop=Pixels(UrdfWeathering.WorkshopTexture(Color.FromRgb(5,8,65),sample,UrdfWorkshopPart.BodyPaint,2));
                int Silver(byte[] p)=>Enumerable.Range(0,p.Length/4).Count(i=>p[i*4+2]>45&&Math.Abs(p[i*4]-p[i*4+2])<35);
                Check(Silver(workshop)>Silver(light),"Workshop exposes more silver than Light Wear on painted contact edges");
                var color=Color.FromRgb(160,160,160);
                var metal1=Pixels(UrdfWeathering.WorkshopTexture(color,sample,UrdfWorkshopPart.BodyMetal,1));
                var metal2=Pixels(UrdfWeathering.WorkshopTexture(color,sample,UrdfWorkshopPart.BodyMetal,2));
                double Average(byte[] p)=>Enumerable.Range(0,p.Length/4).Average(i=>(double)p[i*4+2]);
                Check(Average(metal2)<Average(metal1)&&Average(metal1)<160,"Metal patina darkens gradually rather than whitening the whole surface");
                var scene=UrdfScene.Load(Path.GetFullPath("Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf"));
                var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);
                var clean=visuals.ToDictionary(p=>p.Key,p=>(p.Value.Material,p.Value.Geometry));
                string[] sections={"body_chest_shell_visual","cad_shoulder_left_round_sleeve","cad_shoulder_right_bump_panel","body_waist_blue_panel_r_visual","body_finger_base_pad_left_o_dome_visual","cad_detail_body_finger_prox_left_0_link_cheek_-1","body_tread_plates_left_visual"};
                Color Diffuse(GeometryModel3D visual)=>((SolidColorBrush)((MaterialGroup)visual.Material).Children.OfType<DiffuseMaterial>().Single().Brush).Color;
                int pulses=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};timer.Tick+=(_,_)=>pulses++;timer.Start();
                for(int level=1;level<=2;level++)
                {
                    var watch=Stopwatch.StartNew();await scene.RequestWeathering(level);Console.WriteLine($"Body level {level}: {watch.ElapsedMilliseconds} ms background preparation, {pulses} UI heartbeats");
                    foreach(string name in sections)
                    {
                        var visual=visuals[name];
                        Check(((MaterialGroup)visual.Material).Children.OfType<DiffuseMaterial>().Any(d=>d.Brush is ImageBrush),"Body contact finish includes "+name+" at level "+level);
                        var geometry=(MeshGeometry3D)visual.Geometry;var original=(MeshGeometry3D)clean[name].Geometry;
                        Check(geometry.TriangleIndices.Select(i=>geometry.Positions[i]).SequenceEqual(original.TriangleIndices.Select(i=>original.Positions[i])),"Body wear preserves exact geometry: "+name);
                    }
                    var diskMaterial=(MaterialGroup)visuals["body_finger_base_pad_left_o_dome_visual"].Material;
                    var diskTexture=(BitmapSource)((ImageBrush)diskMaterial.Children.OfType<DiffuseMaterial>().Single().Brush).ImageSource;
                    int diskSilver=Silver(Pixels(diskTexture));Console.WriteLine($"Body level {level}: finger disk exposed-metal texels {diskSilver}");
                    if(level==2)Check(diskSilver>10,"Workshop finger disk rims have actual exposed-metal chips, not merely a blue tint change");
                    foreach(var part in visuals.Where(p=>p.Key.Contains("bellows")||p.Key.Contains("tread_pads")||p.Key.Contains("body_side_hose")))
                        Check(Math.Max(Diffuse(part.Value).R,Math.Max(Diffuse(part.Value).G,Diffuse(part.Value).B))<20,"Rubber remains black: "+part.Key);
                    foreach(string name in new[]{"body_chest_a_red_visual","body_chest_a_cu_0_visual"})
                    {
                        var original=(MaterialGroup)clean[name].Material;
                        Check(Diffuse(visuals[name])==((SolidColorBrush)original.Children.OfType<DiffuseMaterial>().Single().Brush).Color,"Protected electronics retain their colors: "+name);
                    }
                    Check(visuals.Where(p=>p.Key.StartsWith("lip_voice_led_")||p.Key.Contains("iris")||p.Key.Contains("pupil")).All(p=>ReferenceEquals(p.Value.Material,clean[p.Key].Material)),"Optics and mouth LEDs keep their original animated materials");
                }
                timer.Stop();Check(pulses>10,"UI stays responsive during body preparation");
                var cached=visuals[sections[0]].Material;
                await scene.RequestWeathering(1);await scene.RequestWeathering(2);
                Check(ReferenceEquals(cached,visuals[sections[0]].Material),"Returning to Workshop reuses its cached body material");
                await scene.RequestWeathering(0);
                Check(visuals.All(p=>ReferenceEquals(p.Value.Material,clean[p.Key].Material)),"Clean restores every original material after body weathering");
            }
            catch(Exception error){failure=error;}
            finally{app.Shutdown();}
        });
        app.Run();if(failure!=null)throw failure;
    }
}
