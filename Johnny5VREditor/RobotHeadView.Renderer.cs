using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private Dx11Viewport _dx11;
    private bool _rendererSubscribed;
    private bool _lensReflectionsSubscribed;
    private int _bitmapCaptureDepth;
    internal UrdfRenderer ActiveRenderer => _dx11 != null ? UrdfRenderer.Direct3D11 : UrdfRenderer.Wpf;
    internal string RendererAdapter => _dx11?.AdapterName;
    internal double RendererSyncMilliseconds => _dx11?.SynchronizeMilliseconds ?? 0;
    internal long Dx11RenderedFrames => _dx11?.RenderedFrames ?? 0;
    internal string RendererError { get; private set; }
    internal event Action RendererFrameCompleted;

    private void InitializeRendererLifecycle()
    {
        if(VrEditorMode.Active)return;
        Loaded += (_, _) => { ApplyConfiguredRenderer(); UpdateRendererSubscription(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ApplyConfiguredRenderer(); else _scene?.DeferWeatheringPreparation(); UpdateRendererSubscription(); };
        Unloaded += (_, _) =>
        {
            _scene?.DeferWeatheringPreparation();
            SelectRenderer(UrdfRenderer.Wpf);
            UrdfRenderLoop.Current.RemoveSynchronize(UpdateLensReflections);
            _lensReflectionsSubscribed = false;
        };
    }
    internal bool SelectRenderer(UrdfRenderer renderer)
    {
        if(VrEditorMode.Active){ApplyRenderAppearance();return true;}
        if (renderer == ActiveRenderer) { ApplyRenderAppearance(); return true; }
        RendererError = null;
        if (renderer == UrdfRenderer.Direct3D11)
        {
            Dx11Viewport created = null;
            try
            {
                created = new Dx11Viewport();
                created.Failed += error => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_dx11, created)) FallbackRenderer(error); });
                created.Rendered += () => RendererFrameCompleted?.Invoke();
                created.Synchronize(_sceneLightingRoot, _camera, ActualWidth, ActualHeight);
                // Keep the original viewport measured for projection/pose controls, but stop drawing it.
                Children.Insert(Children.IndexOf(_viewport) + 1, created.View);
                _dx11 = created; _viewport.Visibility = Visibility.Hidden;
                _status.Text = "Direct3D 11 (experimental)\n" + created.AdapterName;
            }
            catch (Exception error)
            {
                if (created != null) { Children.Remove(created.View); created.Dispose(); }
                _dx11 = null; _viewport.Visibility = Visibility.Visible;
                RendererError = error.Message; _status.Text = "Using WPF: DX11 unavailable. " + error.Message;
            }
        }
        else
        {
            var previous = _dx11; _dx11 = null;
            _viewport.Visibility = Visibility.Visible;
            if (previous != null) { Children.Remove(previous.View); previous.Dispose(); }
            _status.Text = "WPF renderer\nDrag to orbit · wheel to zoom";
        }
        ApplyRenderAppearance();
        UpdateRendererSubscription();
        return ActiveRenderer == renderer;
    }
    private void FallbackRenderer(Exception error)
    {
        SelectRenderer(UrdfRenderer.Wpf);
        RendererError = error.Message; _status.Text = "DX11 stopped; using WPF. " + error.Message;
    }
    private void UpdateRendererSubscription()
    {
        bool lensesNeeded = IsLoaded && IsVisible && _bitmapCaptureDepth == 0;
        if(lensesNeeded != _lensReflectionsSubscribed)
        {
            _lensReflectionsSubscribed = lensesNeeded;
            if(lensesNeeded) UrdfRenderLoop.Current.AddSynchronize(UpdateLensReflections);
            else UrdfRenderLoop.Current.RemoveSynchronize(UpdateLensReflections);
        }
        bool needed = _dx11 != null && IsLoaded && IsVisible && _bitmapCaptureDepth == 0;
        if (needed == _rendererSubscribed) return;
        _rendererSubscribed = needed;
        if (needed)
        {
            UrdfRenderLoop.Current.AddSynchronize(SynchronizeRenderer);
            UrdfRenderLoop.Current.AddRender(RenderDirect3D);
        }
        else
        {
            UrdfRenderLoop.Current.RemoveSynchronize(SynchronizeRenderer);
            UrdfRenderLoop.Current.RemoveRender(RenderDirect3D);
        }
    }
    private void UpdateLensReflections(TimeSpan frameTime) =>
        _scene?.UpdateIrisLensReflections(_camera.Position,_presentationModel?.Transform,
            _rendererLights.Select(item=>item.Light),
            (_urdfConfiguration.Rendering ?? new()).Profile(ActiveRenderer).LensReflectivity);
    private void SynchronizeRenderer(TimeSpan frameTime)
    {
        try { UpdateRenderEffectSize(); _dx11?.Synchronize(_sceneLightingRoot, _camera, ActualWidth, ActualHeight); }
        catch (Exception error) { FallbackRenderer(error); }
    }
    private void RenderDirect3D(TimeSpan frameTime)
    {
        try { _dx11?.RenderFrame(); }
        catch (Exception error) { FallbackRenderer(error); }
    }
    // RenderTargetBitmap cannot reliably capture an interoperated Direct3D surface.
    // Thumbnails and API snapshots temporarily use WPF with the active appearance profile.
    // Robot Frames captures its window instead and uses the configured renderer.
    private IDisposable UseWpfForBitmapCapture()
    {
        _bitmapCaptureDepth++;
        if (_dx11 != null) { _dx11.View.Visibility = Visibility.Hidden; _viewport.Visibility = Visibility.Visible; }
        UpdateRendererSubscription();
        return new RendererCaptureScope(this);
    }
    private sealed class RendererCaptureScope(RobotHeadView view) : IDisposable
    {
        public void Dispose()
        {
            view._bitmapCaptureDepth--;
            if (view._dx11 != null && view._bitmapCaptureDepth == 0) { view._viewport.Visibility = Visibility.Hidden; view._dx11.View.Visibility = Visibility.Visible; }
            view.UpdateRendererSubscription();
        }
    }
    internal void PrepareRendererComparison(RobotHeadView source)
    {
        ConfigureMeetingPreview(source, ConfigPathService.CurrentRoot);
        InstallModel(source.LoadedModelFile, UrdfScene.Load(source.LoadedModelFile), source.CaptureCurrentFraming());
        SetTimelineBackground(source.CaptureBackground());
        ApplyFraming(source.CaptureCurrentFraming()); MirrorMeetingPose(source);
        _poseOverlay.Visibility = _bottomControls.Visibility = _status.Visibility = _fps.Visibility = _verticalResizeHandle.Visibility = Visibility.Collapsed;
    }
}
