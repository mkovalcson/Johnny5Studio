using System.Numerics;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;
namespace Johnny5VR;
internal sealed partial class Renderer
{
    private Buffer? gloveVertices;
    private int gloveCount,gloveCapacity;
    internal void UpdateGloves(GloveHand[] hands)
    {
        if(hands.Length==0){gloveCount=0;return;}
        var vertices=ControllerGlove.Build(hands);gloveCount=vertices.Length;if(gloveCount==0)return;
        if(gloveVertices==null||gloveCapacity<gloveCount)
        {
            gloveVertices?.Dispose();gloveCapacity=Math.Max(8192,gloveCount);
            gloveVertices=new(device,new BufferDescription(gloveCapacity*Vertex.Stride,ResourceUsage.Dynamic,BindFlags.VertexBuffer,CpuAccessFlags.Write,ResourceOptionFlags.None,0));
        }
        var mapped=context.MapSubresource(gloveVertices,0,MapMode.WriteDiscard,MapFlags.None);
        try{SharpDX.Utilities.Write(mapped.DataPointer,vertices,0,vertices.Length);}finally{context.UnmapSubresource(gloveVertices,0);}
    }
    internal void PreviewGloves(GloveHand[] hands,Matrix4x4 view,Vector3 eye)
    {
        if(desktop==null||back==null)throw new InvalidOperationException("Preview surface missing.");
        UpdateGloves(hands);Draw(desktop,view*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/4,(float)desktop.Width/desktop.Height,.01f,10),eye);
        context.CopyResource(desktop.Resolved,back);swap.Present(0,SharpDX.DXGI.PresentFlags.None);
    }
    private void DrawGloves(ref Frame frame)
    {
        if(gloveVertices==null||gloveCount==0)return;
        frame.Flags=new(0,0,2,0);context.UpdateSubresource(ref frame,constants);context.OutputMerger.SetDepthStencilState(solidDepth);context.OutputMerger.SetBlendState(null);
        context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(gloveVertices,Vertex.Stride,0));context.Draw(gloveCount,0);
    }
}
