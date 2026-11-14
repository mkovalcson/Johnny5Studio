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
    private static void HeadFastenerChecks(bool goldOnly = false)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string archive = ChangeArchivePath("head-fasteners-20261103");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "fastener-placement-manifest.json")));
        var record = manifest.RootElement;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Check(Hash(record.GetProperty("source").GetString()) == record.GetProperty("sourceSha256").GetString(), "The supplied STEP file is unchanged");
        string output = PreviewDirectory("head-fastener-previews"); Directory.CreateDirectory(output);
        foreach (string model in new[] { "Johnny5Head", "Johnny5FullBody" })
        {
            string root = Path.GetFullPath(Path.Combine("Config/Models/Robots", model));
            string path = Path.Combine(root, "johnny5_head.urdf");
            var xml = XDocument.Load(path);
            RemoveFlapScrews(xml);
            var stripped = new XDocument(xml);
            RemoveDomedBolts(stripped);
            stripped.Descendants("visual").Where(v => v.Attribute("name")?.Value.StartsWith("recessed_stainless_bolt_") == true).Remove();
            stripped.Root.Elements("material").Where(m => m.Attribute("name")?.Value == "head_recess_stainless_steel").Remove();
            var beforeFasteners=XDocument.Load(Path.Combine(archive,model+"-before.urdf"));
            RestoreEarlierIris(stripped,beforeFasteners);
            Check(XNode.DeepEquals(stripped,beforeFasteners), "All pre-existing model geometry, joint definitions, collision shapes and rear cover alignment remain unchanged apart from the separately approved iris/lens replacement");
            string meshPath = Path.Combine(root,"Meshes/Fasteners/stainless_socket_head.step.stl");
            Check(Hash(meshPath) == record.GetProperty("meshSha256").GetString(), "Both robot models use the tessellated supplied STEP bolt");
            var scene = UrdfScene.Load(path, optimizeExterior: false);
            var visuals = (Dictionary<string, GeometryModel3D>)typeof(UrdfScene).GetField("_visuals", flags).GetValue(scene);
            var entries = record.GetProperty("models").GetProperty(model).EnumerateArray().ToArray();
            Check(entries.Length == 90, "Exactly 90 verified bolts are installed per model");
            var goldEntries=entries.Where(e=>e.TryGetProperty("goldWhipPlate",out var gold)&&gold.GetBoolean()).ToArray();
            Check(goldEntries.Length==4&&goldEntries.All(e=>e.GetProperty("hostVisual").GetString()=="static_05"),"Four bolts occupy the gold whip-antenna plate");
            var goldNames=goldEntries.Select(e=>e.GetProperty("name").GetString()).ToHashSet();
            var withoutGold=new XDocument(xml);withoutGold.Descendants("visual").Where(v=>goldNames.Contains(v.Attribute("name")?.Value)).Remove();
            RemoveDomedBolts(withoutGold);
            Check(XNode.DeepEquals(withoutGold,XDocument.Load(Path.Combine(ChangeArchivePath("gold-whip-plate-fasteners-20261103"),model+"-before.urdf"))),"Gold-plate addition preserves all 86 earlier bolts, spherical lenses, collisions and joints");
            Check(entries.Count(e => e.TryGetProperty("detailPlate", out var detail) && detail.GetBoolean()) == 53,
                "All 53 smaller side and underside detail-plate recesses have fitted bolts");
            var beforeDetails = XDocument.Load(Path.Combine(ChangeArchivePath("detail-plate-fasteners-20261103"),model+"-before.urdf"));
            var withoutDetails = new XDocument(xml);
            RemoveDomedBolts(withoutDetails);
            var detailNames = entries.Where(e => e.TryGetProperty("detailPlate",out var detail) && detail.GetBoolean()).Select(e => e.GetProperty("name").GetString()).ToHashSet();
            withoutDetails.Descendants("visual").Where(v => detailNames.Contains(v.Attribute("name")?.Value)||goldNames.Contains(v.Attribute("name")?.Value)).Remove();
            RestoreEarlierIris(withoutDetails,beforeDetails);
            Check(XNode.DeepEquals(withoutDetails,beforeDetails), "The earlier 33 bolts and every other model setting survive the detail-plate addition");
            MeshGeometry3D firstMesh = null;
            foreach (var entry in entries)
            {
                string name = entry.GetProperty("name").GetString(), link = entry.GetProperty("link").GetString();
                var bolt = visuals[name]; var mesh = (MeshGeometry3D)bolt.Geometry;
                var element = xml.Descendants("visual").Single(v => v.Attribute("name")?.Value == name);
                Check(element.Parent.Attribute("name")?.Value == link, "Bolt belongs to the same articulated link as the recess");
                if (entry.GetProperty("radialScale").GetDouble() == 1 && entry.GetProperty("axialScale").GetDouble() == 1)
                {
                    if (firstMesh == null) firstMesh = mesh;
                    else Check(ReferenceEquals(firstMesh, mesh), "Same-size bolt instances share cached mesh geometry");
                }
                var rim = entry.GetProperty("rimLinkMetres").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var direction = entry.GetProperty("outwardLink").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var normal = new Vector3D(direction[0], direction[1], direction[2]);
                var target = new Point3D(rim[0], rim[1], rim[2]);
                double front = mesh.Positions.Max(p => Vector3D.DotProduct(bolt.Transform.Transform(p)-target, normal));
                Check(Math.Abs(front) < .000002, "Bolt head sits flush to its recess within 0.002 mm");
                double depth = entry.GetProperty("axialScale").GetDouble()*record.GetProperty("sourceHeadDepthMm").GetDouble();
                double radius = entry.GetProperty("radialScale").GetDouble()*record.GetProperty("sourceHeadRadiusMm").GetDouble();
                Check(depth <= entry.GetProperty("recessDepthMm").GetDouble()+.00001 && radius < entry.GetProperty("recessRadiusMm").GetDouble(), "Fitted head clears the counterbore radius and depth");
                double frontRadius = mesh.Positions.Select(p => bolt.Transform.Transform(p)-target)
                    .Where(p => Vector3D.DotProduct(p,normal) > -depth/3000)
                    .Max(p => (p-normal*Vector3D.DotProduct(p,normal)).Length)*1000;
                Check(Math.Abs(frontRadius-radius) < .01, "The wide socket head, rather than the narrower shank tip, faces the exterior");
            }
            var goldCorners=goldEntries.Select(e=>e.GetProperty("rimLinkMetres").EnumerateArray().Select(v=>v.GetDouble()).ToArray()).ToArray();
            var goldCenter=new Point3D(goldCorners.Average(c=>c[0]),goldCorners.Average(c=>c[1]),goldCorners.Average(c=>c[2]));
            var goldAxis=goldEntries[0].GetProperty("outwardLink").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            var goldView=new Model3DGroup();
            var xmlVisuals=xml.Descendants("link").Single(l=>l.Attribute("name")?.Value=="head_link").Elements("visual");
            foreach(var element in xmlVisuals.Where(v=>goldNames.Contains(v.Attribute("name")?.Value)||
                new[]{"Meshes/SimplifiedHead2/static_e7c863.stl","Meshes/SimplifiedHead2/static_080b4e.stl"}.Contains(v.Element("geometry")?.Element("mesh")?.Attribute("filename")?.Value)))
                goldView.Children.Add(visuals[element.Attribute("name").Value].Clone());
            Render(goldView,goldCenter,new Vector3D(goldAxis[0],goldAxis[1],goldAxis[2])*.055+new Vector3D(.008,0,.003),model+"-gold-whip-plate.png",35);
            if(goldOnly)continue;
            var first = entries.First(e => e.GetProperty("hostVisual").GetString() == "static_01");
            var point = first.GetProperty("rimLinkMetres").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var axis = first.GetProperty("outwardLink").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var rimPoint = new Point3D(point[0],point[1],point[2]);
            scene.TryTransformLinkPoint(first.GetProperty("link").GetString(),rimPoint,out var targetWorld);
            scene.TryTransformLinkPoint(first.GetProperty("link").GetString(),rimPoint+new Vector3D(axis[0],axis[1],axis[2]),out var normalWorld);
            var facing = normalWorld-targetWorld; facing.Normalize();
            Render(scene.RootModel, targetWorld, facing*.021, model+"-bolt-closeup.png", 35);
            var detailGroup = entries.Where(e => e.TryGetProperty("detailPlate",out var detail) && detail.GetBoolean()
                && new[] {581,583,585,587}.Contains(e.GetProperty("cadCylinderIndex").GetInt32())).ToArray();
            Check(detailGroup.Length == 4, "Side detail-plate preview includes all four corner bolts");
            var corners = detailGroup.Select(e => e.GetProperty("rimLinkMetres").EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
            var panelCenter = new Point3D(corners.Average(c => c[0]),corners.Average(c => c[1]),corners.Average(c => c[2]));
            var detailAxis = detailGroup[0].GetProperty("outwardLink").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            scene.TryTransformLinkPoint("head_link",panelCenter,out var panelTarget);
            scene.TryTransformLinkPoint("head_link",panelCenter+new Vector3D(detailAxis[0],detailAxis[1],detailAxis[2]),out var panelNormal);
            var panelFacing = panelNormal-panelTarget; panelFacing.Normalize();
            Render(scene.RootModel,panelTarget,panelFacing*.16,model+"-side-detail-plate.png",35);
            var bounds = scene.GetWorldVisualBoundsPoints(new[] { "head_link" });
            var box = Rect3D.Empty; foreach(var p in bounds) box.Union(p);
            var center = new Point3D(box.X+box.SizeX/2,box.Y+box.SizeY/2,box.Z+box.SizeZ/2);
            Render(scene.RootModel,center,new Vector3D(.55,.4,.25),model+"-head-front.png",38);
            Render(scene.RootModel,center,new Vector3D(-.4,.5,.3),model+"-head-rear.png",38);
        }

        static void RestoreEarlierIris(XDocument current,XDocument baseline)
        {
            current.Descendants("visual").Where(v=>v.Attribute("name")?.Value.EndsWith("_iris_clear_lens")==true).Remove();
            current.Root.Elements("material").Where(m=>m.Attribute("name")?.Value=="iris_clear_lens").Remove();
            foreach(string side in new[]{"left","right"})
            {
                string name=side+"_iris_disc";
                current.Descendants("visual").Single(v=>v.Attribute("name")?.Value==name).Element("geometry").ReplaceWith(
                    new XElement(baseline.Descendants("visual").Single(v=>v.Attribute("name")?.Value==name).Element("geometry")));
            }
        }
        static void RemoveDomedBolts(XDocument document) => document.Descendants("visual")
            .Where(v=>v.Attribute("name")?.Value.StartsWith("domed_stainless_bolt_")==true).Remove();
        void Render(Model3D root, Point3D target, Vector3D offset, string name, double fov)
        {
            var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(90,90,90)));
            group.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-1,-2))); group.Children.Add(root);
            var cameraUp = Math.Abs(offset.Z / offset.Length) > .95 ? new Vector3D(0,1,0) : new Vector3D(0,0,1);
            var view = new Viewport3D { Camera=new PerspectiveCamera(target+offset,-offset,cameraUp,fov) { NearPlaneDistance=.0001, FarPlaneDistance=100 } };
            view.Children.Add(new ModelVisual3D { Content=group });
            var host = new Grid { Background=Brushes.DimGray }; host.Children.Add(view);
            host.Measure(new Size(900,700)); host.Arrange(new Rect(0,0,900,700)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(900,700,96,96,PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output,name)); encoder.Save(stream);
            group.Children.Remove(root);
        }
    }
}
