using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using Device=SharpDX.Direct3D11.Device;
namespace Johnny5VR;
internal sealed partial class Renderer
{
    private SwapChain? cleanSwap;
    private Texture2D? cleanBack;
    private RenderTargetView? cleanBackView;
    private Target? cleanTarget;
    private ShaderResourceView? cleanSource;
    private VertexShader? cleanVertex;
    private PixelShader? cleanPixel;
    private SamplerState? cleanSampler;
    internal bool CleanOutputActive=>cleanSwap!=null;
    internal string CleanOutputFailure {get;private set;}="";
    internal void ConfigureCleanOutput(IntPtr window,int width,int height,int renderWidth,int renderHeight)
    {
        DisableCleanOutput();CleanOutputFailure="";
        try
        {
            if(cleanVertex==null)
            {
                const string source="""
                Texture2D image:register(t0); SamplerState linearSampler:register(s0);
                struct Pixel {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
                Pixel VS(uint id:SV_VertexID){Pixel o;float2 uv=float2((id<<1)&2,id&2);o.position=float4(uv.x*2-1,1-uv.y*2,0,1);o.uv=uv;return o;}
                float4 PS(Pixel p):SV_TARGET{return image.Sample(linearSampler,p.uv);}
                """;
                using var vs=ShaderBytecode.Compile(source,"VS","vs_5_0");using var ps=ShaderBytecode.Compile(source,"PS","ps_5_0");
                cleanVertex=new(device,vs);cleanPixel=new(device,ps);
                cleanSampler=new(device,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue});
            }
            using var factory=new Factory1();
            cleanSwap=new(factory,device,new SwapChainDescription{BufferCount=2,ModeDescription=new(Math.Max(1,width),Math.Max(1,height),new Rational(0,1),Format.R8G8B8A8_UNorm),IsWindowed=true,OutputHandle=window,SampleDescription=new(1,0),SwapEffect=SwapEffect.Discard,Usage=Usage.RenderTargetOutput});
            factory.MakeWindowAssociation(window,WindowAssociationFlags.IgnoreAltEnter);
            cleanTarget=new(device,renderWidth,renderHeight,samples);cleanSource=new(device,cleanTarget.Resolved);
            cleanBack=Texture2D.FromSwapChain<Texture2D>(cleanSwap,0);cleanBackView=new(device,cleanBack);
        }
        catch{DisableCleanOutput();throw;}
    }
    internal void ResizeCleanOutput(int width,int height)
    {
        if(cleanSwap==null||width<1||height<1)return;
        context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);
        cleanBackView?.Dispose();cleanBack?.Dispose();cleanBackView=null;cleanBack=null;
        cleanSwap.ResizeBuffers(2,width,height,Format.R8G8B8A8_UNorm,SwapChainFlags.None);
        cleanBack=Texture2D.FromSwapChain<Texture2D>(cleanSwap,0);cleanBackView=new(device,cleanBack);
    }
    private void BlitCleanOutput()
    {
        if(cleanTarget==null||cleanBack==null)return;
        var fit=CleanOutputDisplay.Fit(cleanTarget.Width,cleanTarget.Height,cleanBack.Description.Width,cleanBack.Description.Height);
        context.OutputMerger.SetTargets((DepthStencilView?)null,cleanBackView);context.OutputMerger.SetBlendState(null);context.OutputMerger.SetDepthStencilState(solidDepth);
        context.ClearRenderTargetView(cleanBackView,new SharpDX.Mathematics.Interop.RawColor4(0,0,0,1));
        context.Rasterizer.State=raster;context.Rasterizer.SetViewport(fit.X,fit.Y,fit.Width,fit.Height);
        context.InputAssembler.InputLayout=null;context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
        context.VertexShader.Set(cleanVertex);context.PixelShader.Set(cleanPixel);context.PixelShader.SetShaderResource(0,cleanSource);context.PixelShader.SetSampler(0,cleanSampler);
        context.Draw(3,0);context.PixelShader.SetShaderResource(0,null);context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);
    }
    private void PresentMirror(bool vr)
    {
        if(cleanSwap!=null)
        {
            try{BlitCleanOutput();cleanSwap.Present(vr?0:1,PresentFlags.None);}
            catch(SharpDX.SharpDXException error){Diagnostics.Log(error);CleanOutputFailure=error.Message;DisableCleanOutput();}
        }
        else{context.CopyResource(desktop!.Resolved,back!);swap.Present(vr?0:1,PresentFlags.None);}
    }
    internal void CheckCleanOutputPixels()
    {
        if(cleanTarget==null||cleanBack==null)throw new InvalidOperationException("Clean output not configured.");
        context.ClearRenderTargetView(cleanTarget.ColorView,new SharpDX.Mathematics.Interop.RawColor4(.8f,.2f,.1f,1));
        if(samples>1)context.ResolveSubresource(cleanTarget.Color,0,cleanTarget.Resolved,0,Format.R8G8B8A8_UNorm);
        BlitCleanOutput();
        var desc=cleanBack.Description;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;desc.Usage=ResourceUsage.Staging;desc.OptionFlags=ResourceOptionFlags.None;
        using var staging=new Texture2D(device,desc);context.CopyResource(cleanBack,staging);var mapped=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
        try
        {
            byte Byte(int x,int y,int channel)=>System.Runtime.InteropServices.Marshal.ReadByte(mapped.DataPointer,y*mapped.RowPitch+x*4+channel);
            int x=desc.Width/2,y=desc.Height/2;
            if(Math.Abs(Byte(x,y,0)-204)>2||Math.Abs(Byte(x,y,1)-51)>2||Math.Abs(Byte(x,y,2)-26)>2)throw new InvalidOperationException("GPU clean output image copy has wrong pixels.");
            var fit=CleanOutputDisplay.Fit(cleanTarget.Width,cleanTarget.Height,desc.Width,desc.Height);
            if(fit.Y>0&&(Byte(0,0,0)!=0||Byte(0,0,1)!=0||Byte(0,0,2)!=0))throw new InvalidOperationException("Letterbox bars are not black.");
        }
        finally{context.UnmapSubresource(staging,0);}
    }
    internal void DisableCleanOutput()
    {
        ClearCapturedOverlays();
        context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);context.PixelShader.SetShaderResource(0,null);
        cleanSource?.Dispose();cleanTarget?.Dispose();cleanBackView?.Dispose();cleanBack?.Dispose();cleanSwap?.Dispose();
        cleanSource=null;cleanTarget=null;cleanBackView=null;cleanBack=null;cleanSwap=null;
    }
}
