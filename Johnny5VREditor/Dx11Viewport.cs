using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using H = HelixToolkit.Wpf.SharpDX;
using D = HelixToolkit.SharpDX;

namespace ServoAnimator;

internal sealed class Dx11Viewport : IDisposable
{
    internal H.Viewport3DX View { get; }
    internal Dx11SceneMirror Mirror { get; } = new();
    private readonly D.DefaultEffectsManager _effects;
    private readonly H.PerspectiveCamera _camera = new();
    private bool _disposed;
    private D.IRenderHost _controlledHost;
    internal string AdapterName { get; }
    internal long RenderedFrames { get; private set; }
    internal double SynchronizeMilliseconds { get; private set; }
    internal event Action<Exception> Failed;
    internal event Action Rendered;
    internal Dx11Viewport()
    {
        try
        {
            _effects = new D.DefaultEffectsManager(new D.EffectsManagerConfiguration { AdapterIndex = -1, EnableSoftwareRendering = false });
            if (_effects.Device.FeatureLevel < SharpDX.Direct3D.FeatureLevel.Level_11_0) throw new NotSupportedException("This adapter does not support Direct3D feature level 11.0.");
            using var device = _effects.Device.QueryInterface<SharpDX.DXGI.Device>();
            using var adapter = device.Adapter;
            using var adapter1 = adapter.QueryInterface<SharpDX.DXGI.Adapter1>();
            if ((adapter1.Description1.Flags & SharpDX.DXGI.AdapterFlags.Software) != 0) throw new NotSupportedException("A hardware Direct3D 11 adapter is required.");
            AdapterName = adapter.Description.Description.TrimEnd('\0');
            View = new H.Viewport3DX
            {
                EffectsManager = _effects, Camera = _camera, BackgroundColor = Color.FromArgb(0, 0, 0, 0), Background = Brushes.Transparent,
                // D3DImage presentation retains WPF overlays and the video behind the robot.
                EnableSwapChainRendering = false, EnableD2DRendering = false,
                ShowViewCube = false, ShowCoordinateSystem = false, ShowFrameRate = false,
                IsHitTestVisible = false, EnableMouseButtonHitTest = false,
                IsRotationEnabled = false, IsZoomEnabled = false, IsPanEnabled = false, IsMoveEnabled = false,
                IsInertiaEnabled = false, MSAA = D.MSAALevel.Four, FXAALevel = D.FXAALevel.None,
                // Weighted OIT's final pass destroys the alpha needed for WPF backdrops/video.
                OITRenderMode = D.OITRenderType.None, EnableRenderFrustum = true,
            };
            View.Items.Add(Mirror.Root);
            View.Loaded += (_, _) => ControlRenderHost();
            View.RenderExceptionOccurred += (_, e) => { e.Handled = true; Failed?.Invoke(e.Exception); };
            View.OnRendered += (_, _) => { RenderedFrames++; Rendered?.Invoke(); };
        }
        catch { _effects?.Dispose(); Mirror.Dispose(); throw; }
    }
    private void ControlRenderHost()
    {
        var host = View.RenderHost;
        if (_disposed || host == null || ReferenceEquals(host, _controlledHost)) return;
        if (_controlledHost != null) _controlledHost.StartRenderLoop -= StopAutomaticRendering;
        _controlledHost = host;
        // StopRendering detaches Helix's independent CompositionTarget callback,
        // while leaving UpdateAndRender available to our ordered frame cycle.
        host.StartRenderLoop += StopAutomaticRendering;
        host.StopRendering();
    }
    private void StopAutomaticRendering(object sender, EventArgs e) => ((D.IRenderHost)sender).StopRendering();
    internal void SetAntiAliasing(int quality, UrdfAntiAliasingMode mode = UrdfAntiAliasingMode.Msaa, int fxaaQuality = 3)
    {
        var level = mode == UrdfAntiAliasingMode.Fxaa ? D.MSAALevel.Disable : Math.Clamp(quality, 0, 3) switch
        {
            0 => D.MSAALevel.Disable, 1 => D.MSAALevel.Two, 3 => D.MSAALevel.Eight, _ => D.MSAALevel.Four
        };
        // Helix's FXAA writes opaque alpha, hiding composited images/video.
        // Apply FXAA in our alpha-preserving surface shader instead.
        var fxaa = D.FXAALevel.None;
        if (View.MSAA == level && View.FXAALevel == fxaa) return;
        View.MSAA = level;
        View.FXAALevel = fxaa;
        View.InvalidateRender();
    }
    internal void RenderFrame()
    {
        if (_disposed) return;
        ControlRenderHost();
        _controlledHost?.UpdateAndRender();
    }
    internal void Synchronize(Model3D source, PerspectiveCamera camera, double width, double height)
    {
        if (_disposed) return;
        long start = Stopwatch.GetTimestamp();
        Mirror.Synchronize(source);
        double aspect = width > 0 && height > 0 ? width / height : 16.0 / 9;
        // Helix uses a vertical FOV; the existing camera and all overlays use WPF's horizontal FOV.
        double vertical = VerticalFieldOfView(camera.FieldOfView, aspect);
        if (_camera.FieldOfView != vertical) _camera.FieldOfView = vertical;
        if (_camera.Position != camera.Position) _camera.Position = camera.Position;
        if (_camera.LookDirection != camera.LookDirection) _camera.LookDirection = camera.LookDirection;
        if (_camera.UpDirection != camera.UpDirection) _camera.UpDirection = camera.UpDirection;
        _camera.NearPlaneDistance = camera.NearPlaneDistance;
        _camera.FarPlaneDistance = double.IsFinite(camera.FarPlaneDistance) ? camera.FarPlaneDistance : 10000;
        View.InvalidateRender();
        SynchronizeMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    internal static double VerticalFieldOfView(double horizontal, double aspect) =>
        2 * Math.Atan(Math.Tan(horizontal * Math.PI / 360) / aspect) * 180 / Math.PI;
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_controlledHost != null) _controlledHost.StartRenderLoop -= StopAutomaticRendering;
        View?.RenderHost?.StopRendering();
        View?.Items.Clear();
        if ((object)View is IDisposable disposable) disposable.Dispose();
        else View?.RenderHost?.EndD3D();
        Mirror.Dispose();
        if (View != null) View.EffectsManager = null;
        _effects.Dispose();
    }
}
