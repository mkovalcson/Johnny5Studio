using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    internal int VrAppearance=>_urdfConfiguration.Rendering.RobotFinish==UrdfRobotFinish.GoldJohnny?4:_urdfConfiguration.Rendering.WeatheringLevel;
    internal int VrAccessory=>!HasLaser?0:ToolboxShown?2:LaserHidden?0:1;
    internal void SetVrAppearance(int value)
    {
        _urdfConfiguration.Rendering.RobotFinish=value==4?UrdfRobotFinish.GoldJohnny:UrdfRobotFinish.Original;
        _urdfConfiguration.Rendering.WeatheringLevel=value==4?0:value;
        ApplyRenderAppearance();
    }
    internal void SetVrAccessory(int value)
    {
        _urdfConfiguration.Rendering.DefaultAccessory=value==1?UrdfAccessory.Laser:value==2?UrdfAccessory.Toolbox:UrdfAccessory.None;
        ApplyAccessoryDefault();
    }
    private (Light Light, Color Color)[] _rendererLights = Array.Empty<(Light, Color)>();
    private UrdfToneEffect _wpfTone, _dx11Tone;
    private Task _observedWeathering = Task.CompletedTask;
    internal Task AppearancePreparation => _scene?.WeatheringPreparation ?? Task.CompletedTask;
    // Explicit image/frame exports need the selected finish, rather than the
    // previous preview. Pump the dispatcher while waiting so the UI and worker
    // publication stay responsive; ordinary preview updates never wait here.
    internal void WaitForAppearancePreparation()
    {
        if(_scene!=null)
        {
            var rendering=_urdfConfiguration.Rendering;
            _scene.RequestWeathering(rendering.WeatheringLevel,rendering.RobotFinish==UrdfRobotFinish.GoldJohnny);
        }
        while(true)
        {
            var pending=AppearancePreparation;
            if(!pending.IsCompleted)
            {
                var frame=new System.Windows.Threading.DispatcherFrame();
                _=pending.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            pending.GetAwaiter().GetResult();
            if(ReferenceEquals(pending,AppearancePreparation))return;
        }
    }
    private void RequestRenderWeathering(UrdfRenderingSettings rendering)
    {
        if(_scene==null)return;
        if(!VrEditorMode.Active&&(!IsLoaded||!IsVisible)&&rendering.WeatheringLevel>0&&rendering.RobotFinish==UrdfRobotFinish.Original)
        {
            _scene.DeferWeatheringPreparation();
            return;
        }
        var pending=_scene.RequestWeathering(rendering.WeatheringLevel,rendering.RobotFinish==UrdfRobotFinish.GoldJohnny);
        if(ReferenceEquals(pending,_observedWeathering))return;
        _observedWeathering=pending;
        ObserveWeathering(pending);
    }
    private async void ObserveWeathering(Task pending)
    {
        try { await pending; }
        catch(Exception error)
        {
            if(Dispatcher.HasShutdownStarted)return;
            System.Diagnostics.Trace.TraceError("URDF weathering preparation: "+error);
            try { await Dispatcher.InvokeAsync(() =>
            { if(ReferenceEquals(pending,_observedWeathering))_status.Text="Unable to prepare weathering. Try another level."; }); }
            catch(OperationCanceledException) when(Dispatcher.HasShutdownStarted) { }
        }
    }

    private void CaptureRendererLighting(Model3DGroup root) =>
        _rendererLights = root.Children.OfType<Light>().Select(light => (light, light.Color)).ToArray();

    private void ApplyConfiguredRenderer()
    {
        if (IsLoaded && IsVisible) SelectRenderer((_urdfConfiguration.Rendering ?? new()).SelectedRenderer);
        ApplyRenderAppearance();
    }
    private void ApplyRenderAppearance()
    {
        var rendering = _urdfConfiguration.Rendering ??= new(); rendering.Normalize();
        RequestRenderWeathering(rendering);
        ApplyAccessoryDefault();
        var profile = rendering.Profile(ActiveRenderer);
        _dx11?.SetAntiAliasing(profile.AntiAliasing, profile.AntiAliasingMode, profile.FxaaQuality);
        foreach (var (light, original) in _rendererLights)
        {
            double multiplier = light is AmbientLight ? profile.AmbientLight : profile.KeyLight;
            var color = multiplier == 1 ? original : Color.FromScRgb(original.ScA,
                (float)Math.Clamp(original.ScR * multiplier, 0, 1),
                (float)Math.Clamp(original.ScG * multiplier, 0, 1),
                (float)Math.Clamp(original.ScB * multiplier, 0, 1));
            if (light.Color != color) light.Color = color;
        }
        UpdateLensReflections(TimeSpan.Zero);
        bool fxaa = ActiveRenderer == UrdfRenderer.Direct3D11 && profile.AntiAliasingMode == UrdfAntiAliasingMode.Fxaa && profile.FxaaQuality > 0;
        if (profile.NeutralTone && !fxaa)
        {
            _viewport.Effect = null;
            if (_dx11 != null) _dx11.View.Effect = null;
            return;
        }
        // Tone applies only to the 3D surface, retaining transparent backgrounds
        // and leaving video, TV media, text and pose controls untouched.
        _wpfTone ??= new(); _wpfTone.Apply(profile, fxaa); _viewport.Effect = _wpfTone;
        if (_dx11 != null) { _dx11Tone ??= new(); _dx11Tone.Apply(profile, fxaa); _dx11.View.Effect = _dx11Tone; }
        UpdateRenderEffectSize();
    }
    private void UpdateRenderEffectSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var step = new System.Windows.Point(1 / Math.Max(1, ActualWidth * dpi.DpiScaleX), 1 / Math.Max(1, ActualHeight * dpi.DpiScaleY));
        if (_wpfTone != null) _wpfTone.PixelStep = step;
        if (_dx11Tone != null) _dx11Tone.PixelStep = step;
    }
}
