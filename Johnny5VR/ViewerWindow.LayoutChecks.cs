namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    // Test-only DPI override: exercise the real layout and font metrics without changing Windows settings.
    private int? layoutCheckDpi;
    internal static void CheckDisplayScaling()
    {
        int assertions=0;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);assertions++;}
        static IEnumerable<Control> Descendants(Control root)
        {
            yield return root;
            foreach(Control child in root.Controls)foreach(var item in Descendants(child))yield return item;
        }
        foreach(int dpi in new[]{96,120,144,192,240,288})
        {
            using var window=new ViewerWindow(new ViewerSettings(),layoutCheckOnly:true);
            window.Show();
            float factor=dpi/(float)window.DeviceDpi;
            var fonts=Descendants(window).ToDictionary(c=>c,c=>(c.Font.FontFamily,c.Font.Size,c.Font.Style));
            window.SuspendLayout();
            window.Scale(new SizeF(factor,factor));
            foreach(var (control,font) in fonts)
                control.Font=new Font(font.FontFamily,font.Size*factor,font.Style);
            window.layoutCheckDpi=dpi;window.menuLayoutReady=true;
            window.ResumeLayout(true);window.PerformLayout();
            foreach(int logicalWidth in new[]{280,380,600})
            {
                window.workspace.SplitterDistance=window.MenuPixels(logicalWidth);
                window.PerformLayout();window.ResizeMenuControls();window.PerformLayout();
                window.VerifyFixedControls();
                foreach(var button in Descendants(window).OfType<Button>())
                {
                    var preferred=button.GetPreferredSize(new Size(button.Width,0));
                    Check(button.Height>=preferred.Height-1,$"{dpi} DPI: button height clips '{button.Text}' ({button.Height} < {preferred.Height})");
                }
                foreach(Panel field in window.sidebar.Controls.OfType<Panel>().Where(c=>Equals(c.Tag,"field")))
                {
                    var label=field.Controls[0];var input=field.Controls[1];
                    Check(!label.Bounds.IntersectsWith(input.Bounds),$"{dpi} DPI: field label overlaps numeric input");
                    Check(field.ClientRectangle.Contains(input.Bounds),$"{dpi} DPI: numeric input clipped");
                }
                foreach(var control in Descendants(window.workspace.Panel2).Where(c=>c.Parent!=null && (c is System.Windows.Forms.Button || c is ComboBox || c is TableLayoutPanel)))
                    Check(control.Parent!.ClientRectangle.Contains(control.Bounds),$"{dpi} DPI: playback control outside its panel: {control.GetType().Name} '{control.Text}' {control.Bounds} in {control.Parent.ClientRectangle}");
                int logicalSaved=(int)Math.Round(window.workspace.SplitterDistance*96.0/window.MenuDpi);
                Check(Math.Abs(logicalSaved-logicalWidth)<=1,$"{dpi} DPI: saved sidebar width is not logical");
            }
            if(dpi is 96 or 192)
            {
                Directory.CreateDirectory(Diagnostics.Folder);
                using var bitmap=new Bitmap(window.Width,window.Height);
                window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.Combine(Diagnostics.Folder,$"viewer-layout-{dpi}dpi.png"));
            }
            window.Close();
        }
        Directory.CreateDirectory(Diagnostics.Folder);
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"display-scaling-checks.txt"),$"PASS: {assertions} layout assertions at simulated 100%, 125%, 150%, 200%, 250%, 300% scaling and three menu widths.");
        Console.WriteLine(File.ReadAllText(Path.Combine(Diagnostics.Folder,"display-scaling-checks.txt")));
    }
}
