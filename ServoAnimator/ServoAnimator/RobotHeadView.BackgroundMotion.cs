using System.Windows.Media;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private bool _backgroundMotionInitialized, _backgroundSubscribed;
    private PlaybackFrameCadence _backgroundCadence;
    private TimeSpan? _backgroundLastTimestamp;
    private double _backgroundElapsed;
    private UrdfPresentationSettings _backgroundMotion;
    private Random _backgroundRandom;
    private MatrixTransform _backgroundTransform;
    private double _backgroundSegmentStart;
    private (double Zoom, double X, double Y, double Angle) _backgroundFrom, _backgroundTo;

    private void ConfigureMovingBackground(UrdfPresentationSettings settings)
    {
        if (!_backgroundMotionInitialized)
        {
            _backgroundMotionInitialized = true;
            Loaded += (_, _) => UpdateMovingBackgroundSubscription();
            Unloaded += (_, _) => StopMovingBackground();
            IsVisibleChanged += (_, _) => UpdateMovingBackgroundSubscription();
            SizeChanged += (_, _) => { if (_backgroundTransform != null) RenderMovingBackground(); };
        }
        StopMovingBackground(); _backgroundElapsed = 0;
        _backgroundTransform = null; _backgroundMotion = null;
        if (settings.BackgroundMode != UrdfBackgroundMode.MovingImage || _configuredBackground is not ImageBrush image) return;
        _backgroundMotion = settings.Clone();
        static double Limit(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        _backgroundMotion.BackgroundMotionSpeed = Limit(settings.BackgroundMotionSpeed, 1, .1, 5);
        _backgroundMotion.BackgroundFrameRate = Math.Clamp(settings.BackgroundFrameRate, 1, 60);
        _backgroundMotion.BackgroundZoomMinimum = Limit(settings.BackgroundZoomMinimum, 110, 100, 500);
        _backgroundMotion.BackgroundZoomMaximum = Limit(settings.BackgroundZoomMaximum, 150, _backgroundMotion.BackgroundZoomMinimum, 500);
        _backgroundMotion.BackgroundPanAmount = Limit(settings.BackgroundPanAmount, 75, 0, 100);
        _backgroundMotion.BackgroundTargetSeconds = Limit(settings.BackgroundTargetSeconds, 8, .5, 120);
        _backgroundRandom = new Random(settings.BackgroundSeed);
        _backgroundFrom = NextBackgroundTarget(); _backgroundTo = NextBackgroundTarget();
        _backgroundSegmentStart = 0;
        _backgroundCadence = new PlaybackFrameCadence(_backgroundMotion.BackgroundFrameRate, earlyToleranceSeconds: .001);
        var moving = image.Clone();
        _backgroundTransform = new MatrixTransform(); moving.RelativeTransform = _backgroundTransform;
        _configuredBackground = moving;
        RenderMovingBackground();
    }

    private (double Zoom, double X, double Y, double Angle) NextBackgroundTarget()
    {
        var s = _backgroundMotion;
        double zoom = (s.BackgroundZoomMinimum + _backgroundRandom.NextDouble() * (s.BackgroundZoomMaximum - s.BackgroundZoomMinimum)) / 100;
        double x = _backgroundRandom.NextDouble() * 2 - 1, y = _backgroundRandom.NextDouble() * 2 - 1;
        double angle = s.BackgroundRotation ? (_backgroundRandom.NextDouble() * 30 - 15) : 0;
        if (s.BackgroundHalfTurns && _backgroundRandom.Next(2) == 1) angle += 180;
        return (zoom, x, y, angle);
    }

    private void UpdateMovingBackgroundSubscription()
    {
        bool needed = IsLoaded && IsVisible && _backgroundTransform != null && _meetingOverride == null;
        if (!needed) { StopMovingBackground(); return; }
        if (_backgroundSubscribed) return;
        _backgroundSubscribed = true;
        _backgroundLastTimestamp = null; _backgroundCadence.Reset();
        UrdfRenderLoop.Current.AddBackground(AdvanceMovingBackground);
    }

    private void StopMovingBackground()
    {
        if (_backgroundSubscribed) UrdfRenderLoop.Current.RemoveBackground(AdvanceMovingBackground);
        _backgroundSubscribed = false; _backgroundLastTimestamp = null;
    }

    private void AdvanceMovingBackground(TimeSpan frameTime)
    {
        if (!_backgroundSubscribed) return;
        if (_backgroundLastTimestamp is TimeSpan previous)
        {
            if (frameTime >= previous) _backgroundElapsed += (frameTime - previous).TotalSeconds;
            else _backgroundCadence.Reset();
        }
        _backgroundLastTimestamp = frameTime;
        // Preserve deadlines instead of resetting them to a late timer tick.
        // One frame samples the current position; missed frames are never replayed.
        if (_backgroundCadence.IsDue(frameTime)) RenderMovingBackground();
    }

    private void RenderMovingBackground()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0 || _backgroundTransform == null) return;
        var s = _backgroundMotion;
        double duration = s.BackgroundTargetSeconds / s.BackgroundMotionSpeed;
        double elapsed = _backgroundElapsed;
        while (elapsed - _backgroundSegmentStart >= duration)
        {
            _backgroundFrom = _backgroundTo; _backgroundTo = NextBackgroundTarget();
            _backgroundSegmentStart += duration;
        }
        double t = Math.Clamp((elapsed - _backgroundSegmentStart) / duration, 0, 1);
        t = t * t * (3 - 2 * t);
        double Lerp(double a, double b) => a + (b - a) * t;
        double zoom = Lerp(_backgroundFrom.Zoom, _backgroundTo.Zoom);
        // Half-turn swaps occur at target boundaries; optional gentle rotation interpolates.
        double halfTurn = s.BackgroundHalfTurns && _backgroundFrom.Angle > 90 ? 180 : 0;
        double SmallAngle(double angle) => angle > 90 ? angle - 180 : angle;
        double angle = (halfTurn + Lerp(SmallAngle(_backgroundFrom.Angle), SmallAngle(_backgroundTo.Angle))) * Math.PI / 180;
        double c = Math.Cos(angle), sin = Math.Sin(angle);
        // Expand the image rectangle to contain every viewport corner after rotation.
        double cover = Math.Max(Math.Abs(c) + height / width * Math.Abs(sin), Math.Abs(c) + width / height * Math.Abs(sin));
        double scale = cover * zoom;
        // Bound translation by a circle within the remaining margin, keeping all edges covered.
        double margin = (zoom - 1) * Math.Min(width, height) / (2 * Math.Sqrt(2)) * s.BackgroundPanAmount / 100;
        double dx = Lerp(_backgroundFrom.X, _backgroundTo.X) * margin / width;
        double dy = Lerp(_backgroundFrom.Y, _backgroundTo.Y) * margin / height;
        double m11 = scale * c, m12 = scale * sin * width / height;
        double m21 = -scale * sin * height / width, m22 = scale * c;
        _backgroundTransform.Matrix = new Matrix(m11, m12, m21, m22,
            .5 + dx - .5 * (m11 + m21), .5 + dy - .5 * (m12 + m22));
    }
}
