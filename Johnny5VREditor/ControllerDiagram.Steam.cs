using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

public sealed partial class ControllerDiagram
{
    private static readonly Lazy<BitmapSource> SteamFace = new(() => LoadControllerArtwork("steam-face.png"));
    private static readonly Lazy<BitmapSource> SteamShoulders = new(() => LoadControllerArtwork("steam-shoulders.png"));
    private static readonly Lazy<BitmapSource> SteamUnderside = new(() => LoadControllerArtwork("steam-underside.png"));
    private bool DrawSteamArtwork(DrawingContext dc)
    {
        if (SteamFace.Value == null || SteamShoulders.Value == null || SteamUnderside.Value == null) return false;
        Text(dc, "SHOULDER EDGE · RIGHT STAYS RIGHT", 240, 7, 11, Brushes.SlateGray);
        dc.DrawImage(SteamShoulders.Value, new Rect(55, 10, 370, 123.333));
        Hot(dc, "LeftTrigger", "L2", 102, 52, 34, 20);
        Hot(dc, "RightTrigger", "R2", 344, 52, 34, 20);
        foreach (bool left in new[] { true, false })
        {
            double x = left ? 83 : 307;
            if (NoMux) Hot(dc, left ? "LeftShoulder" : "RightShoulder", left ? "Left Shoulder" : "Right Shoulder", x, 83, 90, 21);
            else
            {
                int bit = left ? 1 : 2;
                bool held = LiveLayer >= 0 && (LiveLayer & bit) != 0;
                dc.DrawRoundedRectangle((MappingLayer & bit) != 0 ? _accent : _button,
                    new Pen(held ? Brushes.LimeGreen : _edge, held ? 2 : 1), new Rect(x, 83, 90, 21), 6, 6);
                Text(dc, left ? "Left MUX" : "Right MUX", x + 45, 87, 11, Brushes.White);
            }
        }
        Text(dc, "TOP / FACE VIEW", 240, 133, 11, Brushes.SlateGray);
        dc.DrawImage(SteamFace.Value, new Rect(55, 110, 370, 370));
        void Physical(string id, double x, double y, double radius, string label = null)
        {
            _hits[new Rect(x - radius + OriginX + LiveMargin, y - radius + OriginY, radius * 2, radius * 2)] = id;
            _anchors[id] = new Point(x + OriginX, y + OriginY);
            bool live = IsLive(id), selected = SelectedInput == id;
            if (live || selected) dc.DrawEllipse(null, new Pen(live ? Brushes.LimeGreen : Brushes.Turquoise, 2), new Point(x, y), radius, radius);
            if (label != null) Text(dc, label, x, y - 7, 11, Brushes.White);
        }
        void Joystick(string side, double x, double y)
        {
            bool live = new[] { "X", "Y", "Click", "StickTouch" }.Any(s => IsLive(side + s));
            if (live) dc.DrawEllipse(null, new Pen(Brushes.LimeGreen, 2), new Point(x, y), 23, 23);
            var pen = new Pen(Brushes.LightSteelBlue, 1.2);
            dc.DrawLine(pen, new Point(x, y), new Point(x + (side == "Left" ? -28 : 28), y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + 29));
            Hot(dc, side + "StickTouch", "touch", x - 23, y - 48, 46, 17);
            Hot(dc, side + "Click", "click", x - 23, y - 29, 46, 17);
            Hot(dc, side + "X", "X", x + (side == "Left" ? -39 : 17), y - 10, 22, 20);
            Hot(dc, side + "Y", "Y", x - 11, y + 20, 22, 20);
        }
        Joystick("Left", 158, 212); Joystick("Right", 321, 212);
        Physical("Guide", 240, 205, 13); Physical("Back", 221, 235, 10);
        Physical("Start", 260, 235, 10); Physical("Share", 240, 267, 11);
        Physical("DpadUp", 144, 254, 8, "↑"); Physical("DpadDown", 144, 286, 8, "↓");
        Physical("DpadLeft", 128, 270, 8, "←"); Physical("DpadRight", 160, 270, 8, "→");
        Physical("Y", 339, 253, 11); Physical("X", 320, 271, 11);
        Physical("B", 360, 271, 11); Physical("A", 339, 290, 11);
        void Trackpad(string side, double x)
        {
            var box = new Rect(x, 289, 72, 65);
            bool live = new[] { "Touch", "X", "Y", "Click", "Pressure" }.Any(s => IsLive(side + "Pad" + s));
            if (live) dc.DrawRoundedRectangle(null, new Pen(Brushes.LimeGreen, 2), box, 10, 10);
            Hot(dc, side + "PadTouch", "touch", x + 6, 291, 60, 17);
            Hot(dc, side + "PadX", "X", x + 6, 311, 27, 17);
            Hot(dc, side + "PadY", "Y", x + 39, 311, 27, 17);
            Hot(dc, side + "PadClick", "click", x + 6, 332, 60, 17);
            Hot(dc, side + "PadPressure", "pressure", x + 6, 357, 60, 17);
        }
        Trackpad("Left", 158); Trackpad("Right", 250);
        Text(dc, "MOTION · GYRO & ACCELEROMETER", 240, 483, 11, Brushes.Teal);
        for (int i = 0; i < 3; i++)
        {
            Hot(dc, new[] { "GyroPitch", "GyroYaw", "GyroRoll" }[i], new[] { "Pitch (X)", "Yaw (Y)", "Roll (Z)" }[i], 103, 507 + i * 30, 114, 24);
            Hot(dc, "Accel" + "XYZ"[i], "Accel " + "XYZ"[i], 263, 507 + i * 30, 114, 24);
        }
        Text(dc, "UNDERSIDE · REAR BUTTONS & GRIP TOUCH", 240, 615, 11, Brushes.SlateGray);
        dc.DrawImage(SteamUnderside.Value, new Rect(95, 636, 290, 145));
        Hot(dc, "L4", "L4", 164, 690, 30, 19); Hot(dc, "R4", "R4", 286, 690, 30, 19);
        Hot(dc, "L5", "L5", 162, 716, 30, 19); Hot(dc, "R5", "R5", 288, 716, 30, 19);
        Hot(dc, "LeftGripTouch", "Left grip", 98, 752, 68, 20);
        Hot(dc, "RightGripTouch", "Right grip", 314, 752, 68, 20);
        return true;
    }
}
