using System.Reflection;
using System.Windows;
using ServoAnimator;

internal static partial class Program
{
    private static void UrdfRenderLoopChecks()
    {
        VisualCycleOrderChecks();
        foreach (int displayHz in new[] { 15, 24, 30, 60, 75, 120, 144, 240 })
        {
            var loop = new UrdfRenderLoop(false);
            int targets = 0, motions = 0;
            loop.AddMotion(_ => { Check(targets == motions + 1, "Fresh targets precede motion regardless of registration order"); motions++; });
            loop.AddTargets(_ => targets++);
            for (int frame = 0; frame < displayHz * 2; frame++)
            {
                var now = TimeSpan.FromSeconds(frame / (double)displayHz);
                loop.Advance(now);
                Check(!loop.Advance(now), "Repeated WPF render notification cannot apply a duplicate frame");
            }
            Check(Math.Abs(motions - Math.Min(displayHz, 60) * 2) <= 1 && targets == motions,
                $"Synchronized visual cadence follows a {displayHz} Hz display without exceeding its 60 Hz target");
            int before = motions;
            loop.Advance(TimeSpan.FromSeconds(5));
            Check(motions == before + 1, "A delayed frame does one update without a catch-up burst");
        }
        ControllerPreviewChecks();

        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        BackgroundFrameCycleChecks();
        var head = new RobotHeadView();
        head.SetServoConfiguration(ServoConfiguration.CreateDefault()); head.SetCollisionWarningsEnabled(false);
        head.SetUrdfDriveEnabled(true);
        void Reset()
        {
            head.SetSpeedCalibration(new()); head.CalibratedMotionAllowed = null; head.CalibratedMotionPaused = false;
            head.SnapCalibratedMotion = true; head.SetServo(ServoNames.BothEyePop, 0); head.SnapCalibratedMotion = false;
        }
        foreach (int displayHz in new[] { 15, 30, 60, 144 })
        {
            Reset();
            var loop = new UrdfRenderLoop(false);
            var preview = new ControllerPreviewUpdates();
            loop.AddMotion(head.RenderCalibratedMotion);
            loop.AddTargets(_ => preview.Flush());
            preview.Enqueue("pop", () => head.SetServo(ServoNames.BothEyePop, 2000));
            loop.Advance(TimeSpan.Zero);
            for (int frame = 1; frame <= displayHz; frame++) loop.Advance(TimeSpan.FromSeconds(frame / (double)displayHz));
            Check(Math.Abs(head.CapturePose().LeftEyePop - 2000 / 1.1) < .01,
                $"Calibrated travel depends on elapsed time, not {displayHz} Hz display rate");
        }

        Reset();
        var frames = new UrdfRenderLoop(false);
        var queued = new ControllerPreviewUpdates();
        frames.AddMotion(head.RenderCalibratedMotion); frames.AddTargets(_ => queued.Flush());
        frames.Advance(TimeSpan.Zero);
        queued.Enqueue("pop", () => head.SetServo(ServoNames.BothEyePop, 0));
        queued.Enqueue("pop", () => head.SetServo(ServoNames.BothEyePop, 2000));
        frames.Advance(TimeSpan.FromSeconds(.1));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 200 / 1.1) < .01, "Newest controller target affects motion in the same rendered frame");
        frames.Advance(TimeSpan.FromSeconds(.55));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1000) < .01, "Dropped frames preserve calibrated wall-clock speed");
        head.CalibratedMotionPaused = true; frames.Advance(TimeSpan.FromSeconds(10));
        head.CalibratedMotionPaused = false; frames.Advance(TimeSpan.FromSeconds(20));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1000) < .01, "Paused time is not integrated on resume");
        frames.Advance(TimeSpan.FromSeconds(20.11));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1200) < .01, "Motion resumes at its calibrated speed");

        bool allowed = false; head.CalibratedMotionAllowed = () => allowed;
        head.RefreshCalibratedMotionPermission(); // Background input polling observes the loss of permission.
        allowed = true; head.RefreshCalibratedMotionPermission();
        frames.Advance(TimeSpan.FromSeconds(40));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1200) < .01, "Focus transitions while rendering is suspended do not advance forbidden motion");
        frames.Advance(TimeSpan.FromSeconds(40.11));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1400) < .01, "Motion continues normally after focus permission returns");
        head.StopCalibratedMotion(); frames.Advance(TimeSpan.FromSeconds(50)); frames.Advance(TimeSpan.FromSeconds(51));
        Check(Math.Abs(head.CapturePose().LeftEyePop - 1400) < .01, "Stopped motion cannot be replayed by later frames");

        int participants = UrdfRenderLoop.Current.ParticipantCount;
        head.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        int loadedParticipants = UrdfRenderLoop.Current.ParticipantCount;
        head.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(loadedParticipants > participants && UrdfRenderLoop.Current.ParticipantCount == loadedParticipants, "Loading or docking a view cannot double-subscribe calibrated motion or treads");
        head.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Check(UrdfRenderLoop.Current.ParticipantCount == participants, "Unloading a view releases its render subscription");
        head.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(UrdfRenderLoop.Current.ParticipantCount == loadedParticipants, "Reloading a view restores its render subscriptions");
        head.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        // Exercise the actual render path with a blocked move rather than
        // bypassing the collision guard or emitting physical hardware output.
        Reset();
        head.SnapCalibratedMotion = true;
        head.SetServo(ServoNames.FlapTiltUp, 0); head.SetServo(ServoNames.FlapsOpen, 0);
        head.SnapCalibratedMotion = false;
        ServoCommand blocked = null;
        foreach (var servo in new[] { ServoNames.FlapTiltUp, ServoNames.FlapsOpen })
        foreach (int value in new[] { -100, 100 })
        {
            var command = new ServoCommand { Servo = servo, NumericValue = value };
            if (!head.ControllerPathClear(new[] { command }, out _)) { blocked = command; break; }
        }
        Check(blocked != null, "A real flap path exercises collision rejection");
        var scene = (UrdfScene)typeof(RobotHeadView).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(head);
        var joints = scene.CaptureMotionState().JointPositions;
        bool stopped = false;
        head.CollisionSafeguardActive = () => true; head.CollisionSafeguardBlocked = _ => stopped = true;
        head.SetServo(blocked.Servo, blocked.NumericValue);
        var guarded = new UrdfRenderLoop(false); guarded.AddMotion(head.RenderCalibratedMotion);
        guarded.Advance(TimeSpan.Zero); guarded.Advance(TimeSpan.FromSeconds(60));
        var after = scene.CaptureMotionState().JointPositions;
        Check(stopped && joints.All(p => Math.Abs(p.Value - after[p.Key]) < 1e-9),
            "Even a very late rendered frame rejects collision before changing the URDF: stopped=" + stopped + "; changed=" +
            string.Join(", ", joints.Where(p => Math.Abs(p.Value - after[p.Key]) >= 1e-9).Select(p => $"{p.Key}: {p.Value} -> {after[p.Key]}")));
    }

    private static void VisualCycleOrderChecks()
    {
        var loop = new UrdfRenderLoop(false);
        var order = new List<string>();
        Action<TimeSpan> animation = _ => order.Add("animation"), targets = _ => order.Add("targets"),
            motion = _ => order.Add("motion"), background = _ => order.Add("background"),
            synchronize = _ => order.Add("synchronize"), render = _ => order.Add("render");
        // Register backwards to prove order is defined by phases, not event attachment.
        loop.AddRender(render); loop.AddSynchronize(synchronize); loop.AddBackground(background);
        loop.AddMotion(motion); loop.AddTargets(targets); loop.AddAnimation(animation);
        loop.AddRender(render); loop.AddBackground(background);
        Check(loop.ParticipantCount == 6, "Each ordered phase deduplicates its participants");
        loop.Advance(TimeSpan.Zero);
        Check(string.Join(",", order) == "animation,targets,motion,background,synchronize,render", "Animation and background finish before scene synchronization and drawing");
        order.Clear(); loop.Advance(TimeSpan.Zero);
        Check(order.Count == 0, "Duplicate frame notifications do not run any phase twice");
        loop.Advance(TimeSpan.FromSeconds(1.0 / 144));
        Check(order.SequenceEqual(new[] { "animation" }), "High-refresh timeline/audio dispatch is retained while heavy visual work stays capped");
        order.Clear(); loop.Advance(TimeSpan.FromSeconds(5));
        Check(order.Count == 6, "A stalled frame executes one ordered cycle without catch-up bursts");
        loop.RemoveAnimation(animation); loop.RemoveTargets(targets); loop.RemoveMotion(motion);
        loop.RemoveBackground(background); loop.RemoveSynchronize(synchronize); loop.RemoveRender(render);
        Check(loop.ParticipantCount == 0, "All frame phases release their participants");
    }

    private static void BackgroundFrameCycleChecks()
    {
        var view = new RobotHeadView(deferModelLoad: true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Get(string name) => typeof(RobotHeadView).GetField(name, flags).GetValue(view);
        void Set(string name, object value) => typeof(RobotHeadView).GetField(name, flags).SetValue(view, value);
        void Call(string name, params object[] values) => typeof(RobotHeadView).GetMethod(name, flags).Invoke(view, values);
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
            null, new byte[] { 0, 255, 0, 255 }, 4);
        var settings = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundFrameRate = 15 };
        view.Measure(new Size(800, 450)); view.Arrange(new Rect(0, 0, 800, 450));
        foreach (int rate in new[] { 1, 15, 30, 60 })
        foreach (int displayHz in new[] { 30, 60, 144 })
        {
            settings.BackgroundFrameRate = rate;
            Set("_configuredBackground", new System.Windows.Media.ImageBrush(bitmap));
            Call("ConfigureMovingBackground", settings);
            Set("_backgroundFrom", (1.2, -1d, 0d, 0d)); Set("_backgroundTo", (1.2, 1d, 0d, 0d));
            Call("RenderMovingBackground");
            // Feed synthetic display timestamps without subscribing an unloaded test view.
            Set("_backgroundSubscribed", true);
            int changes = 0;
            ((System.Windows.Media.MatrixTransform)Get("_backgroundTransform")).Changed += (_, _) => changes++;
            var cycle = new UrdfRenderLoop(false);
            cycle.AddBackground(time => Call("AdvanceMovingBackground", time));
            for (int frame = 0; frame <= displayHz * 2; frame++)
            {
                double jitter = frame % 2 == 0 ? 0 : -.0002;
                cycle.Advance(TimeSpan.FromSeconds(frame / (double)displayHz + jitter));
            }
            int expected = Math.Min(rate, displayHz) * 2;
            Check(Math.Abs(changes - expected) <= 1, $"{rate} fps background maintains its deadlines on a jittered {displayHz} Hz display ({changes} updates)");
            Call("StopMovingBackground");
            double elapsed = (double)Get("_backgroundElapsed");
            Set("_backgroundSubscribed", true);
            Call("AdvanceMovingBackground", TimeSpan.FromSeconds(20));
            Check(Math.Abs((double)Get("_backgroundElapsed") - elapsed) < 1e-8, "Hidden/paused background time is not integrated on resume");
            Call("AdvanceMovingBackground", TimeSpan.FromSeconds(20.5));
            Check(Math.Abs((double)Get("_backgroundElapsed") - elapsed - .5) < 1e-8, "Delayed frames sample current background time without losing speed");
            Call("StopMovingBackground");
        }
    }
}
