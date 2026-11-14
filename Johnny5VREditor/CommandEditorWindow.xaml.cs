// ---------------------------------------------------------------------------
// CommandEditorWindow.xaml.cs
//
// Modal dialog that edits every ServoCommand at one timeline point.
//
// Design:
//   * Each command is wrapped in a CommandVM whose property setters write
//     through to a draft ServoCommand. Applying merges these drafts with the
//     current document and resolves duplicate targets before committing them.
//   * Value editing is per-servo:
//       - numeric servos: slider (range from ServoCommand.RangeFor:
//         0..2000 for LeftEyePop/RightEyePop, -100..100 otherwise) plus a
//         numeric TextBox. Every user change calls the numeric
//         MoveServoNow(Speed, Servo, int) so hardware can be jogged live.
//       - RGBCommand: a free-text box; committing text calls the text
//         MoveServoNow(Speed, Servo, string) overload.
//   * Changing the Servo combo re-ranges the slider (Min/Max change) and
//     switches between the slider and the text box automatically, clamping
//     the numeric value into the new servo's range.
//   * "Delete" removes a command from the document; if the last command at
//     this point is deleted, MainWindow's marker refresh removes the '+'.
//   * "Add Command" appends a fresh command at the same time point.
//   * Editing the Offset field moves a command to a different time point
//     (a new '+' will appear there after the dialog closes).
// ---------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ServoAnimator
{
    public partial class CommandEditorWindow : Window
    {
        private readonly AnimationDocument _doc;
        private readonly AnimationDocument _sourceDoc;
        private readonly CommandEditSession _session;
        private readonly Func<List<ServoCommand>, IReadOnlyDictionary<ServoNames, bool>, bool> _commitCommands;
        private readonly CommandSplineDraft _splineDraft;
        private bool _discardChanges;
        private readonly double _timeKey;    // the timeline point being edited
        private readonly Action<ServoSpeed, ServoNames, int> _moveServoNow;
        private readonly Action<ServoSpeed, ServoNames, string> _moveServoNowText;
        private readonly Action<ServoSpeed, ServoNames, RobotControls, int> _moveChildNow;
        private readonly Action<ServoSpeed, ServoNames> _configureGangSpeedNow;
        private readonly Action<ServoSpeed, ServoNames, RobotControls> _configureChildSpeedNow;
        private readonly ObservableCollection<CommandVM> _items = new();
        private readonly string _libraryCommandsFolder;

        public CommandEditorWindow(AnimationDocument doc, double time,
                                   Action<ServoSpeed, ServoNames, int> moveServoNow,
                                   Action<ServoSpeed, ServoNames, string> moveServoNowText,
                                   Action<ServoSpeed, ServoNames, RobotControls, int> moveChildNow,
                                   Action<ServoSpeed, ServoNames> configureGangSpeedNow,
                                   Action<ServoSpeed, ServoNames, RobotControls> configureChildSpeedNow,
                                   string libraryCommandsFolder,
                                   IEnumerable<ServoNames> splineServos,
                                   Func<List<ServoCommand>, IReadOnlyDictionary<ServoNames, bool>, bool> commitCommands,
                                   ServoCommand focusCommand = null)
        {
            InitializeComponent();
            HelpSystem.EnableContextHelp(this, "commands");
            HelpSystem.SetTopic(CmdList, "commands");
            _sourceDoc = doc;
            _session = new CommandEditSession(doc.Commands, time);
            _doc = new AnimationDocument { Commands = _session.Commands };
            _commitCommands = commitCommands;
            _splineDraft = new CommandSplineDraft(splineServos);
            // A new command is configured as a draft before comparing its final
            // target and value with existing timeline commands.
            if (focusCommand != null && !doc.Commands.Contains(focusCommand))
                _doc.Commands.Add(focusCommand.Clone());
            _timeKey = ServoCommand.TimeKey(time);
            _moveServoNow = moveServoNow;
            _moveServoNowText = moveServoNowText;
            _moveChildNow = moveChildNow;
            _configureGangSpeedNow = configureGangSpeedNow;
            _configureChildSpeedNow = configureChildSpeedNow;
            _libraryCommandsFolder = libraryCommandsFolder ?? "";

            Title = $"Edit Commands @ {_timeKey:F3} s";

            // Wrap every command currently at this time point.
            foreach (var c in _doc.Commands)
                _items.Add(new CommandVM(c, _moveServoNow, _moveServoNowText, _moveChildNow, _configureGangSpeedNow, _configureChildSpeedNow, _splineDraft));

            CmdList.ItemsSource = _items;
            AllCommandsSpeed.ItemsSource = CommandVM.SpeedChoices;
            AllCommandsSpeed.SelectedItem = "N/C";
            Closing += (_, e) =>
            {
                if (_discardChanges) return;
                // Commit any focused text field even when closing via the X.
                if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
                    box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                try
                {
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.LoadBackdrop))
                        BackdropCommand.Read(command.TextValue).Validate(FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.FallApart))
                        FallApartCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.TvFront)) TvFrontCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.TvShow)) TvShowCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.TvReturn)) TvReturnCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.UrdfBeam)) BeamCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.LcdShow)) LcdShowCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.HideLaser)) HideLaserCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.ToolboxSwap)) ToolboxSwapCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.FireLaser)) FireLaserCommand.Read(command.TextValue);
                    foreach (var command in _doc.Commands.Where(c => c.Servo == ServoNames.VideoClip)) VideoClipCommand.Read(command.TextValue);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load Scene"); e.Cancel = true; return; }
                e.Cancel = !_commitCommands(_session.Merge(_sourceDoc.Commands), _splineDraft.Changes);
            };
        }

        private void SetAllSpeeds_Click(object sender, RoutedEventArgs e)
        {
            if (AllCommandsSpeed.SelectedItem is not string speed) return;
            // Use the same draft editing and speed-only preview path as each row.
            foreach (var item in _items.Where(item => item.SupportsSpeed).ToArray())
                item.SpeedText = speed;
        }

        /// <summary>Append a new command at the same time point and show it.</summary>
        private void AddCommand_Click(object sender, RoutedEventArgs e)
        {
            var previous = _items.LastOrDefault()?.Command;
            var cmd = new ServoCommand
            {
                OffsetSeconds = _timeKey,
                Servo = previous?.Servo ?? CommandVM.ServoPickOptions[0].Servo,
                Control = previous?.Control,
                NumericValue = 0,
                Speed = ServoSpeed.NoChange,
            };
            _doc.Commands.Add(cmd);
            _items.Add(new CommandVM(cmd, _moveServoNow, _moveServoNowText, _moveChildNow, _configureGangSpeedNow, _configureChildSpeedNow, _splineDraft));
        }

        /// <summary>Remove one command from the document and from the list.</summary>
        private void DeleteCommand_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CommandVM vm) return;
            _doc.Commands.Remove(vm.Command);
            _items.Remove(vm);
        }


        /// <summary>Save every command currently shown in this Edit Commands
        /// window as one single-time-point Library Pose. All saved offsets
        /// are normalized to zero so insertion always places the entire group
        /// at the selected timeline point.</summary>
        private void CreateLibraryCommand_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "There are no commands in this window to save.",
                                "Create Library Pose", MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            try { Directory.CreateDirectory(_libraryCommandsFolder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the Library\\Commands folder:\n" + ex.Message,
                                "Create Library Pose", MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return;
            }

            var prompt = new LibraryCommandSaveWindow { Owner = this };
            if (prompt.ShowDialog() != true) return;

            string path = Path.Combine(_libraryCommandsFolder, prompt.FileNameText);
            if (File.Exists(path))
            {
                var overwrite = MessageBox.Show(this,
                    $"'{prompt.FileNameText}' already exists. Replace it?",
                    "Create Library Pose", MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes) return;
            }

            var commands = _items.Select(vm =>
            {
                var copy = vm.Command.Clone();
                copy.OffsetSeconds = 0.0;
                return copy;
            }).ToList();

            if (!CommandConflictWindow.Resolve(this, commands)) return;

            try
            {
                LoadingWindow.Run(this, () => Task.Run(() =>
                {
                    string imageFile = null;
                    string oldImagePath = null;
                    if (File.Exists(path))
                    {
                        try
                        {
                            var old = AnimationDocument.LoadLibraryItem(path);
                            if (!string.IsNullOrWhiteSpace(old.ImageFile))
                            {
                                imageFile = old.ImageFile;
                                oldImagePath = LibraryItemInfo.ResolveImagePath(path, old.ImageFile);
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(prompt.ImageSourcePath))
                    {
                        string ext = Path.GetExtension(prompt.ImageSourcePath);
                        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                        imageFile = Path.GetFileNameWithoutExtension(path) + "_image" + ext.ToLowerInvariant();
                        string destination = Path.Combine(Path.GetDirectoryName(path) ?? _libraryCommandsFolder, imageFile);
                        if (!Path.GetFullPath(prompt.ImageSourcePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                            File.Copy(prompt.ImageSourcePath, destination, overwrite: true);

                        if (!string.IsNullOrWhiteSpace(oldImagePath) &&
                            string.Equals(Path.GetDirectoryName(Path.GetFullPath(oldImagePath)), Path.GetDirectoryName(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase) &&
                            !Path.GetFullPath(oldImagePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(oldImagePath))
                        {
                            try { File.Delete(oldImagePath); } catch { }
                        }
                    }

                    AnimationDocument.SaveLibraryCommand(path, commands, prompt.DescriptionText, imageFile);
                }), "Saving Library Pose and image…");
                MessageBox.Show(this,
                    $"Library Pose saved:\n{ConfigPathService.Display(path)}",
                    "Create Library Pose", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the Library Pose:\n" + ex.Message,
                                "Create Library Pose", MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _discardChanges = true;
            Close();
        }

        /// <summary>Open the RGB command builder for this row: pick one of
        /// the RGBLight.cs commands and fill its arguments; OK writes the
        /// exact formatted text into the command's value.</summary>
        private void BuildRgb_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CommandVM vm) return;

            if (vm.Command.Servo == ServoNames.LoadBackdrop)
            {
                try
                {
                    string root = FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
                    string selected = "";
                    try { selected = BackdropCommand.Read(vm.TextValue).SceneFile; } catch { /* The picker can repair an invalid old value. */ }
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Load Scene — select a saved scene",
                        Filter = "Saved scene (*.json)|*.json",
                        InitialDirectory = System.IO.Directory.Exists(BackgroundLibrary.Folder(root, scenes: true))
                            ? BackgroundLibrary.Folder(root, scenes: true) : root,
                        FileName = string.IsNullOrWhiteSpace(selected) ? "" : System.IO.Path.IsPathRooted(selected) ? selected : System.IO.Path.Combine(root, selected)
                    };
                    if (dialog.ShowDialog(this) == true)
                    {
                        var scene = SavedSceneCommand.Read(root, dialog.FileName);
                        vm.TextValue = new BackdropCommand { SceneFile = scene.FileName }.ToJson();
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load Scene"); }
                return;
            }

            if (vm.Command.Servo == ServoNames.FallApart)
            {
                FallApartCommand current;
                try { current = FallApartCommand.Read(vm.TextValue); } catch { current = new FallApartCommand(); }
                var form = new CommandSettingsWindow("Disassemble", null, blackTheme: true) { Owner = this };
                var direction = form.AddChoice("Direction", new[] { "Disassemble (play forwards)", "Reassemble (play in reverse)" }, current.Reverse ? 1 : 0,
                    "Forwards collapses and stays in pieces. Reverse starts in pieces, flies back together, then servo control resumes.");
                var speed = form.AddNumber("Speed", current.Speed, .1, 10,
                    "Playback-rate multiplier: 1 plays the clip as recorded (about 3.5 s), 0.5 takes twice as long, 2 half as long");
                form.Apply = () =>
                {
                    var settings = new FallApartCommand { Reverse = direction() == 1, Speed = speed() };
                    settings.Validate(); return settings.ToJson();
                };
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }

            if (vm.Command.Servo == ServoNames.TvShow)
            {
                try
                {
                    var settings = TvShowCommand.Read(vm.TextValue);
                    var extensions = string.Join(";", TvShowCommand.ImageExtensions.Concat(TvShowCommand.VideoExtensions).Select(e => "*" + e));
                    var dialog = new Microsoft.Win32.OpenFileDialog { Title = "TV Show image or video", Filter = "Images and videos|" + extensions + "|All files|*.*" };
                    if (dialog.ShowDialog(this) != true) return;
                    settings.Path = TvShowCommand.Import(FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory, dialog.FileName);
                    vm.TextValue = settings.ToJson();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "TV Show"); }
                return;
            }
            if (vm.Command.Servo == ServoNames.HideLaser)
            {
                HideLaserCommand current;
                try { current = HideLaserCommand.Read(vm.TextValue); } catch { current = new HideLaserCommand(); }
                var form = new CommandSettingsWindow("Hide Laser",
                    "Hides or shows the optional shoulder laser from this time on. Without any Hide Laser command the laser is shown. A hidden laser is not drawn at all.") { Owner = this };
                var hide = form.AddChoice("Hide Laser", new[] { "true (hidden)", "false (shown)" }, current.Hide ? 0 : 1, "true hides every laser part; false shows them");
                form.Apply = () => new HideLaserCommand { Hide = hide() == 0 }.ToJson();
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.FireLaser)
            {
                FireLaserCommand current;
                try { current = FireLaserCommand.Read(vm.TextValue); } catch { current = new FireLaserCommand(); }
                string root = FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
                var form = new CommandSettingsWindow("Fire Laser",
                    "A burst of glowing bolts from the shoulder laser's firing window, flying straight out along the laser into the URDF scene. It fires only while the laser is showing: Hide Laser false and Toolbox Swap false. Aim it with B_LaserLower, B_WaistSwivel and the chest controls.", blackTheme: true) { Owner = this };
                var shots = form.AddNumber("Shots", current.Shots, 1, 20, "Bolts in the burst");
                var interval = form.AddNumber("Interval", current.Interval, .05, 2, "Seconds between bolts");
                var speed = form.AddNumber("Speed", current.Speed, 1, 60, "Bolt speed in metres per second");
                var range = form.AddNumber("Range", current.Range, .5, 30, "How far each bolt flies, in metres, before it is gone");
                var length = form.AddNumber("Bolt length", current.Length, .05, 2, "Length of each bolt in metres");
                var color = form.AddText("Colour", current.Color, "Bolt colour as #RRGGBB (default red #FF2A2A)");
                string[] builtIn = { FireLaserCommand.DefaultSound, FireLaserCommand.MediumSound, FireLaserCommand.HeavySound, FireLaserCommand.NoSound };
                int builtInIndex = Array.FindIndex(builtIn, s => current.Sound.Equals(s, StringComparison.OrdinalIgnoreCase));
                bool isFile = builtInIndex < 0;
                var soundChoice = form.AddChoice("Sound", new[] { "Synthesized laser (pew)", "Synthesized medium laser", "Synthesized heavy laser (deep, guttural)", "No sound", "Audio file below" },
                    isFile ? 4 : builtInIndex, "Plays with each bolt");
                var soundFile = form.AddFile("Audio file", isFile ? current.Sound : "", "Audio|*.wav;*.mp3;*.wma;*.m4a;*.aiff|All files|*.*",
                    "Used when Sound is \"Audio file below\". Files outside Configuration are referenced where they are.");
                var volume = form.AddNumber("Volume", current.Volume, 0, 100, "Sound volume in percent");
                form.Apply = () =>
                {
                    string sound = soundChoice() == 4 ? soundFile() : builtIn[soundChoice()];
                    if (soundChoice() == 4)
                    {
                        if (string.IsNullOrWhiteSpace(sound)) throw new InvalidOperationException("Choose an audio file, or pick another Sound option.");
                        string full = System.IO.Path.IsPathRooted(sound) ? sound : System.IO.Path.Combine(root, sound);
                        if (!System.IO.File.Exists(full)) throw new InvalidOperationException("Audio file not found: " + full);
                        sound = ConfigPathService.IsWithin(root, full) ? System.IO.Path.GetRelativePath(root, full) : full;
                    }
                    var settings = new FireLaserCommand { Shots = (int)Math.Round(shots()), Interval = interval(), Speed = speed(), Range = range(), Length = length(),
                        Color = color().Trim(), Sound = sound, Volume = volume() };
                    settings.Validate(); return settings.ToJson();
                };
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.ToolboxSwap)
            {
                ToolboxSwapCommand current;
                try { current = ToolboxSwapCommand.Read(vm.TextValue); } catch { current = new ToolboxSwapCommand(); }
                var form = new CommandSettingsWindow("Toolbox Swap",
                    "Puts the red toolbox (true) or the laser (false) on the shoulder hinge from this time on. Without any Toolbox Swap command he carries the toolbox. Hide Laser hides either one.") { Owner = this };
                var swap = form.AddChoice("Toolbox Swap", new[] { "true (toolbox)", "false (laser)" }, current.Swap ? 0 : 1, "true shows the toolbox in place of the laser; false shows the laser");
                form.Apply = () => new ToolboxSwapCommand { Swap = swap() == 0 }.ToJson();
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.LcdShow)
            {
                LcdShowCommand current;
                try { current = LcdShowCommand.Read(vm.TextValue); } catch { current = new LcdShowCommand(); }
                string root = FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
                var form = new CommandSettingsWindow("LCD Show",
                    "What the waist LCD shows from this time on (open the waist panels with B_PanelsOpen to see it). A picture when one is chosen, otherwise the text. Without any LCD Show the screen reads: " + LcdShowCommand.DefaultText) { Owner = this };
                var text = form.AddText("Text", current.Text, "Message shown when there is no picture; it wraps and sizes itself to fill the screen. Leave empty (and no picture) for a dark screen.");
                var picture = form.AddFile("Picture", current.Image, "Pictures|" + string.Join(";", TvShowCommand.ImageExtensions.Select(e => "*" + e)) + "|All files|*.*",
                    "Optional. Pictures outside Configuration are copied into the LcdMedia folder in Configuration.");
                var fit = form.AddChoice("Picture fit", new[] { "Contain (whole picture)", "Cover (fill, crop edges)", "Stretch" }, (int)current.Fit, "How the picture fits the tall screen");
                var color = form.AddText("Text colour", current.Color, "#RRGGBB, for example #7CFFB2 (LCD green) or #FFFFFF");
                form.Apply = () =>
                {
                    string image = picture();
                    if (!string.IsNullOrWhiteSpace(image)) image = LcdShowCommand.Import(root, System.IO.Path.IsPathRooted(image) ? image : System.IO.Path.Combine(root, image));
                    var settings = new LcdShowCommand { Text = text(), Image = image ?? "", Fit = (TvFit)fit(), Color = string.IsNullOrWhiteSpace(color()) ? LcdShowCommand.DefaultColor : color().Trim() };
                    settings.Validate(); return settings.ToJson();
                };
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.TvFront)
            {
                TvFrontCommand current;
                try { current = TvFrontCommand.Read(vm.TextValue); } catch { current = new TvFrontCommand(); }
                var form = new CommandSettingsWindow("URDF TV Front",
                    "Glides the camera past the robot to face the backdrop TV squarely. The robot is hidden while the camera passes it and while facing the TV. Use URDF TV Return to come back.") { Owner = this };
                var seconds = form.AddNumber("Glide seconds", current.Seconds, 0, 30, "Time to reach the TV; 0 cuts straight to it");
                var fill = form.AddNumber("Screen fill", current.Fill, .2, 1,
                    "How much of the view the screen fills in its limiting direction: 0.85 leaves a small border, 1 fills edge to edge");
                var screen = form.AddText("Screen link", current.Screen,
                    "Backdrop URDF link whose box is the screen. The Matrix Construct backdrop's TV is " + TvFrontCommand.DefaultScreen + ".");
                form.Apply = () =>
                {
                    var settings = new TvFrontCommand { Seconds = seconds(), Fill = fill(), Screen = string.IsNullOrWhiteSpace(screen()) ? TvFrontCommand.DefaultScreen : screen() };
                    settings.Validate(); return settings.ToJson();
                };
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.UrdfBeam)
            {
                BeamCommand current;
                try { current = BeamCommand.Read(vm.TextValue); } catch { current = new BeamCommand(); }
                string root = FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
                var form = new CommandSettingsWindow("URDF Beam",
                    "TNG-style transporter effect. The robot is invisible before a Beam In (and visible when the sequence has no beam commands); after a Beam Out it stays invisible until the next Beam In.", blackTheme: true) { Owner = this };
                var direction = form.AddChoice("Direction", new[] { "Beam in (materialize)", "Beam out (dematerialize)" }, current.Direction == BeamDirection.Out ? 1 : 0,
                    "In: sparkles gather and the robot solidifies. Out: the robot shimmers away into sparkles.");
                var seconds = form.AddNumber("Seconds", current.Seconds, .5, 20, "Length of the effect");
                var particles = form.AddCheck("Particles", current.Particles, "Particles fall from the top of the URDF view when beaming in, and rise to it when beaming out");
                bool isFile = !current.Sound.Equals(BeamCommand.DefaultSound, StringComparison.OrdinalIgnoreCase) && !current.Sound.Equals(BeamCommand.NoSound, StringComparison.OrdinalIgnoreCase);
                var soundChoice = form.AddChoice("Sound", new[] { "Synthesized transporter sound", "No sound", "Audio file below" },
                    isFile ? 2 : current.Sound.Equals(BeamCommand.NoSound, StringComparison.OrdinalIgnoreCase) ? 1 : 0, "Plays from the start of the beam");
                var soundFile = form.AddFile("Audio file", isFile ? current.Sound : "", "Audio|*.wav;*.mp3;*.wma;*.m4a;*.aiff|All files|*.*",
                    "Used when Sound is \"Audio file below\". Files outside Configuration are referenced where they are.");
                var volume = form.AddNumber("Volume", current.Volume, 0, 100, "Sound volume in percent");
                var rgb = form.AddCheck("RGB sparkle", current.Rgb, "Also sparkle the physical robot's eye and vent rings (needs the BEAM effect in the Arduino sketch)");
                form.Apply = () =>
                {
                    string sound = soundChoice() switch { 1 => BeamCommand.NoSound, 2 => soundFile(), _ => BeamCommand.DefaultSound };
                    if (soundChoice() == 2)
                    {
                        if (string.IsNullOrWhiteSpace(sound)) throw new InvalidOperationException("Choose an audio file, or pick another Sound option.");
                        string full = System.IO.Path.IsPathRooted(sound) ? sound : System.IO.Path.Combine(root, sound);
                        if (!System.IO.File.Exists(full)) throw new InvalidOperationException("Audio file not found: " + full);
                        sound = ConfigPathService.IsWithin(root, full) ? System.IO.Path.GetRelativePath(root, full) : full;
                    }
                    var settings = new BeamCommand { Direction = direction() == 1 ? BeamDirection.Out : BeamDirection.In, Seconds = seconds(),
                        Particles = particles(), Sound = sound, Volume = volume(), Rgb = rgb() };
                    settings.Validate(); return settings.ToJson();
                };
                if (form.ShowDialog() == true) vm.TextValue = form.Result;
                return;
            }
            if (vm.Command.Servo == ServoNames.TvReturn)
            {
                MessageBox.Show(this, "Edit the text: {\"seconds\":3}\nseconds: glide time back to the normal camera (0-30).", ServoCommand.DisplayName(vm.Command.Servo));
                return;
            }

            var builder = new RgbBuilderWindow(vm.TextValue, vm.ColorHex) { Owner = this };
            if (builder.ShowDialog() == true)
            {
                vm.TextValue = builder.ResultText;
                if (!string.IsNullOrWhiteSpace(builder.ResultColorHex))
                    vm.ColorHex = builder.ResultColorHex; // legacy file compatibility
            }
        }

    }

    /// <summary>
    /// View-model wrapper for one draft ServoCommand row in the editor.
    /// Setters write through to the draft; hardware previews remain live.
    /// </summary>
    public class CommandVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public ServoCommand Command { get; }
        private readonly Action<ServoSpeed, ServoNames, int> _moveServoNow;
        private readonly Action<ServoSpeed, ServoNames, string> _moveServoNowText;
        private readonly Action<ServoSpeed, ServoNames, RobotControls, int> _moveChildNow;
        private readonly Action<ServoSpeed, ServoNames> _configureGangSpeedNow;
        private readonly Action<ServoSpeed, ServoNames, RobotControls> _configureChildSpeedNow;
        private readonly bool _initializing = true;
        private readonly CommandSplineDraft _splines;

        public bool SupportsSpline => _splines != null && !IsTextServo &&
            Command.Servo != ServoNames.Play && !Command.Control.HasValue;
        public bool SplineEnabled
        {
            get => _splines?.IsEnabled(Command.Servo) == true;
            set { if (SupportsSpline) _splines.SetEnabled(Command.Servo, value); }
        }
        public string SplineToolTip => Command.Control.HasValue
            ? "Spline is a sequence-wide setting for the parent servo group; select the group to change it. Individual child commands are not interpolated."
            : "Enable spline interpolation for this servo throughout the sequence. Neck Nod and Neck Tilt have independent spline curves; their output mixes into the two neck pistons.";
        public bool SupportsBreakSpline => SupportsSpline && SplineEnabled && !Command.Disable;
        public bool BreakSpline
        {
            get => Command.BreakSpline;
            set
            {
                if (!SupportsBreakSpline || Command.BreakSpline == value) return;
                Command.BreakSpline = value; Raise(nameof(BreakSpline));
            }
        }
        private void RefreshSplineState()
        {
            Raise(nameof(SplineEnabled));
            Raise(nameof(SupportsSpline));
            Raise(nameof(SplineToolTip));
            Raise(nameof(SupportsBreakSpline));
            Raise(nameof(BreakSpline));
        }

        // ---- merged Servo picklist ----
        // Display Grid order: each ganged ServoName followed by its child
        // servos prefixed " – ". Picking a child sets the command's
        // Servo (the parent - supplying the range) AND its Control target.
        // BothEyePop's children are LeftEyePop/RightEyePop, which appear as
        // their own entries right after it, so it gets no child items. The
        // Play pseudo-servo is export-only and never listed.
        public static List<ServoPickItem> ServoPickOptions { get; } = BuildPickOptions();

        private static List<ServoPickItem> BuildPickOptions()
        {
            var list = new List<ServoPickItem>();

            void Add(ServoNames servo, bool withChildren = true)
            {
                list.Add(new ServoPickItem(servo, null));
                if (!withChildren) return;
                var controls = ServoConfiguration.ControlsFor(servo);
                if (controls.Length > 1)
                    foreach (var c in controls)
                        list.Add(new ServoPickItem(servo, c));
            }

            // Display Grid order, with the eye pops moved to just above
            // RGBCommand, and RGBCommand at the very bottom.
            Add(ServoNames.FlapsOpen);
            Add(ServoNames.FlapTiltUp);
            Add(ServoNames.IrisClose);
            Add(ServoNames.EyesVerticalUp);
            Add(ServoNames.EyesHorizontalRight);
            Add(ServoNames.NeckTurn);
            Add(ServoNames.NeckNodUp);
            Add(ServoNames.NeckTiltRight);
            Add(ServoNames.NoseBasket);
            Add(ServoNames.NoseBody);
            Add(ServoNames.VentsOpen);
            Add(ServoNames.Microphone_RaiseLower);
            Add(ServoNames.Whip_Antenna_RaiseLower);
            Add(ServoNames.Whip_Antenna_Rotate);
            Add(ServoNames.MFR_UpDown);
            Add(ServoNames.MFR_Rotate);
            Add(ServoNames.BothEyePop, withChildren: false);
            Add(ServoNames.LeftEyePop);
            Add(ServoNames.RightEyePop);
            Add(ServoNames.RGBCommand);
            Add(ServoNames.URDFSpin, false);
            Add(ServoNames.URDFRotate, false);
            Add(ServoNames.URDFSlide, false);
            Add(ServoNames.URDFVertical, false);
            Add(ServoNames.URDFZoom, false);
            foreach (var camera in Enum.GetValues<ServoNames>().Where(UrdfCameraCommands.IsCamera)) Add(camera, false);
            Add(ServoNames.LoadBackdrop, false);
            Add(ServoNames.FallApart, false);
            Add(ServoNames.TvFront, false);
            Add(ServoNames.TvShow, false);
            Add(ServoNames.TvReturn, false);
            Add(ServoNames.UrdfBeam, false);
            Add(ServoNames.LcdShow, false);
            Add(ServoNames.HideLaser, false);
            Add(ServoNames.ToolboxSwap, false);
            Add(ServoNames.FireLaser, false);
            Add(ServoNames.TrackForward, false);
            Add(ServoNames.URDFStageX, false); Add(ServoNames.URDFStageY, false); Add(ServoNames.URDFStageHeading, false);
            foreach (var body in BodyControlCatalog.PairedPicklistControls) Add(body.Servo, false);
            return list;
        }

        /// <summary>The picklist selection mapped onto the command's
        /// Servo + Control pair.</summary>
        public ServoPickItem SelectedServoItem
        {
            get => ServoPickOptions.FirstOrDefault(i =>
                       i.Servo == Command.Servo && i.Control == Command.Control)
                   ?? ServoPickOptions.FirstOrDefault(i =>
                       i.Servo == Command.Servo && i.Control == null);
            set
            {
                if (value == null) return;
                Servo = value.Servo;            // re-ranges + clears bad Control
                Command.Control = value.Control;
                Raise(nameof(SelectedServoItem));
                RefreshSplineState();
            }
        }


        public CommandVM(ServoCommand command,
                         Action<ServoSpeed, ServoNames, int> moveServoNow,
                         Action<ServoSpeed, ServoNames, string> moveServoNowText,
                         Action<ServoSpeed, ServoNames, RobotControls, int> moveChildNow,
                         Action<ServoSpeed, ServoNames> configureGangSpeedNow,
                         Action<ServoSpeed, ServoNames, RobotControls> configureChildSpeedNow,
                         CommandSplineDraft splines = null)
        {
            Command = command;
            _moveServoNow = moveServoNow;
            _moveServoNowText = moveServoNowText;
            _moveChildNow = moveChildNow;
            _configureGangSpeedNow = configureGangSpeedNow;
            _configureChildSpeedNow = configureChildSpeedNow;
            _splines = splines;
            if (_splines != null) _splines.Changed += RefreshSplineState;
            _initializing = false;
        }

        // ---- per-servo value shape ------------------------------------

        /// <summary>Slider range for the currently selected servo.</summary>
        private double _spinSliderMin = -360, _spinSliderMax = 360;
        public double Min => Command.Servo == ServoNames.URDFSpin
            ? (_spinSliderMin = Math.Max(int.MinValue, Math.Min(_spinSliderMin, Command.NumericValue < _spinSliderMin ? Command.NumericValue - 360d : _spinSliderMin)))
            : ServoCommand.RangeFor(Command.Servo).Min;
        public double Max => Command.Servo == ServoNames.URDFSpin
            ? (_spinSliderMax = Math.Min(int.MaxValue, Math.Max(_spinSliderMax, Command.NumericValue > _spinSliderMax ? Command.NumericValue + 360d : _spinSliderMax)))
            : ServoCommand.RangeFor(Command.Servo).Max;

        /// <summary>True when the selected servo takes a text value
        /// (RGBCommand): the row shows a text box instead of the slider.</summary>
        public bool IsTrackForward => Command.Servo == ServoNames.TrackForward;
        public string ValueToolTip => Command.Servo == ServoNames.URDFSpin ? "Cumulative degrees: 720 is two positive turns; -720 is two negative turns. Spline between values for continuous motion." : Command.Servo == ServoNames.TrackForward ? "Speed in cm/s: positive forward, negative reverse, zero stops. URDF stage only." : "Command value in native control units";
        public bool IsTextServo => Command.IsTextServo;
        public string BuilderLabel => Command.Servo == ServoNames.LoadBackdrop ? "Choose Scene…" : "Build…";

        public bool SupportsSpeed =>
            !Command.IsTextServo &&
            Command.Servo != ServoNames.LeftEyePop &&
            Command.Servo != ServoNames.RightEyePop &&
            Command.Servo != ServoNames.BothEyePop &&
            Command.Servo != ServoNames.Play && !ServoCommand.IsUrdfOnly(Command.Servo);

        public bool SupportsDisable => !ServoCommand.IsUrdfOnly(Command.Servo);

        // ---- editable fields -------------------------------------------

        /// <summary>Offset as editable text; a valid non-negative number moves
        /// the command to that point on the timeline.</summary>
        public string OffsetText
        {
            get => Command.OffsetSeconds.ToString("F3", CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out double t) && t >= 0)
                    Command.OffsetSeconds = ServoCommand.TimeKey(t);
                Raise(nameof(OffsetText));   // re-format (or revert bad input)
            }
        }

        /// <summary>Changing the servo re-ranges the slider, switches between
        /// slider/text UI, clamps the numeric value into the new range, and
        /// refreshes the individual-control options for the new gang.</summary>
        public ServoNames Servo
        {
            get => Command.Servo;
            set
            {
                Command.Servo = value;
                Command.ClampToRange();

                // If the old individual-control target doesn't belong to the
                // new servo's gang, revert to driving the whole gang.
                if (Command.Control.HasValue &&
                    !ServoConfiguration.ControlsFor(value).Contains(Command.Control.Value))
                    Command.Control = null;

                Raise(nameof(Servo));
                Raise(nameof(Min));
                Raise(nameof(Max));
                Raise(nameof(IsTextServo)); Raise(nameof(BuilderLabel)); Raise(nameof(ValueToolTip)); Raise(nameof(IsTrackForward));
                Raise(nameof(SupportsSpeed));
                Raise(nameof(SupportsDisable));
                Raise(nameof(Disable));
                Raise(nameof(Value));
                Raise(nameof(SelectedServoItem));
                RefreshSplineState();
            }
        }

        /// <summary>Numeric value (slider + text box). Setting it jogs the
        /// hardware/head live: the whole gang for a gang selection, or just
        /// the one child servo when an individual control is targeted.</summary>
        public int Value
        {
            get => Command.NumericValue;
            set
            {
                Command.NumericValue = value;
                Command.ClampToRange();
                Raise(nameof(Min)); Raise(nameof(Max));
                Raise(nameof(Value));
                if (Command.Control.HasValue)
                    _moveChildNow?.Invoke(Command.Speed, Command.Servo,
                                          Command.Control.Value, Command.NumericValue);
                else
                    _moveServoNow?.Invoke(Command.Speed, Command.Servo,
                                          Command.NumericValue);
            }
        }

        /// <summary>Text value (RGBCommand): jogs the RGB hardware live.</summary>
        public string TextValue
        {
            get => ConfigPathService.DisplayCommand(Command.Servo, Command.TextValue);
            set
            {
                Command.TextValue = value ?? "";
                Raise(nameof(TextValue));
                _moveServoNowText?.Invoke(Command.Speed, Command.Servo,
                                          Command.TextValue);
            }
        }

        /// <summary>Palette color ("#RRGGBB"). Setting it updates the color
        /// button and the R/G/B readout, and persists with the project.</summary>
        public string ColorHex
        {
            get => Command.ColorHex;
            set
            {
                Command.ColorHex = value ?? "";
                Raise(nameof(ColorHex));
            }
        }

        /// <summary>Command speed is optional.  N/C means this command
        /// changes position only and leaves the Maestro's current speed and
        /// acceleration profile untouched.</summary>
        internal static readonly string[] SpeedChoices =
            { "N/C", "Default", "Fast", "Slow", "Crawl" };
        public string[] SpeedOptions => SpeedChoices;

        public string SpeedText
        {
            get => ServoCommand.SpeedToText(Command.Speed);
            set
            {
                if (!ServoCommand.TryParseSpeed(value, out var speed))
                    speed = ServoSpeed.NoChange;
                if (Command.Speed == speed) { Raise(nameof(SpeedText)); return; }

                Command.Speed = speed;
                Raise(nameof(SpeedText));

                // Editing the Speed column can configure the physical robot
                // immediately in Drive HW without moving the servo. For a
                // ganged command every Maestro child receives the profile.
                if (!_initializing && speed != ServoSpeed.NoChange && SupportsSpeed && !Command.Disable)
                {
                    if (Command.Control.HasValue)
                        _configureChildSpeedNow?.Invoke(speed, Command.Servo, Command.Control.Value);
                    else
                        _configureGangSpeedNow?.Invoke(speed, Command.Servo);
                }
            }
        }

        /// <summary>Disable checkbox: a disabled command turns its servo(s)
        /// OFF (PWM disabled) instead of moving them, and exports with the
        /// literal string "Disable" in its value field - for both ganged
        /// and child-servo commands.</summary>
        public bool Disable
        {
            get => Command.Disable;
            set { Command.Disable = value; Raise(nameof(Disable)); RefreshSplineState(); }
        }

        public string Reason
        {
            get => Command.Reason;
            set { Command.Reason = value ?? ""; Raise(nameof(Reason)); }
        }
    }

    /// <summary>One entry of the merged Servo picklist: a ganged ServoName
    /// (Control == null) or one of its child servos (" – " prefix).</summary>
    public class ServoPickItem
    {
        public ServoNames Servo { get; }
        public RobotControls? Control { get; }
        public ImageSource IconSource => ServoIconProvider.For(Servo);
        private readonly string _display;

        public ServoPickItem(ServoNames servo, RobotControls? control)
        {
            Servo = servo;
            Control = control;
            _display = control.HasValue ? " – " + UiNames.Words(control.Value.ToString()) : ServoCommand.DisplayName(servo);
        }

        public override string ToString() => _display;
    }
}
