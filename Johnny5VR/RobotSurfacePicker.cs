using System.Numerics;
using Johnny5.VrBridge;
namespace Johnny5VR;

// Cache mesh-local bounds; reject empty space before intersecting actual triangles.
internal sealed class RobotSurfacePicker
{
    private sealed record Mesh(VrMesh Source,Vector3 Min,Vector3 Max);
    private sealed record Part(Mesh Mesh,Matrix4x4 Inverse);
    private readonly Dictionary<long,Mesh> meshes=[];
    private Part[] parts=[];
    internal void Clear(){meshes.Clear();parts=[];}
    internal void Update(VrFrame frame,Matrix4x4 placement)
    {
        foreach(var source in frame.Meshes)
        {
            if(meshes.TryGetValue(source.Id,out var prior)&&ReferenceEquals(prior.Source,source))continue;
            var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
            for(int i=0;i<source.Positions.Length;i+=3){var p=new Vector3(source.Positions[i],source.Positions[i+1],source.Positions[i+2]);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
            meshes[source.Id]=new(source,min,max);
        }
        var materials=frame.Materials.ToDictionary(m=>m.Id);
        parts=frame.Nodes.Where(n=>n.Robot&&materials[n.Material].Diffuse.W>.75f)
            .Select(n=>Matrix4x4.Invert(n.World*placement,out var inverse)?new Part(meshes[n.Mesh],inverse):null).OfType<Part>().ToArray();
        if(meshes.Count>20000){var active=frame.Nodes.Select(n=>n.Mesh).ToHashSet();foreach(var key in meshes.Keys.Where(k=>!active.Contains(k)).ToArray())meshes.Remove(key);}
    }
    internal Vector3? Hit(Vector3 from,Vector3 direction)
    {
        float nearest=8;
        var candidates=new List<(Part Part,Vector3 From,Vector3 Direction,float Entry)>();
        foreach(var part in parts)
        {
            var origin=Vector3.Transform(from,part.Inverse);var ray=Vector3.TransformNormal(direction,part.Inverse);
            if(Box(origin,ray,part.Mesh.Min,part.Mesh.Max,nearest) is {} entry)candidates.Add((part,origin,ray,entry));
        }
        bool found=false;
        foreach(var item in candidates.OrderBy(c=>c.Entry))
        {
            if(item.Entry>nearest)break;
            var mesh=item.Part.Mesh.Source;
            Vector3 P(int index){int at=index*3;return new(mesh.Positions[at],mesh.Positions[at+1],mesh.Positions[at+2]);}
            for(int i=0;i<mesh.Indices.Length;i+=3)
                if(Triangle(item.From,item.Direction,P(mesh.Indices[i]),P(mesh.Indices[i+1]),P(mesh.Indices[i+2])) is {} t&&t<nearest){nearest=t;found=true;}
        }
        return found?from+direction*nearest:null;
    }
    internal static float? Box(Vector3 from,Vector3 ray,Vector3 min,Vector3 max,float limit)
    {
        float near=0,far=limit;
        for(int i=0;i<3;i++)
        {
            if(Math.Abs(ray[i])<1e-8f){if(from[i]<min[i]||from[i]>max[i])return null;continue;}
            float a=(min[i]-from[i])/ray[i],b=(max[i]-from[i])/ray[i];if(a>b)(a,b)=(b,a);
            near=Math.Max(near,a);far=Math.Min(far,b);if(near>far)return null;
        }
        return near;
    }
    internal static float? Triangle(Vector3 from,Vector3 ray,Vector3 a,Vector3 b,Vector3 c)
    {
        var e1=b-a;var e2=c-a;var p=Vector3.Cross(ray,e2);float det=Vector3.Dot(e1,p);if(Math.Abs(det)<1e-9f)return null;
        float inverse=1/det;var s=from-a;float u=Vector3.Dot(s,p)*inverse;if(u<0||u>1)return null;
        var q=Vector3.Cross(s,e1);float v=Vector3.Dot(ray,q)*inverse;if(v<0||u+v>1)return null;
        float distance=Vector3.Dot(e2,q)*inverse;return distance>0?distance:null;
    }
}
