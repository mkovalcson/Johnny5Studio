using System.Numerics;

namespace Johnny5VR;

internal sealed partial class Renderer
{
    internal void VerifyControllerPointerDrawing()
    {
        if(desktop==null)return;
        var previous=rays;
        try
        {
            rays=[(new(-.2f,1.35f,2.6f),new(-.4f,1.6f,0)),(new(.2f,1.35f,2.6f),new(.4f,1.6f,0))];
            var eye=new Vector3(0,1.6f,3);var view=Matrix4x4.CreateLookAt(eye,new(0,1.6f,0),Vector3.UnitY);
            var projection=Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,desktop.Width/(float)desktop.Height,.03f,150);
            Draw(desktop,view*projection,eye);string path=Path.Combine(Diagnostics.Folder,"controller-rays.png");Screenshot(path);
            using var bitmap=new Bitmap(path);int left=0,right=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {
                var c=bitmap.GetPixel(x,y);if(c.R<100&&c.G>170&&c.B>170){if(x<bitmap.Width/2)left++;else right++;}
            }
            if(left<10||right<10)throw new InvalidOperationException($"Both controller rays must render visibly: left {left}, right {right}.");
            File.WriteAllText(Path.Combine(Diagnostics.Folder,"controller-ray-render-check.txt"),$"PASS: two independent rays rendered without a menu/panel hit ({left} left / {right} right cyan pixels).");
        }
        finally{rays=previous;}
    }
}
