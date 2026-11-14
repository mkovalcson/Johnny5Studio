using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

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
    private bool _tvVideoPlaying, _tvAudible = true;
    private int _tvRobotIndex = -1;

    private void InitializeTvLayer()
    {
        var content = new Grid(); content.Children.Add(_tvImage); content.Children.Add(_tvVideo);
        _tvFrame.Child = content; _tvLayer.Children.Add(_tvFrame);
        Children.Insert(Children.IndexOf(_viewport) + 1, _tvLayer);
        _tvVideo.MediaOpened += (_, _) => _tvVideoDuration = _tvVideo.NaturalDuration.HasTimeSpan ? _tvVideo.NaturalDuration.TimeSpan.TotalSeconds : null;
        _tvVideo.MediaFailed += (_, e) => { _status.Text = "TV Show: " + (e.ErrorException?.Message ?? "video failed"); _tvVideoPath = null; };
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
        if(VrEditorMode.Active)hidden=_beamHidesRobot;
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
        UpdateCamera();                                    // applies the blend, robot visibility and overlay layout
        var show = state.OverlayOpacity > 0 ? state.Show : null;
        if (show == null || show.IsBlank) { ShowTvImage(null, TvFit.Contain); PauseTvVideo(); _tvVideo.Visibility = Visibility.Collapsed; return; }
        string path = show.FullPath(configRoot);
        if (!File.Exists(path)) { _status.Text = "TV Show: file not found: " + path; ShowTvImage(null, TvFit.Contain); PauseTvVideo(); return; }
        if (!show.IsVideo) { PauseTvVideo(); _tvVideo.Visibility = Visibility.Collapsed; ShowTvImage(path, show.Fit); return; }
        ShowTvImage(null, show.Fit);
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

    private void SyncTvVideo(string path, TvShowCommand show, double seconds, bool playing, double rate)
    {
        if (!string.Equals(path, _tvVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            _tvVideoPlaying = false; _tvVideoDuration = null; _tvVideoPath = path;
            _tvVideo.Source = new Uri(path); _tvVideo.Pause();
        }
        _tvVideo.Stretch = StretchFor(show.Fit); _tvVideo.Visibility = Visibility.Visible;
        _tvVideo.Volume = _tvAudible ? show.Volume / 100 : 0; _tvVideo.IsMuted = !_tvAudible || show.Volume <= 0;
        var target = TimeSpan.FromSeconds(TvTimeline.MediaPosition(seconds, _tvVideoDuration, show.Loop));
        bool ended = !show.Loop && _tvVideoDuration is > 0 && seconds >= _tvVideoDuration.Value;
        if (playing && !ended)
        {
            _tvVideo.SpeedRatio = Math.Clamp(rate, .1, 4);
            if (!_tvVideoPlaying) { _tvVideo.Position = target; _tvVideo.Play(); _tvVideoPlaying = true; }
            else if (Math.Abs((_tvVideo.Position - target).TotalSeconds) > .15) _tvVideo.Position = target;
        }
        else
        {
            PauseTvVideo();
            if (Math.Abs((_tvVideo.Position - target).TotalSeconds) > .04) _tvVideo.Position = target;
        }
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
