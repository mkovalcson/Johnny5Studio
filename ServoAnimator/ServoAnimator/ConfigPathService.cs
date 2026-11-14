using System.IO;

namespace ServoAnimator
{
    /// <summary>Centralizes the portable-path rule used by project, movie,
    /// recovery, recent-file, audio, and library data. Persisted paths are
    /// relative to the selected Configuration folder; resolved paths are
    /// accepted only when they remain inside that folder.</summary>
    internal static partial class ConfigPathService
    {
        public static bool IsWithin(string configFolder, string path) => ConfigurationPaths.IsWithin(configFolder, path);

        public static bool TryResolve(string configFolder, string storedPath, out string fullPath) => ConfigurationPaths.TryResolve(configFolder, storedPath, out fullPath);

        public static string ToRelative(string configFolder, string path) => ConfigurationPaths.ToRelative(configFolder, path);

        public static AnimationDocument ResolveDocumentPaths(
            AnimationDocument document, string configFolder)
        {
            if (document == null) return null;
            if (TryResolve(configFolder, document.AudioFilePath, out string primary))
                document.AudioFilePath = primary;
            else if (!string.IsNullOrWhiteSpace(document.AudioFilePath))
                document.AudioFilePath = "";

            foreach (var command in document.Commands ?? new List<ServoCommand>())
            {
                if (command.Servo != ServoNames.Play) continue;
                if (TryResolve(configFolder, command.TextValue, out string audio))
                    command.TextValue = audio;
                else if (!string.IsNullOrWhiteSpace(command.TextValue))
                    command.TextValue = "";
            }
            return document;
        }

        public static AnimationDocument MakeDocumentPathsRelative(
            AnimationDocument document, string configFolder)
        {
            if (document == null) return null;
            if (!string.IsNullOrWhiteSpace(document.AudioFilePath))
                document.AudioFilePath = ToRelative(configFolder, document.AudioFilePath);

            foreach (var command in document.Commands ?? new List<ServoCommand>())
                if (command.Servo == ServoNames.Play &&
                    !string.IsNullOrWhiteSpace(command.TextValue))
                    command.TextValue = ToRelative(configFolder, command.TextValue);
            LocalizeBackdropImages(document.Commands, configFolder);
            document.UrdfBackgroundFile = Reference(configFolder, document.UrdfBackgroundFile);
            return document;
        }
        private static void LocalizeBackdropImages(IEnumerable<ServoCommand> commands, string root)
        {
            foreach (var command in commands ?? Enumerable.Empty<ServoCommand>())
            {
                if (command.Servo == ServoNames.LoadBackdrop && !string.IsNullOrWhiteSpace(command.TextValue))
                {
                    BackdropCommand backdrop = null;
                    try { backdrop = BackdropCommand.Read(command.TextValue); } catch (System.Text.Json.JsonException) { }
                    if (backdrop != null && string.IsNullOrWhiteSpace(backdrop.SceneFile) && backdrop.Background != null)
                    {
                        backdrop.Background.EnsureLocalImage(root);
                        // Embedded stage references follow the same portable rule as saved scenes.
                        command.TextValue = ModelStorage.Serialize(backdrop, root, ProjectPackage.JsonOptions);
                    }
                }
                LocalizeBackdropImages(command.AudioTrim?.HiddenCommands, root);
            }
        }
    }
}
