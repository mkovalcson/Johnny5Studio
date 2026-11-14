using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Johnny5VR;

internal sealed record PanelChromeState(PanelTarget Hover,bool Locked,string Message);
internal sealed record PanelImage(byte[] Rgba,int Width,int Height,Rectangle Content);
internal static class EditorPanelChrome
{
    internal const int Border=26,Top=122,Bottom=34;
    internal static Rectangle Content(int width,int height)=>new(Border,Top,width,height);
    internal static PanelTarget Hit(int width,int height,float u,float v)
    {
        float x=u*width,y=v*height;
        if(x<0||y<0||x>width||y>height)return PanelTarget.None;
        if(x<Border&&y<Border)return PanelTarget.TopLeft;
        if(x>width-Border&&y<Border)return PanelTarget.TopRight;
        if(x<Border&&y>height-Border)return PanelTarget.BottomLeft;
        if(x>width-Border&&y>height-Border)return PanelTarget.BottomRight;
        if(y<Top-54)return PanelTarget.Move;
        if(y<Top)
        {
            int slot=(int)((x-Border)/((width-2f*Border)/7));
            return x<Border||x>=width-Border?PanelTarget.None:slot switch{0=>PanelTarget.PlayPause,1=>PanelTarget.Stop,2=>PanelTarget.Undo,3=>PanelTarget.Redo,4=>PanelTarget.ShowRobot,5=>PanelTarget.Recenter,6=>PanelTarget.Lock,_=>PanelTarget.None};
        }
        if(x>=Border&&x<width-Border&&y<height-Bottom)return PanelTarget.Content;
        return PanelTarget.None;
    }
    internal static PanelImage Draw(EditorDesktopFrame frame,PanelChromeState state)
    {
        int width=frame.Width+2*Border,height=frame.Height+Top+Bottom;var content=Content(frame.Width,frame.Height);
        using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(22,29,40));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var line=new Pen(Color.FromArgb(80,160,195),3);g.DrawRectangle(line,1,1,width-3,height-3);
            if(state.Hover==PanelTarget.Move&&!state.Locked)g.FillRectangle(Brushes.DarkSlateBlue,Border,3,width-2*Border,Top-58);
            var titleArea=new RectangleF(Border+10,12,width-2*Border-20,48);
            string caption=state.Locked?"Animation Editor · Layout locked":"▰  Animation Editor · Hold trigger here to move";
            using var titleFont=VrTextureText.FitFont(g,caption,titleArea.Size,28,FontStyle.Bold);g.DrawString(caption,titleFont,Brushes.White,titleArea);
            string[] labels=["▶ / Ⅱ","■ Stop","↶ Undo","↷ Redo","Show robot","Bring here",state.Locked?"🔒 Unlock":"Lock layout"];
            PanelTarget[] targets=[PanelTarget.PlayPause,PanelTarget.Stop,PanelTarget.Undo,PanelTarget.Redo,PanelTarget.ShowRobot,PanelTarget.Recenter,PanelTarget.Lock];
            float buttonWidth=(width-2f*Border)/7;
            for(int i=0;i<labels.Length;i++)
            {
                var rect=new RectangleF(Border+i*buttonWidth+3,Top-51,buttonWidth-6,44);
                using var brush=new SolidBrush(state.Hover==targets[i]?Color.FromArgb(62,104,151):Color.FromArgb(42,54,72));g.FillRectangle(brush,rect);
                using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};using var buttonFont=VrTextureText.FitFont(g,labels[i],rect.Size,19);g.DrawString(labels[i],buttonFont,Brushes.White,rect,format);
            }
            using var desktop=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb);
            var bits=desktop.LockBits(new(0,0,frame.Width,frame.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            var pixels=(byte[])frame.Rgba.Clone();for(int i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);
            try{for(int y=0;y<frame.Height;y++)Marshal.Copy(pixels,y*frame.Width*4,bits.Scan0+y*bits.Stride,frame.Width*4);}finally{desktop.UnlockBits(bits);}
            g.DrawImageUnscaled(desktop,content.Location);
            var helpArea=new RectangleF(Border,height-Bottom+3,width-2*Border,Bottom-6);using var helpFont=VrTextureText.FitFont(g,state.Message,helpArea.Size,19);
            g.DrawString(state.Message,helpFont,Brushes.LightCyan,helpArea);
            foreach(var (target,x,y) in new[]{(PanelTarget.TopLeft,0,0),(PanelTarget.TopRight,width-Border,0),(PanelTarget.BottomLeft,0,height-Border),(PanelTarget.BottomRight,width-Border,height-Border)})
                g.FillRectangle(state.Locked?Brushes.DimGray:state.Hover==target?Brushes.White:Brushes.Turquoise,x+3,y+3,Border-6,Border-6);
        }
        var rgba=new byte[width*height*4];var data=bitmap.LockBits(new(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<height;y++)Marshal.Copy(data.Scan0+y*data.Stride,rgba,y*width*4,width*4);}finally{bitmap.UnlockBits(data);}
        for(int i=0;i<rgba.Length;i+=4)(rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);
        return new(rgba,width,height,content);
    }
}
