using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private sealed record WeatheringSurface(GeometryModel3D Model, MeshGeometry3D Geometry,
        Material Front, Material Back, Color Color, Color Specular, double Shine, bool Rubber, bool Stainless, bool GoldProtected, string Link, UrdfWorkshopPart WorkshopPart);
    private readonly List<WeatheringSurface> _weatheringSurfaces = new();
    private IReadOnlyDictionary<MeshGeometry3D, MeshGeometry3D> _weatheringGeometry = new Dictionary<MeshGeometry3D, MeshGeometry3D>();
    private int _weatheringLevel;
    private bool _goldJohnny;
    internal int WeatheringLevel => _weatheringLevel;
    internal int WeatheringSurfaceCount => _weatheringSurfaces.Count;
    private void CaptureCleanWeatheringSurfaces()
    {
        // Tread initialization installs phase geometry and retires static back
        // faces. Capture the final clean scene after those initializers run.
        for (int i = _weatheringSurfaces.Count - 1; i >= 0; i--)
        {
            var surface = _weatheringSurfaces[i];
            if (surface.Model.Geometry is not MeshGeometry3D mesh) { _weatheringSurfaces.RemoveAt(i); continue; }
            _weatheringSurfaces[i] = surface with { Geometry = mesh, Front = surface.Model.Material, Back = surface.Model.BackMaterial };
        }
    }

    private void RegisterWeathering(GeometryModel3D model, System.Xml.Linq.XElement visual, string materialName)
    {
        string source = visual.Element("geometry")?.Element("mesh")?.Attribute("filename")?.Value;
        string name = (visual.Attribute("name")?.Value + " " + materialName + " " + source).ToLowerInvariant();
        string identity = (visual.Attribute("name")?.Value + " " + source).ToLowerInvariant();
        // The three pivoting lift rods and their rubber boots below the neck
        // swivel retain their working finish, independently of the gold mounts.
        bool goldNeckRod = visual.Parent?.Attribute("name")?.Value is
            "body_neck_front_rod_link" or "body_neck_rear_core_l_link" or "body_neck_rear_core_r_link";
        string meshFile = System.IO.Path.GetFileName(source?.Replace('\\', '/') ?? "");
        bool goldEyeStructure = meshFile.ToLowerInvariant() is "eye_tube_left.stl" or "eye_tube_right.stl"
            or "eye_fixed_cad1ee_rest_without_gimbal_spacers_or_side_screws.stl" or "eye_fixed_f6f6f3.stl"
            or "eye_gimbal_bar_top_cad1ee.stl" or "eye_gimbal_bar_bottom_cad1ee.stl"
            or "eye_gimbal_spacers_cad1ee.stl" or "eye_gimbal_a0a0a0.stl"
            or "eye_gimbal_schcscrew_0086_56x0125_cad1ee.stl" or "eye_gimbal_side_schcscrew_0086_56x0375_cad1ee.stl";
        if (model.Geometry is not MeshGeometry3D geometry || geometry.Positions.Count == 0
            || new[] { "iris", "pupil" }.Any(identity.Contains)
            || new[] { "dynamic", "neopixel", "lip_led", "glass", "lcd", "screen", "video" }.Any(name.Contains)) return;
        var material = (model.Material ?? model.BackMaterial) as MaterialGroup;
        if (material == null || material.Children.Any(m => m is EmissiveMaterial)) return;
        var diffuse = material.Children.OfType<DiffuseMaterial>().SingleOrDefault();
        if (diffuse?.Brush is not SolidColorBrush brush || brush.Color.A != 255 || brush.Opacity != 1) return;
        var spec = material.Children.OfType<SpecularMaterial>().FirstOrDefault();
        var specColor = (spec?.Brush as SolidColorBrush)?.Color ?? Colors.Black;
        _weatheringSurfaces.Add(new(model, geometry, model.Material, model.BackMaterial, brush.Color, specColor,
            spec?.SpecularPower ?? 32, name.Contains("rubber") || name.Contains("neck_static_black") || name.Contains("tread"),
            name.Contains("/fasteners/") || name.Contains("stainless")
                || ((identity.Contains("bolt") || identity.Contains("screw")) && !identity.Contains("hole") && !identity.Contains("slot")),
            (name.Contains("eye") && !goldEyeStructure) || name.Contains("/left_piston.stl") || name.Contains("/right_piston.stl") || (name.Contains("shaft") && name.Contains("hydraulic"))
                || goldNeckRod || identity.Contains("bellows") || identity.Contains("neck_static_black") || name.Contains("mouth_pcb_green")
                || ((identity.Contains("tread") || identity.StartsWith("body_caster_wheel_",StringComparison.Ordinal))
                    && Math.Max(brush.Color.R,Math.Max(brush.Color.G,brush.Color.B))<70),
            visual.Parent?.Attribute("name")?.Value ?? "", UrdfWeathering.WorkshopPart(source ?? "", name, visual.Parent?.Attribute("name")?.Value ?? "",brush.Color)));
    }

    // Synchronous preparation is retained for batch checks. Live views use
    // RequestWeathering so texture generation never runs on the UI thread.
    internal void ApplyWeathering(int level, bool goldJohnny = false)
    {
        CancelWeatheringPreparation();
        level = goldJohnny ? 0 : Math.Clamp(level, 0, 3);
        if (_weatheringLevel == level && _goldJohnny == goldJohnny) return;
        PreparedWeathering prepared = null;
        if (level > 0)
        {
            var snapshot = CaptureWeatheringWork();
            lock (_weatheringPreparationLock) prepared = PrepareWeathering(snapshot, level, CancellationToken.None);
            _readyWeathering[level] = prepared;
        }
        CommitWeathering(level, goldJohnny, prepared);
    }

    private MeshGeometry3D WeatheredTreadFrame(MeshGeometry3D frame) =>
        _weatheringLevel > 0 && _weatheringGeometry.TryGetValue(frame, out var mapped) ? mapped : frame;
}

