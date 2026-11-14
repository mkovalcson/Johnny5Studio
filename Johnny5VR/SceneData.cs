using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Johnny5VR;

[StructLayout(LayoutKind.Sequential)]
internal struct Vertex(Vector3 position, Vector3 normal, Vector4 color)
{
    public Vector3 Position=position;
    public Vector3 Normal=normal;
    public Vector4 Color=color;
    public int Bone;
    public Vector3 IrisMorph;
    public int Iris;
    public Vector2 UV;
    public int Mouth;
    internal const int Stride=72;
}
internal sealed record TreadFrameSet(int Side,Vertex[][] Frames);
internal readonly record struct Triangle(Vector3 A,Vector3 B,Vector3 C,Vector3 NA,Vector3 NB,Vector3 NC);
internal sealed record SceneData(Vertex[] Opaque,Vertex[] Glass,int Visuals,Vector3 Min,Vector3 Max,string[] Warnings,float RobotHeightMetres)
{
    internal AnimationRig? Rig {get;init;}
    internal TreadFrameSet[] Treads {get;init;}=[];
    internal int Triangles => (Opaque.Length+Glass.Length+Treads.Sum(t=>t.Frames[0].Length))/3;
}

// Independent, read-only URDF importer. No editor assemblies or settings are loaded.
internal sealed partial class UrdfReader
{
    private readonly Dictionary<string,Triangle[]> meshes=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> warnings=[];
    private readonly List<Vertex> opaque=[];
    private readonly List<Vertex> glass=[];
    private int visuals;
    private readonly List<(TreadLayerDefinition Definition,Part[][] Frames)> treadParts=[];
    internal Vector3 Min {get;private set;}=new(float.PositiveInfinity);
    internal Vector3 Max {get;private set;}=new(float.NegativeInfinity);
    internal SceneData Load(ViewerSettings settings,IProgress<string>? progress=null)
    {
        var rig=new AnimationRig(settings.Resolve(settings.Robot),settings.ConfigFolder);
        var robot=Read(settings.Resolve(settings.Robot),progress,rig);
        var bottom=robot.Min(p=>Math.Min(p.T.A.Z,Math.Min(p.T.B.Z,p.T.C.Z)));
        var top=robot.Max(p=>Math.Max(p.T.A.Z,Math.Max(p.T.B.Z,p.T.C.Z)));
        float nativeHeight=top-bottom;
        bool fullBody=robot.Any(p=>p.Name.StartsWith("body_",StringComparison.Ordinal));
        float robotScale=fullBody?settings.RobotHeightMetres/nativeHeight:1;
        // URDF: X forward, Y left, Z up. VR: X right, Y up, forward -Z.
        // Turn the set toward the viewer, preserving relative robot/backdrop positions.
        var axes=new Matrix4x4(0,0,1,0, 1,0,0,0, 0,1,0,0, 0,0,0,1);
        var placement=Matrix4x4.CreateScale(settings.Scale)*axes
            *Matrix4x4.CreateRotationY(settings.Heading*MathF.PI/180)
            *Matrix4x4.CreateTranslation(settings.Horizontal,0,-settings.Distance);
        var robotPlacement=Matrix4x4.CreateScale(robotScale)*Matrix4x4.CreateTranslation(0,0,-bottom*robotScale)*placement;
        rig.SetPlacement(robotPlacement);
        foreach(var p in robot)if(rig.Treads?.Layers.ContainsKey(p.Name)!=true)Add(p.T,p.Color,robotPlacement,p.Bone,p.Morph,p.Iris,p.Mouth);
        var animatedTreads=treadParts.Select(layer=>new TreadFrameSet(layer.Definition.Side,layer.Frames.Select(frame=>
        {
            var vertices=new List<Vertex>();foreach(var p in frame)Add(p.T,p.Color,robotPlacement,p.Bone,p.Morph,p.Iris,p.Mouth,vertices);return vertices.ToArray();
        }).ToArray())).ToArray();
        if(!string.IsNullOrWhiteSpace(settings.Backdrop))
        {
            var stage=Read(settings.Resolve(settings.Backdrop),progress);
            var setup=settings.PlaybackStage;
            var floor=stage.Where(p=>p.Name=="backdrop_floor_visual").ToArray();
            if(setup?.DetectFloor==true && stage.FirstOrDefault(p=>p.FloorLink!="") is {} tagged)
                floor=stage.Where(p=>p.FloorLink==tagged.FloorLink).ToArray();
            float stageFloor=setup?.DetectFloor==false?(float)setup.FloorHeight:
                floor.Length>0?floor.Max(p=>Math.Max(p.T.A.Z,Math.Max(p.T.B.Z,p.T.C.Z))):(float)(setup?.FloorHeight??bottom);
            var stageTransform=Matrix4x4.CreateTranslation(0,0,-stageFloor)*placement;
            foreach(var p in stage)Add(p.T,p.Color,stageTransform);
            if(setup!=null)
            {
                setup.Validate(settings.ConfigFolder);
                var propModels=new Dictionary<string,List<Part>>(StringComparer.OrdinalIgnoreCase);
                foreach(var prop in setup.Props??new())
                {
                    string path=prop.Resolve(settings.ConfigFolder);
                    if(!propModels.TryGetValue(path,out var parts))propModels[path]=parts=Read(path,progress);
                    const float radians=MathF.PI/180;
                    var transform=Matrix4x4.CreateScale((float)prop.Scale)*Matrix4x4.CreateRotationX((float)(prop.Roll%360)*radians)
                        *Matrix4x4.CreateRotationY((float)(prop.Pitch%360)*radians)*Matrix4x4.CreateRotationZ((float)(prop.Yaw%360)*radians)
                        *Matrix4x4.CreateTranslation((float)prop.X,(float)prop.Y,(float)prop.Height)*placement;
                    foreach(var part in parts)Add(part.T,part.Color,transform);
                }
            }
        }
        if(string.IsNullOrWhiteSpace(settings.Backdrop))
            foreach(var t in Shapes.Box(new(30,.02f,30)))Add(t,new(.14f,.17f,.21f,1),Matrix4x4.CreateTranslation(0,-.012f,0));
        if(settings.FloorGrid)AddGrid();
        return new(opaque.ToArray(),glass.ToArray(),visuals,Min,Max,warnings.Distinct().ToArray(),nativeHeight*robotScale*settings.Scale){Rig=rig,Treads=animatedTreads};
    }
    private record Part(Triangle T,Vector4 Color,string Name,int Bone,Vector3[]? Morph,int Iris,int Mouth=0,string FloorLink="");
    private List<Part> Read(string path,IProgress<string>? progress,AnimationRig? rig=null)
    {
        progress?.Report("Reading "+Path.GetFileName(Path.GetDirectoryName(path))+"…");
        var root=XDocument.Load(path).Root??throw new InvalidDataException("URDF has no root.");
        var materials=root.Elements("material").Where(m=>m.Attribute("name")!=null)
            .ToDictionary(m=>(string)m.Attribute("name")!,m=>Color(m),StringComparer.Ordinal);
        var links=root.Elements("link").ToDictionary(l=>(string)l.Attribute("name")!);
        var joints=root.Elements("joint").ToArray();
        var children=joints.ToDictionary(j=>(string)j.Element("child")!.Attribute("link")!);
        var worlds=new Dictionary<string,Matrix4x4>();
        var visiting=new HashSet<string>();
        Matrix4x4 World(string name)
        {
            if(worlds.TryGetValue(name,out var existing))return existing;
            if(!links.ContainsKey(name)||!visiting.Add(name))throw new InvalidDataException("Invalid/cyclic URDF link: "+name);
            var world=children.TryGetValue(name,out var j)?Origin(j.Element("origin"))*World((string)j.Element("parent")!.Attribute("link")!):Matrix4x4.Identity;
            visiting.Remove(name);return worlds[name]=world;
        }
        var parts=new List<Part>();int index=0;
        foreach(var link in links)
        foreach(var visual in link.Value.Elements("visual"))
        {
            string name=(string?)visual.Attribute("name")??link.Key;
            if(++index%30==0)progress?.Report($"Loading {Path.GetFileName(Path.GetDirectoryName(path))} · {index} parts…");
            // Keep accessory geometry available; the rig selects None/Laser/Toolbox per frame.
            var material=visual.Element("material");
            string mat=(string?)material?.Attribute("name")??"";
            var color=material?.Element("color")!=null?Color(material):materials.GetValueOrDefault(mat,new Vector4(.65f,.68f,.72f,1));
            var transform=Origin(visual.Element("origin"))*World(link.Key);
            Matrix4x4.Invert(transform,out var inverse);var normalMatrix=Matrix4x4.Transpose(inverse);
            var geometry=visual.Element("geometry");if(geometry==null)continue;
            string floorLink=link.Key.Contains("floor",StringComparison.OrdinalIgnoreCase)&&ReferenceEquals(link.Value.Element("visual"),visual)&&geometry.Element("box")!=null?link.Key:"";
            Triangle[] triangles;
            if(geometry.Element("mesh") is {} mesh)
            {
                string filename=(string?)mesh.Attribute("filename")??throw new InvalidDataException("Mesh filename is missing.");
                if(filename.StartsWith("package://"))throw new NotSupportedException("Package URIs need local paths: "+filename);
                string file=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,filename.Replace('/',Path.DirectorySeparatorChar)));
                if(!meshes.TryGetValue(file,out triangles!))
                {
                    triangles=MeshFile.Read(file);
                    if(CurvedSurfaces.IsRoundedHeadSurface(file))triangles=CurvedSurfaces.Smooth(triangles);
                    meshes[file]=triangles;
                }
                var scale=Vec((string?)mesh.Attribute("scale"),Vector3.One);
                transform=Matrix4x4.CreateScale(scale)*transform;
                Matrix4x4.Invert(transform,out inverse);normalMatrix=Matrix4x4.Transpose(inverse);
            }
            else if(geometry.Element("box") is {} box)triangles=Shapes.Box(Vec((string?)box.Attribute("size"),Vector3.One));
            else if(geometry.Element("cylinder") is {} cylinder)triangles=Shapes.Cylinder(Number(cylinder,"radius"),Number(cylinder,"length"));
            else if(geometry.Element("sphere") is {} sphere)triangles=Shapes.Sphere(Number(sphere,"radius"));
            else throw new NotSupportedException("Unsupported geometry in "+name);
            bool iris=name.EndsWith("_iris_disc");
            Vector3 Morph(Vector3 p)
            {
                var radial=new Vector3(p.X,p.Y,0);float r=radial.Length();
                return r<1e-8f?Vector3.Zero:Vector3.TransformNormal(radial/r*Math.Clamp((.02286f-r)/.01143f,0,1),transform);
            }
            int mouth=MouthId(mat);
            if(mouth is 192 or 193)color.W=.5f;
            Part PartFor(Triangle t)=>new(new(
                Vector3.Transform(t.A,transform),Vector3.Transform(t.B,transform),Vector3.Transform(t.C,transform),
                SafeNormal(Vector3.TransformNormal(t.NA,normalMatrix)),SafeNormal(Vector3.TransformNormal(t.NB,normalMatrix)),SafeNormal(Vector3.TransformNormal(t.NC,normalMatrix))),color,name,rig?.Bone(link.Key)??0,iris?[Morph(t.A),Morph(t.B),Morph(t.C)]:null,iris?(name.StartsWith("left")?1:2):0,mouth,floorLink);
            foreach(var t in triangles)parts.Add(PartFor(t));
            if(rig?.Treads is {} tread&&tread.Layers.TryGetValue(name,out var layer))
            {
                if(layer.Link!=link.Key)throw new InvalidDataException("Tread animation link does not match its URDF visual: "+name);
                treadParts.Add((layer,layer.Frames.Select(f=>MeshFile.Read(f).Select(PartFor).ToArray()).ToArray()));
            }
            if(material?.Element("texture")!=null||root.Elements("material").Any(m=>(string?)m.Attribute("name")==mat&&m.Element("texture")!=null))
                warnings.Add("Texture maps are not supported yet; showing the base color for "+mat+".");
            visuals++;
        }
        if(rig!=null)AddRingGeometry(parts,rig,World);
        if(parts.Count==0)throw new InvalidDataException("No visible triangles in "+path);
        return parts;
    }
    internal static int MouthId(string material)
    {
        if(material=="left_pupil_dynamic")return 192;
        if(material=="right_pupil_dynamic")return 193;
        if(material.Length>=10&&material.StartsWith("lip_led_")&&int.TryParse(material.AsSpan(8,2),out int lip)&&lip<14)return lip+1;
        int at=material.LastIndexOf("_s",StringComparison.Ordinal);
        if(material.StartsWith("mouth_side_")&&at>=0&&material.Length>=at+4&&int.TryParse(material.AsSpan(at+2,2),out int station)&&station<12)return (material.StartsWith("mouth_side_red_")?32:64)+station;
        return 0;
    }
    private void Add(Triangle t,Vector4 color,Matrix4x4 world,int bone=0,Vector3[]? morph=null,int iris=0,int mouth=0,List<Vertex>? output=null)
    {
        var target=output??(color.W<.99f?glass:opaque);
        Matrix4x4.Invert(world,out var inverse);var n=Matrix4x4.Transpose(inverse);
        int index=0;
        void V(Vector3 p,Vector3 normal)
        {
            p=Vector3.Transform(p,world);Min=Vector3.Min(Min,p);Max=Vector3.Max(Max,p);
            target.Add(new(p,SafeNormal(Vector3.TransformNormal(normal,n)),color){Bone=bone,Iris=iris,Mouth=mouth,IrisMorph=morph==null?Vector3.Zero:Vector3.TransformNormal(morph[index],world)});index++;
        }
        V(t.A,t.NA);V(t.B,t.NB);V(t.C,t.NC);
    }
    private void AddGrid()
    {
        for(int i=-12;i<=12;i++)
        {
            var color=new Vector4(.24f,.28f,.32f,1);
            foreach(var t in Shapes.Box(new(.006f,.001f,24)))Add(t,color,Matrix4x4.CreateTranslation(i,.002f,0));
            foreach(var t in Shapes.Box(new(24,.001f,.006f)))Add(t,color,Matrix4x4.CreateTranslation(0,.002f,i));
        }
    }
    internal static Vector3 SafeNormal(Vector3 n)=>n.LengthSquared()>1e-14f?Vector3.Normalize(n):Vector3.UnitZ;
    internal static float Number(XElement e,string a)=>float.Parse((string?)e.Attribute(a)??"0",CultureInfo.InvariantCulture);
    internal static Vector3 Vec(string? text,Vector3 fallback)
    {
        if(string.IsNullOrWhiteSpace(text))return fallback;
        var x=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(x.Length!=3||x.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid vector: "+text);
        return new(x[0],x[1],x[2]);
    }
    private static Vector4 Color(XElement e)
    {
        var a=((string?)e.Element("color")?.Attribute("rgba")??"0.65 0.68 0.72 1").Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(a.Length!=4||a.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid material color.");
        return Vector4.Clamp(new(a[0],a[1],a[2],a[3]),Vector4.Zero,Vector4.One);
    }
    internal static Matrix4x4 Origin(XElement? e)
    {
        var xyz=Vec((string?)e?.Attribute("xyz"),Vector3.Zero);var rpy=Vec((string?)e?.Attribute("rpy"),Vector3.Zero);
        return Matrix4x4.CreateRotationX(rpy.X)*Matrix4x4.CreateRotationY(rpy.Y)*Matrix4x4.CreateRotationZ(rpy.Z)*Matrix4x4.CreateTranslation(xyz);
    }
}
