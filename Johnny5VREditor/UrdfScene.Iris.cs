using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private readonly Dictionary<string,IrisLensReflection> _irisLensReflections = new();

    internal void UpdateIrisLensReflections(Point3D camera, Transform3D presentation,
        IEnumerable<Light> lights,double reflectivity=1)
    {
        foreach(var (name,reflection) in _irisLensReflections)
        {
            string link=name.StartsWith("left_",StringComparison.Ordinal)?"left_pupil_link":"right_pupil_link";
            var visual=_visuals[name];
            Point3D World(Point3D point)
            {
                TryTransformLinkPoint(link,visual.Transform.Transform(point),out var world);
                return presentation?.Transform(world)??world;
            }
            var origin=World(new Point3D());
            reflection.Update(camera-origin,World(new Point3D(1,0,0))-origin,
                World(new Point3D(0,1,0))-origin,World(new Point3D(0,0,1))-origin,lights,origin,reflectivity);
        }
    }

    public void SetVisualIris(string name, double outerRadius, double innerRadius, double length)
    {
        if (!_visuals.TryGetValue(name, out var visual)) return;
        string side = name.StartsWith("left_", StringComparison.Ordinal) ? "left" : "right";
        visual.Geometry = _visuals.ContainsKey(side + "_iris_clear_lens")
            ? PrimitiveMeshes.IrisBlades(outerRadius, innerRadius, length)
            : PrimitiveMeshes.AnnularCylinder(outerRadius, innerRadius, length, 72);
    }
}

internal static partial class PrimitiveMeshes
{
    internal const int IrisBladeCount = 20;
    internal const double IrisLensRadius = .02286;
    internal const double IrisLensDomeHeight = .006;

    internal static double IrisLensSphereRadius(double radius) =>
        (radius*radius+IrisLensDomeHeight*IrisLensDomeHeight)/(2*IrisLensDomeHeight);

    internal static Vector3D IrisLensNormal(double radius,double x,double y)
    {
        double sphere=IrisLensSphereRadius(radius);
        return new Vector3D(x/sphere,y/sphere,Math.Sqrt(Math.Max(0,1-(x*x+y*y)/(sphere*sphere))));
    }

    /// <summary>The visible portions of twenty overlapping leaves. Their seams
    /// sweep as the opening changes; the circular inner edge retains the exact
    /// existing aperture range. This is a visual mechanism, not a fabrication jig.</summary>
    internal static MeshGeometry3D IrisBlades(double outerRadius, double innerRadius, double length)
    {
        var mesh = new MeshGeometry3D();
        double inner = Math.Clamp(innerRadius, 0.00001, outerRadius);
        if (outerRadius - inner < 0.0000001) return mesh;
        const int radialSteps = 8, angularSteps = 8;
        double step = 2 * Math.PI / IrisBladeCount;
        double twist = (1 - inner / outerRadius) * .95;
        // Hairline clearances and a shallow fold make leaf boundaries visible
        // without the chunky grooves of a segmented cylinder.
        double gap = Math.Min(.00010, (outerRadius - inner) * .04);
        Point3D P(int blade, double s, double t, bool front)
        {
            double radius = inner + (outerRadius - inner) * s;
            double angle = step * (blade + t) + twist * s + (t < .5 ? 1 : -1) * gap / (2 * radius);
            double z = front ? length / 2 - .00012 * Math.Sin(Math.PI * t) * s : length / 2 - .00020;
            return new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), z);
        }
        void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var n = Vector3D.CrossProduct(b - a, c - a); n.Normalize();
            AddFace(mesh, a, b, c, d, n);
        }
        for (int blade = 0; blade < IrisBladeCount; blade++)
        {
            for (int j = 0; j < radialSteps; j++)
            for (int k = 0; k < angularSteps; k++)
            {
                double s0 = (double)j / radialSteps, s1 = (double)(j + 1) / radialSteps;
                double t0 = (double)k / angularSteps, t1 = (double)(k + 1) / angularSteps;
                Quad(P(blade,s0,t0,true), P(blade,s1,t0,true), P(blade,s1,t1,true), P(blade,s0,t1,true));
                Quad(P(blade,s0,t1,false), P(blade,s1,t1,false), P(blade,s1,t0,false), P(blade,s0,t0,false));
            }
            for (int k = 0; k < angularSteps; k++)
            {
                double t0 = (double)k / angularSteps, t1 = (double)(k+1) / angularSteps;
                Quad(P(blade,0,t0,false),P(blade,0,t0,true),P(blade,0,t1,true),P(blade,0,t1,false));
                Quad(P(blade,1,t1,false),P(blade,1,t1,true),P(blade,1,t0,true),P(blade,1,t0,false));
            }
            for (int j = 0; j < radialSteps; j++)
            {
                double s0 = (double)j / radialSteps, s1 = (double)(j+1) / radialSteps;
                Quad(P(blade,s0,0,false),P(blade,s1,0,false),P(blade,s1,0,true),P(blade,s0,0,true));
                Quad(P(blade,s1,1,false),P(blade,s0,1,false),P(blade,s0,1,true),P(blade,s1,1,true));
            }
        }
        // Mutable meshes let the DX mirror discard the previous aperture's
        // buffers immediately instead of retaining frozen geometry in its cache.
        return mesh;
    }

    /// <summary>Shallow optical cover, local +Z faces out. Radius matches the
    /// blue iris, with a 6 mm spherical dome. Smooth normals avoid faceted glass.</summary>
    internal static MeshGeometry3D IrisLens(double radius)
    {
        var m = new MeshGeometry3D();
        double sphere=IrisLensSphereRadius(radius);
        const int rings = 20, sectors = 160;
        for (int j=0;j<=rings;j++)
        for (int k=0;k<=sectors;k++)
        {
            double r=radius*j/rings, a=2*Math.PI*k/sectors;
            double x=r*Math.Cos(a), y=r*Math.Sin(a);
            m.Positions.Add(new Point3D(x,y,Math.Sqrt(sphere*sphere-r*r)-(sphere-IrisLensDomeHeight)));
            m.TextureCoordinates.Add(new System.Windows.Point(.5+x/(2*radius),.5+y/(2*radius)));
            m.Normals.Add(IrisLensNormal(radius,x,y));
        }
        for(int j=0;j<rings;j++)for(int k=0;k<sectors;k++)
        {
            int a=j*(sectors+1)+k,b=a+sectors+1;
            if(j>0){m.TriangleIndices.Add(a);m.TriangleIndices.Add(b);m.TriangleIndices.Add(a+1);}
            m.TriangleIndices.Add(a+1);m.TriangleIndices.Add(b);m.TriangleIndices.Add(b+1);
        }
        m.Freeze(); return m;
    }
}
