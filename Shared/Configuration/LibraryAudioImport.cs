using System.IO;

namespace ServoAnimator;

/// <summary>Brings audio chosen in a file browser into the Configuration
/// folder so the sequence that uses it stays portable. Files already inside
/// Configuration are left where they are. Files outside it are COPIED (never
/// moved) into Config/Library/Audio; the user's original is not touched.</summary>
public static class LibraryAudioImport
{
    /// <summary>Config/Library/Audio for <paramref name="configRoot"/> (created on demand).</summary>
    public static string Folder(string configRoot)
    {
        string dir = Path.Combine(configRoot, "Library", "Audio");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Returns the full path the sequence should reference.
    /// <paramref name="copied"/> is true when a new copy was written.
    /// A library file with the same name and identical content is reused;
    /// one with the same name but different content is never overwritten:
    /// the copy gets a unique "name-2.ext", "name-3.ext" ... name instead.</summary>
    public static string ImportIntoConfig(string configRoot, string sourcePath, out bool copied)
    {
        copied = false;
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Audio file not found.", source);
        if (ConfigurationPaths.IsWithin(configRoot, source)) return source;

        string folder = Folder(configRoot);
        string stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
        string sourceHash = null;
        for (int n = 1; n < 10000; n++)
        {
            string candidate = Path.Combine(folder, n == 1 ? stem + ext : $"{stem}-{n}{ext}");
            if (File.Exists(candidate))
            {
                sourceHash ??= ConfigurationAssets.HashFile(source);
                if (new FileInfo(candidate).Length == new FileInfo(source).Length &&
                    ConfigurationAssets.HashFile(candidate) == sourceHash)
                    return candidate;
                continue;
            }
            try { File.Copy(source, candidate, overwrite: false); }
            catch (IOException) when (File.Exists(candidate)) { n--; continue; } // created meanwhile: re-check it
            copied = true;
            return candidate;
        }
        throw new IOException("No free file name for " + Path.GetFileName(source) + " in " + folder);
    }
}
