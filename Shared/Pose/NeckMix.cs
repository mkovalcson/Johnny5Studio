namespace ServoAnimator;

/// <summary>
/// The neck's two pistons (NeckTiltLeft, NeckTiltRight) are driven by two complementary logical controls:
/// NeckNodUp moves them together and NeckTiltRight moves them oppositely. Both are always resolved into the
/// pistons here, so hardware, the URDF preview and exports agree.
///
/// Per piston: u = s(Nod)·nod + s(Tilt)·tilt, clamped to ±100 of that piston's own travel, where s(P) is −1
/// when ServoConfig reverses the piston for parent P (gang direction) and +1 otherwise. Clamping each piston
/// on its own means nod keeps priority and tilt uses whatever travel is left: at full nod a tilt can only move
/// one piston. Piston values are returned in NeckNodUp child units (value = s(Nod)·u), so the normal pulse
/// mapping (gang direction plus the servo's own Reverse) applies unchanged.
/// </summary>
public static class NeckMix
{
    public static bool IsNeckParent(ServoNames servo) => servo is ServoNames.NeckNodUp or ServoNames.NeckTiltRight;
    public static bool IsPiston(RobotControls control) => control is RobotControls.NeckTiltLeft or RobotControls.NeckTiltRight;

    private static double Sign(ServoConfiguration config, ServoNames parent, RobotControls piston) =>
        config?.GangReversed(parent, piston) == true ? -1 : 1;

    /// <summary>Both pistons, in NeckNodUp child units, for a nod and tilt (each −100..100).</summary>
    public static (double Left, double Right) Pistons(ServoConfiguration config, double nod, double tilt)
    {
        double Piston(RobotControls c)
        {
            double u = Math.Clamp(Sign(config, ServoNames.NeckNodUp, c) * nod + Sign(config, ServoNames.NeckTiltRight, c) * tilt, -100, 100);
            return Sign(config, ServoNames.NeckNodUp, c) * u;
        }
        return (Piston(RobotControls.NeckTiltLeft), Piston(RobotControls.NeckTiltRight));
    }

