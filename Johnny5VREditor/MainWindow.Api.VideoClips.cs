using System.IO;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

public partial class MainWindow
{
    private const int MaxApiVideoFrames = 12;

    /// <summary>Video Clips for get_state and get_video_clips, in start order.</summary>
    private object[] VideoClipsApiState() => _videoSpans.Select((c, index) => (object)new
    {
        index, file = c.Name, path = c.Settings.Path, startSeconds = c.Start, endSeconds = c.End,
        skipToSeconds = c.Settings.SkipSeconds, endAtSeconds = c.Settings.EndSeconds, sourceSeconds = c.Settings.SourceSeconds,
        fit = c.Settings.Fit.ToString(), volume = c.Settings.Volume, muted = c.Settings.Muted,
        fileFound = File.Exists(c.Settings.FullPath(ConfigRoot)),
    }).ToArray();

    private static int ApiImageWidth(EditorApiRequest request, int fallback) => Math.Clamp(request.MaxWidth ?? fallback, 64, 1280);

    /// <summary>get_video_frames: the Video Clip pictures at timeline times (explicit times, or start/end with an optional
    /// interval), so an AI can see what plays behind the robot. Read-only; decoded off the UI thread.</summary>
    private object GetVideoFramesApi(EditorApiRequest request)
    {
        List<double> times;
        if (request.Times is { Count: > 0 }) times = request.Times.ToList();
        else
        {
            if (request.StartSeconds is not double start || request.EndSeconds is not double end || end < start)
                throw new InvalidOperationException("Supply times, or startSeconds and endSeconds (end at or after start), with an optional interval.");
            double interval = request.Interval ?? (end > start ? (end - start) / 5 : 1);
            if (!double.IsFinite(interval) || interval <= 0) throw new InvalidOperationException("interval must be more than 0 seconds.");
            times = new();
            for (int i = 0; start + i * interval <= end + 1e-9 && times.Count <= MaxApiVideoFrames; i++) times.Add(start + i * interval);
        }
        if (times.Count > MaxApiVideoFrames) throw new InvalidOperationException($"Ask for at most {MaxApiVideoFrames} frames at a time.");
        if (times.Any(t => !double.IsFinite(t) || t < 0)) throw new InvalidOperationException("Times must be zero or more seconds.");
        int width = ApiImageWidth(request, 480);
        var at = times.Select(t => (Time: ServoCommand.TimeKey(t), State: VideoClipTimeline.At(_videoSpans, t))).ToArray();
        var decoded = new Dictionary<(VideoClipSpan, double), VideoFrames.Frame>();
        var failures = new Dictionary<VideoClipSpan, string>();
        foreach (var clip in at.Where(a => a.State != null).GroupBy(a => a.State.Clip, a => a.State.MediaSeconds))
        {
            string path = clip.Key.Settings.FullPath(ConfigRoot);
            var positions = clip.Distinct().OrderBy(s => s).ToList();
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Video file not found: " + path);
                var frames = Task.Run(() => VideoFrames.Read(path, positions)).GetAwaiter().GetResult();
                for (int i = 0; i < positions.Count; i++) decoded[(clip.Key, positions[i])] = frames[i];
            }
            catch (Exception ex) { failures[clip.Key] = ex.Message; }
        }
        var results = at.Select(a =>
        {
            if (a.State == null) return (object)new { timelineSeconds = a.Time, image = (string)null, note = "No video clip plays at this time." };
            int index = _videoSpans.IndexOf(a.State.Clip);
            if (failures.TryGetValue(a.State.Clip, out var error))
                return new { timelineSeconds = a.Time, clipIndex = index, file = a.State.Clip.Name, image = (string)null, note = error };
            var frame = decoded[(a.State.Clip, a.State.MediaSeconds)];
            return new { timelineSeconds = a.Time, clipIndex = index, file = a.State.Clip.Name, videoSeconds = Math.Round(frame.Seconds, 3),
                width = Math.Min(width, frame.Width), image = VideoFrames.ToJpegDataUrl(VideoFrames.ToBitmap(frame, width)) };
        }).ToArray();
        return new { ok = true, cursorSeconds = _cursorTime, frames = results };
    }

    /// <summary>get_view_snapshot: the URDF view at the cursor as an image, robot over the Video Clip picture (or the
    /// backdrop), to compare the robot's pose with the video.</summary>
    private object GetViewSnapshotApi(EditorApiRequest request)
    {
        var view = ApiUrdfView ?? throw new InvalidOperationException("Open a URDF view first.");
        var state = VideoClipTimeline.At(_videoSpans, _cursorTime);
        BitmapSource video = null; string note = null; double? videoSeconds = null;
        if (state != null)
        {
            string path = state.Clip.Settings.FullPath(ConfigRoot);
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Video file not found: " + path);
                var frame = Task.Run(() => VideoFrames.Read(path, new[] { state.MediaSeconds })).GetAwaiter().GetResult()[0];
                video = VideoFrames.ToBitmap(frame, 0); videoSeconds = Math.Round(frame.Seconds, 3);
            }
            catch (Exception ex) { note = "Video picture unavailable: " + ex.Message; }
        }
        var image = view.RenderSnapshot(ApiImageWidth(request, 800), video, state?.Clip.Settings.Fit ?? TvFit.Contain);
        return new
        {
            ok = true, cursorSeconds = _cursorTime,
            videoClip = state == null ? null : new { clipIndex = _videoSpans.IndexOf(state.Clip), file = state.Clip.Name, videoSeconds },
            note = note ?? (state == null ? "No video clip plays at the cursor; the robot is shown over the backdrop." : null),
            width = image.PixelWidth, height = image.PixelHeight, image = VideoFrames.ToJpegDataUrl(image),
        };
    }
}
