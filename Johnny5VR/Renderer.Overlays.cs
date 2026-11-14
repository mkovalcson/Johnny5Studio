using System.Numerics;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;
namespace Johnny5VR;
internal sealed partial class Renderer
{
    internal bool IncludeCleanOverlays {get;set;}
    internal string OverlayCaptureFailure {get;private set;}="";
    internal void ConfigureOverlayCapture(bool include){IncludeCleanOverlays=include;OverlayCaptureFailure="";if(!include)ClearCapturedOverlays();}
    private void TryDrawCapturedOverlays(Target target,Matrix4x4 projection,Vector3 head)
    {
        try{DrawCapturedOverlays(target,projection,head,OverlayCapture.Visible());}
        catch(Exception error){Diagnostics.Log(error);OverlayCaptureFailure=error.Message;IncludeCleanOverlays=false;ClearCapturedOverlays();}
    }
    private sealed class CapturedTexture(Texture2D texture,ShaderResourceView view):IDisposable
    {internal readonly Texture2D Texture=texture;internal readonly ShaderResourceView View=view;public void Dispose(){View.Dispose();Texture.Dispose();}}
    private readonly Dictionary<long,CapturedTexture> capturedTextures=[];
    private VertexShader? captureVertex;
    private PixelShader? capturePixel;
    private Buffer? captureTransform;
    private DepthStencilState? captureDepth;
    private SamplerState? captureSampler;
    private RenderTargetView? captureDestination;
    private IntPtr captureDestinationTexture;
    private void DrawCapturedOverlays(Target target,Matrix4x4 projection,Vector3 head,CapturedOverlay[] overlays)
    {
        var used=overlays.Select(o=>o.Image.Id).ToHashSet();
        foreach(long id in capturedTextures.Keys.Where(id=>!used.Contains(id)).ToArray()){capturedTextures[id].Dispose();capturedTextures.Remove(id);}
        if(overlays.Length==0)return;
        if(captureVertex==null)
        {
            const string source="""
            cbuffer Camera:register(b0){row_major float4x4 worldViewProjection;};
            Texture2D image:register(t0);SamplerState linearSampler:register(s0);
            struct Pixel {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
            Pixel VS(uint id:SV_VertexID){const float2 coords[6]={float2(0,0),float2(1,0),float2(0,1),float2(0,1),float2(1,0),float2(1,1)};Pixel p;p.uv=coords[id];p.position=mul(float4(p.uv.x-.5,.5-p.uv.y,0,1),worldViewProjection);return p;}
            float4 PS(Pixel p):SV_TARGET{return image.Sample(linearSampler,p.uv);}
            """;
            using var vs=ShaderBytecode.Compile(source,"VS","vs_5_0");using var ps=ShaderBytecode.Compile(source,"PS","ps_5_0");captureVertex=new(device,vs);capturePixel=new(device,ps);
            captureTransform=new(device,64,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            captureDepth=new(device,new DepthStencilStateDescription{IsDepthEnabled=false,DepthWriteMask=DepthWriteMask.Zero});
            captureSampler=new(device,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue});
        }
        if(captureDestinationTexture!=target.Resolved.NativePointer)
        {captureDestination?.Dispose();captureDestination=new(device,target.Resolved);captureDestinationTexture=target.Resolved.NativePointer;}
        context.OutputMerger.SetTargets((DepthStencilView?)null,captureDestination);context.OutputMerger.SetDepthStencilState(captureDepth);context.OutputMerger.SetBlendState(blend);
        context.Rasterizer.State=raster;context.Rasterizer.SetViewport(0,0,target.Width,target.Height);
        context.InputAssembler.InputLayout=null;context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;context.VertexShader.Set(captureVertex);context.VertexShader.SetConstantBuffer(0,captureTransform);context.PixelShader.Set(capturePixel);context.PixelShader.SetSampler(0,captureSampler);
        foreach(var panel in overlays.OrderBy(p=>p.Order).ThenByDescending(p=>Vector3.DistanceSquared(p.World.Translation,head)))
        {
            if(Vector3.Dot(head-panel.World.Translation,Vector3.TransformNormal(Vector3.UnitZ,panel.World))<=0)continue;
            if(!capturedTextures.TryGetValue(panel.Image.Id,out var texture))
            {
                var shared=device.OpenSharedResource<Texture2D>(panel.Image.SharedHandle);texture=new(shared,new ShaderResourceView(device,shared));capturedTextures.Add(panel.Image.Id,texture);
            }
            float height=panel.Width*panel.Image.Height/panel.Image.Width;
            var matrix=Matrix4x4.CreateScale(panel.Width,height,1)*panel.World*projection;context.UpdateSubresource(ref matrix,captureTransform);
            context.PixelShader.SetShaderResource(0,texture.View);context.Draw(6,0);
        }
        context.PixelShader.SetShaderResource(0,null);context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);context.OutputMerger.SetBlendState(null);
    }
    private void ClearCapturedOverlays()
    {
        context.PixelShader.SetShaderResource(0,null);captureDestination?.Dispose();captureDestination=null;captureDestinationTexture=IntPtr.Zero;
        foreach(var texture in capturedTextures.Values)texture.Dispose();capturedTextures.Clear();
    }
    private void DisposeCapturedOverlays(){ClearCapturedOverlays();captureVertex?.Dispose();capturePixel?.Dispose();captureTransform?.Dispose();captureDepth?.Dispose();captureSampler?.Dispose();}
}
