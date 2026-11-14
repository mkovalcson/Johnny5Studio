using System.Diagnostics;
using System.Text.Json;

namespace ServoAnimator;

internal sealed class FaceCaptureReport
{
    public string Phase { get; set; }
    public string Error { get; set; }
    public int RequestedFps { get; set; }
    public double CaptureFps { get; set; }
    public double CameraWaitMs { get; set; }
    public int Frames { get; set; }
    public double Seconds { get; set; }
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public double? ReportedCaptureFps { get; set; }
    public string PixelFormat { get; set; }
    public string Backend { get; set; }
    public bool? FormatRequestAccepted { get; set; }

    internal string Describe(FacePacket baseline, bool sameCameraAndTarget)
    {
        string driver = ReportedCaptureFps is double rate && double.IsFinite(rate) && rate > 0 ? $"{rate:0.##} fps reported" : "driver rate unavailable";
        string mode = $"{CaptureWidth} × {CaptureHeight} · {driver}";
        if (!string.IsNullOrWhiteSpace(PixelFormat)) mode += " · " + PixelFormat;
        if (!string.IsNullOrWhiteSpace(Backend)) mode += " · " + Backend;
        string text = $"Capture-only: {CaptureFps:0.0} fps · target {RequestedFps} fps\nCamera wait: {CameraWaitMs:0.0} ms avg · {Frames} frames / {Seconds:0.0} s\n{mode}";
        if (FormatRequestAccepted == false) text += $"\nMJPG request not accepted; capture reports {PixelFormat}.";
        bool comparable = sameCameraAndTarget && baseline?.ProcessedFps is > 0 &&
            baseline.CaptureWidth == CaptureWidth && baseline.CaptureHeight == CaptureHeight &&
            (baseline.PixelFormat == null || PixelFormat == null || baseline.PixelFormat == PixelFormat) &&
            (baseline.Backend == null || Backend == null || baseline.Backend == Backend) &&
            (!baseline.ReportedCaptureFps.HasValue || !ReportedCaptureFps.HasValue || Math.Abs(baseline.ReportedCaptureFps.Value - ReportedCaptureFps.Value) < 1);
        if (!comparable) return text + "\nNo matching recent tracking baseline. Start tracking in this mode, then repeat to compare.";
        text += $"\nTracking before test: {baseline.ProcessedFps:0.0} fps";
        if (CaptureFps < RequestedFps * .8)
            return text + "\nCapture alone is below target; investigate the camera mode or capture path.";
        if (CaptureFps >= RequestedFps * .9 && baseline.ProcessedFps < RequestedFps * .8)
            return text + "\nCapture reaches near target without analysis; the combined tracking pipeline is limiting throughput.";
        return text + "\nCompare these short averages; no clear camera-versus-tracking limit is established.";
    }
}

internal sealed partial class FaceTrackingClient
{
    internal static async Task<FaceCaptureReport> TestCaptureAsync(int camera, int frameRate, Action<string> progress, CancellationToken token)
    {
        var start = CreateStartInfo(camera, frameRate); start.ArgumentList.Add("--capture-test");
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start);
        var errors = process.StandardError.ReadToEndAsync();
        FaceCaptureReport result = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is string line)
            {
                var report = JsonSerializer.Deserialize<FaceCaptureReport>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (report?.Error != null) throw new InvalidOperationException(report.Error);
                if (report?.Phase == "complete") result = report;
                else if (report?.Phase == "warming") progress?.Invoke("Warming up camera · 2 seconds…");
                else if (report?.Phase == "measuring") progress?.Invoke("Measuring capture only · 5 seconds…");
            }
            await process.WaitForExitAsync(token);
            string error = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException("Capture test failed. " + error.Trim());
            if (result == null || result.Frames <= 0 || !double.IsFinite(result.Seconds) || result.Seconds <= 0 ||
                !double.IsFinite(result.CaptureFps) || result.CaptureFps <= 0)
                throw new InvalidOperationException("The capture test returned no valid measurement.");
            return result;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* It exited between the check and cancellation. */ }
            try { await errors.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}
