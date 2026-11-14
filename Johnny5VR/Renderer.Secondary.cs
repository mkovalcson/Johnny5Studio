using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;

namespace Johnny5VR;

internal sealed partial class Renderer
{
    private readonly List<(int Side,Buffer[] Frames,int[] Counts)> treadBuffers=[];
    private readonly int[] treadPhases=new int[2];
    private float mouthLevel;
    private void ClearTreads(){foreach(var layer in treadBuffers)foreach(var frame in layer.Frames)frame.Dispose();treadBuffers.Clear();}
    private void UploadTreads(TreadFrameSet[] layers)
    {
        ClearTreads();
        foreach(var layer in layers)
        {
            var frames=new List<Buffer>();
            try{foreach(var vertices in layer.Frames)frames.Add(Buffer.Create(device,BindFlags.VertexBuffer,vertices));}
            catch{foreach(var frame in frames)frame.Dispose();throw;}
            treadBuffers.Add((layer.Side,frames.ToArray(),layer.Frames.Select(f=>f.Length).ToArray()));
        }
        Array.Clear(treadPhases);
    }
    internal void UpdateSecondary(AnimationRig rig,double amplitude,Johnny5VR.Lighting.RgbRingFrame? rgb=null)
    {
        mouthLevel=(float)(Math.Round(Math.Clamp(amplitude*rig.MouthGain,0,1)*128)/128);
        Array.Copy(rig.TreadPhases,treadPhases,2);
        eyeIntensity=(float)rig.EyeIntensity;ventIntensity=(float)rig.VentIntensity;UpdateRgb(rgb);
    }
    private void DrawTreads()
    {
        foreach(var layer in treadBuffers)
        {
            int phase=treadPhases[layer.Side]%layer.Frames.Length;
            context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(layer.Frames[phase],Vertex.Stride,0));context.Draw(layer.Counts[phase],0);
        }
    }
}
