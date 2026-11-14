using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using ServoAnimator;

internal static partial class Program
{
    private static void RemoveFlapScrews(XDocument document)
    {
        document.Descendants("visual").Where(v=>v.Attribute("name")?.Value.StartsWith("flap_black_flat_screw_")==true).Remove();
        document.Root.Elements("material").Where(m=>m.Attribute("name")?.Value=="flap_flat_head_black").Remove();
    }
    private static void FlapScrewChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        string archive=ChangeArchivePath("flap-flat-screws-20261103");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(archive,"placement-manifest.json")));
        var record=manifest.RootElement;
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Check(Hash(record.GetProperty("source").GetProperty("source").GetString())==record.GetProperty("source").GetProperty("sourceSha256").GetString(),"Original vent-fin screw geometry remains untouched");
        string output=PreviewDirectory("head-fastener-previews");Directory.CreateDirectory(output);
        foreach(string model in new[]{"Johnny5Head","Johnny5FullBody"})
        {
            string root=Path.GetFullPath("Config/Models/Robots/"+model),path=Path.Combine(root,"johnny5_head.urdf");
            var xml=XDocument.Load(path);var stripped=new XDocument(xml);RemoveFlapScrews(stripped);
            Check(XNode.DeepEquals(stripped,XDocument.Load(Path.Combine(archive,model+"-before.urdf"))),"All earlier geometry, screws, collisions, joints, optics and materials are preserved: "+model);
            Check(Hash(Path.Combine(root,record.GetProperty("mesh").GetString()))==record.GetProperty("meshSha256").GetString(),"Both robots share the exact copied vent screw shape");
            var scene=UrdfScene.Load(path,optimizeExterior:false);
            var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",flags).GetValue(scene);
            var rows=record.GetProperty("models").GetProperty(model).EnumerateArray().ToArray();
            Check(rows.Length==12&&xml.Descendants("visual").Count(v=>v.Attribute("name")?.Value.StartsWith("flap_black_flat_screw_")==true)==12,"Exactly twelve black screws are installed: "+model);
            foreach(var group in rows.GroupBy(r=>r.GetProperty("hostVisual").GetString()))
            {
                Check(group.Count()==(group.Key.StartsWith("upper")?4:2),"Correct hole count for "+group.Key);
                var part=new Model3DGroup();part.Children.Add(visuals[group.Key].Clone());
                foreach(var row in group)
                {
                    string name=row.GetProperty("name").GetString();var bolt=visuals[name];
                    var element=xml.Descendants("visual").Single(v=>v.Attribute("name")?.Value==name);
                    Check(element.Parent.Attribute("name")?.Value==row.GetProperty("link").GetString(),"Screw is attached to its moving flap link");
                    double[] Values(string key)=>row.GetProperty(key).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                    var p=Values("seatLinkMetres");var n=Values("outwardLink");var seat=new Point3D(p[0],p[1],p[2]);var normal=new Vector3D(n[0],n[1],n[2]);
                    var points=((MeshGeometry3D)bolt.Geometry).Positions.Select(v=>bolt.Transform.Transform(v)-seat).ToArray();
                    Check(Math.Abs(points.Max(v=>Vector3D.DotProduct(v,normal)))<.0000001,"Flat head sits flush with the measured outer flap surface");
                    Check(Math.Abs(points.Min(v=>Vector3D.DotProduct(v,normal))+.003175)<.000002,"Original 3.175 mm shank points into the flap");
                    double radius=points.Max(v=>(v-normal*Vector3D.DotProduct(v,normal)).Length);
                    Check(radius<row.GetProperty("holeRadiusMm").GetDouble()*.001&&Math.Abs(radius-.0012573)<.000003,"Copied head size matches the tiny vent screw and clears the flap recess");
                    Check(row.GetProperty("fitErrorMm").GetDouble()<.0001,"Seat comes from a fitted circular hole in the actual flap mesh");
                    var material=(MaterialGroup)bolt.Material;var color=((SolidColorBrush)material.Children.OfType<DiffuseMaterial>().Single().Brush).Color;
                    Check(color.R<10&&color.G<10&&color.B<10,"New flap screw is black");
                    part.Children.Add(bolt.Clone());
                }
                var first=group.First();var center=first.GetProperty("seatLinkMetres").EnumerateArray().Select(v=>v.GetDouble()).ToArray();var outward=first.GetProperty("outwardLink").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                Render(part,new Point3D(center[0],center[1],center[2]),new Vector3D(outward[0],outward[1],outward[2]),.009,model+"-"+group.Key+"-screw.png");
            }
            scene.SetJoint("BrowLeftTopOpen",.3);scene.SetJoint("BrowRightTopOpen",.3);scene.SetJoint("BrowLeftBottomOpen",.3);scene.SetJoint("BrowRightBottomOpen",.3);
            foreach(var row in rows)
                Check(scene.TryTransformLinkPoint(row.GetProperty("link").GetString(),new Point3D(),out _),"Screw remains on a valid articulated flap after opening");
        }
        void Render(Model3D root,Point3D target,Vector3D normal,double width,string name)
        {
            var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(110,110,110)));lights.Children.Add(new DirectionalLight(Colors.White,-normal));lights.Children.Add(new DirectionalLight(Colors.Gray,new Vector3D(-1,-1,-1)));lights.Children.Add(root);
            var offset=normal*.03+new Vector3D(.006,.004,.001);var up=Math.Abs(normal.Z)>.95?new Vector3D(0,1,0):new Vector3D(0,0,1);
            var view=new Viewport3D{Camera=new OrthographicCamera(target+offset,-offset,up,width){NearPlaneDistance=.0001,FarPlaneDistance=10}};view.Children.Add(new ModelVisual3D{Content=lights});
            var host=new Grid{Background=Brushes.DimGray};host.Children.Add(view);host.Measure(new Size(800,600));host.Arrange(new Rect(0,0,800,600));host.UpdateLayout();
            var image=new RenderTargetBitmap(800,600,96,96,PixelFormats.Pbgra32);image.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name));png.Save(file);
        }
    }
}
