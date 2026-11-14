using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServoAnimator;

internal sealed class FacePacket
{
    public long TimestampMs { get; set; }
    public bool Detected { get; set; }
    public Dictionary<string, double> Values { get; set; } = new();
    public string Preview { get; set; }
    public string Error { get; set; }
    public double LatencyMs { get; set; }
    public double? ProcessedFps { get; set; }
    public int? CaptureWidth { get; set; }
    public int? CaptureHeight { get; set; }
    public double? ReportedCaptureFps { get; set; }
    public double? CameraWaitMs { get; set; }
    public double? FaceProcessingMs { get; set; }
    public string PixelFormat { get; set; }
    public string Backend { get; set; }
    public bool? FormatRequestAccepted { get; set; }
}

internal sealed partial class FaceTrackingClient : IDisposable
{
    private readonly Func<bool> _running;
    internal FaceTrackingClient(Func<bool> running = null) { _running = running; }
    private Process _process;
    private FacePacket _latest;
    private string _preview;
    private long _received;
    public FacePacket Latest => Volatile.Read(ref _latest);
    public bool Running => _process != null && !_process.HasExited;
    public string Preview => Volatile.Read(ref _preview);
    public string Error { get; private set; }
    public int RequestedFrameRate { get; private set; } = 30;
    public int CameraIndex { get; private set; }
    public bool MirrorVideo { get; private set; }
    public bool HasRecentResults => (_running?.Invoke() ?? Running) && Latest != null && Environment.TickCount64 - Interlocked.Read(ref _received) < 1000;
    public double? ProcessingFps => HasRecentResults && Latest?.ProcessedFps is double fps && double.IsFinite(fps) && fps >= 0 ? fps : null;
    public bool Fresh => (_running?.Invoke() ?? (_process != null && !_process.HasExited)) && Latest?.Detected == true && Environment.TickCount64 - Interlocked.Read(ref _received) < 500;
    internal void AcceptPacket(FacePacket packet)
    {
        if (packet?.Error != null) Error = packet.Error;
        if (packet?.Preview != null) Volatile.Write(ref _preview, packet.Preview);
        if (packet != null) { Volatile.Write(ref _latest, packet); Interlocked.Exchange(ref _received, Environment.TickCount64); }
    }
    public static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "FaceTracking", "tracker.py"))) return dir.FullName;
            throw new DirectoryNotFoundException("FaceTracking/tracker.py not found beside this solution.");
        }
    }
    public void Start(int camera, int frameRate = 30, bool mirrorVideo = false)
    {
        if (frameRate is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(frameRate), "Choose 30 or 60 fps.");
        Dispose(); Error = null;
        var start = CreateStartInfo(camera, frameRate, mirrorVideo);
        var process = Process.Start(start); _process = process; RequestedFrameRate = frameRate; CameraIndex = camera;
        MirrorVideo = mirrorVideo;
        ReadTrackingOutput(process);
    }
    private static ProcessStartInfo CreateStartInfo(int camera, int frameRate, bool mirrorVideo = false)
    {
        if (camera is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(camera), "Camera index must be 0–20.");
        if (frameRate is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(frameRate), "Choose 30 or 60 fps.");
        string folder = Path.Combine(Root, "FaceTracking");
        string python = Path.Combine(folder, ".venv", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new FileNotFoundException("Run FaceTracking/Setup.ps1 first.");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(Path.Combine(folder, "tracker.py"));
        start.ArgumentList.Add("--camera"); start.ArgumentList.Add(camera.ToString());
        start.ArgumentList.Add("--fps"); start.ArgumentList.Add(frameRate.ToString());
        if (mirrorVideo) start.ArgumentList.Add("--mirror");
        return start;
    }
    private void ReadTrackingOutput(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is string line)
                {
                    var packet = JsonSerializer.Deserialize<FacePacket>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (!ReferenceEquals(_process, process)) break;
                    AcceptPacket(packet);
                }
                if (ReferenceEquals(_process, process) && Error == null) Error = "Tracking process stopped.";
            }
            catch (Exception ex) { if (ReferenceEquals(_process, process)) Error = ex.Message; }
        });
        _ = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is string line) Debug.WriteLine("MediaPipe: " + line); } catch { } });
    }
    public void Dispose()
    {
        var process = _process; _process = null; Volatile.Write(ref _latest, null); Volatile.Write(ref _preview, null);
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}

