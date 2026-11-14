using System.Globalization;
using System.Numerics;

namespace Johnny5VR;

internal static class MeshFile
{
    internal static Triangle[] Read(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("A model mesh is missing.",path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".stl"=>Stl(path), ".obj"=>Obj(path),
            _=>throw new NotSupportedException("Unsupported mesh format: "+path)
        };
    }
    private static Triangle[] Stl(string path)
    {
        using var reader=new BinaryReader(File.OpenRead(path));
        if(reader.BaseStream.Length>=84)
        {
            reader.BaseStream.Position=80;uint count=reader.ReadUInt32();
            if(84L+50L*count==reader.BaseStream.Length)
            {
                var triangles=new Triangle[checked((int)count)];
                Vector3 V()=>new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                for(int i=0;i<triangles.Length;i++)
                {
                    var n=V();var a=V();var b=V();var c=V();reader.ReadUInt16();
                    if(!float.IsFinite(a.X+a.Y+a.Z+b.X+b.Y+b.Z+c.X+c.Y+c.Z))throw new InvalidDataException("Nonfinite STL vertices: "+path);
                    n=n.LengthSquared()<1e-14f?UrdfReader.SafeNormal(Vector3.Cross(b-a,c-a)):UrdfReader.SafeNormal(n);
                    triangles[i]=new(a,b,c,n,n,n);
                }
                return triangles;
            }
        }
        var vertices=new List<Vector3>();var result=new List<Triangle>();
        foreach(string line in File.ReadLines(path))
        {
            string s=line.Trim();if(!s.StartsWith("vertex ",StringComparison.OrdinalIgnoreCase))continue;
            vertices.Add(UrdfReader.Vec(s[7..],Vector3.Zero));
            if(vertices.Count==3){result.Add(Shapes.Tri(vertices[0],vertices[1],vertices[2]));vertices.Clear();}
        }
        if(vertices.Count!=0||result.Count==0)throw new InvalidDataException("Invalid STL: "+path);
        return result.ToArray();
    }
    private static Triangle[] Obj(string path)
    {
        var positions=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<Triangle>();
        foreach(string line in File.ReadLines(path))
        {
            var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(fields.Length==0||fields[0].StartsWith('#'))continue;
            if(fields[0] is "v" or "vn")
            {
                var v=UrdfReader.Vec(string.Join(' ',fields.Skip(1).Take(3)),Vector3.Zero);
                if(fields[0]=="v")positions.Add(v);else normals.Add(v);
            }
            else if(fields[0]=="f")
            {
                (Vector3 P,Vector3 N) Corner(string text)
                {
                    var a=text.Split('/');int Index(string s,int size){int i=int.Parse(s,CultureInfo.InvariantCulture);return i>0?i-1:size+i;}
                    return (positions[Index(a[0],positions.Count)],a.Length>2&&a[2]!=""?normals[Index(a[2],normals.Count)]:Vector3.Zero);
                }
                for(int i=2;i<fields.Length-1;i++)
                {
                    var a=Corner(fields[1]);var b=Corner(fields[i]);var c=Corner(fields[i+1]);
                    var n=UrdfReader.SafeNormal(Vector3.Cross(b.P-a.P,c.P-a.P));
                    triangles.Add(new(a.P,b.P,c.P,a.N==Vector3.Zero?n:a.N,b.N==Vector3.Zero?n:b.N,c.N==Vector3.Zero?n:c.N));
                }
            }
        }
        if(triangles.Count==0)throw new InvalidDataException("OBJ has no faces: "+path);
        return triangles.ToArray();
    }
}

internal static class Shapes
{
    internal static Triangle Tri(Vector3 a,Vector3 b,Vector3 c)
    {var n=UrdfReader.SafeNormal(Vector3.Cross(b-a,c-a));return new(a,b,c,n,n,n);}
    internal static Triangle[] Box(Vector3 size)
    {
        var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=new((i&1)==0?-size.X/2:size.X/2,(i&2)==0?-size.Y/2:size.Y/2,(i&4)==0?-size.Z/2:size.Z/2);
        int[] ix=[0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5];
        return Enumerable.Range(0,ix.Length/3).Select(i=>Tri(p[ix[3*i]],p[ix[3*i+1]],p[ix[3*i+2]])).ToArray();
    }
    internal static Triangle[] Cylinder(float radius,float length)
    {
        const int count=48;var result=new List<Triangle>();
        for(int i=0;i<count;i++)
        {
            float a=i*MathF.Tau/count,b=(i+1)*MathF.Tau/count;
            var n=new Vector3(MathF.Cos(a),MathF.Sin(a),0);var m=new Vector3(MathF.Cos(b),MathF.Sin(b),0);
            var p=n*radius+new Vector3(0,0,-length/2);var q=m*radius+new Vector3(0,0,-length/2);
            var r=p+new Vector3(0,0,length);var s=q+new Vector3(0,0,length);
            result.Add(new(p,q,s,n,m,m));result.Add(new(p,s,r,n,m,n));
            result.Add(Tri(new(0,0,-length/2),q,p));result.Add(Tri(new(0,0,length/2),r,s));
        }
        return result.ToArray();
    }
    internal static Triangle[] Sphere(float radius)
    {
        var result=new List<Triangle>();
        Vector3 P(int x,int y){float a=x*MathF.Tau/32,b=y*MathF.PI/16;return new(MathF.Sin(b)*MathF.Cos(a),MathF.Sin(b)*MathF.Sin(a),MathF.Cos(b));}
        for(int y=0;y<16;y++)for(int x=0;x<32;x++)
        {
            var a=P(x,y);var b=P(x+1,y);var c=P(x+1,y+1);var d=P(x,y+1);
            if(y>0)result.Add(new(a*radius,b*radius,c*radius,a,b,c));
            if(y<15)result.Add(new(a*radius,c*radius,d*radius,a,c,d));
        }
        return result.ToArray();
    }
}
