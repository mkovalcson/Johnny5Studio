using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    // Video Clip layer: drawn by WPF behind the transparent 3D viewport and above the backdrop brush, so Contain
    // leaves bars in the backdrop's colour. Always muted: the clip's sound plays through VideoClipAudio.
    private readonly MediaElement _backdropVideo = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true,
        IsMuted = true, Volume = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    private string _backdropVideoPath;
    private double? _backdropVideoDuration;
    private bool _backdropVideoPlaying, _videoHidesStage;

    private void InitializeVideoLayer()
    {
        Children.Insert(Children.IndexOf(_viewport), _backdropVideo);
        _backdropVideo.MediaOpened += (_, _) => _backdropVideoDuration = _backdropVideo.NaturalDuration.HasTimeSpan ? _backdropVideo.NaturalDuration.TimeSpan.TotalSeconds : null;
        _backdropVideo.MediaFailed += (_, e) => { _status.Text = "Video Clip: " + (e.ErrorException?.Message ?? "video failed"); _backdropVideoPath = null; };
    }

    /// <summary>Timeline-driven video backdrop. <paramref name="playing"/> and <paramref name="rate"/> follow the editor transport.</summary>
    internal void SetTimelineVideo(VideoClipState state, bool playing, double rate, string configRoot)
    {
        // A hidden view (the docked one while undocked) does not decode; the next update after it shows catches up.
        string path = IsVisible||VrEditorMode.Active ? state?.Clip.Settings.FullPath(configRoot) : null;
        if (path != null && !File.Exists(path)) { _status.Text = "Video Clip: file not found: " + path; path = null; }
        if (path == null)
        {
            if (_backdropVideo.Visibility == Visibility.Visible || _backdropVideoPlaying) { PauseBackdropVideo(); _backdropVideo.Visibility = Visibility.Collapsed; }
            SetStageHiddenForVideo(false);
            return;
        }
        SetStageHiddenForVideo(true);
        if (!string.Equals(path, _backdropVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            _backdropVideoPlaying = false; _backdropVideoDuration = null; _backdropVideoPath = path;
            _backdropVideo.Source = new Uri(path); _backdropVideo.Pause();
        }
        var settings = state.Clip.Settings;
        _backdropVideo.Stretch = StretchFor(settings.Fit); _backdropVideo.Visibility = Visibility.Visible;
        var target = TimeSpan.FromSeconds(_backdropVideoDuration is > 0 ? Math.Min(state.MediaSeconds, _backdropVideoDuration.Value) : state.MediaSeconds);
        if (playing)
        {
            _backdropVideo.SpeedRatio = Math.Clamp(rate, .1, 4);
            if (!_backdropVideoPlaying) { _backdropVideo.Position = target; _backdropVideo.Play(); _backdropVideoPlaying = true; }
            else if (Math.Abs((_backdropVideo.Position - target).TotalSeconds) > .15) _backdropVideo.Position = target;
        }
        else
        {
            PauseBackdropVideo();
            if (Math.Abs((_backdropVideo.Position - target).TotalSeconds) > .02) _backdropVideo.Position = target;   // scrubbing shows the cursor's frame
        }
    }

    private void PauseBackdropVideo()
    {
        if (_backdropVideoPlaying) { _backdropVideo.Pause(); _backdropVideoPlaying = false; }
    }

    /// <summary>The view as an image for the editor API: the backdrop, then <paramref name="video"/> (a decoded picture,
    /// since the player's frames never reach RenderTargetBitmap) placed with <paramref name="fit"/>, then the 3D view.
    /// Overlays (TV screen media, beam sparkles, buttons) are not included.</summary>
    internal BitmapSource RenderSnapshot(int maxWidth, BitmapSource video, TvFit fit)
    {
        WaitForAppearancePreparation();
        using var rendererCapture = UseWpfForBitmapCapture();
        UpdateLensReflections(TimeSpan.Zero);
        double width = ActualWidth, height = ActualHeight;
        if (width < 2 || height < 2) throw new InvalidOperationException("The URDF view is not on screen.");
        double scale = maxWidth > 0 ? Math.Min(1, maxWidth / width) : 1;
        int pw = Math.Max(1, (int)Math.Round(width * scale)), ph = Math.Max(1, (int)Math.Round(height * scale));
        var robot = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        robot.Render(_viewport);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var area = new Rect(0, 0, pw, ph);
            dc.DrawRectangle(Background ?? Brushes.Black, null, area);
            if (video != null)
            {
                double vw = video.PixelWidth, vh = video.PixelHeight;
                double sx = pw / vw, sy = ph / vh, s = fit == TvFit.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
                var place = fit == TvFit.Stretch ? area : new Rect((pw - vw * s) / 2, (ph - vh * s) / 2, vw * s, vh * s);
                dc.PushClip(new RectangleGeometry(area)); dc.DrawImage(video, place); dc.Pop();
            }
            dc.DrawImage(robot, area);
        }
        var result = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze();
        return result;
    }

    /// <summary>A video replaces a 3D stage while it plays; the stage keeps its position and returns when the clip ends.</summary>
    private void SetStageHiddenForVideo(bool hidden)
    {
        if (_videoHidesStage == hidden) return;
        _videoHidesStage = hidden;
        ApplyStageHidden();
    }

    /// <summary>True while a playing video clip or a robot-only frame export keeps the 3D stage out of the scene.</summary>
    private bool StageHidden => _videoHidesStage || _exportHidesStage;

    private void ApplyStageHidden()
    {
        if (_sceneLightingRoot == null || _stageModel == null) return;
        if (StageHidden) _sceneLightingRoot.Children.Remove(_stageModel);
        else if (_stage != null && !_sceneLightingRoot.Children.Contains(_stageModel)) _sceneLightingRoot.Children.Add(_stageModel);
    }
}
