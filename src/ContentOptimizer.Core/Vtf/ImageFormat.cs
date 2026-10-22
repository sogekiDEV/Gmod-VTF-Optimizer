namespace ContentOptimizer.Core.Vtf;

public enum ImageFormat
{
    Rgba8888 = 0,
    Abgr8888 = 1,
    Rgb888 = 2,
    Bgr888 = 3,
    Rgb565 = 4,
    I8 = 5,
    Ia88 = 6,
    P8 = 7,
    A8 = 8,
    Rgb888Bluescreen = 9,
    Bgr888Bluescreen = 10,
    Argb8888 = 11,
    Bgra8888 = 12,
    Dxt1 = 13,
    Dxt3 = 14,
    Dxt5 = 15,
    Bgrx8888 = 16,
    Bgr565 = 17,
    Bgrx5551 = 18,
    Bgra4444 = 19,
    Dxt1OneBitAlpha = 20,
    Bgra5551 = 21,
    Uv88 = 22,
    Uvwq8888 = 23,
    Rgba16161616F = 24,
    Rgba16161616 = 25,
    Uvlx8888 = 26,
}

public static class ImageFormatInfo
{
    private static readonly int[] BitsPerPixel = { 32, 32, 24, 24, 16, 8, 16, 8, 8, 24, 24, 32, 32, 0, 0, 0, 32, 16, 16, 16, 0, 16, 16, 32, 64, 64, 32 };

    public static bool IsKnown(int value) => value >= 0 && value <= 26;

    public static bool IsBlockCompressed(ImageFormat format) =>
        format is ImageFormat.Dxt1 or ImageFormat.Dxt3 or ImageFormat.Dxt5 or ImageFormat.Dxt1OneBitAlpha;

    public static int BlockBytes(ImageFormat format) =>
        format is ImageFormat.Dxt1 or ImageFormat.Dxt1OneBitAlpha ? 8 : 16;

    public static long ImageSize(ImageFormat format, int width, int height)
    {
        if (IsBlockCompressed(format))
        {
            return (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * BlockBytes(format);
        }

        return (long)width * height * BitsPerPixel[(int)format] / 8;
    }
}