/// <summary>Deterministic opaque surface textures, baked on selection. Mapping
/// is in each part's local coordinates so marks follow joints and disassembly.</summary>
internal static partial class UrdfWeathering
{
    internal static readonly Material Gold = CreateGold();
    private static Material CreateGold()
    {
        var material = new MaterialGroup();
        // Preferred bronze-gold swatch, lightened about 8% without shifting hue.
        // Highlights come from scene lighting, not a painted gradient.
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(171,121,54))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(130,255,255,255)),32));
        material.Freeze(); return material;
    }
    // Keep renderer metadata on these generated images only. Existing video,
    // screen and lens textures retain their established conversion behavior.
    private sealed record TextureLighting(Color Ambient);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, TextureLighting> Lighting = new();
    internal static bool TryTextureLighting(BitmapSource texture, out Color ambient)
    {
        bool found = Lighting.TryGetValue(texture, out var lighting);
        ambient = found ? lighting.Ambient : default;
        return found;
    }
    private const int Tile = 128, Width = Tile * 3, Height = Tile * 2, Border = 2;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, float[]> EdgeMaps = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, float[]> DepthMaps = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, float[]> BoltMaps = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, bool[]> ContactMaps = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, float[]> ConvexEdgeMaps = new();
    private sealed class Layout
    {
        internal readonly Rect3D[] Bounds;
        internal readonly int Columns,Size,PageWidth,PageHeight,Width,Height;
        internal Layout(Rect3D[] bounds)
        {
            Bounds=bounds;Columns=(int)Math.Ceiling(Math.Sqrt(bounds.Length));
            Size=Math.Clamp(512/Columns,16,Tile);PageWidth=Size*3;PageHeight=Size*2;
            Width=Columns*PageWidth;Height=((bounds.Length+Columns-1)/Columns)*PageHeight;
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, Layout> Layouts = new();
    internal static MeshGeometry3D WithCoordinates(MeshGeometry3D source, PointCollection coordinates)
    {
        var result = new MeshGeometry3D { Positions = source.Positions, Normals = source.Normals,
            TriangleIndices = source.TriangleIndices, TextureCoordinates = coordinates };
        result.Freeze(); return result;
    }
    internal static MeshGeometry3D Map(MeshGeometry3D source, bool prepareEdges = true, bool includeFinEdges = false)
    {
        bool separate = source.Positions.Count == source.TriangleIndices.Count;
        for (int i = 0; separate && i < source.TriangleIndices.Count; i++) separate = source.TriangleIndices[i] == i;
        var positions = separate ? source.Positions : new Point3DCollection(source.TriangleIndices.Select(i => source.Positions[i]));
        var normals = source.Normals.Count == source.Positions.Count
            ? (separate ? source.Normals : new Vector3DCollection(source.TriangleIndices.Select(i => source.Normals[i]))) : new Vector3DCollection();
        var indices = separate ? source.TriangleIndices : new Int32Collection(Enumerable.Range(0, positions.Count));
        var uv = new PointCollection(positions.Count);
        if (normals.Count != positions.Count)
            for (int i=0;i<positions.Count;i+=3)
            {
                var n = Vector3D.CrossProduct(positions[i+1]-positions[i],positions[i+2]-positions[i]);
                if(n.LengthSquared>1e-22)n.Normalize(); else n=new Vector3D(0,0,1);
                normals.Add(n); normals.Add(n); normals.Add(n);
            }
        // CAD exports group disconnected solids by material. Give each connected
        // part a separate cube chart, so one part cannot stamp edges on another.
        var welded=new Dictionary<(long,long,long),int>(); var parent=new int[positions.Count];
        int Root(int i) { while(parent[i]!=i) { parent[i]=parent[parent[i]]; i=parent[i]; } return i; }
        void Join(int a,int b) { a=Root(a);b=Root(b);if(a!=b)parent[b]=a; }
        for(int i=0;i<positions.Count;i++)
        {
            var p=positions[i]; parent[i]=i;
            var key=((long)Math.Round(p.X*1e6),(long)Math.Round(p.Y*1e6),(long)Math.Round(p.Z*1e6));
            if(welded.TryGetValue(key,out var old))Join(old,i);else welded[key]=i;
            if(i%3==2) { Join(i-2,i-1);Join(i-1,i); }
        }
        var parts=new Dictionary<int,int>(); var boxes=new List<Rect3D>(); var component=new int[positions.Count];
        for(int i=0;i<positions.Count;i++)
        {
            int root=Root(i);if(!parts.TryGetValue(root,out int part)) { parts[root]=part=parts.Count; boxes.Add(Rect3D.Empty); }
            component[i]=part; var box=boxes[part]; box.Union(positions[i]); boxes[part]=box;
        }
        var layout=new Layout(boxes.ToArray());
        for (int i = 0; i < positions.Count; i += 3)
        {
            var n = Vector3D.CrossProduct(positions[i + 1] - positions[i], positions[i + 2] - positions[i]);
            double x = Math.Abs(n.X), y = Math.Abs(n.Y), z = Math.Abs(n.Z);
            int face = x >= y && x >= z ? (n.X >= 0 ? 0 : 1) : y >= z ? (n.Y >= 0 ? 2 : 3) : (n.Z >= 0 ? 4 : 5);
            for (int j = 0; j < 3; j++)
            {
                var p=Project(positions[i+j],layout.Bounds[component[i]],component[i]*6+face,layout);
                uv.Add(new Point(p.X/layout.Width,p.Y/layout.Height));
            }
        }
        var result = new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices, TextureCoordinates = uv };
        result.Freeze();
        Layouts.Add(result,layout);
        if (prepareEdges) EnsureEdges(result,includeFinEdges);
        return result;
    }

    internal static void EnsureEdges(MeshGeometry3D mapped, bool includeFinEdges = false)
    {
        if (!EdgeMaps.TryGetValue(mapped, out _)) EdgeMaps.Add(mapped, BakeEdges(mapped,includeFinEdges));
    }
    internal static int TextureParts(MeshGeometry3D mapped) => Layouts.TryGetValue(mapped,out var layout)?layout.Bounds.Length:1;

    internal static Material Material(Color color, Color specular, double shine, bool rubber, int level, MeshGeometry3D mapped = null, bool stainless = false)
    {
        var texture = Texture(color, rubber, level, mapped, stainless);
        var result = new MaterialGroup();
        result.Children.Add(new DiffuseMaterial(new ImageBrush(texture) { Stretch = Stretch.Fill }));
        double gloss = 1 - level * .16;
        result.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(specular.A,
            (byte)(specular.R * gloss), (byte)(specular.G * gloss), (byte)(specular.B * gloss))), Math.Max(8, shine * gloss)));
        result.Freeze(); return result;
    }

    internal static BitmapSource Texture(Color color, bool rubber, int level, MeshGeometry3D mapped = null, bool stainless = false)
    {
        var layout=mapped!=null && Layouts.TryGetValue(mapped,out var found)?found:new Layout(new[]{Rect3D.Empty});
        int width=layout.Width,height=layout.Height,size=layout.Size;
        var pixels = new byte[width * height * 4];
        bool dark = Math.Max(color.R, Math.Max(color.G, color.B)) < 70;
        bool gold = color.R > 100 && color.G > 70 && color.B < color.G * .85;
        bool paint = !rubber && !stainless && !gold && (dark || Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) > 65);
        double strength = new[] { 0d, .28, .62, 1 }[Math.Clamp(level, 0, 3)];
        float[] edges = !rubber && !stainless && mapped != null && EdgeMaps.TryGetValue(mapped, out var edgeMap) ? edgeMap : null;
        float[] bolts = mapped != null && BoltMaps.TryGetValue(mapped, out var boltMap) ? boltMap : null;
        var fields = Fields.Value;
        double chipStrength = level switch { 1 => .48, 2 => .82, 3 => 1, _ => 0 };
        for(int part=0;part<layout.Bounds.Length;part++) for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int index = (part/layout.Columns*layout.PageHeight+face/3*size+y)*width+part%layout.Columns*layout.PageWidth+face%3*size+x;
                var field = fields[(face/3*Tile+y*Tile/size)*Width+face%3*Tile+x*Tile/size];
                double edge = edges?[index] ?? 0, coarse = field.Coarse, medium = field.Medium;
                // Chipped paint follows actual mesh boundaries and hard creases,
                // never arbitrary UV tile borders or STL triangulation lines.
                double chips = paint ? Smooth(.42 - level * .08, .78 - level * .08, edge * (medium * .65 + .55)) * chipStrength : 0;
                double scuff = rubber ? 0 : field.Scratch * strength * edge * (paint ? .6 : .2);
                double polished = !paint && !rubber && !stainless && !gold ? Smooth(.25,.7,edge) * strength * .85 : 0;
                double exposed = Math.Max(polished,Math.Max(chips, scuff));
                double patina = !paint && !rubber && !stainless
                    ? strength * (.06 + Smooth(.34, .76, coarse)) * (gold ? .65 : .48) : 0;
                // Outdoor corrosion is patchy and concentrated around exposed
                // edges. Rubber, brass and stainless fasteners never rust.
                double rust = level == 3 && !rubber && !gold && !stainless
                    ? Math.Max((bolts?[index] ?? 0) * .92, Smooth(.5,.8,coarse) * Smooth(.35,.7,medium) * edge * (paint ? exposed : .65)) : 0;
                double grime = strength * (rubber ? .035 : .025) * coarse;
                double green = gold && level == 3 ? Smooth(.67, .83, coarse) * Smooth(.38, .7, medium) * .8 : 0;
                double Channel(byte original, double oxide, double tarnish, double verdigris)
                {
                    double value = original * (1 - grime);
                    value = value * (1 - patina) + tarnish * patina;
                    value = value * (1 - green) + verdigris * green;
                    value = value * (1 - exposed) + (185 + field.Fine * 40) * exposed;
                    value = value * (1 - rust) + oxide * rust;
                    return Math.Clamp(value, 0, 255);
                }
                int offset = index * 4;
                pixels[offset] = (byte)Channel(color.B, 24 + medium * 16, gold ? 38 : 116, 82);
                pixels[offset + 1] = (byte)Channel(color.G, 55 + medium * 30, gold ? 79 : 131, 107);
                pixels[offset + 2] = (byte)Channel(color.R, 126 + medium * 43, gold ? 114 : 142, 48);
                pixels[offset + 3] = 255;
            }
        }
        // No alpha channel: DX11 can retain the opaque render pass for the body.
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze();
        // Helix's Phong ambient term is independent of its diffuse map. Use the
        // map's average linear reflectance instead of the white map multiplier.
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            r += LinearChannel[pixels[i + 2]]; g += LinearChannel[pixels[i + 1]]; b += LinearChannel[pixels[i]];
        }
        int countPixels = layout.Bounds.Length*size*size*6;
        Lighting.Add(bitmap, new(Color.FromScRgb(1, (float)(r / countPixels), (float)(g / countPixels), (float)(b / countPixels))));
        return bitmap;
    }

    private static readonly float[] LinearChannel = Enumerable.Range(0,256).Select(i => Color.FromRgb((byte)i,0,0).ScR).ToArray();

    private static double Smooth(double low, double high, double value)
    { double t = Math.Clamp((value - low) / (high - low), 0, 1); return t * t * (3 - 2 * t); }

    private readonly record struct Field(float Coarse, float Medium, float Fine, float Scratch);
    private static readonly Lazy<Field[]> Fields = new(() =>
    {
        var fields = new Field[Width * Height];
        for (int face = 0; face < 6; face++)
        {
            int seed = 1741 + face * 739; var random = new Random(seed); var scratches = new float[Tile * Tile];
            for (int s = 0; s < 34; s++)
            {
                int x = random.Next(4, Tile - 4), y = random.Next(4, Tile - 4), length = random.Next(3, 14);
                double angle = random.NextDouble() * Math.PI;
                for (int step = 0; step < length; step++)
                {
                    int px = x + (int)(Math.Cos(angle) * step), py = y + (int)(Math.Sin(angle) * step);
                    if (px >= 0 && px < Tile && py >= 0 && py < Tile) scratches[py * Tile + px] = .7f;
                }
            }
            for (int y = 0; y < Tile; y++) for (int x = 0; x < Tile; x++)
                fields[(face / 3 * Tile + y) * Width + face % 3 * Tile + x] = new(
                    (float)Noise(x * .06, y * .06, seed), (float)Noise(x * .3, y * .3, seed + 19),
                    (float)Hash(x, y, seed + 31), scratches[y * Tile + x]);
        }
        return fields;
    });

    // Weld only for detecting boundaries/creases. Original vertices, normals,
    // triangle indices and collision geometry are never changed.
    private static float[] BakeEdges(MeshGeometry3D mesh, bool includeFinEdges)
    {
        var layout=Layouts.GetValue(mesh,_=>new Layout(new[]{mesh.Bounds}));
        int Width=layout.Width,Height=layout.Height,Tile=layout.Size;
        var depth = Enumerable.Repeat(float.NegativeInfinity, Width * Height).ToArray();
        // Keep the nearest outward-facing surface in each cube projection.
        // Rear walls and internal details must not stamp wear through the skin.
        for (int f=0;f<mesh.TriangleIndices.Count;f+=3)
        {
            int a=mesh.TriangleIndices[f],b=mesh.TriangleIndices[f+1],c=mesh.TriangleIndices[f+2];
            var p=mesh.TextureCoordinates[a]; var q=mesh.TextureCoordinates[b]; var r=mesh.TextureCoordinates[c];
            int face=Face(p,layout);
            double ax=p.X*Width,ay=p.Y*Height,bx=q.X*Width,by=q.Y*Height,cx=r.X*Width,cy=r.Y*Height;
            double denominator=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy); if(Math.Abs(denominator)<1e-10)continue;
            double da=Depth(mesh.Positions[a],face),db=Depth(mesh.Positions[b],face),dc=Depth(mesh.Positions[c],face);
            for(int y=Math.Max(0,(int)Math.Floor(Math.Min(ay,Math.Min(by,cy))));y<=Math.Min(Height-1,(int)Math.Ceiling(Math.Max(ay,Math.Max(by,cy))));y++)
            for(int x=Math.Max(0,(int)Math.Floor(Math.Min(ax,Math.Min(bx,cx))));x<=Math.Min(Width-1,(int)Math.Ceiling(Math.Max(ax,Math.Max(bx,cx))));x++)
            {
                double u=((by-cy)*(x+.5-cx)+(cx-bx)*(y+.5-cy))/denominator;
                double v=((cy-ay)*(x+.5-cx)+(ax-cx)*(y+.5-cy))/denominator;
                if(u>=-1e-6&&v>=-1e-6&&u+v<=1.000001) depth[y*Width+x]=Math.Max(depth[y*Width+x],(float)(da*u+db*v+dc*(1-u-v)));
            }
        }
        DepthMaps.Add(mesh,depth);
        // Protected interior features are not contact edges. Keep wear near
        // the exterior silhouette of each chart; enclosed holes/recesses do
        // not become a web of bright outlines across the middle of a panel.
        var exterior=new bool[depth.Length]; var queue=new Queue<int>();
        for(int tileY=0;tileY<Height;tileY+=Tile)for(int tileX=0;tileX<Width;tileX+=Tile)
        {
            void Seed(int x,int y)
            { int i=y*Width+x;if(!exterior[i]&&float.IsNegativeInfinity(depth[i])) { exterior[i]=true;queue.Enqueue(i); } }
            for(int d=0;d<Tile;d++) { Seed(tileX+d,tileY);Seed(tileX+d,tileY+Tile-1);Seed(tileX,tileY+d);Seed(tileX+Tile-1,tileY+d); }
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),x=i%Width,y=i/Width;
                if(x>tileX)Seed(x-1,y);if(x<tileX+Tile-1)Seed(x+1,y);
                if(y>tileY)Seed(x,y-1);if(y<tileY+Tile-1)Seed(x,y+1);
            }
        }
        // Use the convex outside envelope as the contact region. Re-entrant
        // notches, seams and grooves inside a part's outline are sheltered.
        var contact=new bool[depth.Length];
        for(int ty=0;ty<Height;ty+=Tile)for(int tx=0;tx<Width;tx+=Tile)
        {
            var points=new List<Point>();
            for(int y=ty;y<ty+Tile;y++)
            {
                int first=-1,last=-1;
                for(int x=tx;x<tx+Tile;x++)if(!float.IsNegativeInfinity(depth[y*Width+x])) { if(first<0)first=x;last=x; }
                if(first>=0) { points.Add(new Point(first+.5,y+.5));points.Add(new Point(last+.5,y+.5)); }
            }
            var ordered=points.Distinct().OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray(); if(ordered.Length<2)continue;
            var hull=new List<Point>();
            double Turn(Point a,Point b,Point c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            foreach(var p in ordered) { while(hull.Count>=2&&Turn(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
            int lower=hull.Count;
            foreach(var p in ordered.Reverse().Skip(1)) { while(hull.Count>lower&&Turn(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
            for(int h=1;h<hull.Count;h++)
            {
                var a=hull[h-1];var delta=hull[h]-a;int steps=Math.Max(1,(int)Math.Ceiling(delta.Length*2));
                for(int step=0;step<=steps;step++)
                {
                    var p=a+delta*((double)step/steps);
                    for(int y=Math.Max(ty,(int)p.Y-3);y<=Math.Min(ty+Tile-1,(int)p.Y+3);y++)
                        for(int x=Math.Max(tx,(int)p.X-3);x<=Math.Min(tx+Tile-1,(int)p.X+3);x++)contact[y*Width+x]=true;
                }
            }
        }
        ContactMaps.Add(mesh,contact);
        var ids = new Dictionary<(long, long, long), int>();
        var vertices = new int[mesh.Positions.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = mesh.Positions[i]; var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
            vertices[i] = id;
        }
        var normals = new Vector3D[mesh.TriangleIndices.Count / 3];
        var edges = new Dictionary<(int, int), (int Face, int A, int B, bool Shared)>();
        var lines = new List<(int A, int B)>();
        for (int f = 0; f < normals.Length; f++)
        {
            int a = mesh.TriangleIndices[f * 3], b = mesh.TriangleIndices[f * 3 + 1], c = mesh.TriangleIndices[f * 3 + 2];
            var n = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            if (n.LengthSquared < 1e-22) continue;
            n.Normalize(); normals[f] = n;
            Add(a, b); Add(b, c); Add(c, a);
            void Add(int x, int y)
            {
                int u = vertices[x], v = vertices[y]; if (u == v) return;
                var key = u < v ? (u, v) : (v, u);
                if (!edges.TryGetValue(key, out var old)) edges[key] = (f, x, y, false);
                else
                {
                    bool crease = Vector3D.DotProduct(normals[old.Face], n) < .766; // 40 degrees
                    var direction=mesh.Positions[old.B]-mesh.Positions[old.A];
                    bool convex=Vector3D.DotProduct(Vector3D.CrossProduct(normals[old.Face],n),direction)>1e-12;
                    if (crease && convex) { lines.Add((old.A, old.B)); lines.Add((x, y)); }
                    edges[key] = (old.Face, old.A, old.B, true);
                }
            }
        }
        foreach (var edge in edges.Values) if (!edge.Shared) lines.Add((edge.A, edge.B));
        var mask = new float[Width * Height];
        var convexMask = includeFinEdges ? new float[Width * Height] : null;
        double tolerance=.0002+Math.Max(mesh.Bounds.SizeX,Math.Max(mesh.Bounds.SizeY,mesh.Bounds.SizeZ))/Tile*.6;
        foreach (var (a, b) in lines)
        {
            var p = mesh.TextureCoordinates[a]; var q = mesh.TextureCoordinates[b];
            double ax = p.X * Width, ay = p.Y * Height, bx = q.X * Width, by = q.Y * Height;
            // UVs on either side of a crease live in separate cube faces.
            int tileX = Math.Clamp((int)(ax / Tile), 0, Width/Tile-1), tileY = Math.Clamp((int)(ay / Tile), 0, Height/Tile-1);
            double dx = bx - ax, dy = by - ay, length = dx * dx + dy * dy;
            int face=Face(p,layout); double da=Depth(mesh.Positions[a],face),db=Depth(mesh.Positions[b],face);
            int minX = Math.Max(tileX * Tile, (int)Math.Floor(Math.Min(ax, bx) - 2)), maxX = Math.Min((tileX + 1) * Tile - 1, (int)Math.Ceiling(Math.Max(ax, bx) + 2));
            int minY = Math.Max(tileY * Tile, (int)Math.Floor(Math.Min(ay, by) - 2)), maxY = Math.Min((tileY + 1) * Tile - 1, (int)Math.Ceiling(Math.Max(ay, by) + 2));
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                double t = length < 1e-10 ? 0 : Math.Clamp(((x + .5 - ax) * dx + (y + .5 - ay) * dy) / length, 0, 1);
                double distance = Math.Sqrt(Math.Pow(x + .5 - ax - t * dx, 2) + Math.Pow(y + .5 - ay - t * dy, 2));
                if (depth[y*Width+x] > da+(db-da)*t+tolerance) continue;
                float wear = (float)(1 - Smooth(.35, 2, distance));
                if(convexMask!=null)convexMask[y*Width+x] = Math.Max(convexMask[y*Width+x],wear);
                if(!contact[y*Width+x])continue;
                bool outer=false;
                for(int sy=Math.Max(tileY*Tile,y-3);sy<=Math.Min((tileY+1)*Tile-1,y+3)&&!outer;sy++)
                    for(int sx=Math.Max(tileX*Tile,x-3);sx<=Math.Min((tileX+1)*Tile-1,x+3);sx++) if(exterior[sy*Width+sx]) { outer=true;break; }
                if(!outer)continue;
                mask[y * Width + x] = Math.Max(mask[y * Width + x], wear);
            }
        }
        if(convexMask!=null)ConvexEdgeMaps.Add(mesh,convexMask);
        return mask;
    }

    private static int Face(Point uv,Layout layout)
    {
        int x=Math.Clamp((int)(uv.X*layout.Width),0,layout.Width-1),y=Math.Clamp((int)(uv.Y*layout.Height),0,layout.Height-1);
        return (y/layout.PageHeight*layout.Columns+x/layout.PageWidth)*6+(y%layout.PageHeight/layout.Size)*3+x%layout.PageWidth/layout.Size;
    }
    private static double Depth(Point3D p,int face)
    { face%=6; return (face<2?p.X:face<4?p.Y:p.Z)*((face&1)==0?1:-1); }
    private static Point Project(Point3D p,Rect3D b,int chart,Layout layout)
    {
        int face=chart%6,part=chart/6;
        double Unit(double v,double start,double size)=>size<1e-10?.5:(v-start)/size;
        double u=face<2?Unit(p.Y,b.Y,b.SizeY):Unit(p.X,b.X,b.SizeX);
        double v=face<4?Unit(p.Z,b.Z,b.SizeZ):Unit(p.Y,b.Y,b.SizeY);
        if((face&1)!=0)u=1-u;
        return new Point(part%layout.Columns*layout.PageWidth+face%3*layout.Size+Border+u*(layout.Size-Border*2),part/layout.Columns*layout.PageHeight+face/3*layout.Size+Border+(1-v)*(layout.Size-Border*2));
    }
    internal static void AddBoltStreaks(MeshGeometry3D mapped,IEnumerable<Point3D> sites,Vector3D down)
    {
        if(!DepthMaps.TryGetValue(mapped,out var depth))return;
        var layout=Layouts.GetValue(mapped,_=>new Layout(new[]{mapped.Bounds}));
        int Width=layout.Width,Height=layout.Height,Tile=layout.Size;
        var mask=new float[Width*Height];
        foreach(var site in sites) for(int face=0;face<layout.Bounds.Length*6;face++)
        {
            var bounds=layout.Bounds[face/6];
            var p=Project(site,bounds,face,layout);
            int tileX=face/6%layout.Columns*layout.PageWidth+face%6%3*Tile,tileY=face/6/layout.Columns*layout.PageHeight+face%6/3*Tile;
            int px=(int)p.X,py=(int)p.Y;
            if(px<tileX+Border||px>=tileX+Tile-Border||py<tileY+Border||py>=tileY+Tile-Border)continue;
            float skin=depth[py*Width+px];
            // A bolt is relevant only when it actually lies on this surface.
            if(float.IsNegativeInfinity(skin)||Math.Abs(skin-Depth(site,face))>.009)continue;
            var end=Project(site+down*.022,bounds,face,layout); var delta=end-p;
            double length=Math.Clamp(delta.Length,3,22); if(delta.Length<.1)continue;
            delta.Normalize();
            for(int step=0;step<=length;step++)
            {
                var centre=p+delta*step; double radius=step<2?2.2:1.25;
                for(int y=(int)centre.Y-3;y<=(int)centre.Y+3;y++)for(int x=(int)centre.X-3;x<=(int)centre.X+3;x++)
                {
                    if(x<tileX+Border||x>=tileX+Tile-Border||y<tileY+Border||y>=tileY+Tile-Border)continue;
                    if(float.IsNegativeInfinity(depth[y*Width+x]))continue;
                    double distance=Math.Sqrt(Math.Pow(x+.5-centre.X,2)+Math.Pow(y+.5-centre.Y,2));
                    float value=(float)((1-Smooth(.65,radius,distance))*(1-step/(length+1)));
                    mask[y*Width+x]=Math.Max(mask[y*Width+x],value);
                }
            }
        }
        BoltMaps.Remove(mapped); BoltMaps.Add(mapped,mask);
    }

    private static double Hash(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 69069));
        h = unchecked((h ^ (h >> 13)) * 1274126177); return (h ^ (h >> 16)) / (double)uint.MaxValue;
    }
    private static double Noise(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y); double u = x - ix, v = y - iy;
        u *= u * (3 - 2 * u); v *= v * (3 - 2 * v);
        double a = Hash(ix, iy, seed) * (1 - u) + Hash(ix + 1, iy, seed) * u;
        double b = Hash(ix, iy + 1, seed) * (1 - u) + Hash(ix + 1, iy + 1, seed) * u;
        return a * (1 - v) + b * v;
    }
}
