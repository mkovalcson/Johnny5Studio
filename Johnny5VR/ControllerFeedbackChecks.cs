using System.Text.Json;
using NAudio.Wave;
namespace Johnny5VR;
internal static class ControllerFeedbackChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var defaults=JsonSerializer.Deserialize<ViewerSettings>("{}")!;
        check(defaults.ControllerConfirmation==FeedbackConfirmation.Sounds&&Math.Abs(defaults.ControllerFeedbackVolume-.4f)<.001f,"Old viewer settings default to Sounds at 40%.");
        defaults.ControllerConfirmation=FeedbackConfirmation.Voice;defaults.ControllerFeedbackVolume=.65f;
        var saved=JsonSerializer.Deserialize<ViewerSettings>(JsonSerializer.Serialize(defaults))!;
        check(saved.ControllerConfirmation==FeedbackConfirmation.Voice&&Math.Abs(saved.ControllerFeedbackVolume-.65f)<.001f,"Confirmation choice and independent volume survive settings serialization.");
        var cues=new List<byte[]>();
        foreach(var toggle in Enum.GetValues<FollowToggle>())
        {
            var pcm=ControllerFeedback.Sound(toggle);cues.Add(pcm);
            check(pcm.Length>=24000*.15*2&&pcm.Length<=24000*.3*2,"Confirmation is a short 150–300 ms cue: "+toggle);
            check(pcm.Any(v=>v!=0),"Confirmation has audible sample data: "+toggle);
            using var file=new WaveFileWriter(Path.Combine(Diagnostics.Folder,"feedback-"+toggle+".wav"),new WaveFormat(24000,16,1));file.Write(pcm,0,pcm.Length);
        }
        check(cues.Select(Convert.ToBase64String).Distinct().Count()==4,"All four setting sounds are distinct.");
        check(VrMenu.SliderAt(0)==0&&VrMenu.SliderAt(1)==1&&Math.Abs(VrMenu.SliderAt(800f/1100)-.5f)<.001f,"VR slider maps its visible endpoints and midpoint and clamps out-of-range input.");
    }
}
internal sealed partial class ViewerWindow
{
    internal static void CheckFeedbackUI()
    {
        Directory.CreateDirectory(Diagnostics.Folder);
        using var window=new ViewerWindow(new ViewerSettings(),layoutCheckOnly:true);window.Show();window.OpenControllerFeedback();Application.DoEvents();
        if(window.feedbackChoice?.Items.Count!=3||window.feedbackLevel?.Value!=40)throw new InvalidOperationException("Feedback defaults are missing in desktop UI.");
        using(var bitmap=new Bitmap(window.feedbackWindow!.Width,window.feedbackWindow.Height)){window.feedbackWindow.DrawToBitmap(bitmap,new(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(Diagnostics.Folder,"controller-feedback-desktop.png"));}
        string[] rows=["● Sounds","○ Voice","○ Off","Volume: 40%","Preview: Mirrored","Preview: Unmirrored","Preview: Move me","Preview: Move robot","Back"];
        using(var bitmap=VrMenu.Draw("Controller feedback","Changes save immediately. Point at the volume bar and hold Trigger, or use right stick left/right. Off silences previews.",rows,3,0,[null,null,null,.4f,null,null,null,null,null]))bitmap.Save(Path.Combine(Diagnostics.Folder,"controller-feedback-vr.png"));
        window.OpenFollowSettings();Application.DoEvents();
        if(window.followBackdropChoice?.Checked!=true||window.followStatusChoice?.Checked!=false)throw new InvalidOperationException("Follow UI defaults do not match preferences.");
        using(var bitmap=new Bitmap(window.followSettingsWindow!.Width,window.followSettingsWindow.Height)){window.followSettingsWindow.DrawToBitmap(bitmap,new(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(Diagnostics.Folder,"follow-settings-desktop.png"));}
        using(var bitmap=VrMenu.Draw("Follow settings","Changes apply and save immediately. Calibration instructions, glove LCDs and recording indicators remain available.",["Backdrop: ● Use editor's loaded URDF backdrop","○ No backdrop","Overhead Follow status: Hidden","Controller feedback · Sounds / Voice / Off"],0,0))bitmap.Save(Path.Combine(Diagnostics.Folder,"follow-settings-vr.png"));
        window.followSettingsWindow.Close();window.feedbackWindow.Close();window.Close();File.WriteAllText(Path.Combine(Diagnostics.Folder,"controller-feedback-ui-check.txt"),"PASS: desktop options, 40% slider, four previews, and VR slider layout rendered without changing saved preferences.");
    }
}
