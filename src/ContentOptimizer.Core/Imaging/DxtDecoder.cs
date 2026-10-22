using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Imaging;

public static class DxtDecoder
{
    public static void Decode(ImageFormat format, ReadOnlySpan<byte> src, int width, int height, Span<byte> rgba)
    {
        int bw = Math.Max(1, (width + 3) / 4);
        int bh = Math.Max(1, (height + 3) / 4);
        int stride = ImageFormatInfo.BlockBytes(format);
        Span<byte> block = stackalloc byte[64];
        int o = 0;
        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                switch (format)
                {
                    case ImageFormat.Dxt1:
                        DecodeColor(src.Slice(o, 8), block, true, true);
                        break;
                    case ImageFormat.Dxt1OneBitAlpha:
                        DecodeColor(src.Slice(o, 8), block, true, false);
                        break;
                    case ImageFormat.Dxt3:
                        DecodeColor(src.Slice(o + 8, 8), block, false, true);
                        for (int i = 0; i < 16; i++)
                        {
                            int nibble = (src[o + i / 2] >> ((i & 1) * 4)) & 15;
                            block[i * 4 + 3] = (byte)(nibble * 17);
                        }

                        break;
                    default:
                        DecodeColor(src.Slice(o + 8, 8), block, false, true);
                        DecodeAlpha(src.Slice(o, 8), block);
                        break;
                }

                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= height)
                    {
                        break;
                    }

                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= width)
                        {
                            break;
                        }

                        block.Slice((py * 4 + px) * 4, 4).CopyTo(rgba.Slice((y * width + x) * 4, 4));
                    }
                }

                o += stride;
            }
        }
    }

    public static void Expand565(int value, out int r, out int g, out int b)
    {
        int r5 = (value >> 11) & 31;
        int g6 = (value >> 5) & 63;
        int b5 = value & 31;
        r = (r5 << 3) | (r5 >> 2);
        g = (g6 << 2) | (g6 >> 4);
        b = (b5 << 3) | (b5 >> 2);
    }

    private static void DecodeColor(ReadOnlySpan<byte> blk, Span<byte> outRgba, bool dxt1Mode, bool opaque)
    {
        int c0 = blk[0] | (blk[1] << 8);
        int c1 = blk[2] | (blk[3] << 8);
        uint bits = (uint)(blk[4] | (blk[5] << 8) | (blk[6] << 16) | (blk[7] << 24));
        Span<int> pal = stackalloc int[16];
        Expand565(c0, out pal[0], out pal[1], out pal[2]);
        pal[3] = 255;
        Expand565(c1, out pal[4], out pal[5], out pal[6]);
        pal[7] = 255;
        if (dxt1Mode && c0 <= c1)
        {
            pal[8] = (pal[0] + pal[4]) / 2;
            pal[9] = (pal[1] + pal[5]) / 2;
            pal[10] = (pal[2] + pal[6]) / 2;
            pal[11] = 255;
            pal[12] = 0;
            pal[13] = 0;
            pal[14] = 0;
            pal[15] = opaque ? 255 : 0;
        }
        else
        {
            pal[8] = (2 * pal[0] + pal[4]) / 3;
            pal[9] = (2 * pal[1] + pal[5]) / 3;
            pal[10] = (2 * pal[2] + pal[6]) / 3;
            pal[11] = 255;
            pal[12] = (pal[0] + 2 * pal[4]) / 3;
            pal[13] = (pal[1] + 2 * pal[5]) / 3;
            pal[14] = (pal[2] + 2 * pal[6]) / 3;
            pal[15] = 255;
        }

        for (int i = 0; i < 16; i++)
        {
            int idx = (int)((bits >> (2 * i)) & 3) * 4;
            outRgba[i * 4] = (byte)pal[idx];
            outRgba[i * 4 + 1] = (byte)pal[idx + 1];
            outRgba[i * 4 + 2] = (byte)pal[idx + 2];
            outRgba[i * 4 + 3] = (byte)pal[idx + 3];
        }
    }

    public static void BuildAlphaPalette(int a0, int a1, Span<byte> pal)
    {
        pal[0] = (byte)a0;
        pal[1] = (byte)a1;
        if (a0 > a1)
        {
            for (int j = 0; j < 6; j++)
            {
                pal[2 + j] = (byte)(((6 - j) * a0 + (1 + j) * a1) / 7);
            }
        }
        else
        {
            for (int j = 0; j < 4; j++)
            {
                pal[2 + j] = (byte)(((4 - j) * a0 + (1 + j) * a1) / 5);
            }

            pal[6] = 0;
            pal[7] = 255;
        }
    }

    public static ulong ReadAlphaBits(ReadOnlySpan<byte> blk)
    {
        ulong bits = 0;
        for (int k = 0; k < 6; k++)
        {
            bits |= (ulong)blk[2 + k] << (8 * k);
        }

        return bits;
    }

    private static void DecodeAlpha(ReadOnlySpan<byte> blk, Span<byte> outRgba)
    {
        Span<byte> pal = stackalloc byte[8];
        BuildAlphaPalette(blk[0], blk[1], pal);
        ulong bits = ReadAlphaBits(blk);
        for (int i = 0; i < 16; i++)
        {
            outRgba[i * 4 + 3] = pal[(int)((bits >> (3 * i)) & 7)];
        }
    }

    public static bool IsDxt5BlockOpaque(ReadOnlySpan<byte> blk)
    {
        Span<byte> pal = stackalloc byte[8];
        BuildAlphaPalette(blk[0], blk[1], pal);
        ulong bits = ReadAlphaBits(blk);
        for (int i = 0; i < 16; i++)
        {
            if (pal[(int)((bits >> (3 * i)) & 7)] != 255)
            {
                return false;
            }
        }

        return true;
    }

    public static void RepackDxt5ToDxt1(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
    {
        int blocks = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);
        for (int b = 0; b < blocks; b++)
        {
            var s = src.Slice(b * 16 + 8, 8);
            var d = dst.Slice(b * 8, 8);
            int c0 = s[0] | (s[1] << 8);
            int c1 = s[2] | (s[3] << 8);
            uint bits = (uint)(s[4] | (s[5] << 8) | (s[6] << 16) | (s[7] << 24));
            if (c0 > c1)
            {
                s.CopyTo(d);
                continue;
            }

            uint mapped = 0;
            for (int i = 0; i < 16; i++)
            {
                uint idx = (bits >> (2 * i)) & 3;
                uint m;
                if (c0 < c1)
                {
                    m = idx switch { 0 => 1u, 1 => 0u, 2 => 3u, _ => 2u };
                }
                else
                {
                    m = idx == 3 ? 0u : idx;
                }

                mapped |= m << (2 * i);
            }

            int n0 = c0 < c1 ? c1 : c0;
            int n1 = c0 < c1 ? c0 : c1;
            d[0] = (byte)n0;
            d[1] = (byte)(n0 >> 8);
            d[2] = (byte)n1;
            d[3] = (byte)(n1 >> 8);
            d[4] = (byte)mapped;
            d[5] = (byte)(mapped >> 8);
            d[6] = (byte)(mapped >> 16);
            d[7] = (byte)(mapped >> 24);
        }
    }
}
