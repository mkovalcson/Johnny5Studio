using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    // TV layer: images and video are drawn by WPF above the 3D viewport, aligned to the
    // backdrop TV's projected screen, instead of being rendered as a 3D texture.
    private readonly Grid _tvLayer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _tvFrame = new() { ClipToBounds = true, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Image _tvImage = new() { Visibility = Visibility.Collapsed };
    private readonly MediaElement _tvVideo = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Visibility = Visibility.Collapsed };
    private TvState _tvState = TvState.Inactive;
    private string _tvImagePath, _tvVideoPath;
    private double? _tvVideoDuration;
    private bool _tvVideoPlaying, _tvVideoReady, _tvAudible = true;
    private TvShowCommand _tvOpeningShow;
    private readonly DispatcherTimer _tvWarmup = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
    private TimeSpan _tvWarmupStart;
    private long _tvWarmupDeadline, _tvNextResync;
    private string _tvConfigRoot;
    private bool _tvTransportPlaying;
    private double _tvTransportRate;
    private int _tvRobotIndex = -1;
    private BitmapSource[] _tvBufferedFrames;
    private bool _tvPicturesPending;

    internal void SetTvFrameBuffer(BitmapSource[] frames, bool pending)
    {
        _tvBufferedFrames = frames;
        _tvPicturesPending = pending;
    }

    private void InitializeTvLayer()
    {
        var content = new Grid(); content.Children.Add(_tvImage); content.Children.Add(_tvVideo);
        _tvFrame.Child = content; _tvLayer.Children.Add(_tvFrame);
        Children.Insert(Children.IndexOf(_viewport) + 1, _tvLayer);
        _tvVideo.MediaOpened += (_, _) =>
        {
            _tvVideoDuration = _tvVideo.NaturalDuration.HasTimeSpan ? _tvVideo.NaturalDuration.TimeSpan.TotalSeconds : null;
            var show = _tvOpeningShow;
            if (show == null) return;
            BeginTvWarmup(show);
        };
        _tvWarmup.Tick += (_, _) =>
        {
            if (_tvVideo.Position >= _tvWarmupStart + TimeSpan.FromMilliseconds(80) ||
                Environment.TickCount64 >= _tvWarmupDeadline) FinishTvWarmup();
        };
        _tvVideo.MediaEnded += (_, _) =>
        {
            if (_tvWarmup.IsEnabled) FinishTvWarmup();
            else _tvVideoPlaying = false;
        };
        Unloaded += (_, _) =>
        {
            if (_tvWarmup.IsEnabled)
            {
                _tvWarmup.Stop();
                _tvVideo.Pause();
                _tvVideoPath = null;
            }
        };
        _tvVideo.MediaFailed += (_, e) =>
        {
            _tvWarmup.Stop();
            _tvVideoReady = _tvVideoPlaying = false;
            _tvVideo.Visibility = Visibility.Collapsed;
            _status.Text = "TV Show: " + (e.ErrorException?.Message ?? "video failed");
        };
        SizeChanged += (_, _) => { if (_tvState.Active) UpdateCamera(); };
    }

    /// <summary>The backdrop TV screen in world coordinates, or null without a 3D stage or screen link.</summary>
    private TvScreen FindTvScreen(string link)
    {
        if (_stageScene == null || _stageModel == null || string.IsNullOrWhiteSpace(link)) return null;
        var points = _stageScene.GetWorldVisualBoundsPoints(new[] { link });
        if (points.Count < 8) return null;
        var toWorld = _stageModel.Transform ?? Transform3D.Identity;
        var viewer = toWorld.Transform(new Point3D(0, 0, 0));
        return TvScreen.FromBoxCorners(points.Take(8).Select(toWorld.Transform).ToArray(), viewer);
    }

    internal bool HasTvScreen(string link) => FindTvScreen(link) != null;

    private double ViewAspect => ActualWidth > 1 && ActualHeight > 1 ? ActualWidth / ActualHeight : 16.0 / 9;

    private bool TryTvShot(TvFrontCommand front, out Point3D position, out Point3D target, out TvScreen screen)
    {
        position = target = default;
        screen = front == null ? null : FindTvScreen(front.Screen);
        if (screen == null) return false;
        double distance = screen.FitDistance(Math.Tan(_camera.FieldOfView * Deg * .5), ViewAspect, front.Fill);
        target = screen.Center; position = screen.Center + screen.Normal * distance;
        return true;
    }

    /// <summary>Called from UpdateCamera: blend the normal camera toward the TV shot and keep the
    /// robot hidden while the camera passes it or faces the TV.</summary>
    private void ApplyTvCamera(ref Point3D position, ref Point3D target)
    {
        bool hideRobot = false;
        if (_tvState.Active && TryTvShot(_tvState.Front, out var shot, out var shotTarget, out _))
        {
            double w = _tvState.Weight;
            position += (shot - position) * w; target += (shotTarget - target) * w;
            hideRobot = _tvState.Arrived || CameraInsideRobot(position);
        }
        SetRobotHidden(hideRobot || _beamHidesRobot);   // the beam effect can also hide the robot
    }

    private bool CameraInsideRobot(Point3D camera)
    {
        if (_scene == null || _presentationModel == null) return false;
        var bounds = _scene.RootModel.Bounds;
        if (bounds.IsEmpty) return false;
        var t = _presentationModel.Transform ?? Transform3D.Identity;
        var centre = t.Transform(new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2));
        double radius = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length / 2;
        return (camera - centre).Length < radius + .15;
    }

    /// <summary>Detaching the robot also stops WPF re-rendering the 3D scene for servo updates while
    /// the static backdrop is on screen, which frees the UI thread for video.</summary>
    private void SetRobotHidden(bool hidden)
    {
        if (_sceneLightingRoot == null || _presentationModel == null) return;
        bool present = _sceneLightingRoot.Children.Contains(_presentationModel);
        if (hidden && present) { _tvRobotIndex = _sceneLightingRoot.Children.IndexOf(_presentationModel); _sceneLightingRoot.Children.Remove(_presentationModel); }
        else if (!hidden && !present)
        {
            int index = _tvRobotIndex >= 0 && _tvRobotIndex <= _sceneLightingRoot.Children.Count ? _tvRobotIndex : _sceneLightingRoot.Children.Count;
            _sceneLightingRoot.Children.Insert(index, _presentationModel);
        }
    }

    private bool TryProjectViewPoint(Point3D world, out Point screen)
    {
        screen = default;
        double width = ActualWidth, height = ActualHeight;
        var forward = _camera.LookDirection; if (width <= 1 || height <= 1 || forward.LengthSquared < 1e-12) return false;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, _camera.UpDirection); if (right.LengthSquared < 1e-12) return false; right.Normalize();
        var up = Vector3D.CrossProduct(right, forward); up.Normalize();
        var q = world - _camera.Position; double depth = Vector3D.DotProduct(q, forward);
        if (depth <= .001) return false;
        double tanH = Math.Tan(_camera.FieldOfView * Deg * .5), tanV = tanH * height / width;
        screen = new Point((Vector3D.DotProduct(q, right) / (depth * tanH) + 1) * width / 2, (1 - Vector3D.DotProduct(q, up) / (depth * tanV)) * height / 2);
        return double.IsFinite(screen.X) && double.IsFinite(screen.Y);
    }

    private void LayoutTvOverlay()
    {
        if (!_tvState.Active || _tvState.OverlayOpacity <= 0 || !TryTvShot(_tvState.Front, out _, out _, out var screen))
        { _tvLayer.Visibility = Visibility.Collapsed; return; }
        var projected = new List<Point>();
        foreach (var corner in screen.FaceCorners())
            if (TryProjectViewPoint(corner, out var p)) projected.Add(p);
        if (projected.Count < 4) { _tvLayer.Visibility = Visibility.Collapsed; return; }
        double left = projected.Min(p => p.X), top = projected.Min(p => p.Y);
        _tvFrame.Margin = new Thickness(left, top, 0, 0);
        _tvFrame.Width = Math.Max(1, projected.Max(p => p.X) - left); _tvFrame.Height = Math.Max(1, projected.Max(p => p.Y) - top);
        _tvFrame.Opacity = _tvState.OverlayOpacity; _tvLayer.Visibility = Visibility.Visible;
    }

    /// <summary>Timeline-driven TV state. <paramref name="playing"/> and <paramref name="rate"/> follow the
    /// editor transport; only the <paramref name="audible"/> view plays video sound.</summary>
    internal void SetTimelineTv(TvState state, bool playing, double rate, string configRoot, bool audible)
    {
        state ??= TvState.Inactive;
        if (!state.Active && !_tvState.Active) return;
        _tvState = state; _tvAudible = audible;
        _tvConfigRoot = configRoot;
        _tvTransportPlaying = playing;
        _tvTransportRate = rate;
        UpdateCamera();                                    // applies the blend, robot visibility and overlay layout
        var show = state.OverlayOpacity > 0 ? state.Show : null;
        if (show == null || show.IsBlank) { ShowTvImage(null, TvFit.Contain); PauseTvVideo(); _tvVideo.Visibility = Visibility.Collapsed; return; }
        string path = show.FullPath(configRoot);
        if (!string.Equals(path, _tvVideoPath, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, _tvImagePath, StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
        { _status.Text = "TV Show: file not found: " + path; PauseTvVideo(); return; }
        if (!show.IsVideo) { PauseTvVideo(); _tvVideo.Visibility = Visibility.Collapsed; ShowTvImage(path, show.Fit); return; }

        SyncTvVideo(path, show, state.MediaSeconds, playing, rate);
    }

    private static Stretch StretchFor(TvFit fit) => fit switch { TvFit.Cover => Stretch.UniformToFill, TvFit.Stretch => Stretch.Fill, _ => Stretch.Uniform };

    private void ShowTvImage(string path, TvFit fit)
    {
        if (path == null) { _tvImage.Visibility = Visibility.Collapsed; return; }
        if (!string.Equals(path, _tvImagePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = 1920; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                _tvImage.Source = bitmap; _tvImagePath = path;
            }
            catch (Exception ex) { _status.Text = "TV Show: " + ex.Message; _tvImagePath = null; _tvImage.Visibility = Visibility.Collapsed; return; }
        }
        _tvImage.Stretch = StretchFor(fit); _tvImage.Visibility = Visibility.Visible;
    }

    internal void PreloadTvVideo(TvShowCommand show, string configRoot)
    {
        string path = show.FullPath(configRoot);
        if (_tvVideo.Visibility == Visibility.Visible || string.Equals(path, _tvVideoPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return;
        OpenTvVideo(path, show);
    }

    private void BeginTvWarmup(TvShowCommand show)
    {
        var (position, _) = TvTimeline.VideoPosition(show, show.StartSeconds, _tvVideoDuration);
        _tvWarmupStart = TimeSpan.FromSeconds(position);
        _tvWarmupDeadline = Environment.TickCount64 + 2000;
        // MediaOpened also fires for a paused poster frame. Run the muted
        // decoder before the cue so playback, including its audio clock, is warm.
        _tvVideo.Position = _tvWarmupStart;
        _tvVideo.SpeedRatio = 1;
        _tvVideo.Play();
        _tvWarmup.Start();
    }

    private void FinishTvWarmup()
    {
        _tvWarmup.Stop();
        _tvVideo.Pause();
        _tvVideo.Position = _tvWarmupStart;
        _tvVideoReady = true;
        var current = _tvState.Show;
        if (_tvState.OverlayOpacity > 0 && current?.IsVideo == true &&
            string.Equals(current.FullPath(_tvConfigRoot), _tvVideoPath, StringComparison.OrdinalIgnoreCase))
            SyncTvVideo(_tvVideoPath, current, _tvState.MediaSeconds, _tvTransportPlaying, _tvTransportRate);
    }

    private void OpenTvVideo(string path, TvShowCommand show)
    {
        if (_tvWarmup.IsEnabled) { _tvWarmup.Stop(); _tvVideo.Pause(); }
        PauseTvVideo();
        _tvVideoReady = false;
        _tvVideoDuration = null;
        _tvVideoPath = path;
        _tvOpeningShow = show;
        _tvVideo.IsMuted = true;
        _tvVideo.Visibility = Visibility.Collapsed;
        _tvVideo.Source = new Uri(path);
        _tvVideo.Pause();
    }

    private void SyncTvVideo(string path, TvShowCommand show, double seconds, bool playing, double rate)
    {
        if (!string.Equals(path, _tvVideoPath, StringComparison.OrdinalIgnoreCase))
            OpenTvVideo(path, show);
        if (_tvBufferedFrames != null)
        {
            _tvImagePath = null;
            _tvImage.Stretch = StretchFor(show.Fit);
            _tvImage.Source = TvFrameBuffer.PictureAt(_tvBufferedFrames, seconds - show.StartSeconds);
            _tvImage.Visibility = Visibility.Visible;
            _tvVideo.Visibility = Visibility.Collapsed;
        }
        else if (_tvPicturesPending)
        {
            PauseTvVideo();
            _tvVideo.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_tvVideoReady) return; // Keep the preceding still visible while the decoder warms up.

        if (_tvBufferedFrames == null)
        {
            _tvVideo.Stretch = StretchFor(show.Fit);
            _tvVideo.Visibility = Visibility.Visible;
            _tvImage.Visibility = Visibility.Collapsed;
        }
        _tvVideo.Volume = _tvAudible ? show.Volume / 100 : 0; _tvVideo.IsMuted = !_tvAudible || show.Volume <= 0;
        var (position, ended) = TvTimeline.VideoPosition(show, seconds, _tvVideoDuration);
        var target = TimeSpan.FromSeconds(position);
        if (playing && !ended)
        {
            _tvVideo.SpeedRatio = Math.Clamp(rate, .1, 4);
            long now = Environment.TickCount64;
            double drift = Math.Abs((_tvVideo.Position - target).TotalSeconds);
            if (!_tvVideoPlaying)
            {
                if (drift > .04) _tvVideo.Position = target;
                _tvVideo.Play();
                _tvVideoPlaying = true;
                _tvNextResync = now + 500;
            }
            else if (now >= _tvNextResync && drift > .3)
            {
                // Seeking can flush queued frames. Give the decoder time
                // to advance before judging another clock discrepancy.
                _tvVideo.Position = target;
                _tvNextResync = now + 500;
            }
        }
        else
        {
            bool justEnded = ended && _tvVideoPlaying;
            PauseTvVideo();
            if (justEnded || Math.Abs((_tvVideo.Position - target).TotalSeconds) > .04) _tvVideo.Position = target;
        }
    }

    internal void PauseTvTimeline()
    {
        _tvTransportPlaying = false;
        PauseTvVideo();
    }

    private void PauseTvVideo()
    {
        if (_tvVideoPlaying) { _tvVideo.Pause(); _tvVideoPlaying = false; }
    }

    /// <summary>API/diagnostic view of the TV: availability, blend, and the screen's pixel rectangle once
    /// the camera faces it (computed from the shot even before arrival).</summary>
    internal object TvInfo()
    {
        var front = _tvState.Front ?? new TvFrontCommand();
        bool available = TryTvShot(front, out _, out _, out var screen);
        object rect = null;
        if (available && ActualWidth > 1 && ActualHeight > 1)
        {
            double tanH = Math.Tan(_camera.FieldOfView * Deg * .5), distance = screen.FitDistance(tanH, ViewAspect, front.Fill);
            double pixelsPerMetre = ActualWidth / 2 / (distance * tanH);
            double w = screen.Width * pixelsPerMetre, h = screen.Height * pixelsPerMetre;
            rect = new { x = Math.Round((ActualWidth - w) / 2, 1), y = Math.Round((ActualHeight - h) / 2, 1), width = Math.Round(w, 1), height = Math.Round(h, 1) };
        }
        return new
        {
            available, screenLink = front.Screen, active = _tvState.Active, weight = Math.Round(_tvState.Weight, 4), arrived = _tvState.Arrived,
            robotHidden = _sceneLightingRoot != null && _presentationModel != null && !_sceneLightingRoot.Children.Contains(_presentationModel),
            screenMetres = screen == null ? null : new { width = Math.Round(screen.Width, 4), height = Math.Round(screen.Height, 4) },
            screenRect = rect, viewport = new { width = Math.Round(ActualWidth, 1), height = Math.Round(ActualHeight, 1) },
            units = "screenRect and viewport are WPF device-independent pixels (1/96 inch) of this URDF view"
        };
    }
}
