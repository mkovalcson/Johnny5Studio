namespace Johnny5VR;
internal static class VrTextureText
{
    internal static Font FitFont(Graphics graphics,string text,SizeF area,float maximum,FontStyle style=FontStyle.Regular)
    {
        for(float size=maximum;size>=8;size--)
        {
            var font=new Font("Segoe UI",size,style,GraphicsUnit.Pixel);
            var measured=graphics.MeasureString(text,font,new SizeF(area.Width,100000));
            if(measured.Height<=area.Height&&measured.Width<=area.Width+1)return font;
            font.Dispose();
        }
        return new Font("Segoe UI",8,style,GraphicsUnit.Pixel);
    }
}
