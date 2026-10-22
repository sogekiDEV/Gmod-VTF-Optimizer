using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Imaging;

public static class PixelCodec
{
    private readonly record struct Layout(int Bytes, int R, int G, int B, int A);

    private static Layout? RawLayout(ImageFormat format) => format switch
    {
        ImageFormat.Rgba8888 => new Layout(4, 0, 1, 2, 3),
        ImageFormat.Abgr8888 => new Layout(4, 3, 2, 1, 0),
        ImageFormat.Argb8888 => new Layout(4, 1, 2, 3, 0),
        ImageFormat.Bgra8888 => new Layout(4, 2, 1, 0, 3),
        ImageFormat.Rgb888 => new Layout(3, 0, 1, 2, -1),
        ImageFormat.Bgr888 => new Layout(3, 2, 1, 0, -1),
        ImageFormat.Bgrx8888 => new Layout(4, 2, 1, 0, -1),
        _ => null,
    };

    public static bool IsRaw(ImageFormat format) => RawLayout(format) != null;

    public static bool CanDecode(ImageFormat format) =>
        IsRaw(format) || ImageFormatInfo.IsBlockCompressed(format) ||
        format is ImageFormat.I8 or ImageFormat.Ia88 or ImageFormat.A8 or ImageFormat.Rgb565 or ImageFormat.Bgr565;

    public static bool CanEncode(ImageFormat format) =>
        IsRaw(format) || format is ImageFormat.Dxt1 or ImageFormat.Dxt5;

    public static byte[] Decode(ImageFormat format, ReadOnlySpan<byte> src, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Decode(format, src, width, height, rgba);
        return rgba;
    }

    public static void Decode(ImageFormat format, ReadOnlySpan<byte> src, int width, int height, Span<byte> rgba)
    {
        int pixels = width * height;
        var layout = RawLayout(format);
        if (layout is { } l)
        {
            for (int i = 0; i < pixels; i++)
            {
                int s = i * l.Bytes;
                int d = i * 4;
                rgba[d] = src[s + l.R];
                rgba[d + 1] = src[s + l.G];
                rgba[d + 2] = src[s + l.B];
                rgba[d + 3] = l.A >= 0 ? src[s + l.A] : (byte)255;
            }

            return;
        }

        if (ImageFormatInfo.IsBlockCompressed(format))
        {
            DxtDecoder.Decode(format, src, width, height, rgba);
            return;
        }

        switch (format)
        {
            case ImageFormat.I8:
                for (int i = 0; i < pixels; i++)
                {
                    byte v = src[i];
                    rgba[i * 4] = v;
                    rgba[i * 4 + 1] = v;
                    rgba[i * 4 + 2] = v;
                    rgba[i * 4 + 3] = 255;
                }

                break;
            case ImageFormat.Ia88:
                for (int i = 0; i < pixels; i++)
                {
                    byte v = src[i * 2];
                    rgba[i * 4] = v;
                    rgba[i * 4 + 1] = v;
                    rgba[i * 4 + 2] = v;
                    rgba[i * 4 + 3] = src[i * 2 + 1];
                }

                break;
            case ImageFormat.A8:
                for (int i = 0; i < pixels; i++)
                {
                    rgba[i * 4] = 255;
                    rgba[i * 4 + 1] = 255;
                    rgba[i * 4 + 2] = 255;
                    rgba[i * 4 + 3] = src[i];
                }

                break;
            case ImageFormat.Rgb565:
            case ImageFormat.Bgr565:
                bool swap = format == ImageFormat.Bgr565;
                for (int i = 0; i < pixels; i++)
                {
                    int v = src[i * 2] | (src[i * 2 + 1] << 8);
                    int hi = (v >> 11) & 31;
                    int mid = (v >> 5) & 63;
                    int lo = v & 31;
                    int r = swap ? lo : hi;
                    int b = swap ? hi : lo;
                    rgba[i * 4] = (byte)((r << 3) | (r >> 2));
                    rgba[i * 4 + 1] = (byte)((mid << 2) | (mid >> 4));
                    rgba[i * 4 + 2] = (byte)((b << 3) | (b >> 2));
                    rgba[i * 4 + 3] = 255;
                }

                break;
            default:
                throw new NotSupportedException($"cannot decode {format}");
        }
    }

    public static void Encode(ImageFormat format, ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst)
    {
        if (format == ImageFormat.Dxt1)
        {
            DxtEncoder.EncodeDxt1(rgba, width, height, dst);
            return;
        }

        if (format == ImageFormat.Dxt5)
        {
            DxtEncoder.EncodeDxt5(rgba, width, height, dst);
            return;
        }

        var layout = RawLayout(format) ?? throw new NotSupportedException($"cannot encode {format}");
        int pixels = width * height;
        for (int i = 0; i < pixels; i++)
        {
            int s = i * 4;
            int d = i * layout.Bytes;
            dst[d + layout.R] = rgba[s];
            dst[d + layout.G] = rgba[s + 1];
            dst[d + layout.B] = rgba[s + 2];
            if (layout.A >= 0)
            {
                dst[d + layout.A] = rgba[s + 3];
            }
            else if (layout.Bytes == 4)
            {
                dst[d + 3] = 255;
            }
        }
    }
}
