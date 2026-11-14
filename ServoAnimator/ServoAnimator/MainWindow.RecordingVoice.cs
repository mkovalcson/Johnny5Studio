using System.IO;

namespace ServoAnimator;

public partial class MainWindow
{
    private async Task ConvertKeptRecordingVoiceAsync(AnimationDocument document, ServoCommand command, string audioPath, string configRoot)
    {
        string originalPath = command.TextValue;
        ShowStatus($"Converting recorded voice: {Path.GetFileName(audioPath)}…");
        try
        {
            string converted = await RecordedVoiceConverter.ConvertAsync(audioPath);
            if (Dispatcher.HasShutdownStarted || !IsLoaded) return;
            // Conversion may outlive a document switch, an Undo, or edits to this Play command.
            // Never redirect a different command or silently rewrite a document on disk.
            if (!ReferenceEquals(_doc, document) || !document.Commands.Contains(command) ||
                command.Servo != ServoNames.Play || command.TextValue != originalPath ||
                !string.Equals(ConfigRoot, configRoot, StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus($"Converted File {Path.GetFileName(converted)} is ready; the original Play command changed or its sequence was closed, so the timeline was not updated.");
                return;
            }
            string relative = ConfigPathService.ToRelative(configRoot, converted);
            PushUndo("Use converted recorded voice");
            command.TextValue = relative;
            command.Reason = Path.GetFileName(converted);
            if (RefreshAfterEdit()) ShowStatus($"Converted File {Path.GetFileName(converted)} is ready to play.");
        }
        catch (Exception ex)
        {
            if (!Dispatcher.HasShutdownStarted && IsLoaded)
                ShowStatus($"Voice conversion failed for {Path.GetFileName(audioPath)}: {ex.Message} The original recording was kept.");
        }
    }
}
