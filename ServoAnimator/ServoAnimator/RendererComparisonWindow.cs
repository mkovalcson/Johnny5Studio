using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed record RendererTrial(string Renderer, double FramesPerSecond, double MedianFrameMs, double P95FrameMs,
    double CpuMillisecondsPerSecond, int Frames, double Seconds, string Adapter, double AverageSyncMs);
internal sealed record RendererComparisonResult(DateTimeOffset MeasuredUtc, string Model, int Width, int Height, string Workload,
    string Measurement, RendererTrial[] Trials)
{
    internal double WpfFps => Trials.Where(t => t.Renderer == "WPF").Average(t => t.FramesPerSecond);
    internal double Dx11Fps => Trials.Where(t => t.Renderer == "Direct3D 11").Average(t => t.FramesPerSecond);
    internal string Summary => $"WPF: {WpfFps:0.0} fps · Direct3D 11: {Dx11Fps:0.0} fps\n" +
        $"Observed frame-rate change: {(Dx11Fps / WpfFps - 1) * 100:+0.0;-0.0;0.0}%\n" +
        "These are visible-view render-callback rates, limited by display refresh and other running work; they are not completed-GPU timings.";
}

internal static class RendererComparison
{
    // Balanced order reduces cache/order bias. Warm-up and renderer construction are excluded.
    internal static async Task<RendererComparisonResult> Run(RobotHeadView view, Action<string> status, CancellationToken token,
        double warmupSeconds = 2, double measureSeconds = 4)
    {
        var frame = view.CaptureCurrentFraming();
        var trials = new List<RendererTrial>();
        try
        {
            foreach (var renderer in new[] { UrdfRenderer.Wpf, UrdfRenderer.Direct3D11, UrdfRenderer.Direct3D11, UrdfRenderer.Wpf })
            {
                token.ThrowIfCancellationRequested();
                status?.Invoke($"Trial {trials.Count + 1}/4 · {(renderer == UrdfRenderer.Wpf ? "WPF" : "Direct3D 11")} · warming up…");
                var completion = new TaskCompletionSource<RendererTrial>(TaskCreationOptions.RunContinuationsAsynchronously);
                var clock = new Stopwatch(); var samples = new List<double>();
                double previous = 0, begin = 0, syncTotal = 0; int frames = 0; TimeSpan cpuBegin = default;
                using var process = Process.GetCurrentProcess();
                void Tick(TimeSpan frameTime)
                {
                    double elapsed = clock.Elapsed.TotalSeconds;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (renderer != view.ActiveRenderer) throw new InvalidOperationException("The requested renderer stopped. " + view.RendererError);
                        var pose = frame.Clone(); pose.Yaw += .26 * Math.Sin(elapsed * 1.4); view.ApplyFraming(pose);
                        view.SetServo(ServoNames.NeckTurn, 30 * Math.Sin(elapsed * 2)); view.SetMouth(.4 + .3 * Math.Sin(elapsed * 4));
                        if (elapsed < warmupSeconds) return;
                        if (begin == 0)
                        {
                            begin = previous = elapsed; cpuBegin = process.TotalProcessorTime;
                            status?.Invoke($"Trial {trials.Count + 1}/4 · measuring for {measureSeconds:0} seconds…"); return;
                        }
                        samples.Add((elapsed - previous) * 1000); previous = elapsed; frames++;
                        syncTotal += view.RendererSyncMilliseconds;
                        if (elapsed - begin < measureSeconds) return;
                        double seconds = elapsed - begin;
                        if (samples.Count < 4) throw new InvalidOperationException("Not enough rendered frames for a meaningful comparison.");
                        var sorted = samples.Order().ToArray();
                        completion.TrySetResult(new(renderer == UrdfRenderer.Wpf ? "WPF" : "Direct3D 11", frames / seconds,
                            Percentile(sorted, .5), Percentile(sorted, .95), (process.TotalProcessorTime - cpuBegin).TotalMilliseconds / seconds,
                            frames, seconds, view.RendererAdapter, syncTotal / frames));
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                }
                UrdfRenderLoop.Current.AddTargets(Tick);
                using var registration = token.Register(() => completion.TrySetCanceled(token));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(warmupSeconds + measureSeconds + 30));
                using var watchdog = timeout.Token.Register(() => completion.TrySetException(new TimeoutException("The view stopped rendering; keep the comparison window visible.")));
                try
                {
                    if (!view.SelectRenderer(renderer)) throw new InvalidOperationException("DX11 is unavailable: " + view.RendererError);
                    view.ApplyFraming(frame); clock.Start();
                    trials.Add(await completion.Task);
                }
                finally { UrdfRenderLoop.Current.RemoveTargets(Tick); }
            }
            return new(DateTimeOffset.UtcNow, ConfigPathService.Reference(ConfigPathService.CurrentRoot, view.LoadedModelFile),
                (int)view.ActualWidth, (int)view.ActualHeight, "Camera orbit, neck motion and mouth lights; same model, backdrop, size and trajectory in WPF/DX11/DX11/WPF order.",
                "Wall-clock intervals between unique WPF render callbacks. Refresh-limited; not GPU fence/presentation timing. CPU includes the whole process.", trials.ToArray());
        }
        finally { view.SelectRenderer(UrdfRenderer.Wpf); view.ApplyFraming(frame); }
    }
    internal static double Percentile(double[] sorted, double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
}

internal sealed class RendererComparisonWindow : Window
{
    private readonly RobotHeadView _view = new(deferModelLoad: true);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CancellationTokenSource _cancel = new();
    private bool _running;
    internal RendererComparisonResult Result { get; private set; }
    internal RendererComparisonWindow(RobotHeadView source)
    {
        Title = "Compare URDF renderers"; Width = 1020; Height = 800; MinWidth = 800; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var header = new TextBlock { Text = "Compare WPF and Direct3D 11", FontSize = 20, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var intro = new TextBlock { Text = "Uses a separate copy of the current robot and background. Pause other playback and keep this window visible. The test moves only this preview; it does not send hardware commands or edit the sequence. WPF remains the default.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) };
        DockPanel.SetDock(intro, Dock.Top); root.Children.Add(intro);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var run = new Button { Content = "Run comparison", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4) };
        var save = new Button { Content = "Save results…", IsEnabled = false, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4) };
        var close = new Button { Content = "Close", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4) };
        buttons.Children.Add(run); buttons.Children.Add(save); buttons.Children.Add(close); close.Click += (_, _) => Close();
        root.Children.Add(new Border { Child = _view, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray });
        _view.PrepareRendererComparison(source);
        run.Click += async (_, _) =>
        {
            if (_running) return; _running = true; run.IsEnabled = save.IsEnabled = false;
            try { Result = await RendererComparison.Run(_view, text => _status.Text = text, _cancel.Token); _status.Text = Result.Summary; save.IsEnabled = true; }
            catch (OperationCanceledException) { }
            catch (Exception error) { _status.Text = "Comparison stopped: " + error.Message; }
            finally { _running = false; run.IsEnabled = true; }
        };
        save.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.SaveFileDialog { Title = "Save renderer comparison", Filter = "JSON results|*.json", FileName = "Renderer-comparison-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
            if (picker.ShowDialog(this) != true) return;
            try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(Result, ProjectPackage.JsonOptions)); _status.Text = Result.Summary + "\nSaved: " + ConfigPathService.Display(picker.FileName); }
            catch (Exception error) { _status.Text = error.Message; }
        };
        Closing += (_, _) => _cancel.Cancel();
        Closed += (_, _) => { _view.SelectRenderer(UrdfRenderer.Wpf); _cancel.Dispose(); };
    }
}
