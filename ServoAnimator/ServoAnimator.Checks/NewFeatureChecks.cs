using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using NAudio.Wave;
using ServoAnimator;

internal static partial class Program
{
    private static void NewFeatureChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "Johnny5 features " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var audio = new ServoCommand { Servo = ServoNames.Play, OffsetSeconds = 10, TextValue = "take.wav", AudioTakeId = "take", AudioTrim = new() { CommandsLinked = true } };
            var commands = new[] { 0d, 1, 2, 3, 4, 5 }.Select(t => new ServoCommand { Servo = ServoNames.NeckTurn, OffsetSeconds = 10 + t, NumericValue = (int)t, AudioTakeId = "take" }).ToList();
            var other = new ServoCommand { Servo = ServoNames.NeckTurn, OffsetSeconds = 25, AudioTakeId = "other" };
            var doc = new AnimationDocument { Commands = new[] { audio, other }.Concat(commands).ToList() };
            var trim = new AudioTrimSettings { CommandsLinked = true, SkipSeconds = 2, SkipCommitted = true, EndSeconds = 4, EndCommitted = true, ScrubBefore = true, ScrubAfter = true };
            AudioTrimSettings.Apply(doc, audio, trim);
            Check(doc.Commands.Where(c => c.AudioTakeId == "take" && c.Servo != ServoNames.Play).Select(c => c.OffsetSeconds).SequenceEqual(new[] { 10d, 11, 12 }), "Trim shifts only retained commands by the skipped amount");
            Check(trim.HiddenCommands.Count == 3 && other.OffsetSeconds == 25 && doc.Commands.Contains(other), "Trim archives both excluded regions and preserves other takes");
            Check(CommandConflicts.Find(doc.Commands).Count == 0, "Hidden boundary commands do not conflict with visible commands");
            var snapshot = doc.Clone();
            string project = Path.Combine(root, "Trim.json"); doc.Save(project); doc = AnimationDocument.Load(project);
            audio = doc.Commands.Single(c => c.Servo == ServoNames.Play);
            Check(audio.AudioTrim.HiddenCommands.Count == 3 && audio.AudioTrim.SkipCommitted && audio.AudioTrim.EndCommitted, "Saved trim retains hidden commands and locks");
            Check(CommandConflicts.SameContent(snapshot.Commands[0], doc.Commands[0]), "Trim and archive round-trip exactly");
            doc.Commands.Single(c => c.AudioTakeId == "take" && c.NumericValue == 3).OffsetSeconds = 11.5;
            var expanded = audio.AudioTrim.Clone(); expanded.SkipSeconds = 1; expanded.EndSeconds = 5;
            AudioTrimSettings.Apply(doc, audio, expanded);
            Check(expanded.HiddenCommands.Count == 1 && doc.Commands.Single(c => c.AudioTakeId == "take" && c.NumericValue == 3).OffsetSeconds == 12.5, "Expanding trim restores commands and preserves a visible timing edit");
            var restore = expanded.Clone(); restore.ScrubBefore = restore.ScrubAfter = false;
            AudioTrimSettings.Apply(doc, audio, restore);
            Check(restore.HiddenCommands.Count == 0 && doc.Commands.Count == 8 && doc.Commands.Single(c => c.AudioTakeId == "take" && c.Servo != ServoNames.Play && c.NumericValue == 0).OffsetSeconds == 10, "Disabling scrubs restores original command times");
            Check(restore.Start == 1 && restore.End(10) == 5, "Audio-only trim remains active without command scrubbing");
            var copies = CommandGroupOperations.CopyAt(snapshot.Commands, 50);
            Check(copies.First(c => c.Servo == ServoNames.Play).AudioTakeId != "take", "Copied audio receives an independent take ID");
            var copiedAudio = copies.First(c => c.Servo == ServoNames.Play);
            Check(copiedAudio.AudioTrim.HiddenCommands.All(c => c.AudioTakeId == copiedAudio.AudioTakeId), "Copied archive belongs to copied take");
            Check(snapshot.Commands[0].AudioTrim.HiddenCommands.Count == 3, "Editing trim does not mutate Undo snapshot");
            var legacy = new ServoCommand { Servo = ServoNames.Play, OffsetSeconds = 2 };
            var legacyDoc = new AnimationDocument { Commands = new() { legacy, new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 3 }, new() { Servo = ServoNames.Play, OffsetSeconds = 6 }, new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 7 } } };
            AudioTrimSettings.Apply(legacyDoc, legacy, new() { SkipCommitted = true, SkipSeconds = 2, ScrubBefore = true });
            Check(legacy.AudioTrim.HiddenCommands.Count == 1 && legacyDoc.Commands.Any(c => c.OffsetSeconds == 7 && c.AudioTakeId == ""), "Legacy linking stops at the next audio handle");

            string wav = Path.Combine(root, "voice take.wav");
            using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 1))) writer.Write(new byte[88200], 0, 88200);
            using (var reader = new AudioFileReader(wav))
            {
                var limited = new LimitedAudioSamples(reader, .125); var buffer = new float[4000]; int total = 0, count;
                while ((count = limited.Read(buffer, 0, buffer.Length)) > 0) total += count;
                Check(total == (int)Math.Round(.125 * 44100), "Audio thread ends at the requested sample independently of UI refresh");
            }
            string script = Path.Combine(root, "mock converter.bat");
            File.WriteAllText(script, "@echo off\r\nif not \"%J5_NOPAUSE%\"==\"1\" exit /b 2\r\ncopy /y \"%~1\" \"%~dpn1_j5.wav\" >nul\r\nexit /b 0\r\n");
            string converted = RecordedVoiceConverter.ConvertAsync(wav, script).GetAwaiter().GetResult();
            Check(File.Exists(converted) && Path.GetFileName(converted) == "voice take_j5.wav", "Background converter handles spaces and no-pause environment");
            void Fails(string expected)
            {
                try { RecordedVoiceConverter.ConvertAsync(wav, script).GetAwaiter().GetResult(); throw new Exception("Expected conversion failure"); }
                catch (IOException ex) { Check(ex.Message.Contains(expected), "Converter reports " + expected); }
            }
            Fails("already exists"); File.Delete(converted);
            File.WriteAllText(script, "@exit /b 7\r\n"); Fails("code 7");
            File.WriteAllText(script, "@exit /b 0\r\n"); Fails("did not create");
            File.WriteAllText(script, "@echo bad>\"%~dpn1_j5.wav\"\r\n@exit /b 0\r\n");
            try { RecordedVoiceConverter.ConvertAsync(wav, script).GetAwaiter().GetResult(); throw new Exception("Accepted corrupt WAV"); }
            catch (FormatException) { Check(true, "Corrupt converter output is rejected"); }
            Check(File.Exists(wav), "Conversion failures preserve the original recording");
            var setting = JsonSerializer.Deserialize<RecordingWindowSettings>(JsonSerializer.Serialize(new RecordingWindowSettings { RecordAudio = true, AutomaticallyConvertVoice = true }));
            Check(setting.AutomaticallyConvertVoice && setting.RecordAudio, "Voice conversion preference persists");

            string model = ModelLocations.DefaultRobot(FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory);
            var xml = XDocument.Load(model);
            var joints = xml.Root.Elements("joint").Where(j => (string)j.Attribute("type") == "revolute" && j.Element("mimic") == null).ToArray();
            var independent = BodyControlCatalog.Controls.Where(c => !BodyControlCatalog.IsGang(c.Servo)).ToArray();
            Check(independent.Length >= 36 && independent.Select(c => c.Joint).Distinct().Count() == independent.Length &&
                independent.All(c => joints.Count(j => (string)j.Attribute("name") == c.Joint) == 1),
                "Current body controls have distinct independent joints in the Config robot");
            foreach (var body in BodyControlCatalog.Controls.Where(c => !BodyControlCatalog.IsGang(c.Servo)))
            {
                var joint = joints.Single(j => (string)j.Attribute("name") == body.Joint);
                // URDF exporters round some joint limits to six decimal places.
                Check(Math.Abs((double)joint.Element("limit").Attribute("lower") - body.MinRadians) < 1e-6 && Math.Abs((double)joint.Element("limit").Attribute("upper") - body.MaxRadians) < 1e-6, "Catalog matches URDF limits: " + body.Joint);
                var motion = BodyMotionSetting.Default(body);
                Check(Math.Abs(motion.Map(-100, body) - body.MinRadians) < 1e-9 && Math.Abs(motion.Map(100, body) - body.MaxRadians) < 1e-9 && motion.Map(0, body) == 0, "Body travel maps zero and both limits: " + body.Joint);
                motion.Reverse = true; Check(Math.Abs(motion.Map(-100, body) - body.MaxRadians) < 1e-9, "Body reversal: " + body.Joint);
                Check(ServoCommand.IsUrdfOnly(body.Servo) && CommandVM.ServoPickOptions.Any(p => p.Servo == body.Servo), "Body is URDF-only and available to command authoring: " + body.Joint);
            }
            var scene = UrdfScene.Load(model); scene.SetLimitedJoint("NeckLift", .2);
            var positions = scene.CaptureMotionState().JointPositions;
            foreach (string name in new[] { "NeckLiftLevel", "NeckLiftFrontRod" })
            {
                var follower = xml.Root.Elements("joint").Single(j => (string)j.Attribute("name") == name);
                if (name == "NeckLiftFrontRod" && (string)xml.Root.Element("johnny5_neck_linkage")?.Attribute("version") == "1")
                {
                    // Current models solve this telescoping linkage geometrically; the mimic ratio is a fallback.
                    double lo = (double)follower.Element("limit").Attribute("lower"), hi = (double)follower.Element("limit").Attribute("upper");
                    Check(double.IsFinite(positions[name]) && Math.Abs(positions[name]) > 1e-6 && positions[name] >= lo && positions[name] <= hi,
                        "Current neck linkage articulates its front rod within the model range");
                }
                else
                {
                    var mimic = follower.Element("mimic");
                    double multiplier = (double)mimic.Attribute("multiplier"), offset = (double?)mimic.Attribute("offset") ?? 0;
                    Check(Math.Abs(positions[name] - (.2 * multiplier + offset)) < 1e-9, "Neck mimic follower uses current URDF metadata: " + name);
                }
            }
            scene.SetLimitedJoint("NeckLift", 99);
            double maximum = (double)joints.Single(j => (string)j.Attribute("name") == "NeckLift").Element("limit").Attribute("upper");
            Check(Math.Abs(scene.CaptureMotionState().JointPositions["NeckLift"] - maximum) < 1e-9, "Body joint motion clamps at the current URDF limit");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
