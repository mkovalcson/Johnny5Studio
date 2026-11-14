// ---------------------------------------------------------------------------
// FolderSettings.cs
//
// Where the app's files live: the CONFIG folder (servo configuration JSONs,
// the TIC\ folder with Pololu's ticcmd, the Library\ folders, and the
// Projects\ folder holding sequence files) and the PROJECT folder (source
// audio and exported animation JSONs). Persisted to Paths.json in the exe
// folder. On first run the app first looks for Config beside the executable
// (the deployed layout), then for Config beside the ServoAnimator
// project folder (the live-development layout). Config > Set Paths… can
// change it later. A legacy Folder.json (with its old "audioFolder" field) is
// migrated to Paths.json automatically.
// ---------------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServoAnimator
{
    public class FolderSettings
    {
        [JsonPropertyName("configFolder")]
        public string ConfigFolder { get; set; } = "";

        [JsonPropertyName("projectFolder")]
        public string ProjectFolder { get; set; } = "";

        /// <summary>Folder most recently used to load or save a sequence.
        /// Persisted independently of ProjectFolder so the sequence dialogs
        /// reopen where the operator last worked.</summary>
        [JsonPropertyName("lastSequenceFolder")]
        public string LastSequenceFolder { get; set; } = "";

        /// <summary>Legacy field name from Folder.json ("audioFolder"):
        /// read-only migration into ProjectFolder, never written back.</summary>
        [JsonPropertyName("audioFolder")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LegacyAudioFolder
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(ProjectFolder) &&
                    !string.IsNullOrWhiteSpace(value))
                    ProjectFolder = value;
            }
        }

        /// <summary>Paths.json lives beside the executable.</summary>
        public static string FilePath =>
            Path.Combine(AppContext.BaseDirectory, "Paths.json");

        private static string LegacyFilePath =>
            Path.Combine(AppContext.BaseDirectory, "Folder.json");

        /// <summary>Optional session-only Config selected by a host, such as the VR Player.</summary>
        public static string SessionConfigFolder { get; set; }

        /// <summary>Load saved paths, or use the explicit Config for the current operation/session.</summary>
        public static FolderSettings Load()
        {
            string selected = ConfigurationContext.CurrentRoot ?? SessionConfigFolder;
            if (!string.IsNullOrWhiteSpace(selected))
                return new FolderSettings { ConfigFolder = selected, ProjectFolder = Path.Combine(selected, "Projects") };
            try
            {
                if (File.Exists(FilePath))
                    return NormalizeLoaded(JsonSerializer.Deserialize<FolderSettings>(
                        File.ReadAllText(FilePath)));

                if (File.Exists(LegacyFilePath))
                {
                    var migrated = JsonSerializer.Deserialize<FolderSettings>(
                        File.ReadAllText(LegacyFilePath));
                    migrated = NormalizeLoaded(migrated);
                    migrated?.Save();          // write Paths.json going forward
                    return migrated;
                }

                string siblingConfig = FindConfigFolder();
                if (!string.IsNullOrWhiteSpace(siblingConfig))
                {
                    var discovered = new FolderSettings { ConfigFolder = siblingConfig };
                    // Persist when possible, but still use the discovered folder for
                    // this session if the executable directory is not writable.
                    try { discovered.Save(); } catch { }
                    return discovered;
                }

                return null;
            }
            catch { return null; }
        }

        /// <summary>Find the portable deployed Config folder first, then retain
        /// the established live-development Config discovery behavior.</summary>
        private static string FindConfigFolder(string applicationFolder = null)
        {
            applicationFolder ??= AppContext.BaseDirectory;
            try
            {
                // Deployment layout:
                //   J5_Animator\AnimationEditorPlayer.exe
                //   J5_Animator\Config\...
                string deployedConfig = Path.Combine(applicationFolder, "Config");
                if (Directory.Exists(deployedConfig))
                    return Path.GetFullPath(deployedConfig);

                for (DirectoryInfo dir = new(applicationFolder);
                     dir != null;
                     dir = dir.Parent)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "FaceTracking")) && Directory.Exists(Path.Combine(dir.FullName, "Config")))
                        return Path.Combine(dir.FullName, "Config");
                    // Typical development layout:
                    //   <root>\ServoAnimator\bin\Debug\net10.0-windows\...
                    //   <root>\Config
                    if (string.Equals(dir.Name, "ServoAnimator",
                                      StringComparison.OrdinalIgnoreCase) &&
                        dir.Parent != null)
                    {
                        string candidate = Path.Combine(
                            dir.Parent.FullName, "Config");
                        if (Directory.Exists(candidate))
                            return Path.GetFullPath(candidate);
                    }

                    // Also recognize an ancestor containing both sibling folders.
                    string projectFolder = Path.Combine(dir.FullName, "ServoAnimator");
                    string configFolder = Path.Combine(dir.FullName, "Config");
                    if (Directory.Exists(projectFolder) && Directory.Exists(configFolder))
                        return Path.GetFullPath(configFolder);
                }
            }
            catch { }

            return null;
        }

        public void Save()
        {
            ProjectFolder = Path.Combine(ConfigFolderOrDefault, "Projects");
            try { Directory.CreateDirectory(ProjectFolder); } catch { }
            string config = Path.GetFullPath(ConfigFolderOrDefault);
            string storedConfig = string.Equals(Path.GetPathRoot(AppContext.BaseDirectory), Path.GetPathRoot(config), StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(AppContext.BaseDirectory, config)
                : config;
            var persisted = new FolderSettings
            {
                ConfigFolder = storedConfig,
                ProjectFolder = "Projects",
                LastSequenceFolder = ConfigurationPaths.IsWithin(config, LastSequenceFolder)
                    ? Path.GetRelativePath(config, Path.GetFullPath(LastSequenceFolder))
                    : "",
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                persisted, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static FolderSettings NormalizeLoaded(FolderSettings settings)
        {
            if (settings == null) return null;
            try
            {
                settings.ConfigFolder = ResolveConfigFolder(settings.ConfigFolder, AppContext.BaseDirectory);

                if (!string.IsNullOrWhiteSpace(settings.ConfigFolder))
                {
                    settings.ProjectFolder = Path.Combine(settings.ConfigFolder, "Projects");
                    if (!string.IsNullOrWhiteSpace(settings.LastSequenceFolder) &&
                        !Path.IsPathRooted(settings.LastSequenceFolder))
                        settings.LastSequenceFolder = Path.GetFullPath(Path.Combine(
                            settings.ConfigFolder, settings.LastSequenceFolder));
                    if (!ConfigurationPaths.IsWithin(settings.ConfigFolder,
                                                     settings.LastSequenceFolder))
                        settings.LastSequenceFolder = "";
                }
            }
            catch { settings.LastSequenceFolder = ""; }
            return settings;
        }

        // A stale Paths.json must use the same discovery as a first run,
        // especially the portable Config folder beside the executable.
        public static string ResolveConfigFolder(string configuredFolder, string applicationFolder)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configuredFolder))
                {
                    string candidate = Path.IsPathRooted(configuredFolder)
                        ? configuredFolder : Path.Combine(applicationFolder, configuredFolder);
                    if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            catch { }
            return FindConfigFolder(applicationFolder) ?? Path.GetFullPath(applicationFolder);
        }

        /// <summary>The selected config folder, then adjacent Config,
        /// development Config, and finally the executable folder.</summary>
        [JsonIgnore]
        public string ConfigFolderOrDefault => ResolveConfigFolder(ConfigFolder, AppContext.BaseDirectory);

        /// <summary>The Projects folder is always directly under the selected
        /// Configuration folder. The persisted ProjectFolder field is retained
        /// only for backward-compatible Paths.json reading.</summary>
        [JsonIgnore]
        public string ProjectFolderOrDefault
        {
            get
            {
                string projects = Path.Combine(ConfigFolderOrDefault, "Projects");
                try { Directory.CreateDirectory(projects); } catch { }
                return Directory.Exists(projects) ? projects : ConfigFolderOrDefault;
            }
        }
    }
}
