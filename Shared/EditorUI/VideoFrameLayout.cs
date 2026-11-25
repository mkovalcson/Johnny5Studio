using System.IO;

namespace ServoAnimator;

internal static class VideoFrameLayout
{
    internal static int LinearPitch(int rowBytes, int height, int declaredStride, int length)
    {
        int declaredPitch = Math.Abs(declaredStride);
        if ((long)rowBytes * height == length) return rowBytes;
        if (declaredPitch >= rowBytes && (long)declaredPitch * height == length) return declaredPitch;

        // Some decoder buffers expose only linear access and include padding
        // to both the row and the allocated height. Match the entire buffer
        // against aligned dimensions before treating any bytes as pixels.
        static long Align(int value, int alignment) => ((long)value + alignment - 1) / alignment * alignment;
        int match = 0;
        for (int yAlign = 1; yAlign <= 128; yAlign *= 2)
        {
            long rows = Align(height, yAlign);
            if (length % rows != 0) continue;
            int pitch = (int)(length / rows);
            if (pitch < rowBytes) continue;
            bool aligned = pitch == rowBytes || pitch == declaredPitch;
            for (int xAlign = 8; !aligned && xAlign <= 1024; xAlign *= 2)
                aligned = pitch == Align(rowBytes, xAlign) || (declaredPitch >= rowBytes && pitch == Align(declaredPitch, xAlign));
            if (!aligned) continue;
            if (match != 0 && match != pitch) throw new InvalidDataException("Ambiguous RGB32 video buffer padding.");
            match = pitch;
        }
        if (match != 0) return match;
        throw new InvalidDataException("Invalid RGB32 video buffer layout.");
    }
}
