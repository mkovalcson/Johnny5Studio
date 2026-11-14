using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Valve.VR;
using Buffer=SharpDX.Direct3D11.Buffer;
using Device=SharpDX.Direct3D11.Device;

namespace Johnny5VR;

internal sealed partial class Renderer:IDisposable
{
    private readonly Device device;
    private readonly DeviceContext context;
    private readonly SwapChain swap;
    private readonly VertexShader vertexShader;
    private readonly PixelShader pixelShader;
    private readonly InputLayout layout;
    private readonly Buffer constants;
    private Buffer? bones;
    private ShaderResourceView? boneView;
    private readonly RasterizerState raster;
    private readonly DepthStencilState solidDepth,glassDepth;
    private readonly BlendState blend;
    private Buffer? vertices,glassVertices;
    private int solidCount,glassCount;
    private Target? desktop,left,right;
    private Texture2D? back;
    private readonly int samples;
    internal string AdapterName {get;}
    [StructLayout(LayoutKind.Sequential)]
    private struct Frame {public Matrix4x4 ViewProjection;public System.Numerics.Vector4 Eye,Apertures,Flags,Ambient,Key,Light,Mouth;}
    internal System.Numerics.Vector4 Apertures;
    internal Renderer(IntPtr window,int width,int height,int adapterIndex=0)
    {
        using var factory=new Factory1();using var adapter=factory.GetAdapter1(adapterIndex);
        AdapterName=adapter.Description.Description.TrimEnd('\0');
        device=new Device(adapter,DeviceCreationFlags.BgraSupport,FeatureLevel.Level_11_0);
        context=device.ImmediateContext;
        swap=new SwapChain(factory,device,new SwapChainDescription
        {
            BufferCount=2,ModeDescription=new ModeDescription(Math.Max(1,width),Math.Max(1,height),new Rational(0,1),Format.R8G8B8A8_UNorm),
            IsWindowed=true,OutputHandle=window,SampleDescription=new SampleDescription(1,0),SwapEffect=SwapEffect.Discard,Usage=Usage.RenderTargetOutput
        });
        factory.MakeWindowAssociation(window,WindowAssociationFlags.IgnoreAltEnter);
        string shader=Path.Combine(AppContext.BaseDirectory,"Shaders/Scene.hlsl");
        using var vs=ShaderBytecode.CompileFromFile(shader,"VS","vs_5_0",ShaderFlags.OptimizationLevel3);
        using var ps=ShaderBytecode.CompileFromFile(shader,"PS","ps_5_0",ShaderFlags.OptimizationLevel3);
        vertexShader=new(device,vs);pixelShader=new(device,ps);
        layout=new(device,ShaderSignature.GetInputSignature(vs),[
            new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),new InputElement("NORMAL",0,Format.R32G32B32_Float,12,0),
            new InputElement("COLOR",0,Format.R32G32B32A32_Float,24,0),new InputElement("BLENDINDICES",0,Format.R32_UInt,40,0),
            new InputElement("TEXCOORD",0,Format.R32G32B32_Float,44,0),new InputElement("TEXCOORD",1,Format.R32_UInt,56,0),new InputElement("TEXCOORD",2,Format.R32G32_Float,60,0),new InputElement("TEXCOORD",3,Format.R32_UInt,(int)Marshal.OffsetOf<Vertex>(nameof(Vertex.Mouth)),0)]);
        constants=new(device,Marshal.SizeOf<Frame>(),ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
        raster=new(device,new RasterizerStateDescription{FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=true,IsMultisampleEnabled=true});
        solidDepth=new(device,new DepthStencilStateDescription{IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.LessEqual});
        glassDepth=new(device,new DepthStencilStateDescription{IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.Zero,DepthComparison=Comparison.LessEqual});
        var desc=new BlendStateDescription();desc.RenderTarget[0]=new RenderTargetBlendDescription
        {IsBlendEnabled=true,SourceBlend=BlendOption.SourceAlpha,DestinationBlend=BlendOption.InverseSourceAlpha,BlendOperation=BlendOperation.Add,
            SourceAlphaBlend=BlendOption.One,DestinationAlphaBlend=BlendOption.InverseSourceAlpha,AlphaBlendOperation=BlendOperation.Add,RenderTargetWriteMask=ColorWriteMaskFlags.All};
        blend=new(device,desc);
        samples=device.CheckMultisampleQualityLevels(Format.R8G8B8A8_UNorm,4)>0&&device.CheckMultisampleQualityLevels(Format.D32_Float,4)>0?4:1;
        DefaultSurfaces();Resize(width,height);
    }
    internal void Upload(SceneData scene)
    {
        ClearLive();DefaultSurfaces();
        // Build replacements first, retaining the last usable scene on allocation failure.
        var newSolid=Buffer.Create(device,BindFlags.VertexBuffer,scene.Opaque);
        Buffer? newGlass=null;
        try{if(scene.Glass.Length>0)newGlass=Buffer.Create(device,BindFlags.VertexBuffer,scene.Glass);}
        catch{newSolid.Dispose();throw;}
        vertices?.Dispose();glassVertices?.Dispose();vertices=newSolid;glassVertices=newGlass;
        solidCount=scene.Opaque.Length;glassCount=scene.Glass.Length;
        boneView?.Dispose();bones?.Dispose();
        var palette=scene.Rig?.Palette??[Matrix4x4.Identity];
        bones=new Buffer(device,new BufferDescription(palette.Length*64,ResourceUsage.Default,BindFlags.ShaderResource,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,64));
        boneView=new ShaderResourceView(device,bones);UpdatePose(palette);
        UploadTreads(scene.Treads);mouthLevel=0;
    }
    internal void UpdatePose(Matrix4x4[] palette){if(bones!=null)context.UpdateSubresource(palette,bones);}
    internal void Resize(int width,int height)
    {
        if(width<1||height<1)return;
        context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);
        desktop?.Dispose();back?.Dispose();desktop=null;back=null;
        swap.ResizeBuffers(2,width,height,Format.R8G8B8A8_UNorm,SwapChainFlags.None);
        back=Texture2D.FromSwapChain<Texture2D>(swap,0);desktop=new(device,width,height,samples);
    }
    private void Draw(Target target,Matrix4x4 viewProjection,System.Numerics.Vector3 eye)
    {
        context.OutputMerger.SetTargets(target.DepthView,target.ColorView);
        context.Rasterizer.SetViewport(0,0,target.Width,target.Height);context.Rasterizer.State=raster;
        context.ClearRenderTargetView(target.ColorView,new SharpDX.Mathematics.Interop.RawColor4(.075f,.10f,.14f,1));
        context.ClearDepthStencilView(target.DepthView,DepthStencilClearFlags.Depth,1,0);
        context.InputAssembler.InputLayout=layout;context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
        context.VertexShader.Set(vertexShader);context.PixelShader.Set(pixelShader);
        var frame=new Frame{ViewProjection=viewProjection,Eye=new(eye,1),Apertures=Apertures,Mouth=new(mouthLevel,eyeIntensity,ventIntensity,0)};context.UpdateSubresource(ref frame,constants);
        context.VertexShader.SetShaderResource(0,boneView);
        context.PixelShader.SetShaderResource(1,surfaceView);context.PixelShader.SetShaderResource(2,textureView);context.PixelShader.SetSampler(0,textureSampler);
        context.PixelShader.SetShaderResource(3,rgbView);
        context.VertexShader.SetConstantBuffer(0,constants);context.PixelShader.SetConstantBuffer(0,constants);
        context.OutputMerger.SetDepthStencilState(solidDepth);context.OutputMerger.SetBlendState(null);
        if(Live)DrawLive(ref frame);
        else
        {
            if(vertices!=null){context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(vertices,Vertex.Stride,0));context.Draw(solidCount,0);}
            DrawTreads();
            if(glassVertices!=null)
            {
                context.OutputMerger.SetDepthStencilState(glassDepth);context.OutputMerger.SetBlendState(blend);
                context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(glassVertices,Vertex.Stride,0));context.Draw(glassCount,0);
            }
        }
        DrawArcade(ref frame);
        if(!Live)DrawEffects(ref frame);
        DrawGloves(ref frame);
        DrawPointer(ref frame,eye);
        context.OutputMerger.SetTargets((DepthStencilView?)null,(RenderTargetView?)null);
        if(samples>1)context.ResolveSubresource(target.Color,0,target.Resolved,0,Format.R8G8B8A8_UNorm);
    }
    internal bool Render(VrSession? vr,Matrix4x4 desktopView,System.Numerics.Vector3 desktopEye)
    {
        if(desktop==null||back==null)return false;
        var mirror=cleanTarget??desktop;
        bool tracked=true;
        if(vr!=null)
        {
            if(left==null||left.Width!=vr.Width||left.Height!=vr.Height)
            {left?.Dispose();right?.Dispose();left=new(device,vr.Width,vr.Height,samples);right=new(device,vr.Width,vr.Height,samples);}
            vr.RobotPose.State=Live?PoseState:null;
            vr.RobotPose.HitRobot=Live?posePicker.Hit:null;
            vr.RobotPose.FloorY=poseFloor;vr.RobotPose.RobotHeight=poseHeight;
            var poseStart=BridgeTiming.Start();
            tracked=vr.UpdatePose(out var head);
            BridgeTiming.Stop(BridgeTiming.Section.UpdatePose,poseStart);
            rays=vr.PointerRays;UpdateGloves(vr.GloveHands);
            // Do not submit a fabricated camera after tracking loss.
            if(tracked)
            {
                var l=vr.Camera(EVREye.Eye_Left,head);var r=vr.Camera(EVREye.Eye_Right,head);
                var drawStart=BridgeTiming.Start();
                Draw(left,l.ViewProjection,l.Eye);Draw(right!,r.ViewProjection,r.Eye);
                context.Flush();
                BridgeTiming.Stop(BridgeTiming.Section.Draw,drawStart);
                var submitStart=BridgeTiming.Start();
                vr.Submit(EVREye.Eye_Left,left.Resolved.NativePointer);vr.Submit(EVREye.Eye_Right,right!.Resolved.NativePointer);
                BridgeTiming.Stop(BridgeTiming.Section.Submit,submitStart);
                var mirrorStart=BridgeTiming.Start();
                var preview=vr.MirrorCamera(head,mirror.Width,mirror.Height);
                Draw(mirror,preview.ViewProjection,preview.Eye);
                if(CleanOutputActive&&IncludeCleanOverlays)
                    TryDrawCapturedOverlays(mirror,vr.OverlayViewProjection(head,mirror.Width,mirror.Height),head.Translation);
                else ClearCapturedOverlays();
                PresentMirror(true);
                vr.EndFrame();
                BridgeTiming.Stop(BridgeTiming.Section.Mirror,mirrorStart);
                vr.SampleFrameTiming();
                return true;
            }
        }
        else
        {
            rays=[];gloveCount=0;ClearCapturedOverlays();
            var projection=Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,(float)mirror.Width/mirror.Height,.03f,150);
            Draw(mirror,desktopView*projection,desktopEye);
        }
        if(tracked)
        {
            PresentMirror(vr!=null);
            vr?.EndFrame();
        }
        return tracked;
    }
    internal void Screenshot(string path)
    {
        if(desktop==null)throw new InvalidOperationException("No rendered frame.");
        var source=cleanTarget??desktop;var desc=source.Resolved.Description;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;desc.Usage=ResourceUsage.Staging;desc.OptionFlags=ResourceOptionFlags.None;
        using var staging=new Texture2D(device,desc);context.CopyResource(source.Resolved,staging);
        var data=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
        try
        {
            using var bitmap=new Bitmap(desc.Width,desc.Height,PixelFormat.Format32bppArgb);
            var bits=bitmap.LockBits(new Rectangle(0,0,desc.Width,desc.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try
            {
                var row=new byte[desc.Width*4];
                for(int y=0;y<desc.Height;y++)
                {
                    Marshal.Copy(data.DataPointer+y*data.RowPitch,row,0,row.Length);
                    for(int x=0;x<row.Length;x+=4)(row[x],row[x+2])=(row[x+2],row[x]);
                    Marshal.Copy(row,0,bits.Scan0+y*bits.Stride,row.Length);
                }
            }
            finally{bitmap.UnlockBits(bits);}
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);bitmap.Save(path,ImageFormat.Png);
        }
        finally{context.UnmapSubresource(staging,0);}
    }
    public void Dispose()
    {
        DisableCleanOutput();DisposeCapturedOverlays();cleanVertex?.Dispose();cleanPixel?.Dispose();cleanSampler?.Dispose();
        context.ClearState();context.Flush();left?.Dispose();right?.Dispose();desktop?.Dispose();back?.Dispose();vertices?.Dispose();glassVertices?.Dispose();
        arcadeTargets?.Dispose();arcadeVertices?.Dispose();gloveVertices?.Dispose();effectVertices?.Dispose();rgbView?.Dispose();rgbColors?.Dispose();ClearTreads();ClearLive();pointerDepth?.Dispose();surfaceView?.Dispose();surfaces?.Dispose();textureView?.Dispose();textureArray?.Dispose();textureSampler?.Dispose();boneView?.Dispose();bones?.Dispose();blend.Dispose();solidDepth.Dispose();glassDepth.Dispose();raster.Dispose();constants.Dispose();layout.Dispose();pixelShader.Dispose();vertexShader.Dispose();swap.Dispose();context.Dispose();device.Dispose();
    }
    private sealed class Target:IDisposable
    {
        internal int Width {get;}
        internal int Height {get;}
        internal Texture2D Color {get;}
        internal Texture2D Resolved {get;}
        internal Texture2D Depth {get;}
        internal RenderTargetView ColorView {get;}
        internal DepthStencilView DepthView {get;}
        internal Target(Device device,int width,int height,int samples)
        {
            Width=width;Height=height;
            var desc=new Texture2DDescription{Width=width,Height=height,MipLevels=1,ArraySize=1,Format=Format.R8G8B8A8_UNorm,
                SampleDescription=new(samples,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource};
            Color=new(device,desc);ColorView=new(device,Color);
            desc.Format=Format.D32_Float;desc.BindFlags=BindFlags.DepthStencil;Depth=new(device,desc);DepthView=new(device,Depth);
            if(samples==1)Resolved=Color;
            else{desc.Format=Format.R8G8B8A8_UNorm;desc.BindFlags=BindFlags.ShaderResource|BindFlags.RenderTarget;desc.SampleDescription=new(1,0);Resolved=new(device,desc);}
        }
        public void Dispose(){DepthView.Dispose();ColorView.Dispose();Depth.Dispose();if(!ReferenceEquals(Resolved,Color))Resolved.Dispose();Color.Dispose();}
    }
}

