using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Johnny5.VrBridge;
using N=System.Numerics;

namespace ServoAnimator;

internal static class VrEditorMode
{
    internal static bool Active {get;private set;}
    internal static string Config {get;private set;}
    internal static string Pipe {get;private set;}
    internal static readonly List<RobotHeadView> Views=new();
    internal static void Configure(string[] args)
    {
        Active=args.Contains("--vr-editor");if(!Active)return;
        string Value(string flag){int i=Array.IndexOf(args,flag);return i>=0&&i+1<args.Length?args[i+1]:"";}
        Config=Path.GetFullPath(Value("--vr-config"));Pipe=Value("--vr-pipe");
        if(!Directory.Exists(Config)||!Guid.TryParse(Pipe,out _))throw new ArgumentException("VR mode needs an existing Config folder and a session ID.");
        FolderSettings.SessionConfigFolder = Config;
    }
    internal static RobotHeadView Source=>Views.LastOrDefault(v=>Window.GetWindow(v) is {IsVisible:true} window&&window is not MainWindow)
        ??Views.FirstOrDefault(v=>Window.GetWindow(v) is MainWindow);
    internal static void Register(RobotHeadView view){if(!Views.Contains(view))Views.Add(view);}
}

public partial class MainWindow
{
    private CancellationTokenSource _vrServerStop;
    internal void InitializeVrEditor()
    {
        if(!VrEditorMode.Active)return;
        MeetingButton.Visibility=Visibility.Collapsed;
        LiveDriveBtn.IsChecked=false;LiveDriveBtn.IsEnabled=false;
        LiveDriveBtn.ToolTip="Use the desktop editor for physical robot output. This editor controls the VR scene.";
        void Filter(DependencyObject parent)
        {
            foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ToArray())
            {
                if(child is MenuItem item)
                {
                    string header=(item.Header?.ToString()??"").Replace("_","");
                    if(header.Contains("Face Tracking",StringComparison.OrdinalIgnoreCase)||header.Contains("Meeting",StringComparison.OrdinalIgnoreCase)||header.Contains("Robot Frames",StringComparison.OrdinalIgnoreCase)||header.Contains("Set Paths",StringComparison.OrdinalIgnoreCase))item.Visibility=Visibility.Collapsed;
                }
                if(child is Button button&&button.Content is string caption&&(caption=="Disable Servos"||caption=="Hardware Defaults"))
                {button.IsEnabled=false;button.ToolTip="Physical robot output is available in the desktop editor.";}
                Filter(child);
            }
        }
        Filter(this);ApplyVrWorkspaceLayout();
        _vrServerStop=new();Closed+=(_,_)=>_vrServerStop.Cancel();
        _=Task.Run(()=>ServeVr(_vrServerStop.Token));
        _=Task.Run(()=>ServeVrDesktop(_vrServerStop.Token));
        ShowStatus("VR editing · 3D output goes to J5 Studio VR Player. A opens its menu; physical keyboard/mouse remain available.");
    }
    private void ApplyVrWorkspaceLayout()
    {
        CommandsEditorColumn.Width=new GridLength(1,GridUnitType.Star);
        UrdfEditorColumn.MinWidth=0;UrdfEditorColumn.Width=new GridLength(0);UrdfSplitterColumn.Width=new GridLength(0);
        UrdfColumnSplitter.Visibility=UrdfOverlayHost.Visibility=DockUrdfButton.Visibility=Visibility.Collapsed;
        Grid.SetColumnSpan(CommandsAtPointPanel,3);
    }
    private volatile bool _vrFollowing,_vrFollowBackdrop=true;
    private int _vrReportedMetadataBuilds,_vrReportedFingerprintBuilds;
    private RobotHeadView _vrReportedSource;
    private async Task ServeVr(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream("Johnny5VR.Editor."+VrEditorMode.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                using var cancel=stop.Register(()=>pipe.Dispose());
                await pipe.WaitForConnectionAsync(stop);
                using var reader=new BinaryReader(pipe,System.Text.Encoding.UTF8,true);using var writer=new BinaryWriter(pipe,System.Text.Encoding.UTF8,true);
                var deltaWriter=new VrSceneDeltaWriter();
                while(pipe.IsConnected&&!stop.IsCancellationRequested)
                {
                    // Background I/O never blocks WPF or SteamVR's pose/render thread.
                    int op=await Task.Run(()=>reader.ReadByte(),stop);
                    if(op==2){await Dispatcher.InvokeAsync(()=>{if(IsRunning)PausePlayback();});continue;}
                    if(op==3)
                    {
                        var command=(VrEditorCommand)reader.ReadByte();
                        if(!Enum.IsDefined(command))throw new InvalidDataException("Unknown VR editor command.");
                        await Dispatcher.InvokeAsync(()=>ExecuteVrInput(()=>RunVrCommand(command)));continue;
                    }
                    if(op==4)
                    {
                        var navigation=new VrEditorNavigation(reader.ReadInt32(),reader.ReadInt32(),reader.ReadSingle(),reader.ReadSingle());
                        if(!float.IsFinite(navigation.Pan+navigation.Zoom))throw new InvalidDataException("Invalid VR navigation.");
                        await Dispatcher.InvokeAsync(()=>ExecuteVrInput(()=>NavigateVrEditor(navigation)));continue;
                    }
                    if(op==7)
                    {
                        var request=new VrRecordingRequest(reader.ReadString(),reader.ReadString(),(VrRecordingAction)reader.ReadByte(),(VrRecordingTracks)reader.ReadInt32());
                        await Dispatcher.InvokeAsync(()=>ExecuteVrInput(()=>{try{ApplyVrRecordingRequest(request);}catch(Exception ex){_vrRecordingMessage="Recording error: "+ex.Message;_vrPoseLastRequest=request.Id;throw;}}));continue;
                    }
                    if(op==6)
                    {
                        var batch=VrMotionWire.Read(reader);
                        await Dispatcher.InvokeAsync(()=>ExecuteVrInput(()=>{try{ApplyVrMotionBatch(batch);}catch(Exception ex){_vrPoseMessage="Body capture error: "+ex.Message;_vrPoseLastRequest=batch.Id;throw;}}));continue;
                    }
                    if(op==5)
                    {
                        var request=new VrPoseRequest(reader.ReadString(),reader.ReadString(),(VrPoseAction)reader.ReadByte(),reader.ReadString(),reader.ReadSingle());
                        await Dispatcher.InvokeAsync(()=>ExecuteVrInput(()=>
                        {
                            try{ApplyVrPoseRequest(request);}
                            catch(Exception ex){_vrPoseMessage=ex.Message;_vrPoseLastRequest=request.Id;throw;}
                        }));continue;
                    }
                    if(op==8){_vrFollowing=reader.ReadBoolean();_vrFollowBackdrop=reader.ReadBoolean();continue;}
                    if(op!=1)throw new InvalidDataException("Unknown VR bridge request.");
                    var meshes=new HashSet<long>();for(int i=SceneWire.Count(reader,20000);i>0;i--)meshes.Add(reader.ReadInt64());
                    var textures=new HashSet<long>();for(int i=SceneWire.Count(reader,2048);i>0;i--)textures.Add(reader.ReadInt64());
                    long acknowledgedSequence=reader.ReadInt64();
                    var uiQueued=System.Diagnostics.Stopwatch.GetTimestamp();
                    var frame=await Dispatcher.InvokeAsync(()=>
                    {
                        double uiWait=System.Diagnostics.Stopwatch.GetElapsedTime(uiQueued).TotalMilliseconds;
                        ApplyVrWorkspaceLayout();
                        ApplyVrEffectPreview();
                        var source=VrEditorMode.Source??EmbeddedHeadView;
                        long captureStart=System.Diagnostics.Stopwatch.GetTimestamp();
                        var scene=source.CaptureVrScene($"{(IsRunning?"Playing":"Editing")} · {_cursorTime:F2}s · {_jsonPath??"Unsaved sequence"}"+_vrCommandStatus,_vrFollowing,_vrFollowBackdrop);
                        double captureMs=System.Diagnostics.Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds;
                        long poseStart=System.Diagnostics.Stopwatch.GetTimestamp();int metadataBefore=ReferenceEquals(source,_vrReportedSource)?_vrReportedMetadataBuilds:0,fingerprintBefore=_vrReportedFingerprintBuilds;
                        var pose=CaptureVrPoseState(source);
                        var result=scene with{Pose=pose,Metrics=new(uiWait,_vrLastMotionMs,captureMs,System.Diagnostics.Stopwatch.GetElapsedTime(poseStart).TotalMilliseconds,source.VrCachedBranches,source.VrVisitedBranches,source.VrHandleMetadataBuilds-metadataBefore,VrFingerprintBuilds-fingerprintBefore)};
                        _vrLastMotionMs=0;_vrReportedSource=source;_vrReportedMetadataBuilds=source.VrHandleMetadataBuilds;_vrReportedFingerprintBuilds=VrFingerprintBuilds;return result;
                    });
                    var encodeStart=System.Diagnostics.Stopwatch.GetTimestamp();
                    byte[] packet=await Task.Run(()=>SceneWire.Encode(deltaWriter.Prepare(frame,acknowledgedSequence),meshes,textures),stop);
                    double encodeMs=System.Diagnostics.Stopwatch.GetElapsedTime(encodeStart).TotalMilliseconds;
                    writer.Write(packet.Length);writer.Write(encodeMs);await pipe.WriteAsync(packet,stop);await pipe.FlushAsync(stop);
                }
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException){if(stop.IsCancellationRequested)return;}
            catch(Exception ex){System.Diagnostics.Trace.TraceError("VR bridge: "+ex);await Task.Delay(500,stop);}
        }
    }
}

