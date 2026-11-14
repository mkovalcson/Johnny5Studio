using System.Numerics;
using Johnny5VR.Lighting;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;

namespace Johnny5VR;

internal sealed partial class Renderer
{
    private Buffer? rgbColors;
    private ShaderResourceView? rgbView;
    private RgbRingFrame? uploadedRgb;
    private bool rgbLoaded;
    private float eyeIntensity=1,ventIntensity=1;
    private void UpdateRgb(RgbRingFrame? frame)
    {
        if(rgbLoaded&&ReferenceEquals(frame,uploadedRgb))return;
        if(rgbColors==null){rgbColors=new(device,new BufferDescription(68*16,ResourceUsage.Default,BindFlags.ShaderResource,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,16));rgbView=new(device,rgbColors);}
        var colors=new Vector4[68];
        if(frame!=null)
        {
            var rings=new[]{frame.LeftEye,frame.LeftVent,frame.RightEye,frame.RightVent};
            for(int ring=0;ring<4;ring++)
            {
                Vector3 sum=Vector3.Zero;float maximum=0;int lit=0;
                for(int pixel=0;pixel<Math.Min(16,rings[ring].Length);pixel++)
                {
                    var c=rings[ring][pixel];var raw=new Vector3(c.R,c.G,c.B)/255;float level=Math.Max(raw.X,Math.Max(raw.Y,raw.Z));colors[ring*16+pixel]=new(raw,level);
                    if(level>2/255f){sum+=raw;lit++;maximum=Math.Max(maximum,level);}
                }
                int pooled=ring switch{0=>64,1=>66,2=>65,_=>67};
                if(lit>0){var average=sum/lit;average/=Math.Max(.0001f,Math.Max(average.X,Math.Max(average.Y,average.Z)));colors[pooled]=new(average*maximum,maximum);}
            }
        }
        context.UpdateSubresource(colors,rgbColors);uploadedRgb=frame;rgbLoaded=true;
    }
}
