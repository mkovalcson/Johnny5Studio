using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ServoAnimator;
using H = HelixToolkit.Wpf.SharpDX;

internal static partial class Program
{
    private static void WeatheringChecks(bool materialsOnly = false)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        RendererSettingsChecks();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string output = PreviewDirectory("weathering-previews"); Directory.CreateDirectory(output);
        var plate = new MeshGeometry3D
        {
            Positions = new Point3DCollection(new[] { new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,1,0), new Point3D(0,0,0), new Point3D(1,1,0), new Point3D(0,1,0) }),
            Normals = new Vector3DCollection(Enumerable.Repeat(new Vector3D(0,0,1), 6)),
            TriangleIndices = new Int32Collection(Enumerable.Range(0,6))
        };
        var mappedPlate = UrdfWeathering.Map(plate);
        var masks = (System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, float[]>)typeof(UrdfWeathering).GetField("EdgeMaps", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check(masks.TryGetValue(mappedPlate, out var mask), "Actual mesh edges have a cached wear mask");
        Check(mask[(128+64)*384+128+64] == 0, "Coplanar STL triangle diagonal does not become a chipped edge");
        Check(mask[(128+64)*384+128+2] > .5, "Open plate boundary becomes a chipped edge");
        var closedBox=UrdfWeathering.Map(PrimitiveMeshes.Box(.1,.1,.1));
        Check(masks.TryGetValue(closedBox,out var boxMask)&&boxMask.Count(v=>v>.5)>100,"Convex edges on closed built-in shoulder/finger shapes receive wear");
        var recessed=plate.Clone();
        foreach(var p in new[]{new Point3D(.3,.3,-.02),new Point3D(.7,.3,-.02),new Point3D(.5,.7,-.02)}) { recessed.Positions.Add(p); recessed.Normals.Add(new Vector3D(0,0,1)); recessed.TriangleIndices.Add(recessed.Positions.Count-1); }
        var recessedMapped=UrdfWeathering.Map(recessed); masks.TryGetValue(recessedMapped,out var recessedMask);
        Check(recessedMapped.TextureCoordinates.Take(6).Max(p=>p.X)<recessedMapped.TextureCoordinates.Skip(6).Min(p=>p.X),"Disconnected rear/internal CAD parts use separate wear charts and cannot stamp lines onto the outer skin");
        byte[] ReadTexture(BitmapSource bitmap) { var bytes = new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0); return bytes; }
        int Silver(byte[] bytes, bool nearEdge) => Enumerable.Range(0,mask.Length).Count(i => (mask[i] > .5) == nearEdge && bytes[i*4+2] > 90 && Math.Abs(bytes[i*4]-bytes[i*4+2]) < 45);
        int Rust(byte[] bytes) => Enumerable.Range(0,mask.Length).Count(i => bytes[i*4+2] > 70 && bytes[i*4+2] > bytes[i*4+1]*1.5 && bytes[i*4+1] > bytes[i*4]*1.4);
        foreach (var paint in new[] { Color.FromRgb(8,11,78), Color.FromRgb(2,2,2) })
        {
            double previousSilver = 0;
            for (int level=1;level<=3;level++)
            {
                var image = UrdfWeathering.Texture(paint,false,level,mappedPlate);
                var bytes = ReadTexture(image); int silver=Silver(bytes,true);
                double edgeSilver=Enumerable.Range(0,mask.Length).Where(i=>mask[i]>.5).Average(i=>(double)bytes[i*4+2]);
                if (level==2) Check(edgeSilver > previousSilver, "Black and blue paint reveal more silver along real edges as wear increases");
                if (level>=2) Check(silver > 100, "Painted edges reveal silver rather than merely getting dusty");
                Check(level==3 ? Rust(bytes)>20 : Rust(bytes)==0, "Localized rust is exclusive to Outdoor aged");
                Check((double)silver/mask.Count(v=>v>.5) > (double)Silver(bytes,false)/mask.Count(v=>v<=.5)*5,
                    "Silver wear density is concentrated at edges rather than covering the whole surface");
                previousSilver=edgeSilver;
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream=File.Create(Path.Combine(output,$"finish-{paint.R}-{paint.B}-level-{level}.png")); encoder.Save(stream);
            }
        }
        var rubberBytes=ReadTexture(UrdfWeathering.Texture(Color.FromRgb(2,2,2),true,3,mappedPlate));
        Check(rubberBytes.Where((_,i)=>i%4!=3).Max()<10,"Rubber stays dark without silver paint chips, rust or metal scratches");
        var stainless=ReadTexture(UrdfWeathering.Texture(Color.FromRgb(210,210,210),false,3,mappedPlate,true));
        Check(Rust(stainless)==0,"Stainless fasteners do not receive outdoor rust");
        var boltGeometry=plate.Clone(); for(int i=0;i<boltGeometry.Positions.Count;i++)boltGeometry.Positions[i]=new Point3D(boltGeometry.Positions[i].X*.1,boltGeometry.Positions[i].Y*.1,0);
        var boltPlate=UrdfWeathering.Map(boltGeometry);
        UrdfWeathering.AddBoltStreaks(boltPlate,new[]{new Point3D(.05,.07,.002)},new Vector3D(0,-1,0));
        Check(Rust(ReadTexture(UrdfWeathering.Texture(Color.FromRgb(233,233,235),false,3,boltPlate)))>5,"Outdoor bare metal has rust streaks originating at actual bolt sites");
        var boltMasks=(System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D,float[]>)typeof(UrdfWeathering).GetField("BoltMaps",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        boltMasks.TryGetValue(boltPlate,out var boltStreak);
        Check(boltStreak[(128+49)*384+128+64]>0 && boltStreak[(128+28)*384+128+64]==0,"Bolt corrosion streaks extend downward from the hole rather than upward");
        Check(Rust(ReadTexture(UrdfWeathering.Texture(Color.FromRgb(233,233,235),false,2,boltPlate)))==0,"Bolt rust streaks stay exclusive to Outdoor aged");
        if (materialsOnly) return;
        foreach (string model in new[] { "Johnny5Head", "Johnny5FullBody" })
        {
            var scene = UrdfScene.Load(Path.GetFullPath("Config/Models/Robots/" + model + "/johnny5_head.urdf"));
            scene.SetMaterialColor("left_pupil_dynamic", Colors.Black); scene.SetMaterialColor("right_pupil_dynamic", Colors.Black);
            var visuals = (Dictionary<string, GeometryModel3D>)typeof(UrdfScene).GetField("_visuals", flags).GetValue(scene);
            var clean = visuals.ToDictionary(p => p.Key, p => (Geometry: p.Value.Geometry, Front: p.Value.Material, Back: p.Value.BackMaterial));
            var links = (Dictionary<string, Model3DGroup>)typeof(UrdfScene).GetField("_links", flags).GetValue(scene);
            Check(scene.WeatheringLevel == 0 && scene.WeatheringSurfaceCount > 30, "Both models default clean with eligible robot surfaces");
            scene.ApplyWeathering(0);
            Check(visuals.All(v => ReferenceEquals(v.Value.Geometry, clean[v.Key].Geometry) && ReferenceEquals(v.Value.Material, clean[v.Key].Front)), "Clean avoids geometry/material conversion");
            Render(links["neck_yaw_link"], model, 0);
            Dictionary<string, Geometry3D> mapped = null;
            for (int level = 1; level <= 3; level++)
            {
                var watch = Stopwatch.StartNew(); scene.ApplyWeathering(level); watch.Stop();
                Console.WriteLine($"{model} weathering {level}: {watch.Elapsed.TotalMilliseconds:F0} ms to select; no per-frame texture generation.");
                Check(scene.WeatheringLevel == level, "Selected weathering level reaches the model");
                int textured = 0;
                foreach (var (name, visual) in visuals)
                {
                    if (visual.Material is MaterialGroup material && material.Children.OfType<DiffuseMaterial>().FirstOrDefault()?.Brush is ImageBrush brush
                        && brush.ImageSource is BitmapSource image && image.Format == PixelFormats.Bgr32)
                    {
                        textured++;
                        var mesh = (MeshGeometry3D)visual.Geometry; var original = (MeshGeometry3D)clean[name].Geometry;
                        Check(mesh.TriangleIndices.Select(i=>mesh.Positions[i]).SequenceEqual(original.TriangleIndices.Select(i=>original.Positions[i])), "Weathering retains the exact triangle shape for imported and built-in geometry: " + name);
                        Check(mesh.TextureCoordinates.Count == mesh.Positions.Count && mesh.TextureCoordinates.All(p => p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1), "Each textured vertex has a valid UV");
                        if (mapped != null) Check(ReferenceEquals(mapped[name], visual.Geometry), "Changing distress reuses mapped geometry");
                    }
                    if (name.Contains("iris") || name.Contains("pupil") || name.StartsWith("lip_voice_led") || name.Contains("neopixel"))
                        Check(ReferenceEquals(visual.Geometry, clean[name].Geometry) && ReferenceEquals(visual.Material, clean[name].Front), "Optics and animated lighting stay untouched: " + name);
                }
                Check(textured > 30, "Weathering creates real surface textures in each robot model");
                if(model=="Johnny5FullBody")
                {
                    foreach(var section in new[]{"finger","shoulder"})
                        Check(visuals.Any(p=>p.Key.Contains(section,StringComparison.OrdinalIgnoreCase)&&p.Value.Material is MaterialGroup m&&m.Children.OfType<DiffuseMaterial>().Any(d=>d.Brush is ImageBrush)),"Body wear includes "+section+" geometry");
                }
                mapped ??= visuals.ToDictionary(p => p.Key, p => p.Value.Geometry);
                var texturedPart = visuals["static_02"];
                var previous = texturedPart.Material; scene.ApplyWeathering(level);
                Check(ReferenceEquals(previous, texturedPart.Material), "Reapplying the same finish is a no-op");
                using var mirror = new Dx11SceneMirror(); mirror.Synchronize(scene.RootModel);
                var nodes = Nodes(mirror.Root).OfType<H.MeshGeometryModel3D>().ToArray();
                Check(nodes.Any(n => n.Material is H.PhongMaterial m && m.DiffuseMap != null && !n.IsTransparent), "Opaque distressed surfaces retain DX11's opaque rendering path");
                Check(nodes.Any(n => n.IsTransparent), "Lens transparency is preserved separately");
                long updates = mirror.GeometryUpdates; scene.SetJoint("NeckTurn", .15); mirror.Synchronize(scene.RootModel);
                Check(mirror.GeometryUpdates == updates, "Animated joints do not regenerate weathering geometry"); scene.SetJoint("NeckTurn", 0);
                // Highlight clearing restores the current finish, not the clean material.
                typeof(UrdfScene).GetMethod("ApplyCollisionHighlights", flags).Invoke(scene, new object[] { new HashSet<GeometryModel3D> { texturedPart } });
                scene.ApplyWeathering(level == 3 ? 2 : 3); scene.ClearCollisionState();
                Check(texturedPart.Material is MaterialGroup restored && restored.Children.OfType<DiffuseMaterial>().First().Brush is ImageBrush, "Collision highlights restore the active weathered finish");
                scene.ApplyWeathering(level);
                Render(links["neck_yaw_link"], model, level);
            }
            if (model == "Johnny5FullBody")
            {
                scene.SetTreadTravel(.137, .084);
                var treadMeshes = visuals.Where(p => p.Key.Contains("tread", StringComparison.OrdinalIgnoreCase) && p.Value.Geometry is MeshGeometry3D m && m.TextureCoordinates.Count > 0).ToArray();
                Check(treadMeshes.Length > 0, "Moving treads retain their weathering UVs");
                scene.ApplyWeathering(0); scene.SetTreadTravel(0, 0);
                scene.ApplyWeathering(3); scene.SetTreadTravel(.137, .084);
                Check(treadMeshes.All(p => ((MeshGeometry3D)p.Value.Geometry).TextureCoordinates.Count == ((MeshGeometry3D)p.Value.Geometry).Positions.Count), "Tread phase changes reuse prepared texture coordinates");
                scene.SetTreadTravel(0, 0);
            }
            scene.ApplyWeathering(0);
            scene.ApplyWeathering(3,true);
            Check(visuals.Values.Count(v=>ReferenceEquals(v.Material,UrdfWeathering.Gold))>30,"Gold Johnny covers the robot body including built-in shapes");
            foreach(var (name,visual) in visuals.Where(v=>v.Key.Contains("iris")||v.Key.Contains("pupil")||v.Key.Contains("fabco_piston")&&!v.Key.Contains("stud")))
                Check(ReferenceEquals(visual.Material,clean[name].Front),"Gold Johnny preserves eyes and piston shafts: "+name);
            scene.ApplyWeathering(0);
            foreach (var (name, visual) in visuals)
            {
                if (!name.Contains("tread", StringComparison.OrdinalIgnoreCase)) Check(ReferenceEquals(visual.Geometry, clean[name].Geometry), "Clean restores the original mesh reference: " + name);
                Check(ReferenceEquals(visual.Material, clean[name].Front) && ReferenceEquals(visual.BackMaterial, clean[name].Back), "Clean restores the exact original materials: " + name);
            }
        }
        var first = UrdfWeathering.Texture(Color.FromRgb(8, 11, 78), false, 2);
        var second = UrdfWeathering.Texture(Color.FromRgb(8, 11, 78), false, 2);
        byte[] Pixels(BitmapSource source) { var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes; }
        Check(Pixels(first).SequenceEqual(Pixels(second)), "Weathering patterns are deterministic across reloads");
        Check(!Pixels(first).SequenceEqual(Pixels(UrdfWeathering.Texture(Color.FromRgb(8, 11, 78), false, 3))), "Higher levels contain different, stronger distress detail");
        foreach (var color in new[] { Color.FromRgb(8, 11, 78), Color.FromRgb(233, 233, 235), Color.FromRgb(20, 20, 20), Color.FromRgb(220, 172, 45) })
        {
            double previous = double.MaxValue;
            for (int level = 1; level <= 3; level++)
            {
                var finish = UrdfWeathering.Material(color, Colors.Black, 32, false, level);
                var part = new GeometryModel3D(UrdfWeathering.Map(PrimitiveMeshes.Box(1, 1, 1)), finish);
                var group = new Model3DGroup(); group.Children.Add(part);
                using var mirror = new Dx11SceneMirror(); mirror.Synchronize(group);
                var material = (H.PhongMaterial)Nodes(mirror.Root).OfType<H.MeshGeometryModel3D>().First(n => n.Material != null).Material;
                var texture = (BitmapSource)((ImageBrush)((MaterialGroup)finish).Children.OfType<DiffuseMaterial>().First().Brush).ImageSource;
                Check(UrdfWeathering.TryTextureLighting(texture, out var ambient), "Generated finish retains explicit linear lighting metadata");
                Check(Math.Abs(material.AmbientColor.Red - ambient.ScR) < .00001 && Math.Abs(material.AmbientColor.Blue - ambient.ScB) < .00001,
                    "DX11 uses surface reflectance rather than white for weathered ambient lighting");
                double lightness = ambient.ScR * .2126 + ambient.ScG * .7152 + ambient.ScB * .0722;
                if (color.R > 100) Check(lightness < previous, "Increasing wear darkens bright body finishes rather than washing them out");
                if (color.B > color.R * 2) Check(material.AmbientColor.Blue > material.AmbientColor.Red * 3, "Weathered navy paint retains its blue color");
                previous = lightness;
            }
        }

        static IEnumerable<H.Element3D> Nodes(H.Element3D node) { yield return node; if (node is H.GroupModel3D group) foreach (var child in group.Children) foreach (var result in Nodes(child)) yield return result; }
        void Render(Model3D model, string name, int level)
        {
            var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(90, 90, 90)));
            group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -.4, -2))); group.Children.Add(model.Clone());
            var bounds = model.Bounds; var target = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            var offset = new Vector3D(.62, .43, .22); offset.Normalize(); offset *= Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) * 2.7;
            var camera = new PerspectiveCamera(target + offset, -offset, new Vector3D(0, 0, 1), 32) { NearPlaneDistance = .0001, FarPlaneDistance = 100 };
            var viewport = new Viewport3D { Camera = camera }; viewport.Children.Add(new ModelVisual3D { Content = group });
            var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(37, 42, 50)) }; host.Children.Add(viewport);
            host.Measure(new Size(800, 800)); host.Arrange(new Rect(0, 0, 800, 800)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(800, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + "-level-" + level + ".png")); encoder.Save(stream);
        }
    }
}
