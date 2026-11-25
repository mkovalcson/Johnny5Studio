using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private BeamOverlay _beamOverlay;
    private BeamState _beamState = BeamState.Visible;
    private double _beamSeconds;
    private bool _beamHidesRobot;
    private UrdfScene _beamScene;
    internal bool BeamHidesRobot => _beamHidesRobot;

    private void InitializeBeamLayer()
    {
        _beamOverlay = new BeamOverlay { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Children.Insert(Children.IndexOf(_tvLayer) + 1, _beamOverlay);
    }

    /// <summary>Timeline-driven transporter effect at <paramref name="cursorSeconds"/> (the sparkles animate with
    /// timeline time, so scrubbing and paused frames are exact).</summary>
    internal void SetTimelineBeam(BeamState state, double cursorSeconds)
    {
        state ??= BeamState.Visible;
        bool sameScene = ReferenceEquals(_beamScene, _scene);
        bool unchanged = !state.Active && !_beamState.Active && state.Hidden == _beamState.Hidden;
        if (sameScene && ((unchanged && _scene?.BeamActive != true) ||
                          (state == _beamState && cursorSeconds == _beamSeconds))) return;
        _beamState = state;
        _beamSeconds = cursorSeconds;
        _beamScene = _scene;
        double opacity = state.RobotOpacity, glow = state.Glow;
        if (_scene != null)
        {
            if (state.Active && (opacity < .999 || glow > .001)) _scene.SetBeamLook(opacity, glow);
            else _scene.ClearBeamLook();
        }
        bool hidden = state.Hidden || (state.Active && opacity < .005 && glow < .005);
        bool visibilityChanged = !sameScene || hidden != _beamHidesRobot;
        _beamHidesRobot = hidden;
        if (visibilityChanged) UpdateCamera();
        else LayoutBeamOverlay();
    }

    /// <summary>Called from UpdateCamera: the sparkle column follows the robot's projected bounds.</summary>
    private void LayoutBeamOverlay()
    {
        if (_beamOverlay == null) return;
        if (!_beamState.Active || _scene == null || ActualWidth < 2 || ActualHeight < 2)
        { _beamOverlay.Visibility = Visibility.Collapsed; return; }
        var b = _scene.RootModel.Bounds;
        var points = new List<Point>();
        if (!b.IsEmpty)
            foreach (double x in new[] { b.X, b.X + b.SizeX })
            foreach (double y in new[] { b.Y, b.Y + b.SizeY })
            foreach (double z in new[] { b.Z, b.Z + b.SizeZ })
                if (TryProjectWorldPoint(new Point3D(x, y, z), out var p)) points.Add(p);
        if (points.Count < 2) { _beamOverlay.Visibility = Visibility.Collapsed; return; }
        double left = points.Min(p => p.X), right = points.Max(p => p.X), top = points.Min(p => p.Y), bottom = points.Max(p => p.Y);
        double padX = (right - left) * .06, padY = (bottom - top) * .04;
        _beamOverlay.Column = new Rect(left - padX, top - padY, right - left + 2 * padX, bottom - top + 2 * padY);
        _beamOverlay.State = _beamState; _beamOverlay.Seconds = _beamSeconds;
        _beamOverlay.Visibility = Visibility.Visible;
        _beamOverlay.InvalidateVisual();
    }

    /// <summary>Draws the TNG column: a soft blue glow, flickering vertical streaks, twinkling sparkles inside the
    /// robot's column, and optional particles travelling between the column and the top of the view.</summary>
    private sealed class BeamOverlay : FrameworkElement
    {
        internal Rect Column;
        internal BeamState State;
        internal double Seconds;

        private static readonly Brush ColumnGlow = Frozen(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(110, 140, 205, 255), 0), new(Color.FromArgb(0, 80, 160, 255), 1)
        }));
        private static readonly Brush Spark = Frozen(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(255, 255, 255, 255), 0), new(Color.FromArgb(220, 170, 225, 255), .35), new(Color.FromArgb(0, 90, 170, 255), 1)
        }));
        private static readonly Brush Streak = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 150, 210, 255), 0), new(Color.FromArgb(150, 200, 235, 255), .5), new(Color.FromArgb(0, 150, 210, 255), 1)
        }, 90));
        private static T Frozen<T>(T brush) where T : Freezable { brush.Freeze(); return brush; }

        protected override void OnRender(DrawingContext dc)
        {
            if (State == null || !State.Active || Column.IsEmpty) return;
            double sparkle = State.Sparkle;
            if (sparkle > .001)
            {
                // Column glow
                dc.PushOpacity(sparkle);
                dc.DrawEllipse(ColumnGlow, null, new Point(Column.X + Column.Width / 2, Column.Y + Column.Height / 2), Column.Width * .62, Column.Height * .58);
                dc.Pop();
                // Flickering vertical streaks
                for (int i = 0; i < 7; i++)
                {
                    double flicker = .5 + .5 * Math.Sin(Seconds * (9 + 3 * i) + i * 1.7);
                    double x = Column.X + Column.Width * (.12 + .76 * BeamTimeline.Hash(i + 900));
                    dc.PushOpacity(sparkle * flicker * .55);
                    dc.DrawRectangle(Streak, null, new Rect(x - 1.5, Column.Y, 3, Column.Height));
                    dc.Pop();
                }
                // Twinkling sparkles
                double unit = Math.Max(2.2, Math.Min(Column.Width, Column.Height) * .012);
                foreach (var (x, y, size, brightness) in BeamTimeline.Sparkles(150, Seconds))
                {
                    double alpha = brightness * sparkle;
                    if (alpha < .03) continue;
                    double r = unit * size * (.7 + .6 * brightness);
                    dc.PushOpacity(alpha);
                    dc.DrawEllipse(Spark, null, new Point(Column.X + x * Column.Width, Column.Y + y * Column.Height), r, r);
                    dc.Pop();
                }
            }
            // Particles falling from (in) or rising to (out) the top of the view
            double travel = State.ParticleTravel;
            foreach (var (p, brightness) in BeamTimeline.Stream(90, travel, State.Direction, Column, new Size(ActualWidth, ActualHeight)))
            {
                dc.PushOpacity(brightness);
                dc.DrawEllipse(Spark, null, p, 2.6, 6.5);              // elongated along the direction of travel
                dc.Pop();
            }
        }
    }
}
