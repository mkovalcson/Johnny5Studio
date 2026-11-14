using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace ServoAnimator;

internal static class RecordedVoiceConverter
{
    // Applio loads a substantial model; do not launch competing conversions for successive takes.
    private static readonly System.Threading.SemaphoreSlim Gate = new(1, 1);

    internal static Task<string> ConvertAsync(string input) => ConvertAsync(input,
        Path.Combine(AppContext.BaseDirectory, "Scripts", "ConvertOneFileToJohnny5.bat"));

    internal static async Task<string> ConvertAsync(string input, string script)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            input = Path.GetFullPath(input);
            script = Path.GetFullPath(script);
            if (!File.Exists(script)) throw new FileNotFoundException("The voice conversion script is missing.", script);
            if (!File.Exists(input)) throw new FileNotFoundException("The recorded audio file is missing.", input);
            // The supplied batch uses CALL (a second expansion) and unquoted ECHO expressions.
            // Refuse shell syntax rather than interpreting characters from a sequence/folder name.
            if (input.IndexOfAny("&|<>^%!\"\r\n".ToCharArray()) >= 0 ||
                script.IndexOfAny("&|<>^%!\"\r\n".ToCharArray()) >= 0)
                throw new InvalidOperationException("The batch converter requires a folder and sequence name without & | < > ^ % ! or quotation marks.");
            string output = Path.Combine(Path.GetDirectoryName(input), Path.GetFileNameWithoutExtension(input) + "_j5.wav");
            // Never let the batch's delete-old-result step overwrite a previously retained conversion.
            if (File.Exists(output)) throw new IOException("A converted file already exists: " + output);
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = "/d /v:off /s /c \"\"%J5_CONVERTER%\" \"%J5_INPUT%\"\"",
                WorkingDirectory = Path.GetDirectoryName(script),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.Environment["J5_NOPAUSE"] = "1";
            start.Environment["J5_CONVERTER"] = script;
            start.Environment["J5_INPUT"] = input;
            using var process = Process.Start(start) ?? throw new IOException("Could not start the voice converter.");
            // Drain both streams concurrently without retaining large Python progress logs in memory.
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException($"Voice converter exited with code {process.ExitCode}. Check the Applio installation and model paths in {script}.");
            if (!File.Exists(output)) throw new IOException("The converter did not create " + Path.GetFileName(output) + ".");
            using var wave = new WaveFileReader(output);
            if (wave.Length <= 0 || wave.TotalTime <= TimeSpan.Zero)
                throw new IOException("The converted WAV contains no audio.");
            return output;
        }
        finally { Gate.Release(); }
    }
}
