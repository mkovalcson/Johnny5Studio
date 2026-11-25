using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;

namespace ServoAnimator;

/// <summary>Decoded video pictures for the editor API (so an AI can see a Video Clip), read by a Media Foundation
/// source reader with its video processor converting to 32-bit RGB. Independent of the on-screen player.</summary>
internal static class VideoFrames
{
    internal sealed record Frame(int Width, int Height, byte[] Pixels, double Seconds);

    private static readonly Guid VideoMajorType = new("73646976-0000-0010-8000-00AA00389B71");        // MFMediaType_Video
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");                 // MFVideoFormat_RGB32
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");             // MF_MT_FRAME_SIZE
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");         // MF_MT_DEFAULT_STRIDE
    private static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d"); // MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC), AllStreams = unchecked((int)0xFFFFFFFE);
    private const int EndOfStream = 2;   // MF_SOURCE_READERF_ENDOFSTREAM
    // Reading forward is cheaper than seeking (a seek decodes from the previous keyframe), up to about this far.
    private const double ReadAheadSeconds = 1.5;

    /// <summary>The pictures shown at each video position (seconds, ascending works best). A position past the end
    /// gives the last picture. Top-down BGRA rows.</summary>
    internal static List<Frame> Read(string path, IReadOnlyList<double> seconds) => ReadSequential(path, seconds).ToList();

    internal static IEnumerable<Frame> ReadSequential(string path, IReadOnlyList<double> seconds, CancellationToken token = default)
    {
        if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException("Video not found.", path);
        double duration = VideoClipCommand.ProbeSeconds(path);
        IMFSourceReader reader = null;
        int width = 0, height = 0, stride = 0;
        void Open()
        {
            if (reader != null) Marshal.ReleaseComObject(reader);
            MediaFoundationApi.Startup();
            var attributes = MediaFoundationApi.CreateAttributes(1);
            attributes.SetUINT32(EnableVideoProcessing, 1);
            MediaFoundationInterop.MFCreateSourceReaderFromURL(path, attributes, out reader);
            reader.SetStreamSelection(AllStreams, false);
            reader.SetStreamSelection(FirstVideoStream, true);
            var wanted = MediaFoundationApi.CreateMediaType();
            wanted.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, VideoMajorType);
            wanted.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, Rgb32);
            try { reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, wanted); }
            catch (COMException) { throw new InvalidOperationException("This file has no video picture Windows can decode."); }
            reader.GetCurrentMediaType(FirstVideoStream, out IMFMediaType actual);
            actual.GetUINT64(FrameSize, out long size);
            width = (int)(size >> 32); height = (int)(size & 0xFFFFFFFF); stride = width * 4;
            try { actual.GetUINT32(DefaultStride, out int reported); if (reported != 0) stride = reported; }
            catch (COMException) { /* not reported: packed top-down */ }
            Marshal.ReleaseComObject(actual);
        }
        try
        {
            Open();

            double position = 0;                   // a fresh reader starts at the beginning
            bool seekable = true;
            byte[] last = null; double lastTime = 0, lastEnd = 0;
            foreach (double requested in seconds)
            {
                token.ThrowIfCancellationRequested();
                double target = Math.Clamp(requested, 0, Math.Max(0, duration - .001));   // past the end: the last picture
                if (target < position - 1e-9 || target > lastEnd + ReadAheadSeconds)
                {
                    last = null; lastEnd = 0;
                    if (seekable)
                    {
                        try { Seek(reader, target); }
                        // Some files (for example fragmented MP4) refuse to seek: read them forward from the start instead.
                        catch (COMException) { seekable = false; }
                    }
                    if (!seekable && target < position - 1e-9) { Open(); position = 0; }
                }
                bool ended = false;
                while (last == null || lastEnd <= target)
                {
                    reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out ulong timestamp, out IMFSample sample);
                    long time = (long)timestamp;
                    if (((int)flags & EndOfStream) != 0) { ended = true; if (sample != null) Marshal.ReleaseComObject(sample); break; }
                    if (sample == null) continue;
                    try
                    {
                        sample.GetSampleDuration(out long frameLength);
                        lastTime = time / 1e7; lastEnd = (time + Math.Max(frameLength, 1)) / 1e7; position = lastTime;
                        if (lastEnd > target || last == null) last = Copy(sample, width, height, stride);
                    }
                    finally { Marshal.ReleaseComObject(sample); }
                }
                if (last == null) throw new InvalidOperationException("No picture could be decoded from this video.");
                yield return new Frame(width, height, last, lastTime);
                if (ended) position = double.PositiveInfinity;   // reposition for any later request
            }
        }
        finally { if (reader != null) Marshal.ReleaseComObject(reader); }
    }

    private static void Seek(IMFSourceReader reader, double seconds)
    {
        // A PROPVARIANT holding VT_I8 100 ns units, laid out by hand (24 bytes on x64: type, reserved, value).
        IntPtr ptr = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i += 8) Marshal.WriteInt64(ptr, i, 0);
            Marshal.WriteInt16(ptr, 0, 20);   // VT_I8
            Marshal.WriteInt64(ptr, 8, (long)(Math.Max(0, seconds) * 1e7));
            reader.SetCurrentPosition(Guid.Empty, ptr);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44EC-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface VideoBuffer2D
    {
        void Lock2D(out IntPtr firstScanline, out int pitch);
        void Unlock2D();
    }

    /// <summary>Copy RGB32 into top-down, tightly packed BGRA rows.</summary>
    private static byte[] Copy(IMFSample sample, int width, int height, int stride)
    {
        sample.GetBufferCount(out int count);
        IMFMediaBuffer buffer;
        if (count == 1) sample.GetBufferByIndex(0, out buffer);
        else sample.ConvertToContiguousBuffer(out buffer);

        int rowBytes = checked(width * 4);
        var pixels = new byte[checked(rowBytes * height)];
        try
        {
            if (buffer is VideoBuffer2D surface)
            {
                // A 2D surface supplies the actual pitch and the address of
                // the visual top row, even for a bottom-up image.
                surface.Lock2D(out var firstScanline, out int pitch);
                try { CopyRows(firstScanline, pitch); }
                finally { surface.Unlock2D(); }
            }
            else
            {
                buffer.Lock(out var data, out _, out int length);
                try
                {
                    int pitch = VideoFrameLayout.LinearPitch(rowBytes, height, stride, length);
                    CopyRows(stride < 0 ? data + (height - 1) * pitch : data, stride < 0 ? -pitch : pitch);
                }
                finally { buffer.Unlock(); }
            }

            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally { Marshal.ReleaseComObject(buffer); }

        void CopyRows(IntPtr top, int pitch)
        {
            if (Math.Abs((long)pitch) < rowBytes) throw new System.IO.InvalidDataException("Invalid RGB32 video row pitch.");
            for (int y = 0; y < height; y++)
                Marshal.Copy(top + checked(y * pitch), pixels, y * rowBytes, rowBytes);
        }
    }

    internal static BitmapSource ToBitmap(Frame frame, int maxWidth)
    {
        BitmapSource bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
        if (maxWidth > 0 && frame.Width > maxWidth)
        {
            double scale = (double)maxWidth / frame.Width;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }
        bitmap.Freeze();
        return bitmap;
    }

    internal static string ToJpegDataUrl(BitmapSource bitmap)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new System.IO.MemoryStream();
        encoder.Save(output);
        return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
    }
}
