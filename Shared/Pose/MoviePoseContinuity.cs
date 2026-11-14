namespace ServoAnimator
{
    public static class MoviePoseContinuity
    {
        // An untouched child keeps its individual carried pose, not the gang's
        // default value. Use the same precedence for capture and display.
        /// <summary>Editors let a gang win an equal-time tie; existing VR tracks let a child win.
        /// Keep that boundary explicit while sharing the precedence calculation.</summary>
        public static bool ChildOverrides(double? parentTime, double? childTime, bool childWinsTies = false)
            => childTime.HasValue && (!parentTime.HasValue || childTime > parentTime || childWinsTies && childTime == parentTime);

        public static string Key(string servo, string control)
            => string.IsNullOrEmpty(control) ? "servo:" + servo : "control:" + control;

        /// <summary>Movie carry uses a stamp before any authored key in the next sequence.</summary>
        public static void Carry(IDictionary<string, (double Value, double Stamp, string Servo)> pose,
            string servo, string control, double value) => pose[Key(servo, control)] = (value, -1, servo);

        /// <summary>Resolve a prepared track pair without changing native units or physical calibration.</summary>
        public static bool TryChannel(IReadOnlyDictionary<string, (double Value, double Stamp, string Servo)> pose,
            string servo, string control, out double value, bool childWinsTies = true)
        {
            bool parent = pose.TryGetValue("servo:" + servo, out var p), child = pose.TryGetValue("control:" + control, out var c);
            var source = ChildOverrides(parent ? p.Stamp : null, child ? c.Stamp : null, childWinsTies) ? c : p;
            value = source.Value;
            // A physical child is owned by the logical control that authored it.
            return (parent || child) && (!child || source.Servo == servo);
        }

        public static double ChildValue(double gangValue, double? gangTime,
            ServoCommand child, double? carried)
        {
            if (child != null && ChildOverrides(gangTime, child.OffsetSeconds))
                return child.NumericValue;
            if (!gangTime.HasValue && child == null && carried.HasValue)
                return carried.Value;
            return gangValue;
        }
    }
}
