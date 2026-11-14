using System.Drawing.Drawing2D;

namespace Johnny5VR;

internal static class StudioDesktopTheme
{
    internal static readonly Color Background = Color.FromArgb(0x19,0x1E,0x27);
    internal static readonly Color Panel = Color.FromArgb(0x22,0x2A,0x35);
    internal static readonly Color Input = Color.FromArgb(0x17,0x1D,0x26);
    internal static readonly Color Status = Color.FromArgb(0x15,0x1B,0x24);
    internal static readonly Color Text = Color.FromArgb(0xEA,0xF0,0xF7);
    internal static readonly Color Secondary = Color.FromArgb(0xB0,0xBD,0xCF);
    internal static readonly Color Divider = Color.FromArgb(0x34,0x41,0x52);
    internal static readonly Color Accent = Color.FromArgb(0x84,0xB5,0xF0);
    internal static void Apply(Control root)
    {
        if(root is Form){root.BackColor=Background;root.ForeColor=Text;}
        foreach(Control child in root.Controls)
        {
            if(child is TextBoxBase or NumericUpDown or ComboBox)
            {
                child.BackColor=Input;child.ForeColor=Text;
            }
            if(child is ComboBox combo && combo.DropDownStyle==ComboBoxStyle.DropDownList)
            {
                combo.FlatStyle=FlatStyle.Flat;combo.DrawMode=DrawMode.OwnerDrawFixed;
                combo.DrawItem-=DrawChoice;combo.DrawItem+=DrawChoice;
            }
            Apply(child);
        }
    }
    private static void DrawChoice(object? sender,DrawItemEventArgs e)
    {
        if(sender is not ComboBox combo||e.Index<0)return;
        using var fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(0x35,0x57,0x7F):Input);
        e.Graphics.FillRectangle(fill,e.Bounds);
        TextRenderer.DrawText(e.Graphics,combo.GetItemText(combo.Items[e.Index]),combo.Font,e.Bounds,combo.Enabled?Text:Secondary,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
}

internal sealed class StudioDesktopButton : Button
{
    private bool hovered,pressed;
    internal bool Accent;
    internal StudioDesktopButton()
    {
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        UseVisualStyleBackColor=false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);pressed=false;Invalidate();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space){pressed=true;Invalidate();}}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);pressed=false;Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);pressed=false;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor??StudioDesktopTheme.Background);
        if(Width<4||Height<4)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float d=Math.Min(12f*DeviceDpi/96,Math.Min(Width,Height)-2);
        float right=Width-1.5f,bottom=Height-1.5f;
        using var path=new GraphicsPath();
        path.AddArc(.5f,.5f,d,d,180,90);path.AddArc(right-d,.5f,d,d,270,90);
        path.AddArc(right-d,bottom-d,d,d,0,90);path.AddArc(.5f,bottom-d,d,d,90,90);path.CloseFigure();
        var fill=!Enabled?StudioDesktopTheme.Panel:pressed&&(hovered||Focused)?Color.FromArgb(0x42,0x59,0x75):
            hovered?Color.FromArgb(0x37,0x47,0x5D):Accent?Color.FromArgb(0x35,0x57,0x7F):Color.FromArgb(0x2B,0x36,0x45);
        using var brush=new SolidBrush(fill);
        using var pen=new Pen(Focused&&ShowFocusCues?Color.FromArgb(0xA1,0xC8,0xFA):Accent&&Enabled?StudioDesktopTheme.Accent:Color.FromArgb(0x46,0x57,0x6D));
        e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);
        var rect=Rectangle.Inflate(ClientRectangle,-4,0);
        TextRenderer.DrawText(e.Graphics,Text,Font,rect,Enabled?StudioDesktopTheme.Text:StudioDesktopTheme.Secondary,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|
            (ShowKeyboardCues?TextFormatFlags.Default:TextFormatFlags.HidePrefix));
    }
}
