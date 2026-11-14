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
    private static void ButtonHeadFastenerChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        string archive=ChangeArchivePath("button-head-fasteners-20261103");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(archive,"placement-manifest.json")));
        var record=manifest.RootElement;
        string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        Check(Hash(record.GetProperty("source").GetString())==record.GetProperty("sourceSha256").GetString(),"Supplied button-head STEP is preserved");
        string output=PreviewDirectory("head-fastener-previews");Directory.CreateDirectory(output);
        foreach(string model in new[]{"Johnny5Head","Johnny5FullBody"})
        {
            string root=Path.GetFullPath("Config/Models/Robots/"+model),file=Path.Combine(root,"johnny5_head.urdf");
            var xml=XDocument.Load(file);var before=XDocument.Load(Path.Combine(archive,model+"-before.urdf"));var stripped=new XDocument(xml);
            RemoveFlapScrews(stripped);
            stripped.Descendants("visual").Where(v=>v.Attribute("name")?.Value.StartsWith("domed_stainless_bolt_")==true).Remove();
            Check(XNode.DeepEquals(stripped,before),model+": every older bolt, lens, origin, joint, collision and other setting is unchanged");
            Check(xml.Descendants("visual").Count(v=>v.Attribute("name")?.Value.StartsWith("recessed_stainless_bolt_")==true)==90,"All 90 previous recessed bolts remain");
            string source=record.GetProperty("mesh").GetString();
            Check(Hash(Path.Combine(root,source))==record.GetProperty("meshSha256").GetString(),"Both models use the identical supplied button-head mesh");
            Check(PrimitiveMeshes.IsRoundedHeadSurface(source),"Domed heads receive curved shading");
            var scene=UrdfScene.Load(file,optimizeExterior:false);
            var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",flags).GetValue(scene);
            var rows=record.GetProperty("models").GetProperty(model).EnumerateArray().ToArray();
            Check(rows.Length==12&&rows.Count(r=>r.GetProperty("section").GetString()=="front")==8&&rows.Count(r=>r.GetProperty("section").GetString()=="rear")==4,"Eight front and four rear cover button heads installed");
            foreach(var row in rows)
            {
                string name=row.GetProperty("name").GetString(),hostName=row.GetProperty("hostVisual").GetString();
                var bolt=visuals[name];var geometry=(MeshGeometry3D)bolt.Geometry;var host=visuals[hostName];double scale=row.GetProperty("uniformScale").GetDouble();
                double[] Values(string field)=>row.GetProperty(field).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var c=Values("seatLinkMetres");var n=Values("outwardLink");var native=Values("centerCadMm");
                var seat=new Point3D(c[0],c[1],c[2]);var normal=new Vector3D(n[0],n[1],n[2]);
                Check((host.Transform.Transform(new Point3D(native[0]/1000,native[1]/1000,native[2]/1000))-seat).Length<1e-9,"Seat follows the host origin including the rear-cover vertical correction");
                Check(xml.Descendants("visual").Single(v=>v.Attribute("name")?.Value==name).Parent.Attribute("name")?.Value=="head_link","Bolts stay on the head's articulated link");
                Check(geometry.Normals.All(n=>double.IsFinite(n.X+n.Y+n.Z)&&Math.Abs(n.Length-1)<1e-6),"Rounded head normals are finite unit vectors");
                var installedScale=xml.Descendants("visual").Single(v=>v.Attribute("name")?.Value==name)
                    .Element("geometry").Element("mesh").Attribute("scale").Value.Split(' ').Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var raw=PrimitiveMeshes.Stl(Path.Combine(root,source),new Vector3D(installedScale[0],installedScale[1],installedScale[2]));
                Check(raw.Positions.SequenceEqual(geometry.Positions)&&raw.TriangleIndices.SequenceEqual(geometry.TriangleIndices),"Curved shading retains the supplied hex socket and dome geometry");
                Check(raw.Normals.Zip(geometry.Normals).Any(p=>(p.First-p.Second).Length>.001),"Dome shading smooths the curved surfaces");
                var points=geometry.Positions.Select(p=>bolt.Transform.Transform(p)-seat).ToArray();
                double top=points.Max(p=>Vector3D.DotProduct(p,normal));
                Check(Math.Abs(top-1.1684*scale/1000)<.000002,"Domed head projects outward by its correct scaled height");
                Check(Math.Abs(points.Min(p=>Vector3D.DotProduct(p,normal))+14.2875*scale/1000)<.000002,"Bolt shank faces inward");
                Check(1.0922*scale<row.GetProperty("holeRadiusMm").GetDouble(),"Scaled shank clears the measured through hole");
                var underHead=points.Where(p=>Math.Abs(Vector3D.DotProduct(p,normal))<1e-8).ToArray();
                Check(underHead.Length>20&&Math.Abs(underHead.Max(p=>p.Length)-2.0828*scale/1000)<.000002,"The wide underside seats on the panel rather than burying the dome");
                Check(row.GetProperty("fitErrorMm").GetDouble()<.003,"Hole circles fit the runtime mesh within 3 micrometres");
            }
            foreach(string section in new[]{"front","rear"})
            {
                var selected=rows.Where(r=>r.GetProperty("section").GetString()==section).ToArray();var group=new Model3DGroup();
                group.Children.Add(visuals[selected[0].GetProperty("hostVisual").GetString()].Clone());
                foreach(var row in selected)group.Children.Add(visuals[row.GetProperty("name").GetString()].Clone());
                var seats=selected.Select(r=>r.GetProperty("seatLinkMetres").EnumerateArray().Select(v=>v.GetDouble()).ToArray()).ToArray();
                var target=new Point3D(seats.Average(p=>p[0]),seats.Average(p=>p[1]),seats.Average(p=>p[2]));
                double sign=section=="front"?1:-1;
                Render(group,target,new Vector3D(sign*.35,.012,.012),section=="front"?.15:.30,model+"-button-head-"+section+".png");
                var corner=seats[0];Render(group,new Point3D(corner[0],corner[1],corner[2]),new Vector3D(sign*.04,.007,.005),.010,model+"-button-head-"+section+"-closeup.png");
            }
        }
        void Render(Model3D root,Point3D target,Vector3D offset,double width,string name)
        {
            var group=new Model3DGroup();group.Children.Add(new AmbientLight(Color.FromRgb(95,95,95)));group.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-1,-2)));group.Children.Add(new DirectionalLight(Color.FromRgb(145,145,145),new Vector3D(1,.2,-1)));group.Children.Add(root);
            var view=new Viewport3D{Camera=new OrthographicCamera(target+offset,-offset,new Vector3D(0,0,1),width){NearPlaneDistance=.0001,FarPlaneDistance=10}};
            view.Children.Add(new ModelVisual3D{Content=group});var host=new Grid{Background=Brushes.DimGray};host.Children.Add(view);host.Measure(new Size(1000,650));host.Arrange(new Rect(0,0,1000,650));host.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1000,650,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);group.Children.Remove(root);
        }
    }
}
