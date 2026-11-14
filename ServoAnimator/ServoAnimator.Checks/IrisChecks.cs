#nullable enable annotations
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using ServoAnimator;
using H = HelixToolkit.Wpf.SharpDX;

internal static partial class Program
{
    private static void IrisChecks(bool install, bool refreshMeshes = false, bool refreshLenses = false)
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        string backup=ChangeArchivePath("articulated-irises-20261103");
        string output=PreviewDirectory("iris-previews");Directory.CreateDirectory(output);
        Model3DGroup hardwareEye=null;
        foreach(string model in new[]{"Johnny5Head","Johnny5FullBody"})
        {
            string root=Path.GetFullPath(Path.Combine("Config/Models/Robots",model)),file=Path.Combine(root,"johnny5_head.urdf");
            if(install && !File.Exists(Path.Combine(backup,model,"johnny5_head.urdf")))
            {
                string saved=Path.Combine(backup,model,"johnny5_head.urdf");Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                if(File.Exists(saved)) throw new InvalidOperationException("Iris backup already exists. Use --iris to verify without reinstalling.");
                File.Copy(file,saved);
                var doc=XDocument.Load(file,LoadOptions.PreserveWhitespace);
                string meshes=Path.Combine(root,"Meshes/EyeMechanism");
                WriteStl(PrimitiveMeshes.IrisBlades(.02286,.01143,.003175),Path.Combine(meshes,"iris_twenty_blades.stl"));
                WriteStl(PrimitiveMeshes.IrisLens(.02286),Path.Combine(meshes,"iris_clear_lens.stl"));
                doc.Root!.AddFirst(new XElement("material",new XAttribute("name","iris_clear_lens"),new XElement("color",new XAttribute("rgba","0.78 0.90 1 0.035"))));
                foreach(string side in new[]{"left","right"})
                {
                    var link=doc.Descendants("link").Single(e=>(string?)e.Attribute("name")==side+"_pupil_link");
                    var iris=link.Elements("visual").Single(e=>(string?)e.Attribute("name")==side+"_iris_disc");
                    iris.Element("geometry")!.ReplaceNodes(new XElement("mesh",new XAttribute("filename","Meshes/EyeMechanism/iris_twenty_blades.stl")));
                    link.Add(new XElement("visual",new XAttribute("name",side+"_iris_clear_lens"),
                        new XElement("origin",new XAttribute("xyz","0.0035 0 0"),new XAttribute("rpy","0 1.5707963267948966 0")),
                        new XElement("geometry",new XElement("mesh",new XAttribute("filename","Meshes/EyeMechanism/iris_clear_lens.stl"))),
                        new XElement("material",new XAttribute("name","iris_clear_lens"))));
                }
                doc.Save(file,SaveOptions.DisableFormatting);
            }
            if(refreshMeshes)
            {
                string meshFile=Path.Combine(root,"Meshes/EyeMechanism/iris_twenty_blades.stl");
                string saved=Path.Combine(backup,model,"iris_twenty_blades-before-contrast.stl");
                if(!File.Exists(saved))File.Copy(meshFile,saved);
                WriteStl(PrimitiveMeshes.IrisBlades(.02286,.01143,.003175),meshFile);
            }
            if(refreshLenses)
            {
                string meshFile=Path.Combine(root,"Meshes/EyeMechanism/iris_clear_lens.stl");
                string saved=Path.Combine(backup,model,"iris_clear_lens-before-dome.stl");
                if(!File.Exists(saved))File.Copy(meshFile,saved);
                WriteStl(PrimitiveMeshes.IrisLens(.02286),meshFile);
            }
            var document=XDocument.Load(file);var original=XDocument.Load(Path.Combine(backup,model,"johnny5_head.urdf"));
            Check(document.Descendants("collision").Select(e=>e.ToString()).SequenceEqual(original.Descendants("collision").Select(e=>e.ToString())),model+": collisions unchanged");
            Check(document.Descendants("joint").Select(e=>e.ToString()).SequenceEqual(original.Descendants("joint").Select(e=>e.ToString())),model+": all joints and original iris controls unchanged");
            foreach(var old in original.Descendants("link").Elements("visual").Where(e=>!((string?)e.Attribute("name")??"").EndsWith("_iris_disc")))
                Check(document.Descendants("link").Single(e=>(string?)e.Attribute("name")== (string?)old.Parent!.Attribute("name")).Elements("visual").Any(e=>XNode.DeepEquals(old,e)),"Unrelated visual preserved: "+(string?)old.Attribute("name"));
            var scene=UrdfScene.Load(file,optimizeExterior:false);
            var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",flags)!.GetValue(scene)!;
            foreach(string side in new[]{"left","right"})
            {
                var lens=visuals[side+"_iris_clear_lens"];
                Check(document.Descendants("visual").Single(e=>(string?)e.Attribute("name")==side+"_iris_clear_lens").Parent!.Attribute("name")!.Value==side+"_pupil_link","Lens follows same eye hierarchy");
                var lensMesh=(MeshGeometry3D)lens.Geometry;
                Check(Math.Abs(lensMesh.Bounds.SizeX-.04572)<1e-7&&Math.Abs(lensMesh.Bounds.SizeY-.04572)<1e-7,"Lens covers exactly 1.80 inch iris diameter");
                Check(lensMesh.Normals.All(n=>Math.Abs(n.Length-1)<1e-7),"Glass has smooth unit normals");
                Check(Math.Abs(lensMesh.Bounds.SizeZ-.006)<1e-8,"Clear lens has a 6 mm dome with its outer rim fixed");
                Check(Math.Abs(lensMesh.Bounds.Z)<1e-9,"Stronger lens curvature retains the original rim plane");
                var diffuse=(DiffuseMaterial)((MaterialGroup)lens.Material).Children[0];
                var glassEdge=(ImageBrush)diffuse.Brush;
                var texture=(BitmapSource)glassEdge.ImageSource;
                var texturePixels=new byte[texture.PixelWidth*texture.PixelHeight*4];texture.CopyPixels(texturePixels,texture.PixelWidth*4,0);
                Check(texturePixels.Where((_,i)=>i%4==3).Max()==0,"No hidden studio lights are added to a newly loaded lens");
                Check(texturePixels[((texture.PixelHeight/2)*texture.PixelWidth+texture.PixelWidth/2)*4+3]<90,"Lens center remains transparent");
                Check(lensMesh.TextureCoordinates.Count==lensMesh.Positions.Count,"Glass edge texture maps correctly in both renderers");
                foreach(double radius in new[]{.02286,.01143,.00381})
                {
                    scene.SetVisualIris(side+"_iris_disc",.02286,radius,.003175);
                    var mesh=(MeshGeometry3D)visuals[side+"_iris_disc"].Geometry;
                    Check(mesh.Positions.All(p=>double.IsFinite(p.X+p.Y+p.Z)),"Finite animated blade positions");
                    Check(mesh.Normals.All(n=>double.IsFinite(n.X+n.Y+n.Z)&&Math.Abs(n.Length-1)<1e-6),"Finite blade normals");
                    if(radius==.02286)Check(mesh.Positions.Count==0,"Blades retract completely at fully open endpoint");
                    else
                    {
                        Check(Math.Abs(mesh.Positions.Min(p=>Math.Sqrt(p.X*p.X+p.Y*p.Y))-radius)<1e-9,"Original opening radius is preserved");
                        Check(Math.Abs(mesh.Positions.Max(p=>Math.Sqrt(p.X*p.X+p.Y*p.Y))-.02286)<1e-9,"Blade outer extent stays fixed");
                        Check(Components(mesh)==20,"Exactly twenty separate visible leaves");
                    }
                }
                scene.SetVisualIris(side+"_iris_disc",.02286,.01143,.003175);
                var eye=new Model3DGroup();
                foreach(string suffix in new[]{"iris_disc","iris_black_band","iris_gold_continuous","pupil_aperture","iris_clear_lens"})
                    eye.Children.Add(visuals[side+"_"+suffix].Clone());
                // Include the real silver backing, which can become exposed
                // when a bezel is recessed. It belongs to the parent eye link.
                var silver=visuals[side+"_eye_lens_cad1ee"].Clone();
                var silverTransform=silver.Transform.Value;
                silverTransform.Translate(new Vector3D(-.011436691,0,0));
                silver.Transform=new MatrixTransform3D(silverTransform);eye.Children.Add(silver);
                eye.Children.Add(new AmbientLight(Color.FromRgb(95,95,95)));
                eye.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,.4,-.5)));
                Render(eye,Path.Combine(output,model+"-"+side+"-neutral.png"));
                if(side=="left")Render(eye,Path.Combine(output,model+"-side-lens.png"),true);
                hardwareEye??=eye;
                using var mirror=new Dx11SceneMirror();mirror.Synchronize(eye);
                var dxMeshes=Flatten(mirror.Root).OfType<H.MeshGeometryModel3D>().Where(m=>m.IsRendering).ToList();
                Check(dxMeshes.Any(m=>m.IsTransparent&&m.Material is H.PhongMaterial p&&p.DiffuseMap!=null),"DX11 retains transparent lens edge texture");
                Check(dxMeshes.Any(m=>!m.IsTransparent),"DX11 retains opaque blades and rings");
            }
            var view=new RobotHeadView(deferModelLoad:true);
            view.InstallModel(file,scene,new UrdfFraming());
            var calibration=UrdfConfiguration.CreateDefault();
            // The robot embeds its own narrower physical extents. Exercise the
            // complete legacy 10/55/100 percent conversion explicitly, without
            // changing the user's saved or embedded calibration.
            foreach(var setting in calibration.Motions.Where(m=>m.Servo==ServoNames.IrisClose))
            {setting.MinExtent=10;setting.ZeroExtent=55;setting.MaxExtent=100;setting.ReverseOverride=false;}
            view.SetUrdfConfiguration(calibration);
            foreach(var (value,radius) in new[]{(-100,.02286),(0,.01143),(100,.00381)})
            {
                view.SetServo(ServoNames.IrisClose,value);
                foreach(string side in new[]{"left","right"})
                {
                    var mesh=(MeshGeometry3D)visuals[side+"_iris_disc"].Geometry;
                    Check(value==-100?mesh.Positions.Count==0:Math.Abs(mesh.Positions.Min(p=>Math.Sqrt(p.X*p.X+p.Y*p.Y))-radius)<1e-9,
                        "Existing ganged Iris Close control reaches the original opening: "+value);
                }
            }
            var rightBefore=visuals["right_iris_disc"].Geometry;
            view.SetChildServo(ServoNames.IrisClose,RobotControls.LeftIris,0);
            Check(ReferenceEquals(rightBefore,visuals["right_iris_disc"].Geometry),"Left iris child control leaves right eye unchanged");
            Check(Math.Abs(((MeshGeometry3D)visuals["left_iris_disc"].Geometry).Positions.Min(p=>Math.Sqrt(p.X*p.X+p.Y*p.Y))-.01143)<1e-9,"Left child control still opens its own iris");
            view.SetChildServo(ServoNames.IrisClose,RobotControls.RightIris,-100);
            Check(((MeshGeometry3D)visuals["right_iris_disc"].Geometry).Positions.Count==0,"Right iris child control still works independently");
            void UpdateViewLighting()=>typeof(RobotHeadView).GetMethod("UpdateLensReflections",flags)!.Invoke(view,new object[]{TimeSpan.Zero});
            var liveBrush=(ImageBrush)((DiffuseMaterial)((MaterialGroup)visuals["left_iris_clear_lens"].Material).Children[0]).Brush;
            UpdateViewLighting();var litImage=liveBrush.ImageSource;
            calibration.Rendering.Wpf.KeyLight=0;calibration.Rendering.Wpf.AmbientLight=0;
            view.SetUrdfConfiguration(calibration);UpdateViewLighting();
            var darkImage=(BitmapSource)liveBrush.ImageSource;var darkPixels=new byte[128*128*4];darkImage.CopyPixels(darkPixels,512,0);
            Check(!ReferenceEquals(litImage,darkImage)&&darkPixels.Where((_,i)=>i%4==3).All(a=>a==0),"The actual robot lighting profile also controls the lens reflections");
            calibration.Rendering.Wpf.KeyLight=1;calibration.Rendering.Wpf.AmbientLight=1;
            view.SetUrdfConfiguration(calibration);UpdateViewLighting();
            Check(!ReferenceEquals(liveBrush.ImageSource,darkImage),"Restoring robot lighting refreshes the lens without moving the camera");
            var geometryBefore=visuals["left_iris_clear_lens"].Geometry;
            calibration.Rendering.Wpf.LensReflectivity=0;
            view.SetUrdfConfiguration(calibration);
            foreach(string side in new[]{"left","right"})
            {
                var glass=(MaterialGroup)visuals[side+"_iris_clear_lens"].Material;
                var pixels=new byte[128*128*4];((BitmapSource)((ImageBrush)((DiffuseMaterial)glass.Children[0]).Brush).ImageSource).CopyPixels(pixels,512,0);
                Check(pixels.Where((_,i)=>i%4==3).All(a=>a==0),model+": live slider Off clears both lens reflection textures immediately");
                Check(((SolidColorBrush)((SpecularMaterial)glass.Children[1]).Brush).Color==Color.FromArgb(0,0,0,0),model+": Off also removes native specular highlights in both renderers");
            }
            Check(ReferenceEquals(geometryBefore,visuals["left_iris_clear_lens"].Geometry),"Reflectivity leaves spherical lens geometry unchanged");
            calibration.Rendering.Wpf.LensReflectivity=1;view.SetUrdfConfiguration(calibration);
        }
        var reflectionTest=new IrisLensReflection();
        var key=new DirectionalLight(Colors.White,new Vector3D(-1,.4,-.5));
        Light[] actualLights={key};
        reflectionTest.Update(new Vector3D(1,-.15,.15),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights);
        int initial=reflectionTest.Updates;
        var before=((BitmapSource)reflectionTest.Brush.ImageSource);
        Check(!reflectionTest.Update(new Vector3D(1,-.15,.15),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights)&&reflectionTest.Updates==initial,"Stationary glass does not regenerate or upload a texture");
        Check(reflectionTest.Update(new Vector3D(.4,-1,.2),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights),"Reflections react to a side view");
        var after=(BitmapSource)reflectionTest.Brush.ImageSource;
        var aBytes=new byte[128*128*4];var bBytes=new byte[aBytes.Length];before.CopyPixels(aBytes,512,0);after.CopyPixels(bBytes,512,0);
        Check(aBytes.Zip(bBytes).Count(p=>Math.Abs(p.First-p.Second)>10)>100,"Reflection visibly moves across the curved cover with the camera");
        var reflectionClock=System.Diagnostics.Stopwatch.StartNew();
        int updatesBefore=reflectionTest.Updates;
        for(int i=0;i<30;i++)reflectionTest.Update(new Vector3D(.4+i*.02,-1,.2),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights);
        Console.WriteLine($"Lens reflection texture: {reflectionClock.Elapsed.TotalMilliseconds/Math.Max(1,reflectionTest.Updates-updatesBefore):F2} ms per changed eye; stationary views are cached.");
        byte[] ReflectionPixels(){var data=new byte[128*128*4];((BitmapSource)reflectionTest.Brush.ImageSource).CopyPixels(data,512,0);return data;}
        void Relight()=>reflectionTest.Update(new Vector3D(1,-.15,.15),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights);
        Relight();var white=ReflectionPixels();
        void SetReflectivity(double strength)=>reflectionTest.Update(new Vector3D(1,-.15,.15),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights,reflectivity:strength);
        SetReflectivity(0);Check(ReflectionPixels().Where((_,i)=>i%4==3).All(a=>a==0),"Reflection slider minimum is fully transparent");
        SetReflectivity(2);var stronger=ReflectionPixels();
        Check(stronger.Where((_,i)=>i%4==3).Sum(a=>(long)a)>white.Where((_,i)=>i%4==3).Sum(a=>(long)a)*1.5,"Rightmost slider setting visibly increases reflections");
        int cached=reflectionTest.Updates;SetReflectivity(2);Check(reflectionTest.Updates==cached,"Unchanged reflection strength remains cached");
        SetReflectivity(1);Check(ReflectionPixels().SequenceEqual(white),"Slider center restores the exact original appearance");
        key.Color=Colors.Black;Relight();Check(ReflectionPixels().Where((_,i)=>i%4==3).All(a=>a==0),"Turning off the real light removes all reflections");
        key.Color=Colors.Red;Relight();var red=ReflectionPixels();
        Check(red.Where((_,i)=>i%4==1||i%4==0).All(a=>a==0)&&red.Where((_,i)=>i%4==2).Max()>200,"Reflected color follows the actual red light");
        key.Color=Colors.White;key.Direction=new Vector3D(-1,-.5,.3);Relight();
        Check(ReflectionPixels().Zip(white).Count(p=>Math.Abs(p.First-p.Second)>10)>100,"Moving a real light moves its reflection");
        var point=new PointLight(Colors.White,new Point3D(1,-.4,.5)){Range=4,ConstantAttenuation=1};actualLights=new Light[]{point};Relight();
        long nearEnergy=ReflectionPixels().Where((_,i)=>i%4==3).Sum(a=>(long)a);
        point.ConstantAttenuation=2;Relight();Check(ReflectionPixels().Where((_,i)=>i%4==3).Sum(a=>(long)a)<nearEnergy*.6,"Point-light attenuation dims its reflection");
        point.Position=new Point3D(10,0,0);Relight();Check(ReflectionPixels().Where((_,i)=>i%4==3).All(a=>a==0),"Point lights outside their actual range produce no reflection");
        actualLights=new Light[]{new AmbientLight(Color.FromRgb(95,95,95))};
        reflectionTest.Update(new Vector3D(.4,-1,.2),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),actualLights);
        var sheen=ReflectionPixels();
        int centerAlpha=sheen[(64*128+64)*4+3];
        Check(sheen.Where((_,i)=>i%4==3).Max()>centerAlpha+25,"Existing ambient light reveals the spherical dome at grazing angles");
        Check(centerAlpha<40,"Ambient sheen keeps the center of the lens clear");
        Exception hardwareFailure=null;
        app.Dispatcher.BeginInvoke(async()=>
        {
            using var dx=new Dx11Viewport();
            var host=new Grid{Width=650,Height=600,Background=new SolidColorBrush(Color.FromRgb(35,40,49))};host.Children.Add(dx.View);
            var window=new Window{Content=host,SizeToContent=SizeToContent.WidthAndHeight,Title="Lens reflection verification",ShowInTaskbar=false};
            try
            {
                window.Show();await Task.Delay(500);
                var cover=(GeometryModel3D)hardwareEye.Children[4];
                foreach(bool side in new[]{false,true})
                {
                    var position=side?new Point3D(.065,-.13,.025):new Point3D(.14,-.025,.025);
                    var studio=new IrisLensReflection();studio.Update(position-new Point3D(.0035,0,0),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),hardwareEye.Children.OfType<Light>(),new Point3D(.0035,0,0));
                    ((DiffuseMaterial)((MaterialGroup)cover.Material).Children[0]).Brush=studio.Brush;
                    var camera=new PerspectiveCamera(position,new Point3D()-position,new Vector3D(0,0,1),30){NearPlaneDistance=.001};
                    async Task Draw(){dx.Synchronize(hardwareEye,camera,650,600);for(int i=0;i<12;i++){dx.RenderFrame();await Task.Delay(25);}}
                    await Draw();
                    var pixels=WindowCapture.CaptureClient(window,out int width,out int height);
                    Check(dx.RenderedFrames>0&&pixels!=null,"Lens close-up renders on hardware DX11");
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgr32,null,pixels,width*4)));
                    using(var stream=File.Create(Path.Combine(output,side?"Dx11-side-lens.png":"Dx11-front-lens.png")))png.Save(stream);
                    hardwareEye.Children.Remove(cover);await Draw();var plain=WindowCapture.CaptureClient(window,out _,out _);hardwareEye.Children.Insert(4,cover);
                    int bright=0,clear=0;
                    for(int y=side?0:height*2/5;y<(side?height:height*3/5);y++)for(int x=side?0:width*2/5;x<(side?width:width*3/5);x++)
                    {int i=(y*width+x)*4;bool pupil=plain[i]<10&&plain[i+1]<10&&plain[i+2]<10;if((side||pupil)&&pixels[i+2]-plain[i+2]>(side?5:20))bright++;if(pupil&&pixels[i]-plain[i]<10)clear++;}
                    Check(bright>50,"DX11 actual-light reflection is visible at "+(side?"side":"front")+" angle");
                    Check(clear>50,"DX11 leaves part of the pupil clear");
                }
                Console.WriteLine("Lens reflection adapter: "+dx.AdapterName);
            }
            catch(Exception error){hardwareFailure=error;}
            finally{window.Close();app.Shutdown();}
        });
        app.Run();if(hardwareFailure!=null)throw hardwareFailure;
        static IEnumerable<H.Element3D> Flatten(H.Element3D element){yield return element;if(element is H.GroupModel3D group)foreach(var child in group.Children)foreach(var e in Flatten(child))yield return e;}
        static int Components(MeshGeometry3D mesh)
        {
            // Vertices are duplicated at quad edges. Quantized positions join
            // touching faces, while the twenty intentional hairline seams remain separate.
            var parent=new Dictionary<(long,long,long),(long,long,long)>();
            (long,long,long) Key(Point3D p)=>( (long)Math.Round(p.X*1e9),(long)Math.Round(p.Y*1e9),(long)Math.Round(p.Z*1e9));
            (long,long,long) Find((long,long,long) p){if(!parent.ContainsKey(p))parent[p]=p;if(parent[p]!=p)parent[p]=Find(parent[p]);return parent[p];}
            for(int i=0;i<mesh.TriangleIndices.Count;i+=3){var a=Key(mesh.Positions[mesh.TriangleIndices[i]]);for(int j=1;j<3;j++){var b=Key(mesh.Positions[mesh.TriangleIndices[i+j]]);parent[Find(b)]=Find(a);}}
            return parent.Keys.ToArray().Select(Find).Distinct().Count();
        }
        static void WriteStl(MeshGeometry3D mesh,string path)
        {
            using var writer=new BinaryWriter(File.Create(path));writer.Write(new byte[80]);writer.Write((uint)(mesh.TriangleIndices.Count/3));
            for(int i=0;i<mesh.TriangleIndices.Count;i+=3)
            {
                var a=mesh.Positions[mesh.TriangleIndices[i]];var b=mesh.Positions[mesh.TriangleIndices[i+1]];var c=mesh.Positions[mesh.TriangleIndices[i+2]];
                var n=Vector3D.CrossProduct(b-a,c-a);n.Normalize();
                foreach(double x in new[]{n.X,n.Y,n.Z,a.X,a.Y,a.Z,b.X,b.Y,b.Z,c.X,c.Y,c.Z})writer.Write((float)x);writer.Write((ushort)0);
            }
        }
        static void Render(Model3DGroup eye,string path,bool side=false)
        {
            var position=side?new Point3D(.065,-.13,.025):new Point3D(.14,-.025,.025);
            var direction=new Point3D()-position;
            var cover=(GeometryModel3D)eye.Children[4];
            // Same world-space axes as the actual URDF's +Y lens rotation.
            var studio=new IrisLensReflection();studio.Update(position-new Point3D(.0035,0,0),new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0),eye.Children.OfType<Light>(),new Point3D(.0035,0,0));
            ((DiffuseMaterial)((MaterialGroup)cover.Material).Children[0]).Brush=studio.Brush;
            var viewport=new Viewport3D{Width=650,Height=600,Camera=new PerspectiveCamera(position,direction,new Vector3D(0,0,1),30){NearPlaneDistance=.001}};
            viewport.Children.Add(new ModelVisual3D{Content=eye});
            var host=new Grid{Width=650,Height=600,Background=new SolidColorBrush(Color.FromRgb(35,40,49))};host.Children.Add(viewport);
            host.Measure(new Size(650,600));host.Arrange(new Rect(0,0,650,600));host.UpdateLayout();
            var bitmap=new RenderTargetBitmap(650,600,96,96,PixelFormats.Pbgra32);bitmap.Render(host);
            var pixels=new byte[650*600*4];bitmap.CopyPixels(pixels,650*4,0);
            eye.Children.Remove(cover);
            var without=new RenderTargetBitmap(650,600,96,96,PixelFormats.Pbgra32);without.Render(host);
            var plain=new byte[pixels.Length];without.CopyPixels(plain,650*4,0);eye.Children.Insert(4,cover);
            int bright=0,clear=0;
            for(int y=side?0:250;y<(side?600:350);y++)for(int x=side?0:275;x<(side?650:375);x++)
            {
                int i=(y*650+x)*4;
                bool pupil=plain[i]<10&&plain[i+1]<10&&plain[i+2]<10;
                if((side||pupil)&&pixels[i+2]-plain[i+2]>(side?5:20))bright++;
                if(pupil&&pixels[i]-plain[i]<10)clear++;
            }
            Check(bright>50,"Actual scene lights reflect visibly on the lens in the WPF render");
            Check(clear>50,"The pupil remains visible through the clear part of the cover");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
        }
    }
}
