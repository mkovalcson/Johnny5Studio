using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private readonly VrVideoPicture _vrTvPicture=new(),_vrBackdropPicture=new();
    private GeometryModel3D _vrTvSurface,_vrVideoSurface,_vrImageSurface;
    private Brush _vrBackdropBrush;
    private readonly Model3DGroup _vrSparks=new();
    private IEnumerable<(Model3D Model,bool Robot)> VrExtraGeometry()
    {
        string config=PresentationRootOverride??FolderSettings.Load()?.ConfigFolderOrDefault??AppContext.BaseDirectory;
        var presentation=_timelineBackground??_meetingPresentation??_urdfConfiguration?.Presentation;
        if(_backdropVideo.Visibility!=Visibility.Visible&&presentation?.BackgroundMode is UrdfBackgroundMode.Image or UrdfBackgroundMode.MovingImage or UrdfBackgroundMode.Color&&_configuredBackground!=null)
        {
            if(_vrImageSurface==null||!ReferenceEquals(_vrBackdropBrush,_configuredBackground))
            {
                _vrBackdropBrush=_configuredBackground;
                double aspect=_configuredBackground is ImageBrush ib&&ib.ImageSource?.Height>0?ib.ImageSource.Width/ib.ImageSource.Height:16.0/9;
                if(presentation.BackgroundMode==UrdfBackgroundMode.MovingImage)aspect=16.0/9;
                var mesh=new MeshGeometry3D{Positions=new Point3DCollection{new(-1.5,-2,_vrRestBounds.Z+_vrRestBounds.SizeZ*.5-2/aspect),new(-1.5,2,_vrRestBounds.Z+_vrRestBounds.SizeZ*.5-2/aspect),new(-1.5,2,_vrRestBounds.Z+_vrRestBounds.SizeZ*.5+2/aspect),new(-1.5,-2,_vrRestBounds.Z+_vrRestBounds.SizeZ*.5+2/aspect)},TextureCoordinates=new PointCollection{new(0,1),new(1,1),new(1,0),new(0,0)},TriangleIndices=new Int32Collection{0,1,2,0,2,3}};mesh.Freeze();
                var material=new EmissiveMaterial(_configuredBackground);
                _vrImageSurface=new(mesh,material){BackMaterial=material};
            }
            yield return (_vrImageSurface,false);
        }
        if(_tvState.Active&&_tvState.Show is {} show&&!show.IsBlank&&FindTvScreen((_tvState.Front??new()).Screen) is {} screen)
        {
            var picture=show.IsVideo?_vrTvPicture.Request(show.FullPath(config),_tvState.MediaSeconds):_tvImage.Source as BitmapSource;
            if(picture!=null)
            {
                _vrTvSurface??=new GeometryModel3D();
                UpdateVrScreen(_vrTvSurface,picture,screen.Center+screen.Normal*.002,screen.Right,screen.Up,screen.Width,screen.Height,show.Fit);
                yield return (_vrTvSurface,false);
            }
        }
        if(_backdropVideoPath!=null&&_backdropVideo.Visibility==Visibility.Visible)
        {
            var picture=_vrBackdropPicture.Request(_backdropVideoPath,_backdropVideo.Position.TotalSeconds);
            if(picture!=null)
            {
                _vrVideoSurface??=new GeometryModel3D();
                UpdateVrScreen(_vrVideoSurface,picture,new Point3D(-1.5,0,_vrRestBounds.Z+_vrRestBounds.SizeZ*.5),new Vector3D(0,1,0),new Vector3D(0,0,1),4,2.25,TvFit.Contain);
                yield return (_vrVideoSurface,false);
            }
        }
        // The editor's beam material/visibility already comes through the scene
        // graph. Add physical sparkles instead of its screen-space 2D column.
        if(_beamState.Active&&_beamState.Sparkle>.001)
        {
            if(_vrSparks.Children.Count==0)
            {
                var mesh=new MeshGeometry3D{Positions=new Point3DCollection{new(-.001,0,0),new(.001,0,0),new(0,-.001,0),new(0,.001,0),new(0,0,-.001),new(0,0,.001)},TriangleIndices=new System.Windows.Media.Int32Collection{0,2,4,2,1,4,1,3,4,3,0,4,2,0,5,1,2,5,3,1,5,0,3,5}};mesh.Freeze();
                var material=new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(220,155,215,255)));material.Freeze();
                for(int i=0;i<96;i++)_vrSparks.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});
            }
            var b=_vrRestBounds;
            for(int i=0;i<_vrSparks.Children.Count;i++)
            {
                double phase=i*2.39996323,t=(_beamSeconds*.7+i*.61803399)%1;
                var group=new Transform3DGroup();double s=1+2*Math.Abs(Math.Sin(phase+_beamSeconds*7));
                group.Children.Add(new ScaleTransform3D(s,s,s));group.Children.Add(new TranslateTransform3D(b.X+b.SizeX*(.5+.55*Math.Sin(phase)),b.Y+b.SizeY*(.5+.55*Math.Cos(phase)),b.Z+b.SizeZ*t));
                if(_presentationModel?.Transform!=null)group.Children.Add(_presentationModel.Transform);
                _vrSparks.Children[i].Transform=group;
            }
            yield return (_vrSparks,true);
        }
    }
    private static void UpdateVrScreen(GeometryModel3D model,BitmapSource picture,Point3D center,Vector3D right,Vector3D up,double width,double height,TvFit fit)
    {
        var mesh=model.Geometry as MeshGeometry3D;
        if(mesh==null)
        {
            mesh=new(){TextureCoordinates=new PointCollection{new(0,1),new(1,1),new(1,0),new(0,0)},TriangleIndices=new Int32Collection{0,1,2,0,2,3}};
            model.Geometry=mesh;
        }
        double aspect=picture.PixelWidth/(double)picture.PixelHeight;
        if(fit==TvFit.Contain){if(width/height>aspect)width=height*aspect;else height=width/aspect;}
        double u=1,v=1;if(fit==TvFit.Cover){if(width/height>aspect)v=aspect/(width/height);else u=width/height/aspect;}
        var uv=new PointCollection{new((1-u)*.5,(1+v)*.5),new((1+u)*.5,(1+v)*.5),new((1+u)*.5,(1-v)*.5),new((1-u)*.5,(1-v)*.5)};
        if(!mesh.TextureCoordinates.SequenceEqual(uv))mesh.TextureCoordinates=uv;
        var positions=new Point3DCollection{center-right*width*.5-up*height*.5,center+right*width*.5-up*height*.5,center+right*width*.5+up*height*.5,center-right*width*.5+up*height*.5};
        if(!mesh.Positions.SequenceEqual(positions))mesh.Positions=positions;
        if(model.Material is EmissiveMaterial existing&&existing.Brush is ImageBrush current&&ReferenceEquals(current.ImageSource,picture))return;
        var brush=new ImageBrush(picture){Stretch=Stretch.Fill};brush.Freeze();
        var material=new EmissiveMaterial(brush);material.Freeze();model.Material=model.BackMaterial=material;
    }
}

internal sealed class VrVideoPicture
{
    private Task _task;
    private BitmapSource _picture;
    private string _lastPath;
    private string _readerPath;
    private VideoFrames.Reader _reader;
    private double _lastTime=double.NegativeInfinity;
    internal BitmapSource Request(string path,double seconds)
    {
        if(_task is {IsCompleted:false})return _lastPath==path?_picture:null;
        if(_lastPath==path&&Math.Abs(seconds-_lastTime)<1/30.0)return _picture;
        bool changed=_lastPath!=path;_lastPath=path;_lastTime=seconds;if(changed)_picture=null;
        _task=Task.Run(()=>
        {
            try
            {
                if(_readerPath!=path){_reader?.Dispose();_reader=null;_readerPath=null;_reader=new VideoFrames.Reader(path);_readerPath=path;}
                var frame=_reader.Read(seconds);var bitmap=VideoFrames.ToBitmap(frame,1024);System.Threading.Volatile.Write(ref _picture,bitmap);
            }
            catch(Exception ex){System.Diagnostics.Trace.TraceWarning("VR video: "+ex.Message);}
        });
        return _picture;
    }
}