public sealed partial class RobotHeadView
{
    private VrGraphCapture _vrGraph;
    internal int VrCachedBranches=>_vrGraph?.CachedBranches??0;
    internal int VrVisitedBranches=>_vrGraph?.VisitedBranches??0;
    private Rect3D _vrRestBounds;
    internal VrFrame CaptureVrScene(string status,bool following=false,bool useBackdrop=true)
    {
        _vrGraph??=new();
        return _vrGraph.Capture(_sceneLightingRoot,_presentationModel,_stageModel,_vrRestBounds,(float)_stageFloor,LoadedModelFile,status,VrExtraGeometry(),!following||useBackdrop,following&&useBackdrop&&CaptureBackground().BackgroundMode==UrdfBackgroundMode.Urdf3D,_scene?.VrTreadState());
    }
}

internal sealed class VrGraphCapture
{
    private sealed class Id {internal long Value,Revision;internal object Cached;}
    private sealed class Branch
    {
        internal long Revision=-1;internal Matrix3D Parent;internal bool Robot,Backdrop;internal Model3D RobotRoot;
        internal VrNode[] Nodes=[];internal VrMesh[] Meshes=[];internal VrMaterial[] Materials=[];internal VrTexture[] Textures=[];internal VrLight[] Lights=[];
    }
    private sealed record MaterialSnapshot(VrMaterial Material,VrTexture[] Textures);
    private readonly ConditionalWeakTable<Model3D,Branch> branches=new();
    internal int CachedBranches {get;private set;}
    internal int VisitedBranches {get;private set;}
    private readonly ConditionalWeakTable<object,Id> ids=new();
    private static long next;
    private long sequence;
    private Id Identify(object value)=>ids.GetValue(value,key=>
    {
        var id=new Id{Value=++next};
        if(key is Freezable f&&!f.IsFrozen)f.Changed+=(_,_)=>{id.Revision++;id.Cached=null;if(key is MeshGeometry3D or BitmapSource or Brush)id.Value=++next;};
        return id;
    });
    internal VrFrame Capture(Model3D root,Model3D robot,Model3D stage,Rect3D rest,float stageFloor,string model,string status,IEnumerable<(Model3D Model,bool Robot)> extra,bool includeBackdrop=true,bool forceStage=false,(double Step,double Left,double Right,(GeometryModel3D Visual,bool Left,MeshGeometry3D[] Frames)[] Layers)? treads=null)
    {
        var nodes=new List<VrNode>();var meshes=new Dictionary<long,VrMesh>();var materials=new Dictionary<long,VrMaterial>();var textures=new Dictionary<long,VrTexture>();var lights=new List<VrLight>();
        CachedBranches=VisitedBranches=0;
        void Walk(Model3D source,Matrix3D parent,bool isRobot)
        {
            if(source==null)return;
            var id=Identify(source);var cached=branches.GetValue(source,_=>new Branch());
            if(cached.Revision==id.Revision&&cached.Parent==parent&&cached.Robot==isRobot&&cached.Backdrop==includeBackdrop&&ReferenceEquals(cached.RobotRoot,robot))
            {
                CachedBranches++;nodes.AddRange(cached.Nodes);lights.AddRange(cached.Lights);
                foreach(var mesh in cached.Meshes)meshes[mesh.Id]=mesh;foreach(var material in cached.Materials)materials[material.Id]=material;foreach(var texture in cached.Textures)textures[texture.Id]=texture;
                return;
            }
            VisitedBranches++;int firstNode=nodes.Count,firstLight=lights.Count;
            WalkChanged(source,parent,isRobot);
            cached.Revision=id.Revision;cached.Parent=parent;cached.Robot=isRobot;cached.Backdrop=includeBackdrop;cached.RobotRoot=robot;
            cached.Nodes=nodes.Skip(firstNode).ToArray();cached.Lights=lights.Skip(firstLight).ToArray();
            cached.Meshes=cached.Nodes.Select(n=>n.Mesh).Distinct().Select(id=>meshes[id]).ToArray();
            cached.Materials=cached.Nodes.Select(n=>n.Material).Distinct().Select(id=>materials[id]).ToArray();
            cached.Textures=cached.Materials.Where(m=>m.Texture!=0).Select(m=>m.Texture).Distinct().Select(id=>textures[id]).ToArray();
        }
        void WalkChanged(Model3D source,Matrix3D parent,bool isRobot)
        {
            if(source==null)return;
            var world=source.Transform?.Value??Matrix3D.Identity;world.Append(parent);isRobot|=ReferenceEquals(source,robot);
            if(source is Model3DGroup group){foreach(var child in group.Children)Walk(child,world,isRobot);return;}
            if(source is AmbientLight ambient){lights.Add(new(Color(ambient.Color),N.Vector3.Zero,true));return;}
            if(source is DirectionalLight directional){var d=world.Transform(directional.Direction);lights.Add(new(Color(directional.Color),new((float)d.X,(float)d.Y,(float)d.Z),false));return;}
            if(source is not GeometryModel3D geometry||geometry.Geometry is not MeshGeometry3D mesh||mesh.Positions.Count==0||mesh.TriangleIndices.Count==0)return;
            if(!isRobot&&!includeBackdrop)return;
            var material=geometry.Material??geometry.BackMaterial;if(material==null)return;
            var data=MeshData(mesh);var id=Identify(mesh);
            meshes[id.Value]=data;
            var mid=Identify(material);if(!materials.ContainsKey(mid.Value))
            {
                if(mid.Cached is MaterialSnapshot materialSnapshot)
                {
                    materials[mid.Value]=materialSnapshot.Material;foreach(var image in materialSnapshot.Textures)textures[image.Id]=image;
                }
                else
                {
                N.Vector4 diffuse=N.Vector4.Zero,emissive=N.Vector4.Zero,specular=N.Vector4.Zero;float power=32;long texture=0;
                void Visit(Material m)
                {
                    if(m is MaterialGroup mg){foreach(var child in mg.Children)Visit(child);return;}
                    Brush brush=m switch{DiffuseMaterial d=>d.Brush,EmissiveMaterial e=>e.Brush,SpecularMaterial s=>s.Brush,_=>null};
                    if(brush==null)return;
                    var color=brush is SolidColorBrush solid?Color(solid.Color):N.Vector4.One;
                    if(brush is SolidColorBrush)color.W*=(float)brush.Opacity;
                    if(m is DiffuseMaterial)diffuse=color;else if(m is EmissiveMaterial)emissive=color;else if(m is SpecularMaterial sp){specular=color;power=(float)sp.SpecularPower;}
                    if(brush is not SolidColorBrush)
                    {
                        object key=brush is ImageBrush image&&image.ImageSource!=null&&image.IsFrozen&&image.RelativeTransform.Value.IsIdentity&&image.Transform.Value.IsIdentity?(object)image.ImageSource:brush;
                        var tid=Identify(key);
                        if(tid.Cached is not VrTexture imageData)
                        {
                            int height=brush is ImageBrush moving&&!moving.RelativeTransform.Value.IsIdentity?144:256;
                            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                            {
                                if(key is ImageSource original)dc.DrawImage(original,new Rect(0,0,256,height));
                                else dc.DrawRectangle(brush,null,new Rect(0,0,256,height));
                            }
                            var bitmap=new RenderTargetBitmap(256,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);
                            tid.Cached=imageData=new(tid.Value,output.ToArray());
                        }
                        textures[tid.Value]=imageData;texture=tid.Value;
                    }
                }
                Visit(material);diffuse.W=Math.Max(diffuse.W,emissive.W);
                materials[mid.Value]=new(mid.Value,diffuse,emissive,specular,power,texture);
                mid.Cached=new MaterialSnapshot(materials[mid.Value],texture==0?[]:[textures[texture]]);
                }
            }
            nodes.Add(new(Identify(geometry).Value,id.Value,mid.Value,Matrix(world),isRobot));
        }
        Walk(root,Matrix3D.Identity,false);
        if(includeBackdrop&&forceStage&&stage!=null&&root is Model3DGroup rootGroup&&!rootGroup.Children.Contains(stage))Walk(stage,root.Transform?.Value??Matrix3D.Identity,false);
        foreach(var item in extra)Walk(item.Model,Matrix3D.Identity,item.Robot);
        if(rest.IsEmpty)rest=new Rect3D(0,0,0,.25,.25,.25);
        // Tread belts: every frame mesh goes with the scene (sent once, then known), so the
        // player can show any of them. Only layers whose belt is in this scene are listed.
        VrTreads treadState=null;
        if(treads is {} state)
        {
            var present=nodes.Select(n=>n.Id).ToHashSet();var layers=new List<VrTreadLayer>();
            foreach(var layer in state.Layers)
            {
                long node=Identify(layer.Visual).Value;if(!present.Contains(node))continue;
                var frames=layer.Frames.Select(MeshData).ToArray();foreach(var frame in frames)meshes[frame.Id]=frame;
                layers.Add(new(node,layer.Left,frames.Select(f=>f.Id).ToArray()));
            }
            if(layers.Count>0)treadState=new(state.Step,state.Left,state.Right,layers.ToArray());
        }
        return new(++sequence,status,model??"",(float)rest.Z,(float)rest.SizeZ,(float)rest.Z,nodes.ToArray(),meshes.Values.ToArray(),materials.Values.ToArray(),textures.Values.ToArray(),lights.ToArray()){Treads=treadState};
    }
    private VrMesh MeshData(MeshGeometry3D mesh)
    {
        var id=Identify(mesh);
        if(id.Cached is not VrMesh data)
        {
            var p=mesh.Positions.SelectMany(v=>new[]{(float)v.X,(float)v.Y,(float)v.Z}).ToArray();
            var n=mesh.Normals.SelectMany(v=>new[]{(float)v.X,(float)v.Y,(float)v.Z}).ToArray();
            if(n.Length!=p.Length)
            {
                var normals=new N.Vector3[p.Length/3];var indices=mesh.TriangleIndices;
                N.Vector3 Position(int i)=>new(p[i*3],p[i*3+1],p[i*3+2]);
                for(int j=0;j+2<indices.Count;j+=3){int a=indices[j],b=indices[j+1],c=indices[j+2];var normal=N.Vector3.Cross(Position(b)-Position(a),Position(c)-Position(a));normals[a]+=normal;normals[b]+=normal;normals[c]+=normal;}
                n=normals.SelectMany(v=>{v=v.LengthSquared()>1e-15?N.Vector3.Normalize(v):N.Vector3.UnitZ;return new[]{v.X,v.Y,v.Z};}).ToArray();
            }
            var uv=mesh.TextureCoordinates.Count==mesh.Positions.Count?mesh.TextureCoordinates.SelectMany(v=>new[]{(float)v.X,(float)v.Y}).ToArray():new float[p.Length/3*2];
            id.Cached=data=new(id.Value,!mesh.IsFrozen,p,n,uv,mesh.TriangleIndices.ToArray());
        }
        return data;
    }
    private static N.Vector4 Color(Color c)=>new(c.R/255f,c.G/255f,c.B/255f,c.A/255f);
    private static N.Matrix4x4 Matrix(Matrix3D m)=>new((float)m.M11,(float)m.M12,(float)m.M13,(float)m.M14,(float)m.M21,(float)m.M22,(float)m.M23,(float)m.M24,(float)m.M31,(float)m.M32,(float)m.M33,(float)m.M34,(float)m.OffsetX,(float)m.OffsetY,(float)m.OffsetZ,(float)m.M44);
}
