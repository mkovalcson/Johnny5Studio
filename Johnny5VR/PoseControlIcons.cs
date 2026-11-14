using ServoAnimator;
namespace Johnny5VR;

internal static class PoseControlIcons
{
    private static readonly Dictionary<string,Bitmap?> cache=new();
    internal static Bitmap? For(string key)
    {
        lock(cache)return Get(key);
    }
    private static Bitmap? Get(string key)
    {
        if(cache.TryGetValue(key,out var result))return result;
        Bitmap? Load(ServoNames servo)
        {
            string? file=ControlIconFiles.For(servo);if(file==null)return null;
            using var stream=typeof(PoseControlIcons).Assembly.GetManifestResourceStream("J5Studio.ControlIcons."+file);
            if(stream==null)return null;using var source=new Bitmap(stream);return new Bitmap(source);
        }
        if(PairedBodyControls.Find(key) is {} pair)
        {
            result=new Bitmap(96,64);using var g=Graphics.FromImage(result);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var left=Load(pair.Left);using var right=Load(pair.Right);
            if(left!=null)g.DrawImage(left,new Rectangle(0,0,44,44));if(right!=null)g.DrawImage(right,new Rectangle(52,0,44,44));
            using var link=new Pen(Color.MediumPurple,4);g.DrawEllipse(link,29,46,22,12);g.DrawEllipse(link,45,46,22,12);g.DrawLine(link,40,52,56,52);
        }
        else
        {
            var fields=key.Split(':');string name=fields.Length>1?fields[1]:"";
            result=Enum.TryParse<ServoNames>(name,out var servo)?Load(servo):null;
        }
        cache[key]=result;return result;
    }
}
