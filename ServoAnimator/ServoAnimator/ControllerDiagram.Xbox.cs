using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

public sealed partial class ControllerDiagram
{
    // Decode once and share frozen images across the configuration and mapping views.
    private static readonly Lazy<BitmapSource> XboxFace = new(() => LoadControllerArtwork("xbox-face.png"));
    private static readonly Lazy<BitmapSource> XboxShoulders = new(() => LoadControllerArtwork("xbox-shoulders.png"));
    private static BitmapSource LoadControllerArtwork(string file)
    {
        try
        {
            var image = new BitmapImage(); image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/AnimationEditorPlayer;component/Assets/ControllerArtwork/" + file);
            image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 1024;
            image.EndInit(); image.Freeze(); return image;
        }
        catch { return null; } // Retain the existing vector diagram if a resource cannot load.
    }
    private bool DrawXboxArtwork(DrawingContext dc)
    {
        if (XboxFace.Value == null || XboxShoulders.Value == null) return false;
        Text(dc, "SHOULDER EDGE · RIGHT STAYS RIGHT", 240, 7, 11, Brushes.SlateGray);
        dc.DrawImage(XboxShoulders.Value, new Rect(55, 18, 370, 123.333));
        Hot(dc, "LeftTrigger", "LT", 102, 52, 34, 20);
        Hot(dc, "RightTrigger", "RT", 344, 52, 34, 20);
        void Shoulder(bool left)
        {
            int bit = left ? 1 : 2;
            bool held = LiveLayer >= 0 && (LiveLayer & bit) != 0;
            var bounds = new Rect(left ? 83 : 307, 88, 90, 21);
            dc.DrawRoundedRectangle((MappingLayer & bit) != 0 ? _accent : _button,
                new Pen(held ? Brushes.LimeGreen : _edge, held ? 2 : 1), bounds, 6, 6);
            Text(dc, left ? "Left MUX" : "Right MUX", bounds.X + bounds.Width / 2, bounds.Y + 3, 11, Brushes.White);
        }
        Shoulder(true); Shoulder(false);
        Text(dc, "TOP / FACE VIEW", 240, 139, 11, Brushes.SlateGray);
        dc.DrawImage(XboxFace.Value, new Rect(55, 156, 370, 277.5));

        // Hit regions and leader anchors follow the physical control centers in the artwork.
        // Transparent idle overlays leave the molded buttons and their lettering visible.
        void Physical(string id, double x, double y, double radius, string label = null)
        {
            var rect = new Rect(x - radius, y - radius, radius * 2, radius * 2);
            _hits[new Rect(rect.X + OriginX + LiveMargin, rect.Y + OriginY, rect.Width, rect.Height)] = id;
            _anchors[id] = new Point(x + OriginX, y + OriginY);
            bool live = IsLive(id), selected = SelectedInput == id;
            if (live || selected)
                dc.DrawEllipse(null, new Pen(live ? Brushes.LimeGreen : Brushes.Turquoise, 2), new Point(x, y), radius, radius);
            if (label != null) Text(dc, label, x, y - 7, 12, live ? Brushes.LightGreen : Brushes.White);
        }
        void Joystick(string side, double x, double y)
        {
            bool live = IsLive(side + "X") || IsLive(side + "Y") || IsLive(side + "Click");
            bool selected = SelectedInput == side + "X" || SelectedInput == side + "Y" || SelectedInput == side + "Click";
            if (live || selected) dc.DrawEllipse(null, new Pen(live ? Brushes.LimeGreen : Brushes.Turquoise, 2), new Point(x, y), 27, 27);
            var pen = new Pen(Brushes.LightSteelBlue, 1.2);
            dc.DrawLine(pen, new Point(x, y), new Point(x + (side == "Left" ? -30 : 30), y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + 30));
            Hot(dc, side + "Click", "click", x - 23, y - 38, 46, 18);
            Hot(dc, side + "X", "X", x + (side == "Left" ? -41 : 19), y - 10, 22, 20);
            Hot(dc, side + "Y", "Y", x - 11, y + 20, 22, 20);
        }
        Joystick("Left", 147, 237);
        Joystick("Right", 292, 295);
        Physical("Guide", 240, 203, 15);
        Physical("Back", 215, 237, 10);
        Physical("Start", 266, 237, 10);
        Physical("Share", 240, 256, 10);
        Physical("Y", 333, 211, 13); Physical("X", 310, 237, 13);
        Physical("B", 358, 237, 13); Physical("A", 334, 263, 13);
        void DpadArrow(string id, double x, double y, double dx, double dy)
        {
            Physical(id, x, y, 9);
            // Explicit geometry keeps each arrow centered, independent of font metrics.
            var tip = new Point(x + dx * 4.5, y + dy * 4.5);
            var pen = new Pen(Brushes.White, 1.3);
            dc.DrawLine(pen, new Point(x - dx * 4.5, y - dy * 4.5), tip);
            dc.DrawLine(pen, tip, new Point(tip.X - dx * 3 - dy * 3, tip.Y - dy * 3 + dx * 3));
            dc.DrawLine(pen, tip, new Point(tip.X - dx * 3 + dy * 3, tip.Y - dy * 3 - dx * 3));
            _anchors[id] = new Point(tip.X + OriginX, tip.Y + OriginY);
        }
        DpadArrow("DpadUp", 193, 280, 0, -1); DpadArrow("DpadDown", 193, 310, 0, 1);
        DpadArrow("DpadLeft", 178, 295, -1, 0); DpadArrow("DpadRight", 208, 295, 1, 0);
        if (!ReadOnly) Text(dc, "Click a control or its mapping to edit", 240, 465, 12, Brushes.SlateGray);
        return true;
    }
}
