// ---------------------------------------------------------------------------
// RobotControlsConfig.cs
//
// The individual hardware servo channels (RobotControls), their PWM
// configuration (default/min/max pulse widths, direction, 4-element speed
// and acceleration arrays indexed Default/Slow/Fast/Crawl), JSON load/save
// for the Servo Configuration window, and the GANG MAP tying the logical
// ServoNames used on the timeline to the physical controls they drive:
//
//   FlapsOpen      -> BrowLeftTopOpen, BrowRightTopOpen,
//                     BrowLeftBottomOpen, BrowRightBottomOpen
//   FlapTiltUp     -> BrowLeftTopTilt, BrowRightTopTilt
//   IrisClose      -> LeftIris, RightIris
//   VentsOpen      -> LeftEyeVent, RightEyeVent
//   NeckTiltRight  -> NeckTiltLeft, NeckTiltRight   (same pair as NeckNodUp)
//   NeckNodUp      -> NeckTiltLeft, NeckTiltRight
//   EyesHorizontalRight -> LeftLensHorizontal, RightLensHorizontal
//   EyesVerticalUp      -> LeftLensVertical, RightLensVertical
//   (single-control names map 1:1; RGBCommand and the eye pops map to none)
//
// The built-in defaults match ServoConfig.default.json. RobotControls keeps
// the historical Maestro channels as default assignments, while each servo's
// MaestroPort field can remap its physical output channel.
// ---------------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServoAnimator
{
    // Physical control identities are supplied by J5Studio.Controls.

    /// <summary>Configuration of one physical servo channel.</summary>
    public class ServoConfigEntry
    {
        public const int PwmFloor = 500;
        public const int PwmCeiling = 2400;

        [JsonPropertyName("control")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RobotControls Control { get; set; }

        /// <summary>Pololu Maestro channel (0-23) used by this physical
        /// servo. Older ServoConfig.json files do not contain this field;
        /// when absent, the historical RobotControls enum value is used so
        /// existing configurations retain their current channel assignments.</summary>
        [JsonPropertyName("maestroPort")]
        public int MaestroPort
        {
            get
            {
                if (_maestroPort.HasValue) return ClampMaestroPort(_maestroPort.Value);
                int legacy = (int)Control;
                return legacy >= 0 && legacy <= 23 ? legacy : 0;
            }
            set => _maestroPort = ClampMaestroPort(value);
        }
        private int? _maestroPort;

        /// <summary>Direction relative to its ganged ServoName: false =
        /// Normal, true = Reversed.</summary>
        [JsonPropertyName("reversed")]
        public bool Reversed { get; set; }

        [JsonPropertyName("defaultPwm")]
        public int DefaultPwm { get => _def; set => _def = ClampPwm(value); }
        private int _def = 1500;

        [JsonPropertyName("minPwm")]
        public int MinPwm { get => _min; set => _min = ClampPwm(value); }
        private int _min = PwmFloor;

        [JsonPropertyName("maxPwm")]
        public int MaxPwm { get => _max; set => _max = ClampPwm(value); }
        private int _max = PwmCeiling;

        /// <summary>Speed per ServoSpeed: [Default, Slow, Fast, Crawl].</summary>
        [JsonPropertyName("speeds")]
        public int[] Speeds { get; set; } = new int[4];

        /// <summary>Acceleration per ServoSpeed: [Default, Slow, Fast, Crawl].</summary>
        [JsonPropertyName("accels")]
        public int[] Accels { get; set; } = new int[4];

        public static int ClampPwm(int v) => Math.Clamp(v, PwmFloor, PwmCeiling);
        public static int ClampMaestroPort(int v) => Math.Clamp(v, 0, 23);
    }

    /// <summary>The whole servo configuration document (JSON root).</summary>
    public class ServoConfiguration
    {
        [JsonPropertyName("servos")]
        public List<ServoConfigEntry> Servos { get; set; } = new();

        /// <summary>
        /// Direction of one control RELATIVE TO one ganged ServoName. This
        /// is per-(gang, control) because a control can serve two gangs
        /// differently: NeckNodUp moves the neck-tilt pair the SAME way,
        /// NeckTiltRight moves them OPPOSITE each other.
        /// </summary>
        public class GangDirectionEntry
        {
            [JsonPropertyName("servo")]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public ServoNames Servo { get; set; }

            [JsonPropertyName("control")]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public RobotControls Control { get; set; }

            [JsonPropertyName("reversed")]
            public bool Reversed { get; set; }
        }

        [JsonPropertyName("gangDirections")]
        public List<GangDirectionEntry> GangDirections { get; set; } = new();

        /// <summary>Is this control reversed RELATIVE TO this gang? This
        /// is a separate layer ON TOP of the control's own hardware
        /// Reverse flag (which stays in its ServoConfigEntry): absent
        /// entries mean "not gang-reversed". The hardware directions
        /// already make NeckTiltRight move the pair opposite each other;
        /// NeckNodUp adds a gang reversal on one servo to move them the
        /// same way instead.</summary>
        public bool GangReversed(ServoNames servo, RobotControls control)
        {
            var e = GangDirections.FirstOrDefault(
                        g => g.Servo == servo && g.Control == control);
            return e?.Reversed ?? false;
        }

        public void SetGangReversed(ServoNames servo, RobotControls control, bool reversed)
        {
            var e = GangDirections.FirstOrDefault(
                        g => g.Servo == servo && g.Control == control);
            if (e == null)
                GangDirections.Add(new GangDirectionEntry
                { Servo = servo, Control = control, Reversed = reversed });
            else
                e.Reversed = reversed;
        }

        /// <summary>Serial number of the LEFT Tic T249 eye-pop controller
        /// (the finder uses it to tell left from right; empty = the first
        /// Tic found is treated as left).</summary>
        [JsonPropertyName("leftTicSerialNumber")]
        public string LeftTicSerialNumber { get; set; } = "00475552";

        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public ServoConfigEntry Get(RobotControls c) =>
            Servos.FirstOrDefault(s => s.Control == c);

        public static ServoConfiguration Load(string path)
        {
            var cfg = JsonSerializer.Deserialize<ServoConfiguration>(
                          File.ReadAllText(path), Opts) ?? new ServoConfiguration();
            // Backfill any controls missing from the file with defaults so
            // the window always shows every channel.
            var defaults = CreateDefault();
            foreach (var d in defaults.Servos)
                if (cfg.Get(d.Control) == null) cfg.Servos.Add(d);
            foreach (var d in defaults.GangDirections)
                if (!cfg.GangDirections.Any(g => g.Servo == d.Servo &&
                                                 g.Control == d.Control))
                    cfg.GangDirections.Add(d);
            return cfg;
        }

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));

        /// <summary>Gang map: which physical controls each logical
        /// ServoName drives. Empty = no hardware mapping (RGBCommand,
        /// eye pops, and the export-only Play).</summary>
        public static readonly Dictionary<ServoNames, RobotControls[]> GangMap = ControlDefinitions.GangMap;

        public static RobotControls[] ControlsFor(ServoNames s) =>
            GangMap.TryGetValue(s, out var c) ? c : Array.Empty<RobotControls>();

        /// <summary>Default configuration scraped from ConfigureServos.cs:
        /// home/min/max pulse widths, direction, and the Default/Slow/Fast/
        /// Crawl speed and acceleration arrays.</summary>
        public static ServoConfiguration CreateDefault()
        {
            var defaults = ControlDefaults.Create();
            return new ServoConfiguration
            {
                GangDirections = defaults.GangDirections.Select(g => new GangDirectionEntry
                { Servo = g.Servo, Control = g.Control, Reversed = g.Reversed }).ToList(),
                Servos = defaults.Servos.Select(c => new ServoConfigEntry
                {
                    Control = c.Control,
                    MaestroPort = c.MaestroPort,
                    Reversed = c.Reversed,
                    DefaultPwm = c.DefaultPwm,
                    MinPwm = c.MinPwm,
                    MaxPwm = c.MaxPwm,
                    Speeds = c.Speeds,
                    Accels = c.Accels
                }).ToList()
            };
        }
    }
}
