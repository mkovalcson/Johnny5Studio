using System.IO;
using System.Security.Cryptography;

namespace ServoAnimator;

public static class ConfigurationAssets
{
    public static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }

    public static string SafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("A package file path is empty.");
        path = path.Replace('\\', '/');
        var parts = path.Split('/');
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9'))
                throw new InvalidDataException("Unsafe package path: " + path);
        }
        return path;
    }

    public static string Destination(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, SafeRelative(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!ConfigurationPaths.IsWithin(root, full)) throw new InvalidDataException("Package path leaves Configuration.");
        // Do not let a junction, symbolic link or file parent redirect an import.
        for (string current = full; !string.Equals(current, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
        {
            if (string.IsNullOrEmpty(current)) throw new InvalidDataException("Invalid package destination.");
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("An import destination is a link: " + current);
            if (!string.Equals(current, full, StringComparison.OrdinalIgnoreCase) && File.Exists(current))
                throw new InvalidDataException("A file blocks an import folder: " + current);
        }
        return full;
    }
}
