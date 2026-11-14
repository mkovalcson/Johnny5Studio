using System.Numerics;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;
namespace Johnny5VR;

internal sealed partial class Renderer
{
    private Buffer? effectVertices;
    private int effectCount;
    internal void UpdateEffects(IReadOnlyList<FlyingBolt> bolts)
    {
        var vertices=new List<Vertex>();
        foreach(var bolt in bolts)
        {
            var direction=bolt.Tip-bolt.Tail;float length=direction.Length();if(length<.00001f)continue;
            var aim=direction/length;var side=Vector3.Normalize(Vector3.Cross(Math.Abs(aim.Y)<.9?Vector3.UnitY:Vector3.UnitZ,aim));var up=Vector3.Cross(aim,side);
            void Tube(float radius,Vector4 color)
            {
                for(int i=0;i<10;i++)
                {
                    var a=(side*MathF.Cos(i*MathF.Tau/10)+up*MathF.Sin(i*MathF.Tau/10))*radius;
                    var b=(side*MathF.Cos((i+1)*MathF.Tau/10)+up*MathF.Sin((i+1)*MathF.Tau/10))*radius;
                    foreach(var p in new[]{bolt.Tail+a,bolt.Tip+a,bolt.Tail+b,bolt.Tail+b,bolt.Tip+a,bolt.Tip+b})vertices.Add(new(p,up,color));
                }
            }
            Tube(.011f,bolt.Color with{W=bolt.Color.W*.45f});Tube(.0045f,new(Vector3.Lerp(new(bolt.Color.X,bolt.Color.Y,bolt.Color.Z),Vector3.One,.7f),bolt.Color.W));
            if(bolt.Flash>0)
                foreach(var t in Shapes.Sphere(.03f*bolt.Flash))foreach(var p in new[]{t.A,t.B,t.C})vertices.Add(new(p+bolt.Muzzle,up,bolt.Color));
        }
        effectVertices?.Dispose();effectVertices=null;effectCount=vertices.Count;
        if(effectCount>0)effectVertices=Buffer.Create(device,BindFlags.VertexBuffer,vertices.ToArray());
    }
    private void DrawEffects(ref Frame frame)
    {
        if(effectVertices==null)return;
        frame.Flags=new(0,0,1,0);context.UpdateSubresource(ref frame,constants);
        context.OutputMerger.SetDepthStencilState(glassDepth);context.OutputMerger.SetBlendState(blend);
        context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(effectVertices,Vertex.Stride,0));context.Draw(effectCount,0);
    }
}