internal sealed class FaceMapping
{
    public bool Enabled { get; set; }
    public string Signal { get; set; } = "headYaw";
    public string Target { get; set; } = "Servo:NeckTurn";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServoSpeed Speed { get; set; } = ServoSpeed.Default;
    public double InputMin { get; set; } = -30;
    public double InputMax { get; set; } = 30;
    public double OutputMin { get; set; } = -50;
    public double OutputMax { get; set; } = 50;
    public bool Reverse { get; set; }
    public double DeadZone { get; set; } = .04;
    public double SmoothingMs { get; set; } = 100;
    public double Neutral { get; set; }
    public double Map(double input)
    {
        double normalized = Math.Clamp((input - Neutral - InputMin) / (InputMax - InputMin), 0, 1);
        double center = Math.Clamp(-InputMin / (InputMax - InputMin), 0, 1);
        if (Math.Abs(normalized - center) < DeadZone) normalized = center;
        if (Reverse) normalized = 1 - normalized;
        return OutputMin + normalized * (OutputMax - OutputMin);
    }
}

internal sealed class FaceTrackingSettings
{
    public int SchemaVersion { get; set; } = 2;
    public PistonSignalSettings Pistons { get; set; } = new();
    public int Camera { get; set; }
    public int FrameRate { get; set; } = 30;
    public bool MirrorVideo { get; set; }
    public bool EnforceServoSpeeds { get; set; }
    public List<FaceMapping> Mappings { get; set; } = new()
    {
        new() { Enabled = true },
        new() { Enabled = true, Signal = "headPitch", Target = "Servo:NeckNodUp" },
        new() { Enabled = true, Signal = "headRoll", Target = "Servo:NeckTiltRight" },
        new() { Signal = "gazeX", Target = "Servo:EyesHorizontalRight", InputMin = -.5, InputMax = .5 },
        new() { Signal = "gazeY", Target = "Servo:EyesVerticalUp", InputMin = -.5, InputMax = .5 },
        new() { Signal = "browLeft", Target = "Servo:FlapsOpen", InputMin = 0, InputMax = .6, OutputMin = 0, OutputMax = 70 },
        new() { Enabled = true, Signal = "squint", Target = "Servo:IrisClose", InputMin = 0, InputMax = .6, OutputMin = 0, OutputMax = 70 }
    };
    // Derived signals followed by all 52 MediaPipe blendshape category names.
    // The helper already forwards these scores; this list also validates saved mappings.
    public static readonly string[] Signals =
    {
        "headYaw", "headPitch", "headRoll", "LPiston", "RPiston", "gazeX", "gazeY", "browLeft", "browRight", "squint",
        "_neutral",
        "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
        "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
        "eyeBlinkLeft", "eyeBlinkRight",
        "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight",
        "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight",
        "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight",
        "jawForward", "jawLeft", "jawOpen", "jawRight",
        "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft", "mouthFrownRight",
        "mouthFunnel", "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight",
        "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight",
        "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
        "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
        "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft", "noseSneerRight"
    };
    public static bool IsNeckComponent(FaceMapping m) =>
        m.Signal == "headPitch" && m.Target == "Servo:NeckNodUp" ||
        m.Signal == "headRoll" && m.Target == "Servo:NeckTiltRight";
    public const string NeckLeftTarget = "Child:NeckNodUp:NeckTiltLeft";
    public const string NeckRightTarget = "Child:NeckNodUp:NeckTiltRight";
    public Dictionary<string, double> SignalValues(FacePacket packet, out bool clipped)
    {
        var values = new Dictionary<string, double>(packet.Values);
        clipped = false;
        if (values.TryGetValue("headPitch", out double pitch) && values.TryGetValue("headRoll", out double roll)
            && double.IsFinite(pitch) && double.IsFinite(roll))
        {
            var result = Pistons.Evaluate(pitch - Pistons.PitchNeutral, roll - Pistons.RollNeutral);
            values["LPiston"] = result.Left; values["RPiston"] = result.Right; clipped = result.Clipped;
        }
        return values;
    }
    public IEnumerable<string> PistonWarnings()
    {
        var rows = Mappings.Select((m, i) => (Mapping: m, Number: i + 1)).Where(r => r.Mapping.Enabled).ToArray();
        foreach (var row in rows.Where(r => r.Mapping.Signal is "LPiston" or "RPiston"))
        {
            string expected = row.Mapping.Signal == "LPiston" ? NeckLeftTarget : NeckRightTarget;
            if (row.Mapping.Target != expected)
                yield return $"Row {row.Number} ({row.Mapping.Signal}) targets {row.Mapping.Target}; recommended: {expected}.";
            foreach (var parent in rows.Where(r => r.Mapping.Target is "Servo:NeckNodUp" or "Servo:NeckTiltRight"))
                yield return $"Conflict: row {row.Number} ({row.Mapping.Signal}) and row {parent.Number} ({parent.Mapping.Signal} → {parent.Mapping.Target}). Disable one path before applying.";
        }
    }
    public void Validate()
    {
        if (SchemaVersion != 2) throw new InvalidDataException("Unsupported face mapping schema version.");
        Pistons.Validate();
        var conflict = PistonWarnings().FirstOrDefault(w => w.StartsWith("Conflict:"));
        if (conflict != null) throw new InvalidDataException(conflict);
        if (Camera < 0 || Camera > 20) throw new InvalidDataException("Camera index must be 0–20.");
        if (FrameRate is not (30 or 60)) throw new InvalidDataException("Face tracking frame rate must be 30 or 60 fps.");
        var occupied = new HashSet<RobotControls>();
        var neckComponents = new HashSet<string>();
        bool neckReserved = false;
        ServoSpeed? neckSpeed = null;
        foreach (var m in Mappings.Where(m => m.Enabled))
        {
            if (!Enum.IsDefined(m.Speed)) throw new InvalidDataException("Choose a valid Speed profile for each enabled mapping.");
            if (!Signals.Contains(m.Signal) || !ControllerTargets.TryServo(m.Target, out var servo, out var child)) throw new InvalidDataException("Choose a signal and numeric target for each enabled mapping.");
            if (BodyControlCatalog.IsBody(servo)) throw new InvalidDataException("Body controls are available to timeline commands and controllers, not face tracking.");
            if (!new[] { m.InputMin, m.InputMax, m.OutputMin, m.OutputMax, m.DeadZone, m.SmoothingMs, m.Neutral }.All(double.IsFinite) || m.InputMin >= m.InputMax || m.OutputMin > m.OutputMax || m.DeadZone < 0 || m.DeadZone >= .5 || m.SmoothingMs < 0 || m.SmoothingMs > 2000) throw new InvalidDataException("Invalid mapping range, dead zone, or smoothing.");
            var range = ServoCommand.RangeFor(servo);
            if (m.OutputMin < range.Min || m.OutputMax > range.Max) throw new InvalidDataException($"{servo} output must be within {range.Min}–{range.Max}.");
            if (IsNeckComponent(m))
            {
                if (neckSpeed.HasValue && neckSpeed != m.Speed)
                    throw new InvalidDataException("Head pitch and head roll share the neck actuators and must use the same Speed profile. Use Left piston / Right piston mappings for separate actuator profiles.");
                neckSpeed = m.Speed;
                if (!neckComponents.Add(m.Signal)) throw new InvalidDataException("Only one mapping per neck pitch/roll component is allowed.");
                if (neckReserved) continue;
                neckReserved = true;
            }
            foreach (var control in child.HasValue ? new[] { child.Value } : ServoConfiguration.ControlsFor(servo))
                if (!occupied.Add(control)) throw new InvalidDataException("Two face mappings own the same actuator. Use independent targets; only headPitch/NeckNodUp and headRoll/NeckTiltRight may share the mixed neck pair.");
        }
    }
    public HashSet<RobotControls> OwnedControls => Mappings.Where(m => m.Enabled).SelectMany(m => ControllerTargets.TryServo(m.Target, out var servo, out var child) ? child.HasValue ? new[] { child.Value } : ServoConfiguration.ControlsFor(servo) : Array.Empty<RobotControls>()).ToHashSet();
    public static FaceTrackingSettings Load(string root)
    {
        string path = Path.Combine(root, "FaceTracking.json");
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<FaceTrackingSettings>(File.ReadAllText(path)) : new();
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Mappings", out var mappings))
            {
                int index = 0;
                foreach (var mapping in mappings.EnumerateArray())
                {
                    // Older mappings inherited the last actuator profile. Preserve that behavior.
                    if (!mapping.TryGetProperty("Speed", out _) && index < settings.Mappings.Count)
                        settings.Mappings[index].Speed = ServoSpeed.NoChange;
                    index++;
                }
            }
            if (!document.RootElement.TryGetProperty("Pistons", out _))
            {
                settings.Pistons.PitchNeutral = settings.Mappings.FirstOrDefault(m => m.Signal == "headPitch")?.Neutral ?? 0;
                settings.Pistons.RollNeutral = settings.Mappings.FirstOrDefault(m => m.Signal == "headRoll")?.Neutral ?? 0;
            }
            if (!document.RootElement.TryGetProperty("SchemaVersion", out _) || settings.SchemaVersion == 1) settings.SchemaVersion = 2;
        }
        settings.Validate(); return settings;
    }
    public void Save(string root) { Validate(); File.WriteAllText(Path.Combine(root, "FaceTracking.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
}
