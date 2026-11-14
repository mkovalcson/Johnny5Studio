using System.Numerics;

namespace Johnny5VR;

internal static partial class CurvedSurfaces
{
    // Same 35 degree crease, 0.1 micrometre weld and corner-angle weights
    // as the editor. Only shading changes: positions and topology are retained.
    internal static Triangle[] Smooth(Triangle[] source,double creaseDegrees=35)
    {
        var normals=new Vector3[source.Length];
        var corners=new Dictionary<(long,long,long),List<(int Face,float Weight)>>();
        static (long,long,long) Key(Vector3 p)=>((long)Math.Round(p.X*1e7),(long)Math.Round(p.Y*1e7),(long)Math.Round(p.Z*1e7));
        for(int i=0;i<source.Length;i++)
        {
            var t=source[i];var n=Vector3.Cross(t.B-t.A,t.C-t.A);
            if(n.LengthSquared()<1e-24f){normals[i]=UrdfReader.SafeNormal(t.NA);continue;}
            normals[i]=Vector3.Normalize(n);
            foreach(var (p,a,b) in new[]{(t.A,t.B,t.C),(t.B,t.C,t.A),(t.C,t.A,t.B)})
            {
                float angle=MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a-p),Vector3.Normalize(b-p)),-1,1));
                var key=Key(p);if(!corners.TryGetValue(key,out var list))corners[key]=list=[];
                list.Add((i,angle));
            }
        }
        float threshold=(float)Math.Cos(creaseDegrees*Math.PI/180);
        Vector3 Normal(int face,Vector3 p)
        {
            var sum=Vector3.Zero;
            if(corners.TryGetValue(Key(p),out var neighbors))
                foreach(var item in neighbors)if(Vector3.Dot(normals[face],normals[item.Face])>=threshold)sum+=normals[item.Face]*item.Weight;
            return sum.LengthSquared()>1e-24f?Vector3.Normalize(sum):normals[face];
        }
        var result=new Triangle[source.Length];
        for(int i=0;i<source.Length;i++){var t=source[i];result[i]=new(t.A,t.B,t.C,Normal(i,t.A),Normal(i,t.B),Normal(i,t.C));}
        return result;
    }
}
