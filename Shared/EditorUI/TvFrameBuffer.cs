using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

/// <summary>Short TV excerpts have a bounded picture buffer independent of the audio player.</summary>
internal sealed class TvFrameBuffer
{
    private const int FramesPerSecond = 30;
    private const int MaxWidth = 720, MaxHeight = 480;
    private const long PixelBudget = 120L * 1024 * 1024;
    private (string Path, double Start, double Length)? _key;
    private CancellationTokenSource _cancel;
    private Task<BitmapSource[]> _decode;

    internal BitmapSource[] Frames => _decode?.IsCompletedSuccessfully == true ? _decode.Result : null;
    internal bool IsPending => _decode is { IsCompleted: false };

    internal void Prepare(TvShowCommand show, string configRoot)
    {
        (string Path, double Start, double Length)? wanted = show is { IsVideo: true, Loop: false, DurationSeconds: > 0 and <= 8 }
            ? (show.FullPath(configRoot), show.StartSeconds, show.DurationSeconds) : null;
        if (wanted == _key) return;
        _key = wanted;
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
        _decode = null;
        if (wanted is not { } clip) return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _decode = Task.Run(() =>
        {
            try { return Decode(clip.Path, clip.Start, clip.Length, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
            catch (Exception ex) { Debug.WriteLine("TV picture buffer unavailable: " + ex.Message); return null; }
        });
    }

    private static BitmapSource[] Decode(string path, double start, double length, CancellationToken token)
    {
        int count = (int)Math.Ceiling(length * FramesPerSecond) + 1;
        var times = Enumerable.Range(0, count).Select(i => start + Math.Min(i / (double)FramesPerSecond, length)).ToArray();
        var pictures = new List<BitmapSource>(count);
        byte[] previousPixels = null;
        foreach (var frame in VideoFrames.ReadSequential(path, times, token))
        {
            if (ReferenceEquals(previousPixels, frame.Pixels)) { pictures.Add(pictures[^1]); continue; }
            double pixels = (double)frame.Width * frame.Height;
            double scale = Math.Min((double)MaxHeight / frame.Height, Math.Sqrt(PixelBudget / (pixels * 4 * count)));
            int width = Math.Clamp((int)(frame.Width * scale), 1, MaxWidth);
            var sized = VideoFrames.ToBitmap(frame, width);
            int stride = sized.PixelWidth * 4;
            var pixelsAtSize = new byte[stride * sized.PixelHeight];
            sized.CopyPixels(pixelsAtSize, stride, 0);
            // Materialize the scaled image so it releases the original full-resolution frame.
            var bitmap = BitmapSource.Create(sized.PixelWidth, sized.PixelHeight, 96, 96, sized.Format, null, pixelsAtSize, stride);
            bitmap.Freeze();
            pictures.Add(bitmap);
            previousPixels = frame.Pixels;
        }
        return pictures.ToArray();
    }

    internal static BitmapSource PictureAt(BitmapSource[] frames, double elapsed)
    {
        int last = frames.Length - 1;
        double frame = Math.Clamp(elapsed * FramesPerSecond, 0, last);
        return frames[(int)(frame + 1e-9)];
    }
}
