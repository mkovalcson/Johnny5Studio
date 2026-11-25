using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void TvChecks()
    {
        // Command settings
        var front = TvFrontCommand.Read("");
        Check(front.Seconds == 3 && front.Fill == .85 && front.Screen == "backdrop_tv_screen_link", "TV Front defaults: 3 s, 85% fill, backdrop TV");
        Check(TvReturnCommand.Read("").Seconds == 3, "TV Return defaults to 3 s");
        var show = TvShowCommand.Read("");
        Check(show.IsBlank && show.Volume == 100 && show.Fit == TvFit.Contain && !show.Loop, "TV Show defaults: blank, 100% volume, contain");
        var video = TvShowCommand.Read("{\"path\":\"TvMedia\\\\clip.mp4\",\"fit\":\"cover\",\"volume\":40,\"loop\":true,\"startSeconds\":2}");
        Check(video.IsVideo && video.Fit == TvFit.Cover && video.Volume == 40 && video.Loop && video.StartSeconds == 2, "TV Show settings parse");
        Check(TvShowCommand.Read(video.ToJson()).Fit == TvFit.Cover, "TV Show settings round-trip");
        Check(!TvShowCommand.Read("{\"path\":\"a.PNG\"}").IsVideo, "Images are not treated as video");
        Check(Throws(() => TvShowCommand.Read("{\"volume\":101}")) && Throws(() => TvShowCommand.Read("{\"volume\":-1}")), "Volume is limited to 0-100");
        Check(Throws(() => TvShowCommand.Read("{\"path\":\"notes.txt\"}")), "Unsupported media is rejected");
        Check(Throws(() => TvFrontCommand.Read("{\"fill\":1.5}")) && Throws(() => TvFrontCommand.Read("{\"seconds\":-1}")), "Front fill and seconds are validated");
        Check(Throws(() => TvReturnCommand.Read("{\"secs\":3}")), "Unknown settings are rejected");
        foreach (var s in new[] { ServoNames.TvFront, ServoNames.TvShow, ServoNames.TvReturn })
            Check(ServoCommand.IsTextValued(s) && ServoCommand.IsUrdfOnly(s), s + " is a URDF-only text command");
        Check(ServoCommand.DisplayName(ServoNames.TvFront) == "URDF TV Front" && ServoCommand.DisplayName(ServoNames.TvShow) == "TV Show" &&
              ServoCommand.DisplayName(ServoNames.TvReturn) == "URDF TV Return", "TV display names");

        // Timeline evaluation
        ServoCommand C(ServoNames servo, double at, string text = "", bool disable = false) => new() { Servo = servo, OffsetSeconds = at, TextValue = text, Disable = disable };
        TvState At(double t, params ServoCommand[] cs) => TvTimeline.StateAt(cs, t);
        var f = C(ServoNames.TvFront, 10); var sh = C(ServoNames.TvShow, 12, "{\"path\":\"x.mp4\"}"); var r = C(ServoNames.TvReturn, 20);
        Check(!At(9, f, sh, r).Active && !At(10, f, sh, r).Active, "Inactive before and at the start of the glide");
        Check(Math.Abs(At(11.5, f, sh, r).Weight - .5) < 1e-9 && !At(11.5, f, sh, r).Arrived, "Halfway through a 3 s glide");
        var arrived = At(13, f, sh, r);
        Check(arrived.Weight == 1 && arrived.Arrived && arrived.OverlayOpacity == 0, "Arrives after 3 s; the show fades in from there");
        Check(At(13.3, f, sh, r).OverlayOpacity == 1, "Show is fully visible 0.3 s after arrival");
        Check(Math.Abs(At(14, f, sh, r).MediaSeconds - 1) < 1e-9, "Video time counts from arrival when the show was placed during the glide");
        Check(Math.Abs(At(14, f, C(ServoNames.TvShow, 13.5, "{\"path\":\"x.mp4\",\"startSeconds\":5}")).MediaSeconds - 5.5) < 1e-9, "Video time counts from the show command plus startSeconds");
        Check(Math.Abs(At(20.15, f, sh, r).OverlayOpacity - .5) < 1e-9 && At(20.15, f, sh, r).Weight > .99, "Show fades out as the return begins");
        Check(Math.Abs(At(21.5, f, sh, r).Weight - .5) < 1e-9 && !At(23, f, sh, r).Active, "Return glides back over 3 s");
        var again = C(ServoNames.TvFront, 21.5);
        Check(Math.Abs(At(22.25, f, sh, r, again).Weight - .75) < 1e-9 && At(23, f, sh, r, again).Arrived, "A Front during a return continues from the current blend");
        Check(At(14, f, C(ServoNames.TvShow, 5, "{\"path\":\"x.png\"}")).Show == null, "Shows before the Front are ignored");
        Check(At(14, f, C(ServoNames.TvShow, 13.5)).OverlayOpacity == 0, "A blank show hides the overlay");
        Check(!At(12, C(ServoNames.TvFront, 10, "", disable: true)).Active && !At(12, C(ServoNames.TvReturn, 5)).Active, "Disabled Fronts and lone Returns do nothing");
        Check(At(10, C(ServoNames.TvFront, 10, "{\"seconds\":0}")).Arrived, "A zero-second Front cuts straight to the TV");
        Check(TvTimeline.MediaPosition(25, 10, true) == 5 && TvTimeline.MediaPosition(25, 10, false) == 10 && TvTimeline.MediaPosition(3, null, false) == 3,
              "Video loops or holds its last frame");

        var limited = TvShowCommand.Read("{\"path\":\"x.mp4\",\"startSeconds\":2,\"durationSeconds\":5}");
        Check(TvShowCommand.Read(limited.ToJson()).DurationSeconds == 5 && show.DurationSeconds == 0 &&
              Throws(() => TvShowCommand.Read("{\"durationSeconds\":-1}")), "TV length round-trips, defaults to unlimited, and rejects negatives.");
        Check(TvTimeline.VideoPosition(limited, 6.9, 20) == (6.9, false) &&
              TvTimeline.VideoPosition(limited, 7, 20) == (7d, true) &&
              TvTimeline.VideoPosition(limited, 10, 20) == (7d, true), "A five-second TV cue holds the endpoint on playback and seeks.");
        limited.Loop = true;
        Check(TvTimeline.VideoPosition(limited, 6, 3) == (0d, false) &&
              TvTimeline.VideoPosition(limited, 9, 3).Ended, "A loop stops when the cue reaches its chosen length.");

        // Real backdrop: the TV screen and the camera fit
        var stage = UrdfScene.Load(Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf"), optimizeExterior: false);
        var screen = TvScreen.FromBoxCorners(stage.GetWorldVisualBoundsPoints(new[] { TvFrontCommand.DefaultScreen }).Take(8).ToArray(), new Point3D());
        Check(screen != null && Math.Abs(screen.Width - .57) < 1e-6 && Math.Abs(screen.Height - .43) < 1e-6, "Screen is the 0.57 x 0.43 m (4:3) face");
        Check(screen.Normal.X > .999 && Math.Abs(screen.Center.X - -10.604) < 1e-6 && Math.Abs(screen.Center.Y - .13) < 1e-6, "Screen faces the room (+X) from the front face");
        // Facing the screen means looking along -X with +Z up, so the viewer's right is +Y.
        Check(screen.Right.Y > .999 && screen.Up.Z > .999, "Screen right/up match the viewer's view");
        double tanH = Math.Tan(38 * Math.PI / 360);
        foreach (var (aspect, fill) in new[] { (16.0 / 9, .85), (4.0 / 3, 1.0), (1.0, .5) })
        {
            double d = screen.FitDistance(tanH, aspect, fill), usedW = screen.Width / (2 * d * tanH), usedH = screen.Height / (2 * d * tanH / aspect);
            Check(Math.Abs(Math.Max(usedW, usedH) - fill) < 1e-9 && usedW <= fill + 1e-9 && usedH <= fill + 1e-9, $"Screen fills {fill:P0} of a {aspect:F2} view in its limiting direction");
        }

        // View: full body on the backdrop, into TV mode and back
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var view = new RobotHeadView();
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming());
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D,
            Stage = new UrdfStageSettings { ModelFile = Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf") } });
        Check(view.HasTvScreen(TvFrontCommand.DefaultScreen) && !view.HasTvScreen("no_such_link"), "View finds the backdrop TV screen");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var camera = (System.Windows.Media.Media3D.PerspectiveCamera)typeof(RobotHeadView).GetField("_camera", flags).GetValue(view);
        var presentation = (Model3DGroup)typeof(RobotHeadView).GetField("_presentationModel", flags).GetValue(view);
        var root = (Model3DGroup)typeof(RobotHeadView).GetField("_sceneLightingRoot", flags).GetValue(view);
        var normal = camera.Position;
        view.SetTimelineTv(TvTimeline.StateAt(new[] { f }, 13.5), false, 0, Path.GetTempPath(), true);
        var look = camera.LookDirection; look.Normalize();
        Check(look.X < -.999 && !root.Children.Contains(presentation), "Arrived: camera faces the TV squarely and the robot is hidden");
        view.SetTimelineTv(TvTimeline.StateAt(new[] { f }, 11.5), false, 0, Path.GetTempPath(), true);
        Check((camera.Position - normal).Length > .1, "Mid-glide the camera is between the two shots");
        view.SetTimelineTv(TvState.Inactive, false, 0, Path.GetTempPath(), true);
        Check((camera.Position - normal).Length < 1e-9 && root.Children.Contains(presentation), "Return restores the camera and the robot exactly");
        var info = JsonSerializer.Serialize(view.TvInfo());
        Check(info.Contains("\"available\":true") && info.Contains("\"width\":0.57"), "TvInfo reports the screen");
        var request = JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_tv_show\",\"mediaPath\":\"a.mp4\",\"fit\":\"cover\",\"volume\":50,\"startSeconds\":1,\"loop\":true,\"fill\":0.9,\"screen\":\"s\"}", EditorApiServer.Json);
        Check(request.MediaPath == "a.mp4" && request.Fit == "cover" && request.Volume == 50 && request.StartSeconds == 1 && request.Fill == .9 && request.Screen == "s", "TV API request fields deserialize");
        // Build… settings forms: readers, validation and the exact command text they produce
        var form = new CommandSettingsWindow("URDF TV Front", "");
        var seconds = form.AddNumber("Glide seconds", 3, 0, 30, ""); var fillReader = form.AddNumber("Screen fill", .85, .2, 1, "");
        var screenLink = form.AddText("Screen link", TvFrontCommand.DefaultScreen, "");
        Check(new TvFrontCommand { Seconds = seconds(), Fill = fillReader(), Screen = screenLink() }.ToJson() == "{\"seconds\":3,\"fill\":0.85,\"screen\":\"backdrop_tv_screen_link\"}",
              "TV Front form produces the command text");
        var bad = form.AddNumber("Speed", 20, .1, 10, "");
        Check(Throws(() => bad()), "Form rejects out-of-range numbers");
        var fallForm = new CommandSettingsWindow("Fall Apart", "");
        var direction = fallForm.AddChoice("Direction", new[] { "Fall apart", "Reassemble" }, 1, ""); var speed = fallForm.AddNumber("Speed", .5, .1, 10, "");
        Check(new FallApartCommand { Reverse = direction() == 1, Speed = speed() }.ToJson() == "{\"reverse\":true,\"speed\":0.5}", "Fall Apart form produces the command text");
        app.Shutdown();
    }
}
