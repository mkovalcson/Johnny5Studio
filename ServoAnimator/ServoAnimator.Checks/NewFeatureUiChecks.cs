using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServoAnimator;

internal static partial class Program
{
    private static void NewFeatureUiChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var window = new MainWindow();
        var doc = new AnimationDocument();
        var audio = new ServoCommand { Servo = ServoNames.Play, TextValue = "missing-test.wav", OffsetSeconds = 10 };
        doc.Commands.Add(audio); doc.Commands.Add(new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 11 });
        doc.Commands.Add(new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 14 });
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, doc);
        typeof(MainWindow).GetField("_cursorTime", flags).SetValue(window, 2d);
        var clip = new AudioClipVisual { Command = audio, Name = "Test take", SourceDuration = 8, Duration = 8 };
        var dialog = window.CreateAudioTrimDialog(clip);
        var panel = (StackPanel)dialog.Content;
        var rows = panel.Children.OfType<StackPanel>().ToArray();
        TextBox Value(int row) => rows[row].Children.OfType<TextBox>().Single();
        Button Commit(int row) => rows[row].Children.OfType<Button>().Single();
        Check(Value(1).Text == "2.000" && Value(1).IsEnabled, "Skip defaults to current cursor and starts unlocked");
        rows[1].Children.OfType<CheckBox>().Single().IsChecked = true;
        Commit(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!Value(1).IsEnabled && audio.AudioTrim.SkipCommitted && audio.AudioTrim.HiddenCommands.Count == 1, "Skip Commit hides preceding command and locks row");
        Check(doc.Commands.Single(c => c.Servo == ServoNames.NeckTurn).OffsetSeconds == 12, "UI commit shifts remaining command left");
        Commit(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Value(1).Text = "0";
        Check(audio.AudioTrim.Start == 2 && Value(1).IsEnabled, "Unlock and edit do not affect committed trim yet");
        Commit(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(audio.AudioTrim.HiddenCommands.Count == 0 && doc.Commands.Count == 3, "Recommit restores hidden command");
        Value(2).Text = "3"; rows[2].Children.OfType<CheckBox>().Single().IsChecked = true;
        Commit(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!Value(2).IsEnabled && audio.AudioTrim.HiddenCommands.Count == 1, "End Commit archives later commands and locks independently");
        var reopened = window.CreateAudioTrimDialog(clip);
        var reopenRows = ((StackPanel)reopened.Content).Children.OfType<StackPanel>().ToArray();
        Check(!reopenRows[1].Children.OfType<TextBox>().Single().IsEnabled && !reopenRows[2].Children.OfType<TextBox>().Single().IsEnabled, "Reopened dialog honors committed locks");
        var waveform = (WaveformView)window.FindName("Waveform"); waveform.AudioClips = new() { clip };
        typeof(MainWindow).GetField("_cursorTime", flags).SetValue(window, 12.5);
        typeof(MainWindow).GetMethod("UpdateTimeText", flags).Invoke(window, null);
        var relative = (TextBlock)window.FindName("AudioRelativeTimeText");
        Check(relative.Text == "Audio +2.500 s" && ((SolidColorBrush)relative.Foreground).Color == Color.FromRgb(235, 170, 70), "Audio cursor text matches elapsed time and handle color");
        string previews = PreviewDirectory("audio-trim-previews"); Directory.CreateDirectory(previews);
        RenderControl((FrameworkElement)dialog.Content, Path.Combine(previews, "AudioTiming.png"), 720, 330);
        string temporary = Path.Combine(Path.GetTempPath(), "Johnny5 UI " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var recorder = new ControllerRecordingWindow(0, temporary);
            var recordAudio = (CheckBox)typeof(ControllerRecordingWindow).GetField("_recordAudio", flags).GetValue(recorder);
            var convert = (CheckBox)typeof(ControllerRecordingWindow).GetField("_convertVoice", flags).GetValue(recorder);
            Check(!convert.IsEnabled, "Voice conversion unavailable until audio recording is selected");
            recordAudio.IsChecked = convert.IsChecked = true;
            Check(convert.IsEnabled, "Record Audio enables conversion option");
            typeof(ControllerRecordingWindow).GetMethod("SaveRecordingSettings", flags).Invoke(recorder, null);
            var restored = new ControllerRecordingWindow(0, temporary);
            Check(restored.RecordAudio && restored.AutomaticallyConvertVoice, "Conversion choice is restored in recording window");
            var configuration = new UrdfConfigWindow(UrdfConfiguration.CreateDefault(), ServoConfiguration.CreateDefault(), (_, _) => { }, (_, _, _) => { }, () => { }, temporary);
            var body = (StackPanel)configuration.FindName("BodyControlsPanel");
            var cards = body.Children.OfType<Grid>().Single().Children.OfType<Border>().ToArray();
            Check(cards.Length == 30, "Body configuration renders every independent joint");
            var catalog = BodyControlCatalog.PairedPicklistControls.ToArray();
            for (int i = 0; i < cards.Length; i++)
            {
                var content = (StackPanel)cards[i].Child;
                var dial = content.Children.OfType<AngularRangeDial>().Single();
                var boxes = content.Children.OfType<WrapPanel>().Single().Children.OfType<TextBox>().ToArray();
                dial.MinimumExtent -= 1;
                Check(Math.Abs(double.Parse(boxes[0].Text) - dial.MinimumExtent) < .000001, "Body dial updates numeric range");
                boxes[1].Text = (dial.MaximumExtent + 1).ToString();
                boxes[1].RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                Check(Math.Abs(double.Parse(boxes[1].Text) - dial.MaximumExtent) < .000001, "Numeric range updates body dial");
                if (catalog[i].Joint.EndsWith("Left"))
                    Check(Grid.GetRow(cards[i]) == Grid.GetRow(cards[i + 1]) && Grid.GetColumn(cards[i]) == 0 && Grid.GetColumn(cards[i + 1]) == 1, "Left and Right cards share a row");
            }
            foreach (var kind in Enum.GetValues<ControllerKind>())
            {
                var mapping = new ControllerMappingWindow(ControllerProfile.Defaults(kind), () => new ControllerSample(), _ => { });
                var targets = (ComboBox)typeof(ControllerMappingWindow).GetField("_target", flags).GetValue(mapping);
                Check(targets.Items.Cast<object>().TakeLast(29).All(item => item.ToString().StartsWith("B_")), kind + " appends body targets after actions");
            }
            Check(!window.IsLoaded && !recorder.IsLoaded && !configuration.IsLoaded, "UI checks never load hardware-owning windows");
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}


