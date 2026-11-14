using System.IO;
using System.Windows;
namespace ServoAnimator;
public partial class MainWindow
{
    /// <summary>Audio picked in a file browser (Open Audio…, the Start a
    /// sequence panel's Insert Audio…, and Insert audio at the cursor).
    /// A file outside Configuration is COPIED (never moved) into
    /// Config/Library/Audio and the copy's path is returned; a file already
    /// inside Configuration is returned unchanged. Returns null after showing
    /// an error when the copy fails.</summary>
    private string ImportPickedAudio(string picked)
    {
        try
        {
            string path = LibraryAudioImport.ImportIntoConfig(ConfigRoot, picked, out bool copied);
            if (copied) ShowStatus($"Copied {Path.GetFileName(picked)} into Library\\Audio as {Path.GetFileName(path)}");
            return path;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not copy the audio file into Library\\Audio:\n" + ex.Message,
                            "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
}
