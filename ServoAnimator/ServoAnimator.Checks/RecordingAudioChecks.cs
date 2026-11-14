using System.IO;
using System.Reflection;
using System.Windows;
using NAudio.Wave;
using ServoAnimator;

internal static partial class Program
{
    private static void RecordingAudioChecks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RecordingAudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var paths = new List<string>();
        try
        {
            var first = TakeAudioCapture.CreateTakeFile(folder, "Demo"); paths.Add(first.Path);
            using (var writer = new WaveFileWriter(first.Stream, new WaveFormat(44100, 16, 1))) writer.Write(new byte[88200], 0, 88200);
            var second = TakeAudioCapture.CreateTakeFile(folder, "Demo"); paths.Add(second.Path); second.Stream.Dispose();
            Check(Path.GetFileName(first.Path) == "Demo_Take1.wav" && Path.GetFileName(second.Path) == "Demo_Take2.wav", "Take numbering preserves earlier recordings");
            using (var reader = new WaveFileReader(first.Path))
            {
                Check(reader.WaveFormat.Channels == 1 && reader.WaveFormat.SampleRate == 44100 && reader.WaveFormat.BitsPerSample == 16, "Recorded WAV format");
                Check(Math.Abs(reader.TotalTime.TotalSeconds - 1) < .001, "WAV duration and header finalized");
            }
            string sequence = Path.Combine(folder, "Empty.json"); paths.Add(sequence);
            new AnimationDocument { Description = "Empty" }.Save(sequence);
            var doc = AnimationDocument.Load(sequence);
            Check(doc.Description == "Empty" && doc.Commands.Count == 0, "New sequence stores header without initial commands");

            var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
            var window = new MainWindow(); // Never Show: no camera, microphone or robot is started.
            void Set(string name, object value) => typeof(MainWindow).GetField(name, flags).SetValue(window, value);
            var command = new ServoCommand { Servo = ServoNames.Play, TextValue = first.Path, OffsetSeconds = 2 };
            doc.Commands.Add(command); Set("_doc", doc); Set("_recordingAudioPath", first.Path); Set("_recordingAudioCommand", command);
            typeof(MainWindow).GetMethod("DiscardRecordingAudio", flags).Invoke(window, null);
            Check(!File.Exists(first.Path) && !doc.Commands.Contains(command), "Discard removes provisional Play and only its WAV");
            Check(File.Exists(second.Path), "Discard preserves other takes");
            Check(!window.IsLoaded, "Checks do not launch the editor or hardware");
        }
        finally { foreach (var path in paths) if (File.Exists(path)) File.Delete(path); Directory.Delete(folder); }
    }
}