    /// <summary>The nod and tilt a piston pair represents (inverse of <see cref="Pistons"/> when nothing clips).</summary>
    public static (double Nod, double Tilt) Decompose(ServoConfiguration config, double left, double right)
    {
        double uL = Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltLeft) * left;
        double uR = Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltRight) * right;
        double nod = (Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltLeft) * uL + Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltRight) * uR) / 2;
        double tilt = (Sign(config, ServoNames.NeckTiltRight, RobotControls.NeckTiltLeft) * uL + Sign(config, ServoNames.NeckTiltRight, RobotControls.NeckTiltRight) * uR) / 2;
        return (nod, tilt);
    }

    /// <summary>Convert a direct piston value authored under either neck parent into NeckNodUp child units.</summary>
    public static double ToNodUnits(ServoConfiguration config, ServoNames parent, RobotControls piston, double value) =>
        parent == ServoNames.NeckNodUp ? value : Sign(config, ServoNames.NeckNodUp, piston) * Sign(config, parent, piston) * value;

    /// <summary>True when nod moves the pistons together and tilt moves them oppositely (a valid neck setup).
    /// If a configuration breaks this, Decompose cannot separate the two and callers should warn.</summary>
    public static bool IsComplementary(ServoConfiguration config) =>
        Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltLeft) * Sign(config, ServoNames.NeckTiltRight, RobotControls.NeckTiltLeft) ==
        -Sign(config, ServoNames.NeckNodUp, RobotControls.NeckTiltRight) * Sign(config, ServoNames.NeckTiltRight, RobotControls.NeckTiltRight);

    /// <summary>For exports: replace every enabled NeckNodUp/NeckTiltRight command and direct piston command with
    /// the piston commands they resolve to, in time order (commands at the same time form one piston pair).
    /// Pistons are written as NeckNodUp child commands; with <paramref name="gangAdjusted"/> (Individual export)
    /// the value is already adjusted for the gang direction, like expanded ganged commands. Disable commands and
    /// all other commands pass through unchanged.</summary>
    public static List<ServoCommand> ResolveCommands(IEnumerable<ServoCommand> commands, ServoConfiguration config, bool gangAdjusted)
    {
        var list = commands.ToList();
        bool IsNeck(ServoCommand c) => !c.Disable && !c.IsTextServo && IsNeckParent(c.Servo) && (!c.Control.HasValue || IsPiston(c.Control.Value));
        var result = list.Where(c => !IsNeck(c)).ToList();
        var state = new State(() => config);
        foreach (var group in list.Where(IsNeck).GroupBy(c => ServoCommand.TimeKey(c.OffsetSeconds)).OrderBy(g => g.Key))
        {
            bool left = false, right = false;
            foreach (var c in group)
            {
                if (c.Control.HasValue)
                {
                    state.SetPiston(c.Servo, c.Control.Value, c.NumericValue);
                    if (c.Control == RobotControls.NeckTiltLeft) left = true; else right = true;
                }
                else { state.SetParent(c.Servo, c.NumericValue); left = right = true; }
            }
            var speed = group.Select(c => c.Speed).FirstOrDefault(s => s != ServoSpeed.NoChange, ServoSpeed.NoChange);
            foreach (var (piston, used, value) in new[] { (RobotControls.NeckTiltLeft, left, state.Left), (RobotControls.NeckTiltRight, right, state.Right) })
            {
                if (!used) continue;
                var command = group.First().Clone();
                command.Servo = ServoNames.NeckNodUp; command.Control = piston; command.Speed = speed;
                double output = gangAdjusted ? Sign(config, ServoNames.NeckNodUp, piston) * value : value;
                command.NumericValue = (int)Math.Round(Math.Clamp(output, -100, 100));
                command.Reason = "neck nod/tilt resolved to piston";
                result.Add(command);
            }
        }
        return result;
    }

    /// <summary>Resolve prepared playback keys using the same saturated piston mix as live/editor poses.</summary>
    public static (double Nod, double Tilt) Inputs(ServoConfiguration config,
        IReadOnlyDictionary<string, (double Value, double Stamp, string Servo)> pose, bool childWinsTies = true)
    {
        bool hasNod = pose.TryGetValue("servo:NeckNodUp", out var n), hasTilt = pose.TryGetValue("servo:NeckTiltRight", out var t);
        double stamp = Math.Max(hasNod ? n.Stamp : double.NegativeInfinity, hasTilt ? t.Stamp : double.NegativeInfinity);
        var mixed = Pistons(config, hasNod ? n.Value : 0, hasTilt ? t.Value : 0);
        double Piston(RobotControls control, double value)
        {
            if (pose.TryGetValue("control:" + control, out var c)
                && MoviePoseContinuity.ChildOverrides(stamp, c.Stamp, childWinsTies)
                && Enum.TryParse<ServoNames>(c.Servo, out var parent) && IsNeckParent(parent))
                return Math.Clamp(ToNodUnits(config, parent, control, c.Value), -100, 100);
            return value;
        }
        return Decompose(config, Piston(RobotControls.NeckTiltLeft, mixed.Left), Piston(RobotControls.NeckTiltRight, mixed.Right));
    }

    /// <summary>Running nod/tilt/piston state shared by consumers that receive commands one at a time.</summary>
    public sealed class State
    {
        private readonly Func<ServoConfiguration> _config;
        public State(Func<ServoConfiguration> config) => _config = config;
        public double Nod { get; private set; }
        public double Tilt { get; private set; }
        public double Left { get; private set; }
        public double Right { get; private set; }

        /// <summary>A NeckNodUp or NeckTiltRight value: re-mix both pistons.</summary>
        public void SetParent(ServoNames parent, double value)
        {
            if (parent == ServoNames.NeckNodUp) Nod = value; else Tilt = value;
            (Left, Right) = Pistons(_config(), Nod, Tilt);
        }

        public void Set(double nod, double tilt) { Nod = nod; Tilt = tilt; (Left, Right) = Pistons(_config(), Nod, Tilt); }

        /// <summary>A direct piston value under either parent: replace that piston, then re-derive nod and tilt
        /// so the next nod or tilt continues from this pair.</summary>
        public void SetPiston(ServoNames parent, RobotControls piston, double value)
        {
            double v = Math.Clamp(ToNodUnits(_config(), parent, piston, value), -100, 100);
            if (piston == RobotControls.NeckTiltLeft) Left = v; else Right = v;
            (Nod, Tilt) = Decompose(_config(), Left, Right);
        }
    }
}
