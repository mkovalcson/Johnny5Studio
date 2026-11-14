using System.IO;
using ServoAnimator;

/// <summary>Audio picked outside Configuration when inserting into a sequence
/// is copied into Config/Library/Audio (Desktop and VR Editor share
/// LibraryAudioImport).</summary>
internal static class LibraryAudioImportChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        string parent = Directory.CreateTempSubdirectory("J5-LibraryAudioImport-").FullName;
        try
        {
            string config = Path.Combine(parent, "Config"), outside = Path.Combine(parent, "Outside");
            Directory.CreateDirectory(config); Directory.CreateDirectory(outside);
            string library = Path.Combine(config, "Library", "Audio");

            // 1. Outside file: copied, original untouched, reference is the copy.
            string source = Path.Combine(outside, "beep.wav");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
            DateTime sourceTime = File.GetLastWriteTimeUtc(source);
            string first = LibraryAudioImport.ImportIntoConfig(config, source, out bool copied);
            Check(copied && first == Path.Combine(library, "beep.wav"), "Outside audio is copied into Library/Audio under its own name.");
            Check(File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "The library copy has the same content.");
            Check(File.Exists(source) && File.ReadAllBytes(source).Length == 4 && File.GetLastWriteTimeUtc(source) == sourceTime, "The original file is left in place, unchanged.");
            Check(ConfigurationPaths.ToRelative(config, first).Replace('\\', '/') == "Library/Audio/beep.wav", "The command reference is Config-relative.");

            // 2. Same file again: identical library file is reused, no duplicate.
            string again = LibraryAudioImport.ImportIntoConfig(config, source, out copied);
            Check(!copied && again == first && Directory.GetFiles(library).Length == 1, "Re-inserting identical audio reuses the library copy.");

            // 3. Same name, different content: no overwrite, unique name.
            string other = Path.Combine(outside, "Other"); Directory.CreateDirectory(other);
            string clash = Path.Combine(other, "beep.wav");
            File.WriteAllBytes(clash, new byte[] { 9, 9, 9 });
            string second = LibraryAudioImport.ImportIntoConfig(config, clash, out copied);
            Check(copied && second == Path.Combine(library, "beep-2.wav"), "A different file with the same name gets a unique name.");
            Check(File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "The existing library file is not overwritten.");
            Check(File.ReadAllBytes(second).SequenceEqual(new byte[] { 9, 9, 9 }), "The renamed copy has the new content.");
            string clashAgain = LibraryAudioImport.ImportIntoConfig(config, clash, out copied);
            Check(!copied && clashAgain == second, "The renamed copy is found again by content.");
            string third = Path.Combine(other, "x"); Directory.CreateDirectory(third);
            File.WriteAllBytes(Path.Combine(third, "beep.wav"), new byte[] { 5 });
            Check(LibraryAudioImport.ImportIntoConfig(config, Path.Combine(third, "beep.wav"), out copied) == Path.Combine(library, "beep-3.wav") && copied, "Suffixes keep counting up.");

            // 4. Files already inside Configuration keep their reference.
            string inside = Path.Combine(config, "Projects", "Media", "voice.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
            File.WriteAllBytes(inside, new byte[] { 7 });
            string kept = LibraryAudioImport.ImportIntoConfig(config, inside, out copied);
            Check(!copied && kept == inside && !File.Exists(Path.Combine(library, "voice.mp3")), "Audio already inside Configuration is not copied.");

            bool missing = false;
            try { LibraryAudioImport.ImportIntoConfig(config, Path.Combine(outside, "none.wav"), out _); } catch (FileNotFoundException) { missing = true; }
            Check(missing, "A missing source file is reported, not silently referenced.");
            Console.WriteLine($"PASS: {count} library audio import assertions (copy outside audio, keep originals, unique names, keep Config references).");
        }
        finally
        {
            string full = Path.GetFullPath(parent);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(full, true);
        }
    }
}
