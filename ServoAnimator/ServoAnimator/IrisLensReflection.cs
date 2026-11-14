using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>Rounded reflections of the robot's actual scene lights, shared by
/// WPF and DX11. No synthetic panels or backdrop/video readback.</summary>
internal sealed class IrisLensReflection
{
    private const int TextureSize=128;
    private readonly record struct Texel(int Offset,Vector3D Normal,double Edge);
    private readonly record struct Highlight(Vector3D Half,Vector3D Color);
    private static readonly Texel[] Texels=CreateTexels();
    private readonly byte[] _pixels=new byte[TextureSize*TextureSize*4];
    internal ImageBrush Brush { get; } = new();
    internal SolidColorBrush SpecularBrush { get; } = new(Color.FromArgb(170,245,252,255));
    private Vector3D _view, _u, _v, _w, _edgeColor, _ambientColor;
    private Highlight[] _lights=Array.Empty<Highlight>();
    private bool _initialized;
    private double _reflectivity = 1;
    internal int Updates { get; private set; }

    internal IrisLensReflection() => Update(new Vector3D(1,-.15,.15),
        new Vector3D(0,0,-1),new Vector3D(0,1,0),new Vector3D(1,0,0));

    internal bool Update(Vector3D view, Vector3D u, Vector3D v, Vector3D w,
        IEnumerable<Light> sources=null,Point3D position=default,double reflectivity=1)
    {
        reflectivity=double.IsFinite(reflectivity)?Math.Clamp(reflectivity,0,2):1;
        if(view.LengthSquared<1e-12) return false;
        view.Normalize();u.Normalize();v.Normalize();w.Normalize();
        var highlights=new List<Highlight>();
        var edgeColor=new Vector3D();
        var ambientColor=new Vector3D();
        foreach(var source in sources??Enumerable.Empty<Light>())
        {
            var c=source.Color;
            var color=new Vector3D(c.ScR,c.ScG,c.ScB)*c.ScA;
            if(source is AmbientLight){ambientColor+=color;edgeColor+=color*.35;continue;}
            Vector3D toLight;double attenuation=1;
            if(source is DirectionalLight directional)
                toLight=-directional.Transform.Transform(directional.Direction);
            else if(source is PointLight point)
            {
                toLight=point.Transform.Transform(point.Position)-position;
                double distance=toLight.Length;
                if(distance>point.Range)continue;
                attenuation=1/Math.Max(.00001,point.ConstantAttenuation+
                    point.LinearAttenuation*distance+point.QuadraticAttenuation*distance*distance);
            }
            else continue;
            if(toLight.LengthSquared<1e-12)continue;
            toLight.Normalize();color*=attenuation;edgeColor+=color*.15;
            var half=toLight+view;if(half.LengthSquared<1e-12)continue;
            half.Normalize();highlights.Add(new Highlight(half,color));
        }
        bool sameLights=highlights.Count==_lights.Length&&(edgeColor-_edgeColor).LengthSquared<1e-10&&
            (ambientColor-_ambientColor).LengthSquared<1e-10;
        for(int i=0;sameLights&&i<highlights.Count;i++)
            sameLights=(highlights[i].Half-_lights[i].Half).LengthSquared<.000025&&
                (highlights[i].Color-_lights[i].Color).LengthSquared<1e-10;
        if(_initialized && reflectivity==_reflectivity && sameLights && (view-_view).LengthSquared+(u-_u).LengthSquared+
            (v-_v).LengthSquared+(w-_w).LengthSquared<.000025) return false;
        _view=view;_u=u;_v=v;_w=w;_edgeColor=edgeColor;_ambientColor=ambientColor;_lights=highlights.ToArray();_initialized=true;
        _reflectivity=reflectivity;
        // DX11's specular shader uses RGB independently of alpha. Black out
        // the RGB as well so Off suppresses native highlights in both engines.
        double tint=Math.Min(1,reflectivity);
        var specular=Color.FromArgb((byte)Math.Round(Math.Clamp(170*reflectivity,0,255)),
            (byte)Math.Round(245*tint),(byte)Math.Round(252*tint),(byte)Math.Round(255*tint));
        if(SpecularBrush.Color!=specular)SpecularBrush.Color=specular;
        foreach(var sample in Texels)
        {
            var normal=u*sample.Normal.X+v*sample.Normal.Y+w*sample.Normal.Z;
            double cosine=Math.Abs(Vector3D.DotProduct(normal,view));
            var glint=new Vector3D();
            foreach(var light in _lights)
            {
                // A glossy Blinn-Phong lobe gives a distinct rounded glint,
                // rather than diffuse-looking shading across the whole lens.
                double dot=Math.Max(0,Vector3D.DotProduct(normal,light.Half));
                double d2=dot*dot,d4=d2*d2,d8=d4*d4;
                double d16=d8*d8,d32=d16*d16;
                glint+=light.Color*(d32*d32);
            }
            double f=1-Math.Clamp(cosine,0,1),f2=f*f;
            double fresnel=.04+.96*f2*f2*f;
            // Ambient radiance also reflects from glass. Its Fresnel response
            // follows the spherical normals, revealing the dome at grazing
            // angles even when a direct-light glint falls outside the lens.
            var reflected=glint*(1+fresnel*.75)+ambientColor*(5*fresnel)+edgeColor*sample.Edge;
            double energy=Math.Max(reflected.X,Math.Max(reflected.Y,reflected.Z));
            int i=sample.Offset;
            _pixels[i]=(byte)Math.Round(Math.Clamp(reflected.Z/Math.Max(1e-12,energy),0,1)*255);
            _pixels[i+1]=(byte)Math.Round(Math.Clamp(reflected.Y/Math.Max(1e-12,energy),0,1)*255);
            _pixels[i+2]=(byte)Math.Round(Math.Clamp(reflected.X/Math.Max(1e-12,energy),0,1)*255);
            _pixels[i+3]=(byte)Math.Round(Math.Clamp(Math.Clamp(energy,0,.65)*reflectivity,0,1)*255);
        }
        var image=BitmapSource.Create(TextureSize,TextureSize,96,96,PixelFormats.Bgra32,null,_pixels,TextureSize*4);
        image.Freeze();Brush.ImageSource=image;Updates++;return true;
    }

    private static Texel[] CreateTexels()
    {
        var samples=new List<Texel>();
        for(int y=0;y<TextureSize;y++)for(int x=0;x<TextureSize;x++)
        {
            double px=(x+.5)/TextureSize*2-1,py=(y+.5)/TextureSize*2-1;
            double radius=Math.Sqrt(px*px+py*py);if(radius>1)continue;
            var normal=PrimitiveMeshes.IrisLensNormal(PrimitiveMeshes.IrisLensRadius,
                PrimitiveMeshes.IrisLensRadius*px,PrimitiveMeshes.IrisLensRadius*py);
            double edge=Math.Clamp((radius-.92)/.08,0,1);
            samples.Add(new Texel((y*TextureSize+x)*4,normal,edge*edge*.20));
        }
        return samples.ToArray();
    }
}
