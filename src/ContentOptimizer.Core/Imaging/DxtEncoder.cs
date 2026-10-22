namespace ContentOptimizer.Core.Imaging;

public static class DxtEncoder
{
    public static void EncodeDxt1(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst) =>
        Encode(rgba, width, height, dst, false);

    public static void EncodeDxt5(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst) =>
        Encode(rgba, width, height, dst, true);

    private static void Encode(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> dst, bool withAlpha)
    {
        int bw = Math.Max(1, (width + 3) / 4);
        int bh = Math.Max(1, (height + 3) / 4);
        int stride = withAlpha ? 16 : 8;
        Span<byte> block = stackalloc byte[64];
        int o = 0;
        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                Gather(rgba, width, height, bx, by, block);
                if (withAlpha)
                {
                    EncodeAlpha(block, dst.Slice(o, 8));
                    EncodeColor(block, dst.Slice(o + 8, 8));
                }
                else
                {
                    EncodeColor(block, dst.Slice(o, 8));
                }

                o += stride;
            }
        }
    }

    private static void Gather(ReadOnlySpan<byte> rgba, int width, int height, int bx, int by, Span<byte> block)
    {
        for (int py = 0; py < 4; py++)
        {
            int y = Math.Min(by * 4 + py, height - 1);
            for (int px = 0; px < 4; px++)
            {
                int x = Math.Min(bx * 4 + px, width - 1);
                rgba.Slice((y * width + x) * 4, 4).CopyTo(block.Slice((py * 4 + px) * 4, 4));
            }
        }
    }

    private static int Quantize(float r, float g, float b)
    {
        int r5 = Math.Clamp((int)(r * 31f / 255f + 0.5f), 0, 31);
        int g6 = Math.Clamp((int)(g * 63f / 255f + 0.5f), 0, 63);
        int b5 = Math.Clamp((int)(b * 31f / 255f + 0.5f), 0, 31);
        return (r5 << 11) | (g6 << 5) | b5;
    }

    private static void BuildPalette(int e0, int e1, Span<int> pal)
    {
        DxtDecoder.Expand565(e0, out int r0, out int g0, out int b0);
        DxtDecoder.Expand565(e1, out int r1, out int g1, out int b1);
        pal[0] = r0;
        pal[1] = g0;
        pal[2] = b0;
        pal[3] = r1;
        pal[4] = g1;
        pal[5] = b1;
        pal[6] = (2 * r0 + r1) / 3;
        pal[7] = (2 * g0 + g1) / 3;
        pal[8] = (2 * b0 + b1) / 3;
        pal[9] = (r0 + 2 * r1) / 3;
        pal[10] = (g0 + 2 * g1) / 3;
        pal[11] = (b0 + 2 * b1) / 3;
    }

    private static int Fit(ReadOnlySpan<int> rgb, ref int e0, ref int e1, Span<int> idx)
    {
        if (e0 < e1)
        {
            (e0, e1) = (e1, e0);
        }

        Span<int> pal = stackalloc int[12];
        BuildPalette(e0, e1, pal);
        int total = 0;
        for (int i = 0; i < 16; i++)
        {
            int r = rgb[i * 3];
            int g = rgb[i * 3 + 1];
            int b = rgb[i * 3 + 2];
            int best = int.MaxValue;
            int bestK = 0;
            for (int k = 0; k < 4; k++)
            {
                int dr = r - pal[k * 3];
                int dg = g - pal[k * 3 + 1];
                int db = b - pal[k * 3 + 2];
                int d = dr * dr + dg * dg + db * db;
                if (d < best)
                {
                    best = d;
                    bestK = k;
                }
            }

            idx[i] = bestK;
            total += best;
        }

        return total;
    }

    private static bool Refine(ReadOnlySpan<int> rgb, ReadOnlySpan<int> idx, out int n0, out int n1)
    {
        n0 = 0;
        n1 = 0;
        double aa = 0, bb = 0, ab = 0;
        double ax0 = 0, ax1 = 0, ax2 = 0, bx0 = 0, bx1 = 0, bx2 = 0;
        for (int i = 0; i < 16; i++)
        {
            double a = idx[i] switch { 0 => 1.0, 1 => 0.0, 2 => 2.0 / 3.0, _ => 1.0 / 3.0 };
            double b = 1.0 - a;
            aa += a * a;
            bb += b * b;
            ab += a * b;
            double x0 = rgb[i * 3];
            double x1 = rgb[i * 3 + 1];
            double x2 = rgb[i * 3 + 2];
            ax0 += a * x0;
            ax1 += a * x1;
            ax2 += a * x2;
            bx0 += b * x0;
            bx1 += b * x1;
            bx2 += b * x2;
        }

        double det = aa * bb - ab * ab;
        if (Math.Abs(det) < 1e-4)
        {
            return false;
        }

        float r0 = (float)((bb * ax0 - ab * bx0) / det);
        float g0 = (float)((bb * ax1 - ab * bx1) / det);
        float b0 = (float)((bb * ax2 - ab * bx2) / det);
        float r1 = (float)((aa * bx0 - ab * ax0) / det);
        float g1 = (float)((aa * bx1 - ab * ax1) / det);
        float b1 = (float)((aa * bx2 - ab * ax2) / det);
        n0 = Quantize(r0, g0, b0);
        n1 = Quantize(r1, g1, b1);
        return true;
    }

    private static void WriteColor(Span<byte> dst, int e0, int e1, ReadOnlySpan<int> idx)
    {
        uint bits = 0;
        for (int i = 0; i < 16; i++)
        {
            bits |= (uint)idx[i] << (2 * i);
        }

        dst[0] = (byte)e0;
        dst[1] = (byte)(e0 >> 8);
        dst[2] = (byte)e1;
        dst[3] = (byte)(e1 >> 8);
        dst[4] = (byte)bits;
        dst[5] = (byte)(bits >> 8);
        dst[6] = (byte)(bits >> 16);
        dst[7] = (byte)(bits >> 24);
    }

    private static void EncodeColor(ReadOnlySpan<byte> px, Span<byte> dst)
    {
        Span<int> rgb = stackalloc int[48];
        Span<int> idx = stackalloc int[16];
        Span<int> bestIdx = stackalloc int[16];
        int sr = 0, sg = 0, sb = 0;
        bool solid = true;
        for (int i = 0; i < 16; i++)
        {
            int r = px[i * 4];
            int g = px[i * 4 + 1];
            int b = px[i * 4 + 2];
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
            sr += r;
            sg += g;
            sb += b;
            if (r != rgb[0] || g != rgb[1] || b != rgb[2])
            {
                solid = false;
            }
        }

        if (solid)
        {
            int q = Quantize(rgb[0], rgb[1], rgb[2]);
            bestIdx.Clear();
            WriteColor(dst, q, q, bestIdx);
            return;
        }

        float mr = sr / 16f;
        float mg = sg / 16f;
        float mb = sb / 16f;
        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        for (int i = 0; i < 16; i++)
        {
            float dr = rgb[i * 3] - mr;
            float dg = rgb[i * 3 + 1] - mg;
            float db = rgb[i * 3 + 2] - mb;
            cxx += dr * dr;
            cxy += dr * dg;
            cxz += dr * db;
            cyy += dg * dg;
            cyz += dg * db;
            czz += db * db;
        }

        float vx = cxx + cxy + cxz;
        float vy = cxy + cyy + cyz;
        float vz = cxz + cyz + czz;
        for (int it = 0; it < 4; it++)
        {
            float nx = cxx * vx + cxy * vy + cxz * vz;
            float ny = cxy * vx + cyy * vy + cyz * vz;
            float nz = cxz * vx + cyz * vy + czz * vz;
            float m = MathF.Max(MathF.Abs(nx), MathF.Max(MathF.Abs(ny), MathF.Abs(nz)));
            if (m < 1e-6f)
            {
                break;
            }

            vx = nx / m;
            vy = ny / m;
            vz = nz / m;
        }

        float len = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (len < 1e-6f)
        {
            vx = vy = vz = 0.57735f;
        }
        else
        {
            vx /= len;
            vy /= len;
            vz /= len;
        }

        float tmin = float.MaxValue;
        float tmax = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float t = (rgb[i * 3] - mr) * vx + (rgb[i * 3 + 1] - mg) * vy + (rgb[i * 3 + 2] - mb) * vz;
            tmin = MathF.Min(tmin, t);
            tmax = MathF.Max(tmax, t);
        }

        float inset = (tmax - tmin) / 16f;
        float t0 = tmax - inset;
        float t1 = tmin + inset;
        int e0 = Quantize(mr + vx * t0, mg + vy * t0, mb + vz * t0);
        int e1 = Quantize(mr + vx * t1, mg + vy * t1, mb + vz * t1);
        int bestE0 = e0;
        int bestE1 = e1;
        int bestErr = Fit(rgb, ref bestE0, ref bestE1, bestIdx);

        for (int iter = 0; iter < 3 && bestErr > 0; iter++)
        {
            if (!Refine(rgb, bestIdx, out int n0, out int n1))
            {
                break;
            }

            int err = Fit(rgb, ref n0, ref n1, idx);
            if (err >= bestErr)
            {
                break;
            }

            bestErr = err;
            bestE0 = n0;
            bestE1 = n1;
            idx.CopyTo(bestIdx);
        }

        if (bestE0 == bestE1)
        {
            bestIdx.Clear();
        }

        WriteColor(dst, bestE0, bestE1, bestIdx);
    }

    private static int AlphaError(ReadOnlySpan<byte> px, ReadOnlySpan<byte> pal, Span<int> idx)
    {
        int total = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = px[i * 4 + 3];
            int best = int.MaxValue;
            int bestK = 0;
            for (int k = 0; k < 8; k++)
            {
                int d = a - pal[k];
                d *= d;
                if (d < best)
                {
                    best = d;
                    bestK = k;
                }
            }

            idx[i] = bestK;
            total += best;
        }

        return total;
    }

    private static void EncodeAlpha(ReadOnlySpan<byte> px, Span<byte> dst)
    {
        int amin = 255;
        int amax = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = px[i * 4 + 3];
            amin = Math.Min(amin, a);
            amax = Math.Max(amax, a);
        }

        dst.Clear();
        if (amin == amax)
        {
            dst[0] = (byte)amax;
            dst[1] = (byte)amax;
            return;
        }

        Span<byte> pal = stackalloc byte[8];
        Span<int> idx = stackalloc int[16];
        Span<int> bestIdx = stackalloc int[16];
        int bestA0 = amax;
        int bestA1 = amin;
        DxtDecoder.BuildAlphaPalette(bestA0, bestA1, pal);
        int bestErr = AlphaError(px, pal, bestIdx);

        if (amin == 0 || amax == 255)
        {
            int lo = 255;
            int hi = 0;
            bool interior = false;
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3];
                if (a > 0 && a < 255)
                {
                    interior = true;
                    lo = Math.Min(lo, a);
                    hi = Math.Max(hi, a);
                }
            }

            if (!interior)
            {
                lo = 0;
                hi = 0;
            }

            DxtDecoder.BuildAlphaPalette(lo, hi, pal);
            int err = AlphaError(px, pal, idx);
            if (err < bestErr)
            {
                bestErr = err;
                bestA0 = lo;
                bestA1 = hi;
                idx.CopyTo(bestIdx);
            }
        }

        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            bits |= (ulong)bestIdx[i] << (3 * i);
        }

        dst[0] = (byte)bestA0;
        dst[1] = (byte)bestA1;
        for (int k = 0; k < 6; k++)
        {
            dst[2 + k] = (byte)(bits >> (8 * k));
        }
    }
}
