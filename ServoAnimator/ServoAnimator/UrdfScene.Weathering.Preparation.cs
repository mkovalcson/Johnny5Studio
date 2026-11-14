using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    private sealed record WeatheringWork(MeshGeometry3D Key, MeshGeometry3D Mesh, Color Color, Color Specular,
        double Shine, bool Rubber, bool Stainless, Point3D[] Bolts, Vector3D Down, UrdfWorkshopPart WorkshopPart);
    private sealed record WeatheringFrames(MeshGeometry3D Key, MeshGeometry3D Mesh);
    private sealed record WeatheringSnapshot(WeatheringWork[] Surfaces, WeatheringFrames[][] Treads);
    private sealed record PreparedWeathering(MeshGeometry3D[] Surfaces, Material[] Materials,
        IReadOnlyDictionary<MeshGeometry3D, MeshGeometry3D> Geometry);

    private WeatheringSnapshot _weatheringSnapshot;
    private readonly object _weatheringPreparationLock = new();
    // Accessed only by the worker while holding the preparation lock.
    private readonly Dictionary<MeshGeometry3D, MeshGeometry3D> _preparedWeatheringGeometry = new();
    private readonly Dictionary<int, PreparedWeathering> _preparedWeathering = new();
    // Completed finishes are published/read only on the UI thread.
    private readonly Dictionary<int, PreparedWeathering> _readyWeathering = new();
    private CancellationTokenSource _weatheringCancellation;
    private int _weatheringRequest;
    private (int Level, bool Gold)? _weatheringRequested;
    internal Task WeatheringPreparation { get; private set; } = Task.CompletedTask;
    private static readonly SemaphoreSlim WeatheringWorker = new(1, 1);
    private static int _weatheringJobs;
    private static string _weatheringProgress = "";
    internal static string WeatheringActivity => Volatile.Read(ref _weatheringJobs) > 0
        ? Volatile.Read(ref _weatheringProgress) : "";

    private WeatheringSnapshot CaptureWeatheringWork()
    {
        if (_weatheringSnapshot != null) return _weatheringSnapshot;
        static MeshGeometry3D Frozen(MeshGeometry3D mesh) => mesh.IsFrozen ? mesh : (MeshGeometry3D)mesh.GetAsFrozen();
        var bolts = _weatheringSurfaces.Where(s => s.Stainless).GroupBy(s => s.Link).ToDictionary(g => g.Key,
            g => g.Select(s => { var b = s.Geometry.Bounds; return s.Model.Transform.Transform(new Point3D(b.X+b.SizeX/2,b.Y+b.SizeY/2,b.Z+b.SizeZ/2)); }).Distinct().ToArray());
        var surfaces = _weatheringSurfaces.Select(s =>
        {
            var inverse = s.Model.Transform.Inverse;
            var sites = !s.Stainless && !s.Rubber && inverse != null && bolts.TryGetValue(s.Link, out var positions)
                ? positions.Select(inverse.Transform).ToArray() : Array.Empty<Point3D>();
            var down = inverse == null ? new Vector3D(0,0,-1) : inverse.Transform(new Point3D(0,0,-1))-inverse.Transform(new Point3D());
            return new WeatheringWork(s.Geometry, Frozen(s.Geometry), s.Color, s.Specular, s.Shine, s.Rubber, s.Stainless, sites, down, s.WorkshopPart);
        }).ToArray();
        var treads = new[] { _treadLeft, _treadRight }.Where(s => s != null).SelectMany(s => s.Layers)
            .Select(l => l.Frames.Select(m => new WeatheringFrames(m, Frozen(m))).ToArray()).ToArray();
        return _weatheringSnapshot = new(surfaces, treads);
    }

    private PreparedWeathering PrepareWeathering(WeatheringSnapshot snapshot, int level, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_preparedWeathering.TryGetValue(level, out var cached)) return cached;
        var geometry = new MeshGeometry3D[snapshot.Surfaces.Length];
        var materials = new Material[geometry.Length];
        var finishes = new Dictionary<(Color,Color,double,bool,bool,MeshGeometry3D,UrdfWorkshopPart), Material>();
        var uniformLayouts = new Dictionary<int,MeshGeometry3D>();
        for (int i=0;i<geometry.Length;i++)
        {
            token.ThrowIfCancellationRequested();
            var s=snapshot.Surfaces[i];
            bool detail=!s.Rubber&&!s.Stainless||s.WorkshopPart is UrdfWorkshopPart.BodyPaint or UrdfWorkshopPart.BodyMetal or UrdfWorkshopPart.BodyBrass;
            if (!_preparedWeatheringGeometry.TryGetValue(s.Key,out var mapped))
            {
                mapped=UrdfWeathering.Map(s.Mesh,detail,s.WorkshopPart==UrdfWorkshopPart.Vent);
                if(detail) UrdfWeathering.AddBoltStreaks(mapped,s.Bolts,s.Down);
                _preparedWeatheringGeometry[s.Key]=mapped;
            }
            else if(detail) UrdfWeathering.EnsureEdges(mapped);
            geometry[i]=mapped;
            // Rubber/stainless have no spatial wear masks. Equal atlas layouts
            // and colors produce identical images, regardless of mesh shape.
            var finishMap=mapped;
            if(!detail)
            {
                int parts=UrdfWeathering.TextureParts(mapped);
                if(!uniformLayouts.TryGetValue(parts,out finishMap))uniformLayouts[parts]=finishMap=mapped;
            }
            var workshop=level==2||level==1&&UrdfWeathering.IsBodyRecipe(s.WorkshopPart)?s.WorkshopPart:UrdfWorkshopPart.None;
            var key=(s.Color,s.Specular,s.Shine,s.Rubber,s.Stainless,finishMap,workshop);
            if(!finishes.TryGetValue(key,out var material))
                finishes[key]=material=workshop==UrdfWorkshopPart.None
                    ? UrdfWeathering.Material(s.Color,s.Specular,s.Shine,s.Rubber,level,finishMap,s.Stainless)
                    : UrdfWeathering.WorkshopMaterial(s.Color,s.Specular,s.Shine,finishMap,workshop,level);
            materials[i]=material;
            if(i%20==0) Volatile.Write(ref _weatheringProgress,$"Preparing weathering… {i*100/geometry.Length}% · You can keep using the editor.");
        }
        foreach(var frames in snapshot.Treads)
        {
            token.ThrowIfCancellationRequested();
            if(frames.Length==0)continue;
            if(!_preparedWeatheringGeometry.TryGetValue(frames[0].Key,out var template))
                _preparedWeatheringGeometry[frames[0].Key]=template=UrdfWeathering.Map(frames[0].Mesh,false);
            foreach(var frame in frames)
            {
                token.ThrowIfCancellationRequested();
                if(!_preparedWeatheringGeometry.ContainsKey(frame.Key))
                    _preparedWeatheringGeometry[frame.Key]=frame.Mesh.Positions.Count==template.Positions.Count&&frame.Mesh.TriangleIndices.SequenceEqual(template.TriangleIndices)
                        ? UrdfWeathering.WithCoordinates(frame.Mesh,template.TextureCoordinates) : UrdfWeathering.Map(frame.Mesh,false);
            }
        }
        token.ThrowIfCancellationRequested();
        var result=new PreparedWeathering(geometry,materials,new Dictionary<MeshGeometry3D,MeshGeometry3D>(_preparedWeatheringGeometry));
        _preparedWeathering[level]=result;
        return result;
    }

    private void CommitWeathering(int level, bool gold, PreparedWeathering prepared)
    {
        for(int i=0;i<_weatheringSurfaces.Count;i++)
        {
            var s=_weatheringSurfaces[i]; Material front=s.Front,back=s.Back;
            var geometry=s.Geometry;
            if(prepared!=null)
            {
                geometry=prepared.Surfaces[i];
                front=s.Front==null?null:prepared.Materials[i];back=s.Back==null?null:prepared.Materials[i];
            }
            else if(gold&&!s.GoldProtected)
            { front=s.Front==null?null:UrdfWeathering.Gold;back=s.Back==null?null:UrdfWeathering.Gold; }
            if(!ReferenceEquals(s.Model.Geometry,geometry))s.Model.Geometry=geometry;
            _originalMaterials[s.Model]=(front,back);
            if(!_highlightedVisuals.Contains(s.Model))
            { if(!ReferenceEquals(s.Model.Material,front))s.Model.Material=front;if(!ReferenceEquals(s.Model.BackMaterial,back))s.Model.BackMaterial=back; }
        }
        if(prepared!=null)_weatheringGeometry=prepared.Geometry;
        _weatheringLevel=level;_goldJohnny=gold;
        foreach(var side in new[]{_treadLeft,_treadRight})
            if(side!=null)foreach(var layer in side.Layers)
                if(layer.Index>=0)layer.Visual.Geometry=WeatheredTreadFrame(layer.Frames[layer.Index]);
    }

    private void CancelWeatheringPreparation()
    {
        _weatheringRequest++;
        _weatheringCancellation?.Cancel();
        _weatheringRequested=null;
    }
    internal void DeferWeatheringPreparation()
    {
        CancelWeatheringPreparation();
        WeatheringPreparation=Task.CompletedTask;
    }

    internal Task RequestWeathering(int level, bool gold=false)
    {
        level=gold?0:Math.Clamp(level,0,3);
        if(_weatheringRequested==(level,gold))return WeatheringPreparation;
        CancelWeatheringPreparation();
        if(_weatheringLevel==level&&_goldJohnny==gold)return WeatheringPreparation=Task.CompletedTask;
        if(level==0) { CommitWeathering(0,gold,null);return WeatheringPreparation=Task.CompletedTask; }
        if(_readyWeathering.TryGetValue(level,out var cached)) { CommitWeathering(level,false,cached);return WeatheringPreparation=Task.CompletedTask; }
        var snapshot=CaptureWeatheringWork();
        var cancellation=new CancellationTokenSource();_weatheringCancellation=cancellation;
        _weatheringRequested=(level,gold);
        int request=_weatheringRequest;var dispatcher=Dispatcher.CurrentDispatcher;
        Volatile.Write(ref _weatheringProgress,"Preparing weathering… 0% · You can keep using the editor.");
        Interlocked.Increment(ref _weatheringJobs);
        return WeatheringPreparation=PrepareAndPublish();

        async Task PrepareAndPublish()
        {
            try
            {
                var prepared=await Task.Run(async () =>
                {
                    await WeatheringWorker.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    try { lock(_weatheringPreparationLock)return PrepareWeathering(snapshot,level,cancellation.Token); }
                    finally { WeatheringWorker.Release(); }
                }).ConfigureAwait(false);
                await dispatcher.InvokeAsync(() =>
                {
                    _readyWeathering[level]=prepared;
                    if(request==_weatheringRequest&&!cancellation.IsCancellationRequested)CommitWeathering(level,false,prepared);
                });
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested||dispatcher.HasShutdownStarted) { }
            finally
            {
                Interlocked.Decrement(ref _weatheringJobs);
                if(!dispatcher.HasShutdownStarted) try { await dispatcher.InvokeAsync(() =>
                {
                    if(request==_weatheringRequest)_weatheringRequested=null;
                    if(ReferenceEquals(_weatheringCancellation,cancellation))_weatheringCancellation=null;
                }); } catch(OperationCanceledException) when(dispatcher.HasShutdownStarted) { }
                cancellation.Dispose();
            }
        }
    }
}
