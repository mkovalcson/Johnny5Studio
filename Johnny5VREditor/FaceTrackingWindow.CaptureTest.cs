using System.Windows;
using System.Windows.Controls;

namespace ServoAnimator;

internal sealed partial class FaceTrackingWindow
{
    private readonly Button _captureTestButton = new() { Content = "Test capture only (7 seconds)", HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 6),
        ToolTip = "Temporarily pauses tracking, warms up for 2 seconds and measures camera delivery for 5 seconds without face analysis, preview generation or rate pacing. Resumes tracking afterward if it was running." };
    private readonly TextBlock _captureTestReadout = new() { TextWrapping = TextWrapping.Wrap,
        Text = "Compare camera delivery with face tracking. No test has been run.",
        ToolTip = "Measured decoded frames delivered through the same capture backend. This does not verify unique sensor exposures. Driver FPS and pixel format are reported metadata. Results remain visible after tracking resumes." };
    private CancellationTokenSource _captureTestCancellation;
    private bool _captureWindowClosed;
    internal Func<int, int, Action<string>, CancellationToken, Task<FaceCaptureReport>> CaptureTestRunner { get; set; } = FaceTrackingClient.TestCaptureAsync;

    private async Task RunCaptureTestAsync(int camera, Panel cameraButtons)
    {
        if (_captureTestCancellation != null) { _captureTestCancellation.Cancel(); return; }
        if (camera is < 0 or > 20) throw new InvalidOperationException("Camera index must be 0–20.");
        bool resume = _client.Running;
        int previousCamera = _client.CameraIndex, previousRate = _client.RequestedFrameRate;
        bool previousMirror = _client.MirrorVideo;
        var baseline = _client.HasRecentResults ? _client.Latest : null;
        bool sameSession = previousCamera == camera && previousRate == _draft.FrameRate;
        var controls = cameraButtons.Children.OfType<Control>().Concat(_configurationActions.Children.OfType<Button>())
            .Select(c => (Control: c, Enabled: c.IsEnabled)).ToArray();
        bool previewEnabled = _preview.IsEnabled;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25)); _captureTestCancellation = cancel;
        foreach (var control in controls) control.Control.IsEnabled = false;
        _preview.IsEnabled = false; _captureTestButton.Content = "Cancel capture test";
        _client.Dispose(); _lastPreview = null; _image.Source = null;
        _captureTestReadout.Text = "Opening camera for capture-only test…";
        try
        {
            var result = await CaptureTestRunner(camera, _draft.FrameRate, text => _captureTestReadout.Text = text, cancel.Token);
            _captureTestReadout.Text = result.Describe(baseline, sameSession);
        }
        catch (OperationCanceledException) { _captureTestReadout.Text = "Capture test stopped (cancelled or timed out)."; }
        catch (Exception error) { _captureTestReadout.Text = "Capture test: " + error.Message; }
        finally
        {
            _captureTestCancellation = null; _captureTestButton.Content = "Test capture only (7 seconds)";
            foreach (var control in controls) control.Control.IsEnabled = control.Enabled;
            _preview.IsEnabled = previewEnabled;
            if (resume && !_captureWindowClosed && !Dispatcher.HasShutdownStarted)
            {
                try { _client.Start(previousCamera, previousRate, previousMirror); }
                catch (Exception error) { _message = "Capture test finished; tracking could not restart: " + error.Message; }
            }
            Refresh();
        }
    }
}
