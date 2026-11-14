using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Valve.VR;

namespace Johnny5VR;

internal static class Diagnostics
{
    internal static string Folder=>Path.Combine(ViewerSettings.Home,"Diagnostics");
    internal static void Log(Exception ex){Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"viewer.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine);}
    internal static void WriteScene(SceneData scene,ViewerSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder,"scene-report.json"),JsonSerializer.Serialize(new{
            Robot=settings.Robot,Backdrop=settings.Backdrop,scene.Visuals,scene.Triangles,scene.RobotHeightMetres,
            Bounds=new{Min=new[]{scene.Min.X,scene.Min.Y,scene.Min.Z},Max=new[]{scene.Max.X,scene.Max.Y,scene.Max.Z}},scene.Warnings,
            SteamVrInstalled=OpenVR.IsRuntimeInstalled(),HeadsetConnected=OpenVR.IsHmdPresent()
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void VerifyScreenshot(string path)
    {
        using var b=new Bitmap(path);var colors=new HashSet<int>();int changed=0;
        for(int y=0;y<b.Height;y+=4)for(int x=0;x<b.Width;x+=4)
        {
            var c=b.GetPixel(x,y);colors.Add(c.ToArgb());
            if(Math.Abs(c.R-19)+Math.Abs(c.G-26)+Math.Abs(c.B-36)>35)changed++;
        }
        if(colors.Count<100||changed<1000)throw new InvalidOperationException("The D3D11 preview is blank or lacks scene detail.");
    }
    internal static void Check(ViewerSettings settings)
    {
        Directory.CreateDirectory(Folder);int count=0;
        void Check(bool pass,string message){if(!pass)throw new Exception(message);count++;}
        SharedControlChecks.Run(Check);
        NavigationTests.Run(Check);
        ProjectionTests.Run(Check);
        BridgeChecks.Run(Check);
        PanelNavigationChecks.Run(Check);
        OverlayReliabilityChecks.Run(Check);
        RobotPoseChecks.Run(Check);
        BodyMotionChecks.Run(Check);
        ControllerFeedbackChecks.Run(Check);
        VrTaskMenuChecks.Run(Check);
        GloveChecks.Run(Check);
        SpecialEffectChecks.Run(settings,Check);
        SecondaryChecks.Run(settings,Check);
        CurvedCasterChecks.Run(settings,Check);
        TipToeChecks.Run(settings,Check);
        RgbChecks.Run(settings,Check);
        PlaybackChecks.Run(settings,Check);
        var translated=VrSession.Pose(new HmdMatrix34_t{m0=1,m5=1,m10=1,m3=2,m7=3,m11=4});
        Check(Vector3.Transform(Vector3.One,translated)==new Vector3(3,4,5),"OpenVR pose matrix must preserve translation.");
        var origin=UrdfReader.Origin(System.Xml.Linq.XElement.Parse("<origin xyz='1 2 3' rpy='0 0 1.57079632679'/>"));
        Check(Vector3.Distance(Vector3.Transform(Vector3.UnitX,origin),new(1,3,3))<1e-5f,"URDF origin order must rotate, then translate.");
        Check(Shapes.Box(Vector3.One).Length==12,"Box triangulation.");
        Check(Shapes.Cylinder(1,2).All(t=>Math.Abs(t.NA.Length()-1)<1e-5),"Cylinder surface normals.");
        var fixture=Path.Combine(Folder,"Fixtures");Directory.CreateDirectory(fixture);
        string obj=Path.Combine(fixture,"quad.obj");File.WriteAllText(obj,"v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf -4 -3 -2 -1\n");
        Check(MeshFile.Read(obj).Length==2,"OBJ negative-index quad triangulation.");
        string stl=Path.Combine(fixture,"triangle.stl");File.WriteAllText(stl,"solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid\n");
        Check(MeshFile.Read(stl).Length==1,"ASCII STL import.");
        byte[] Hash(string path)=>SHA256.HashData(File.ReadAllBytes(path));
        foreach(string robot in new[]{"Johnny5Head","Johnny5FullBody"})
        {
            var options=new ViewerSettings{ConfigFolder=settings.ConfigFolder,Robot=$"Models/Robots/{robot}/johnny5_head.urdf",Backdrop="",FloorGrid=false};
            string source=options.Resolve(options.Robot);var before=Hash(source);
            var scene=new UrdfReader().Load(options);
            Check(scene.Triangles>100000,robot+" actual meshes loaded.");
            Check(scene.Opaque.Length%3==0&&scene.Glass.Length%3==0,"Complete triangle buffers.");
            var robotVertices=scene.Opaque.Where(v=>v.Color!=new Vector4(.14f,.17f,.21f,1)).ToArray();
            Check(robotVertices.Min(v=>v.Position.Y)>=-.001f&&robotVertices.Min(v=>v.Position.Y)<.001f,"Robot rests on the VR floor.");
            Check(scene.Opaque.All(v=>float.IsFinite(v.Position.X+v.Position.Y+v.Position.Z)&&Math.Abs(v.Normal.Length()-1)<.001),"Finite vertices and normalized normals.");
            Check(Hash(source).SequenceEqual(before),"Source URDF remains untouched.");
            if(robot=="Johnny5FullBody")Check(Math.Abs(scene.RobotHeightMetres-1.8288f)<.0001f&&Math.Abs(robotVertices.Max(v=>v.Position.Y)-1.8288f)<.0001f,"Full robot is exactly six feet from its tread bottoms to its highest part at default scene scale.");
            else Check(scene.RobotHeightMetres<.5f,"Head/neck-only model retains its original dimensions.");
            Console.WriteLine($"{robot}: {scene.Visuals:N0} parts, {scene.Triangles:N0} triangles; robot height {scene.RobotHeightMetres:F4} m");
        }
        foreach(string stage in Directory.EnumerateFiles(Path.Combine(settings.ConfigFolder,"Models","Scenery"),"*.urdf",SearchOption.AllDirectories))
        {
            var options=new ViewerSettings{ConfigFolder=settings.ConfigFolder,Backdrop=Path.GetRelativePath(settings.ConfigFolder,stage),FloorGrid=false};
            var before=Hash(stage);var scene=new UrdfReader().Load(options);WriteScene(scene,options);
            Check(scene.Triangles>100000,"Robot and scenery load together.");
            Check(scene.Opaque.Any(v=>Math.Abs(v.Position.Y)<.001f),"Backdrop floor aligns with standing origin.");
            Check(Hash(stage).SequenceEqual(before),"Source backdrop remains untouched.");
        }
        if(!OpenVR.IsRuntimeInstalled())
        {
            try{using var vr=new VrSession();throw new Exception("Missing SteamVR must be reported.");}
            catch(InvalidOperationException ex){Check(ex.Message.Contains("not installed"),"Missing SteamVR gives an actionable message.");}
        }
        string result=$"PASS: {count} importer, coordinates, actual model/backdrop, source preservation and runtime checks.";
        File.WriteAllText(Path.Combine(Folder,"checks-result.txt"),result);Console.WriteLine(result);
    }
}
