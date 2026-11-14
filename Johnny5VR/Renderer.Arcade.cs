using System.Numerics;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;
namespace Johnny5VR;
internal sealed partial class Renderer
{
    private Buffer? arcadeVertices,arcadeTargets;private Vertex[]? arcadeSource;private int arcadeCount,arcadeCapacity,arcadeTargetCount;
    internal void UpdateArcade(Vertex[] targets,Vertex[] data)
    {
        if(!ReferenceEquals(arcadeSource,targets)){arcadeTargets?.Dispose();arcadeTargets=targets.Length>0?Buffer.Create(device,BindFlags.VertexBuffer,targets):null;arcadeTargetCount=targets.Length;arcadeSource=targets;}
        arcadeCount=data.Length;if(arcadeCount==0)return;
        if(arcadeVertices==null||arcadeCapacity<arcadeCount)
        {arcadeVertices?.Dispose();arcadeCapacity=Math.Max(65536,arcadeCount);arcadeVertices=new(device,new BufferDescription(arcadeCapacity*Vertex.Stride,ResourceUsage.Dynamic,BindFlags.VertexBuffer,CpuAccessFlags.Write,ResourceOptionFlags.None,0));}
        var map=context.MapSubresource(arcadeVertices,0,MapMode.WriteDiscard,MapFlags.None);
        try{SharpDX.Utilities.Write(map.DataPointer,data,0,data.Length);}finally{context.UnmapSubresource(arcadeVertices,0);}
    }
    private void DrawArcade(ref Frame frame)
    {
        if(arcadeTargetCount==0&&arcadeCount==0)return;
        frame.Flags=new(0,0,2,0);context.UpdateSubresource(ref frame,constants);context.OutputMerger.SetDepthStencilState(solidDepth);context.OutputMerger.SetBlendState(null);
        if(arcadeTargets!=null){context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(arcadeTargets,Vertex.Stride,0));context.Draw(arcadeTargetCount,0);}
        if(arcadeVertices!=null&&arcadeCount>0){context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(arcadeVertices,Vertex.Stride,0));context.Draw(arcadeCount,0);}
    }
}
